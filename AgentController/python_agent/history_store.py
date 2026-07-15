# -*- coding: utf-8 -*-
"""Chat history and event persistence used by both the Python Agent and Unity UI.

The Unity 2018 editor and the Python Agent can read/write the same JSON files on
Windows. Direct ``open(path, "w")`` may fail with ``PermissionError`` when the
editor is reading the file, so writes are atomic and retried. Persistent file-lock
failures are downgraded to side-car recovery files instead of crashing the Agent.
"""

import json
import os
import threading
import time
from typing import Any, Dict, List, Optional


class HistoryStore:
    def __init__(self, project_path: str):
        self.project_path = os.path.abspath(project_path)
        self.controller_dir = os.path.join(self.project_path, "AgentController")
        self.history_path = os.path.join(self.controller_dir, "ai_chat_history.json")
        self.events_path = os.path.join(self.controller_dir, "ai_agent_events.json")
        self._lock = threading.RLock()
        os.makedirs(self.controller_dir, exist_ok=True)

    @staticmethod
    def now_text() -> str:
        return time.strftime("%H:%M:%S")

    def _read_json_file(self, path: str, default: Dict[str, Any]) -> Dict[str, Any]:
        for attempt in range(20):
            try:
                if not os.path.exists(path):
                    return default
                with open(path, "r", encoding="utf-8-sig") as f:
                    data = json.load(f)
                return data if isinstance(data, dict) else default
            except PermissionError:
                time.sleep(0.03 * (attempt + 1))
            except Exception:
                return default
        return default

    def _atomic_write_json(self, path: str, payload: Dict[str, Any]) -> bool:
        os.makedirs(os.path.dirname(path), exist_ok=True)
        tmp = f"{path}.{os.getpid()}.{threading.get_ident()}.tmp"
        last_error: Optional[Exception] = None
        for attempt in range(30):
            try:
                with open(tmp, "w", encoding="utf-8") as f:
                    json.dump(payload, f, ensure_ascii=False, indent=4)
                    f.flush()
                    try:
                        os.fsync(f.fileno())
                    except OSError:
                        pass
                os.replace(tmp, path)
                return True
            except PermissionError as exc:
                last_error = exc
                time.sleep(0.04 * (attempt + 1))
            except OSError as exc:
                last_error = exc
                time.sleep(0.04 * (attempt + 1))
            finally:
                # 如果 os.replace 成功，tmp 已不存在；失败时可能残留。
                pass

        # 不让 Agent 因 UI 文件锁崩溃：写入恢复文件，供排查或后续合并。
        recovery = f"{path}.pending.{int(time.time())}.{os.getpid()}.json"
        try:
            with open(recovery, "w", encoding="utf-8") as f:
                json.dump({"_write_failed_for": path, "_error": str(last_error), "payload": payload}, f, ensure_ascii=False, indent=4)
        except Exception:
            pass
        try:
            if os.path.exists(tmp):
                os.remove(tmp)
        except Exception:
            pass
        return False

    def load_messages(self) -> List[Dict[str, Any]]:
        with self._lock:
            data = self._read_json_file(self.history_path, {"messages": []})
            messages = data.get("messages", [])
            return messages if isinstance(messages, list) else []

    def save_messages(self, messages: List[Dict[str, Any]]) -> None:
        with self._lock:
            # 返回值不向上抛出；UI 文件锁不应中断 Agent 主循环。
            self._atomic_write_json(self.history_path, {"messages": messages})

    def append_message(self, role: str, content: str, reasoning_content: str = "") -> Dict[str, Any]:
        with self._lock:
            messages = self.load_messages()
            msg = {
                "role": role,
                "content": content or "",
                "reasoning_content": reasoning_content or "",
                "time": self.now_text(),
            }
            messages.append(msg)
            self.save_messages(messages)
            return msg

    def load_events(self) -> List[Dict[str, Any]]:
        with self._lock:
            data = self._read_json_file(self.events_path, {"events": []})
            events = data.get("events", [])
            return events if isinstance(events, list) else []

    def save_events(self, events: List[Dict[str, Any]]) -> None:
        with self._lock:
            self._atomic_write_json(self.events_path, {"events": events[-500:]})

    def append_event(self, event_type: str, message: str = "", data: Optional[Dict[str, Any]] = None, session_id: str = "") -> Dict[str, Any]:
        with self._lock:
            events = self.load_events()
            event_id = (events[-1]["id"] + 1) if events and isinstance(events[-1].get("id"), int) else 1
            event = {
                "id": event_id,
                "session_id": session_id,
                "type": event_type,
                "message": message or "",
                "data": data or {},
                "time": self.now_text(),
                "timestamp": time.time(),
            }
            events.append(event)
            self.save_events(events)
            return event

    def events_after(self, after_id: int = 0, session_id: str = "") -> List[Dict[str, Any]]:
        events = self.load_events()
        result = []
        for event in events:
            if event.get("id", 0) <= after_id:
                continue
            if session_id and event.get("session_id") not in ("", session_id):
                continue
            result.append(event)
        return result
