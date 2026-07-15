# -*- coding: utf-8 -*-
"""Unity AgentBridge client.

This module is intentionally free of LLM concerns. It only knows how to reach the
Unity-side command executor and how to wait through Domain Reload / compilation.
"""

import hashlib
import json
import os
import time
from typing import Any, Dict, List, Optional

import requests

NO_PROXY = {"http": None, "https": None}


def _count_unescaped_quotes(text: str) -> int:
    count = 0
    for i, ch in enumerate(text):
        if ch != '"':
            continue
        slash_count = 0
        j = i - 1
        while j >= 0 and text[j] == "\\":
            slash_count += 1
            j -= 1
        if slash_count % 2 == 0:
            count += 1
    return count


def _looks_over_escaped_source(code: str) -> bool:
    if not code or '\\"' not in code:
        return False
    escaped = code.count('\\"')
    plain = _count_unescaped_quotes(code)
    if plain == 0:
        return True
    patterns = [
        'Find(\\"', 'new GameObject(\\"', 'Shader.Find(\\"', 'ToString(\\"',
        'return \\"', 'name = \\"', 'Debug.Log(\\"',
    ]
    return any(p in code for p in patterns) or escaped > plain * 2


def normalize_generated_source(code: Any) -> Any:
    """Repair common LLM/JSON double-escaping before sending source to Unity.

    Unity still performs the authoritative LitJson parse and source validation. This
    pre-normalization reduces the chance that code like GameObject.Find(\"Name\")
    reaches Unity as C# source with a literal backslash before the string delimiter.
    """
    if not isinstance(code, str):
        return code
    text = code.replace("\r\n", "\n").replace("\r", "\n").strip()
    if text.startswith("```"):
        first_newline = text.find("\n")
        if first_newline >= 0:
            text = text[first_newline + 1:]
        fence = text.rfind("```")
        if fence >= 0:
            text = text[:fence]
        text = text.strip()
    if _looks_over_escaped_source(text):
        for _ in range(3):
            before = text
            text = text.replace('\\\\"', '"').replace('\\"', '"')
            if text == before:
                break
    if "\\n" in text and "\n" not in text:
        text = text.replace("\\r\\n", "\n").replace("\\n", "\n").replace("\\t", "\t")
    return text



def project_hash_port(project_path: str, base: int = 8000, span: int = 2000) -> int:
    normalized = os.path.abspath(project_path).replace("\\", "/").lower()
    digest = hashlib.md5(normalized.encode("utf-8")).digest()
    h = digest[0] | (digest[1] << 8) | (digest[2] << 16) | (digest[3] << 24)
    return base + (h % span)


def get_agent_url(project_path: str) -> str:
    return f"http://127.0.0.1:{project_hash_port(project_path)}/agent"


def get_service_port(project_path: str) -> int:
    # Keep deterministic but separate from the Unity AgentBridge port range.
    return project_hash_port(project_path, base=11000, span=2000)


def get_service_url(project_path: str) -> str:
    return f"http://127.0.0.1:{get_service_port(project_path)}"


class UnityClient:
    def __init__(self, project_path: str, agent_url: Optional[str] = None, event_sink=None):
        self.project_path = os.path.abspath(project_path)
        self._custom_agent_url = agent_url
        self.event_sink = event_sink

    @property
    def agent_url(self) -> str:
        if self._custom_agent_url:
            return self._custom_agent_url
        return get_agent_url(self.project_path)

    def _emit(self, event_type: str, message: str = "", data: Optional[Dict[str, Any]] = None) -> None:
        if self.event_sink:
            self.event_sink(event_type, message, data or {})

    def ping(self, timeout: float = 2.0) -> bool:
        try:
            response = requests.get(f"{self.agent_url}/ping", timeout=timeout, proxies=NO_PROXY)
            return response.status_code == 200
        except requests.RequestException:
            return False

    def wait_for_unity(self, timeout: float = 120.0, interval: float = 1.0) -> bool:
        start = time.time()
        self._emit("unity_waiting", f"等待 Unity AgentBridge 上线: {self.agent_url}")
        while time.time() - start < timeout:
            if self.ping(timeout=2.0):
                self._emit("unity_online", "Unity AgentBridge 已上线")
                return True
            time.sleep(interval)
        self._emit("unity_offline", "等待 Unity AgentBridge 超时")
        return False

    def load_tools_schema(self) -> List[Dict[str, Any]]:
        try:
            response = requests.get(f"{self.agent_url}/tools_schema", timeout=10, proxies=NO_PROXY)
            response.raise_for_status()
            return response.json()
        except Exception:
            # Fallback: during very early startup Unity may not be available yet.
            path = os.path.join(self.project_path, "Assets", "Editor", "AgentTools.json")
            if not os.path.exists(path):
                path = os.path.join(self.project_path, "Editor", "AgentTools.json")
            if os.path.exists(path):
                with open(path, "r", encoding="utf-8-sig") as f:
                    return json.load(f)
            return []

    def call_tool(self, tool_name: str, args: Optional[Dict[str, Any]] = None, timeout: float = 30.0) -> Dict[str, Any]:
        safe_args = dict(args or {})
        if tool_name in {"compile_temp_method", "compile_script"} and "code" in safe_args:
            safe_args["code"] = normalize_generated_source(safe_args.get("code"))
        payload = {"tool": tool_name, "args": safe_args}
        for attempt in range(5):
            try:
                response = requests.post(f"{self.agent_url}/tool", json=payload, timeout=timeout, proxies=NO_PROXY)
                if response.status_code == 200:
                    try:
                        result = response.json()
                    except ValueError:
                        result = {"Success": False, "Error": "Unity 返回了非 JSON 响应", "Raw": response.text}
                    return result
                return {"Success": False, "Error": f"HTTP {response.status_code}: {response.text}"}
            except (requests.exceptions.ConnectionError, requests.exceptions.Timeout):
                self._emit("unity_offline", f"Unity 离线或无响应，准备重试 ({attempt + 1}/5)")
                time.sleep(2.0)
                self.wait_for_unity(timeout=60.0)
            except Exception as exc:
                return {"Success": False, "Error": str(exc)}
        return {"Success": False, "Error": "多次重试后仍无法连接 Unity"}

    def call_tool_with_compile_wait(
        self,
        tool_name: str,
        args: Optional[Dict[str, Any]] = None,
        poll_interval: float = 3.0,
        timeout: float = 120.0,
    ) -> Dict[str, Any]:
        result = self.call_tool(tool_name, args)
        if not isinstance(result, dict):
            return {"Success": False, "Error": "Unity 工具返回值不是对象", "Raw": str(result)}

        if result.get("Status") != "compiling":
            return result

        target = result.get("TargetMethod") or result.get("TypeName") or result.get("Message", "")
        self._emit("compile_started", f"Unity 编译已触发: {target}", result)
        start = time.time()
        while time.time() - start < timeout:
            time.sleep(poll_interval)
            poll_result = self.call_tool("check_compile_status", {}, timeout=30.0)
            if not isinstance(poll_result, dict):
                self._emit("compile_polling", f"编译轮询返回非法格式: {poll_result}")
                continue

            status = poll_result.get("Status", "")
            if status == "done":
                self._emit("compile_done", poll_result.get("Message", "编译完成"), poll_result)
                return poll_result
            if status == "error":
                self._emit("compile_error", poll_result.get("Message", "编译失败"), poll_result)
                return poll_result
            if status == "timeout":
                self._emit("compile_timeout", poll_result.get("Message", "编译超时"), poll_result)
                return poll_result
            self._emit("compile_polling", poll_result.get("Message", "Unity 仍在编译中"), poll_result)

        timeout_result = {"Status": "timeout", "Success": False, "Error": "Python 端轮询编译超时"}
        self._emit("compile_timeout", timeout_result["Error"], timeout_result)
        return timeout_result
