# -*- coding: utf-8 -*-
"""Python-side ReAct / tool-calling Agent core.

2026-06-16 refactor: Engine-agnostic. Uses EngineClient base class instead of
hardcoded UnityClient. The engine type is determined by config["engine"] (default "unity").
All engine-specific behavior is encapsulated in EngineClient subclasses.
"""

import json
import os
import time
import traceback
import uuid
from typing import Any, Dict, List, Optional

from .engine_client import EngineClient
from .history_store import HistoryStore
from .prompts import build_system_prompt
from .providers import create_provider


# ── Engine Client Factory ─────────────────────────────────────────────────────

def create_engine_client(config: Dict[str, Any], **kwargs) -> EngineClient:
    """Create an engine client based on config["engine"].

    Currently supported engines: "unity" (default).
    Future engines (autocad, tekla, ue4) will register here.
    """
    engine = (config.get("engine") or "unity").lower()

    if engine == "unity":
        from .unity_client import UnityClient
        return UnityClient(**kwargs)
    # Future:
    # elif engine == "autocad":
    #     from .autocad_client import AutoCADClient
    #     return AutoCADClient(**kwargs)
    # elif engine == "tekla":
    #     from .tekla_client import TeklaClient
    #     return TeklaClient(**kwargs)
    else:
        raise ValueError(
            f"Unknown engine: '{engine}'. Supported: unity. "
            f"Set config['engine'] = 'unity' or add a new engine client."
        )


class AgentRunner:
    def __init__(self, config: Dict[str, Any], session_id: Optional[str] = None, cancel_flag=None):
        self.config = dict(config)
        self.project_path = os.path.abspath(self.config["project_path"])
        self.session_id = session_id or uuid.uuid4().hex
        self.history = HistoryStore(self.project_path)
        self.cancel_flag = cancel_flag

        # Create the appropriate engine client based on config["engine"]
        self.engine: EngineClient = create_engine_client(
            self.config,
            project_path=self.project_path,
            event_sink=self.emit_event,
            cancel_flag=self.is_cancelled,
            request_timeout=float(self.config.get("engine_tool_timeout") or self.config.get("unity_tool_timeout") or 10),
            retries=int(self.config.get("engine_tool_retries") or self.config.get("unity_tool_retries") or 2),
            reconnect_timeout=float(self.config.get("engine_reconnect_timeout") or self.config.get("unity_reconnect_timeout") or 4),
        )
        # Backward-compatible alias
        self.unity = self.engine

        self.provider = create_provider(self.config)
        self.max_steps = int(self.config.get("max_steps") or 30)

        # Load translations from Localization.json
        self.translations = {}
        try:
            loc_path = os.path.join(self.project_path, "Assets", "AIBridge", "Editor", "Localization.json")
            if os.path.exists(loc_path):
                with open(loc_path, "r", encoding="utf-8") as f:
                    self.translations = json.load(f)
        except Exception:
            pass

    def get_tr(self, key: str, default: str) -> str:
        lang = self.config.get("language") or "zh"
        if key in self.translations:
            lang_data = self.translations[key]
            if lang in lang_data:
                return lang_data[lang]
        return default

    def is_cancelled(self) -> bool:
        if self.cancel_flag is None:
            return False
        try:
            return bool(self.cancel_flag())
        except Exception:
            return False

    def emit_event(self, event_type: str, message: str = "", data: Optional[Dict[str, Any]] = None) -> None:
        self.history.append_event(event_type, message, data or {}, session_id=self.session_id)

    def ui_message(self, role: str, content: str, reasoning: str = "") -> None:
        self.history.append_message(role, content, reasoning)

    def _prepare_messages(self) -> List[Dict[str, Any]]:
        ui_messages = self.history.load_messages()
        messages: List[Dict[str, Any]] = [{"role": "system", "content": build_system_prompt(self.config)}]
        for item in ui_messages:
            role = item.get("role")
            if role in ("user", "assistant"):
                messages.append({
                    "role": role,
                    "content": item.get("content") or "",
                    "reasoning_content": item.get("reasoning_content") or "",
                })
        # The UI already appends the current user message before starting the session.
        # If the session was started from CLI/service without the UI doing that, add it here.
        user_message = self.config.get("user_message") or ""
        if user_message and (not messages or messages[-1].get("role") != "user" or messages[-1].get("content") != user_message):
            messages.append({"role": "user", "content": user_message})
            self.ui_message("user", user_message)
        return messages

    @staticmethod
    def _tool_args(call: Dict[str, Any]) -> Dict[str, Any]:
        fn = call.get("function") or {}
        args_raw = fn.get("arguments") or "{}"
        if isinstance(args_raw, dict):
            return args_raw
        try:
            parsed = json.loads(args_raw)
            return parsed if isinstance(parsed, dict) else {"value": parsed}
        except Exception:
            return {"_raw_arguments": str(args_raw)}

    def _tool_name(self, call: Dict[str, Any]) -> str:
        return ((call.get("function") or {}).get("name") or call.get("name") or "").strip()

    def run(self) -> Dict[str, Any]:
        engine_name = self.engine.engine_name
        compile_tools = self.engine._compile_tool_names()

        self.emit_event("session_started", self.get_tr("agent_session_started", "Agent 会话已启动"), {"max_steps": self.max_steps})
        try:
            messages = self._prepare_messages()
            tools = self.engine.load_tools_schema()
            self.emit_event("tools_loaded", f"已加载 {len(tools)} 个工具定义", {"tool_count": len(tools)})

            startup_timeout = float(
                self.config.get("engine_startup_timeout") or self.config.get("unity_startup_timeout") or 8
            )
            if not self.engine.wait_for_engine(timeout=startup_timeout, interval=0.5):
                msg = self.get_tr("err_bridge_offline", "[智能体错误] {0} 代理桥接器 (AgentBridge) 未连接，已停止本次会话。\n请先确认宿主应用已启动 AgentBridge 服务，且 Python 端可访问 {1}/ping。").format(engine_name, self.engine.agent_url)
                self.emit_event("agent_error", f"{engine_name} AgentBridge 未连接，Agent 会话停止",
                               {"agent_url": self.engine.agent_url})
                self.ui_message("assistant", msg)
                self.emit_event("session_finished", f"Agent 会话结束：{engine_name} 未连接")
                return {"Success": False, "Error": f"{engine_name} AgentBridge offline",
                        "AgentUrl": self.engine.agent_url}

            for step in range(1, self.max_steps + 1):
                if self.is_cancelled():
                    self.emit_event("session_cancelled", "用户终止了 Agent 会话")
                    self.ui_message("system", self.get_tr("msg_session_terminated", "[系统] ⏹ 用户手动终止了智能体会话。"))
                    return {"Success": False, "Cancelled": True}

                step_msg = self.get_tr("step_request_llm_decision", "[步骤 {0}/{1}] 请求大语言模型决策").format(step, self.max_steps)
                self.emit_event("llm_request_started", step_msg, {"step": step})
                self.ui_message("system", step_msg)

                response = self.provider.chat(messages, tools)
                content = response.get("content") or ""
                reasoning = response.get("reasoning_content") or ""
                tool_calls = response.get("tool_calls") or []

                assistant_msg: Dict[str, Any] = {"role": "assistant", "content": content}
                if reasoning:
                    assistant_msg["reasoning_content"] = reasoning
                if tool_calls:
                    assistant_msg["tool_calls"] = tool_calls
                messages.append(assistant_msg)

                if not tool_calls:
                    final_text = content or self.get_tr("err_no_content_or_tool", "[智能体] 模型未返回文本内容，也没有工具调用。")
                    self.ui_message("assistant", final_text, reasoning)
                    self.emit_event("assistant_final", "Agent 已完成最终回复", {"step": step})
                    self.emit_event("session_finished", "Agent 会话完成")
                    return {"Success": True, "Final": final_text}

                self.emit_event("tool_batch_started", f"模型请求调用 {len(tool_calls)} 个工具", {"step": step})
                consecutive_failures = 0
                last_failed_tool = ""
                for call in tool_calls:
                    if self.is_cancelled():
                        self.emit_event("session_cancelled", "用户终止了 Agent 会话")
                        self.ui_message("system", self.get_tr("msg_session_terminated", "[系统] ⏹ 用户手动终止了智能体会话。"))
                        return {"Success": False, "Cancelled": True}

                    tool_name = self._tool_name(call)
                    tool_call_id = call.get("id") or f"call_{step}_{len(messages)}"
                    args = self._tool_args(call)

                    if tool_name == last_failed_tool and consecutive_failures >= 3:
                        skip_msg = self.get_tr("msg_tool_skipped", "[系统] ⏩ 跳过调用：相同工具 `{0}` 已连续失败 3 次。").format(tool_name)
                        self.ui_message("system", skip_msg)
                        messages.append({
                            "role": "tool",
                            "tool_call_id": tool_call_id,
                            "name": tool_name,
                            "content": json.dumps({"Success": False, "Message": "跳过：相同工具已连续失败，请检查参数或是否需要先探查"}, ensure_ascii=False)
                        })
                        continue

                    self.emit_event("tool_call_started", f"🔧 {tool_name}",
                                   {"tool": tool_name, "args": args, "step": step})
                    
                    args_json = json.dumps(args, ensure_ascii=False, indent=2)
                    self.ui_message("system", self.get_tr("step_call_tool", "[步骤 {0}] 🔧 调用工具 `{1}`\n```json\n{2}\n```").format(step, tool_name, args_json))

                    if tool_name in compile_tools:
                        result = self.engine.call_tool_with_compile_wait(tool_name, args)
                    else:
                        result = self.engine.call_tool(tool_name, args)

                    result_text = json.dumps(result, ensure_ascii=False, indent=2)
                    self.emit_event("tool_result", f"工具 {tool_name} 返回结果",
                                   {"tool": tool_name, "result": result, "step": step})
                    self.ui_message("system", self.get_tr("step_tool_returned", "[步骤 {0}] ✅ `{1}` 返回：\n```json\n{2}\n```").format(step, tool_name, result_text))

                    if isinstance(result, dict) and result.get("Success") is False:
                        consecutive_failures = consecutive_failures + 1 if tool_name == last_failed_tool else 1
                        last_failed_tool = tool_name
                    else:
                        consecutive_failures = 0
                        last_failed_tool = ""

                    # Check for engine offline during tool call
                    if (isinstance(result, dict) and result.get("Success") is False
                            and "无法连接" in str(result)):
                        final_text = self.get_tr("err_bridge_offline_during_tool", "[智能体错误] {0} 代理桥接器 (AgentBridge) 已离线，本次会话停止，避免继续卡在工具调用。").format(engine_name)
                        self.emit_event("agent_error", final_text,
                                       {"tool": tool_name, "result": result})
                        self.ui_message("assistant", final_text)
                        self.emit_event("session_finished", f"Agent 会话结束：{engine_name} 离线")
                        return {"Success": False,
                                "Error": f"{engine_name} AgentBridge offline during tool call",
                                "Tool": tool_name}

                    messages.append({
                        "role": "tool",
                        "tool_call_id": tool_call_id,
                        "name": tool_name,
                        "content": result_text,
                    })

            self.emit_event("agent_error", "达到最大步骤数，Agent 停止", {"max_steps": self.max_steps})
            self.ui_message("assistant", self.get_tr("err_max_steps_reached", "[智能体] 已达到最大步骤数 {0}，请检查是否存在循环调用或工具错误。").format(self.max_steps))
            return {"Success": False, "Error": "Max steps reached"}
        except Exception as exc:
            tb = traceback.format_exc()
            self.emit_event("agent_error", str(exc), {"traceback": tb})
            self.ui_message("assistant", f"[Agent Error] {exc}\n\n```text\n{tb}\n```")
            return {"Success": False, "Error": str(exc), "Traceback": tb}


def run_agent_loop(config: Dict[str, Any]) -> Dict[str, Any]:
    return AgentRunner(config).run()


def run_from_config_file(config_path: str) -> Dict[str, Any]:
    with open(config_path, "r", encoding="utf-8-sig") as f:
        config = json.load(f)
    return run_agent_loop(config)
