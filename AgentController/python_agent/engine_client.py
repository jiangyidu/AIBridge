# -*- coding: utf-8 -*-
"""Engine-agnostic bridge client base class.

All HTTP protocol logic for communicating with an engine-side AgentBridge lives here.
Engine-specific subclasses (UnityClient, AutoCADClient, TeklaClient, ...) override
only the hook methods that differ per engine:
  - engine_name          : display name
  - _resolve_agent_url() : how to find the bridge's HTTP address
  - _tools_schema_fallback_paths() : where to find AgentTools.json on disk
  - _compile_tool_names(): which tools trigger a compile-then-poll cycle
  - _normalize_source()  : pre-process AI-generated source before sending

2026-06-16 : Extracted from unity_client.py for cross-engine portability.
"""

import hashlib
import json
import os
import time
from typing import Any, Callable, Dict, List, Optional

import requests

NO_PROXY = {"http": None, "https": None}


# ─── Shared Utility ──────────────────────────────────────────────────────────

def project_hash_port(project_path: str, base: int = 8000, span: int = 2000) -> int:
    """Deterministic port number derived from the project path (MD5 hash)."""
    normalized = os.path.abspath(project_path).replace("\\", "/").lower()
    digest = hashlib.md5(normalized.encode("utf-8")).digest()
    h = digest[0] | (digest[1] << 8) | (digest[2] << 16) | (digest[3] << 24)
    return base + (h % span)


def get_service_port(project_path: str) -> int:
    """Python Agent Service port – deterministic, separate from the engine bridge range."""
    return project_hash_port(project_path, base=11000, span=2000)


def get_service_url(project_path: str) -> str:
    return f"http://127.0.0.1:{get_service_port(project_path)}"


# ─── Source Code Normalization (shared by C#-based engines) ───────────────────

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


def normalize_csharp_source(code: Any) -> Any:
    """Repair common LLM/JSON double-escaping in C# source code.

    Shared by all C#-based engine clients (Unity, AutoCAD, Tekla).
    """
    if not isinstance(code, str):
        return code
    text = code.replace("\r\n", "\n").replace("\r", "\n").strip()

    # 新增：HTML 实体反编码
    text = text.replace("&lt;", "<").replace("&gt;", ">")\
               .replace("&amp;", "&").replace("&quot;", '"')
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
            text = text.replace('\\\\\\"', '"').replace('\\"', '"')
            if text == before:
                break
    if "\\n" in text and "\n" not in text:
        text = text.replace("\\r\\n", "\n").replace("\\n", "\n").replace("\\t", "\t")
    return text


# ─── Abstract Engine Client ──────────────────────────────────────────────────

class EngineClient:
    """Engine-agnostic HTTP bridge client.

    Subclasses must override the hook methods marked with 'Override in subclass'.
    All HTTP protocol logic (ping, tool calls, compile polling) is engine-agnostic
    and lives in this base class.
    """

    # ── Override in subclass ──────────────────────────────────────────────────
    engine_name: str = "unknown"
    """Human-readable engine name for log messages."""

    def _resolve_agent_url(self) -> str:
        """Return the HTTP base URL of the engine-side bridge (e.g. http://127.0.0.1:8123/agent).

        Override in subclass to implement engine-specific port file reading, hash port, etc.
        """
        return f"http://127.0.0.1:{project_hash_port(self.project_path)}/agent"

    def _tools_schema_fallback_paths(self) -> List[str]:
        """Return ordered list of local file paths to try for AgentTools.json fallback.

        Override in subclass to provide engine-specific paths.
        """
        return []

    def _compile_tool_names(self) -> set:
        """Return the set of tool names that trigger a compile-then-poll cycle.

        Override in subclass. Default empty (no compile tools).
        """
        return set()

    def _normalize_source(self, code: Any) -> Any:
        """Pre-process AI-generated source code before sending to the engine.

        Override in subclass. Default: C# normalization (shared by Unity/AutoCAD/Tekla).
        """
        return normalize_csharp_source(code)

    # ── Constructor ───────────────────────────────────────────────────────────

    def __init__(
        self,
        project_path: str,
        agent_url: Optional[str] = None,
        event_sink=None,
        cancel_flag: Optional[Callable[[], bool]] = None,
        request_timeout: float = 10.0,
        retries: int = 2,
        reconnect_timeout: float = 4.0,
    ):
        self.project_path = os.path.abspath(project_path)
        self._custom_agent_url = agent_url
        self.event_sink = event_sink
        self.cancel_flag = cancel_flag
        self.request_timeout = max(1.0, float(request_timeout or 10.0))
        self.retries = max(1, int(retries or 2))
        self.reconnect_timeout = max(1.0, float(reconnect_timeout or 4.0))

    # ── Agent URL ─────────────────────────────────────────────────────────────

    @property
    def agent_url(self) -> str:
        if self._custom_agent_url:
            return self._custom_agent_url
        return self._resolve_agent_url()

    # ── Internal helpers ──────────────────────────────────────────────────────

    def _cancelled(self) -> bool:
        if self.cancel_flag is None:
            return False
        try:
            return bool(self.cancel_flag())
        except Exception:
            return False

    def _sleep(self, seconds: float) -> bool:
        """Sleep in small increments, returning False immediately if cancelled."""
        end = time.time() + max(0.0, seconds)
        while time.time() < end:
            if self._cancelled():
                return False
            time.sleep(min(0.1, end - time.time()))
        return not self._cancelled()

    def _emit(self, event_type: str, message: str = "", data: Optional[Dict[str, Any]] = None) -> None:
        if self.event_sink:
            self.event_sink(event_type, message, data or {})

    # ── Protocol: Ping ────────────────────────────────────────────────────────

    def ping(self, timeout: float = 1.0) -> bool:
        if self._cancelled():
            return False
        try:
            response = requests.get(f"{self.agent_url}/ping", timeout=timeout, proxies=NO_PROXY)
            return response.status_code == 200
        except requests.RequestException:
            return False

    # ── Protocol: Wait for Engine ─────────────────────────────────────────────

    def wait_for_engine(self, timeout: float = 8.0, interval: float = 0.5) -> bool:
        """Block until the engine bridge responds to ping, or timeout."""
        start = time.time()
        self._emit("engine_waiting", f"等待 {self.engine_name} AgentBridge 上线: {self.agent_url}")
        while time.time() - start < timeout:
            if self._cancelled():
                self._emit("session_cancelled", f"等待 {self.engine_name} 时检测到会话已取消")
                return False
            if self.ping(timeout=min(1.0, max(0.2, interval))):
                self._emit("engine_online", f"{self.engine_name} AgentBridge 已上线")
                return True
            if not self._sleep(interval):
                return False
        self._emit("engine_offline", f"等待 {self.engine_name} AgentBridge 超时")
        return False

    # ── Protocol: Load Tools Schema ───────────────────────────────────────────

    def load_tools_schema(self) -> List[Dict[str, Any]]:
        """Fetch tools schema from the bridge HTTP endpoint, with local file fallback."""
        try:
            response = requests.get(f"{self.agent_url}/tools_schema", timeout=3, proxies=NO_PROXY)
            response.raise_for_status()
            return response.json()
        except Exception:
            for path in self._tools_schema_fallback_paths():
                if os.path.exists(path):
                    with open(path, "r", encoding="utf-8-sig") as f:
                        return json.load(f)
            return []

    # ── Protocol: Call Tool ───────────────────────────────────────────────────

    def call_tool(self, tool_name: str, args: Optional[Dict[str, Any]] = None,
                  timeout: Optional[float] = None) -> Dict[str, Any]:
        if self._cancelled():
            return {"Success": False, "Cancelled": True, "Error": "Agent 会话已取消"}
        safe_args = dict(args or {})
        # Normalize source code for compile tools
        if tool_name in self._compile_tool_names() and "code" in safe_args:
            safe_args["code"] = self._normalize_source(safe_args.get("code"))
        payload = {"tool": tool_name, "args": safe_args}
        timeout = float(timeout or self.request_timeout)

        for attempt in range(self.retries):
            if self._cancelled():
                return {"Success": False, "Cancelled": True, "Error": "Agent 会话已取消"}

            if not self.ping(timeout=1.0):
                self._emit("engine_offline",
                           f"{self.engine_name} AgentBridge 未响应，准备重试 ({attempt + 1}/{self.retries})")
                self.wait_for_engine(timeout=self.reconnect_timeout, interval=0.5)
                continue

            try:
                response = requests.post(f"{self.agent_url}/tool", json=payload, timeout=timeout, proxies=NO_PROXY)
                if response.status_code == 200:
                    try:
                        result = response.json()
                    except ValueError:
                        result = {"Success": False, "Error": f"{self.engine_name} 返回了非 JSON 响应",
                                  "Raw": response.text}
                    return result
                return {"Success": False, "Error": f"HTTP {response.status_code}: {response.text}"}
            except requests.exceptions.Timeout:
                self._emit("engine_offline",
                           f"{self.engine_name} 工具 `{tool_name}` 调用超时，准备重试 ({attempt + 1}/{self.retries})")
                self.wait_for_engine(timeout=self.reconnect_timeout, interval=0.5)
            except requests.exceptions.ConnectionError:
                self._emit("engine_offline",
                           f"{self.engine_name} 离线或连接被拒绝，准备重试 ({attempt + 1}/{self.retries})")
                self.wait_for_engine(timeout=self.reconnect_timeout, interval=0.5)
            except Exception as exc:
                return {"Success": False, "Error": str(exc)}
        return {"Success": False, "Error": f"多次重试后仍无法连接 {self.engine_name}"}

    # ── Protocol: Compile + Poll ──────────────────────────────────────────────

    def call_tool_with_compile_wait(
        self,
        tool_name: str,
        args: Optional[Dict[str, Any]] = None,
        poll_interval: float = 3.0,
        timeout: float = 120.0,
    ) -> Dict[str, Any]:
        """Call a compile tool, then poll check_compile_status until done/error/timeout."""
        result = self.call_tool(tool_name, args)
        if not isinstance(result, dict):
            return {"Success": False, "Error": f"{self.engine_name} 工具返回值不是对象", "Raw": str(result)}

        if result.get("Status") != "compiling":
            return result

        target = result.get("TargetMethod") or result.get("TypeName") or result.get("Message", "")
        self._emit("compile_started", f"{self.engine_name} 编译已触发: {target}", result)
        start = time.time()
        while time.time() - start < timeout:
            if self._cancelled():
                return {"Success": False, "Cancelled": True, "Error": "Agent 会话已取消"}
            if not self._sleep(poll_interval):
                return {"Success": False, "Cancelled": True, "Error": "Agent 会话已取消"}
            poll_result = self.call_tool("check_compile_status", {}, timeout=self.request_timeout)
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
            self._emit("compile_polling",
                       poll_result.get("Message", f"{self.engine_name} 仍在编译中"), poll_result)

        timeout_result = {"Status": "timeout", "Success": False, "Error": "Python 端轮询编译超时"}
        self._emit("compile_timeout", timeout_result["Error"], timeout_result)
        return timeout_result
