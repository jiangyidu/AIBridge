# -*- coding: utf-8 -*-
"""Small local HTTP service for Python-side Agent sessions."""

import argparse
import json
import os
import threading
import time
import traceback
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any, Dict
from urllib.parse import parse_qs, urlparse

from .agent_core import AgentRunner
from .history_store import HistoryStore
from .engine_client import get_service_port


class AgentSession:
    def __init__(self, config: Dict[str, Any]):
        self.id = uuid.uuid4().hex
        self.config = config
        self.cancelled = False
        self.started_at = time.time()
        self.finished = False
        self.result: Dict[str, Any] = {}
        self.thread = threading.Thread(target=self._run, name=f"AgentSession-{self.id[:8]}")
        self.thread.daemon = True

    def start(self) -> None:
        self.thread.start()

    def cancel(self) -> None:
        self.cancelled = True

    def _run(self) -> None:
        runner = AgentRunner(self.config, session_id=self.id, cancel_flag=lambda: self.cancelled)
        self.result = runner.run()
        self.finished = True


class AgentServiceState:
    def __init__(self, project_path: str):
        self.project_path = os.path.abspath(project_path)
        self.history = HistoryStore(self.project_path)
        self.sessions: Dict[str, AgentSession] = {}
        self.lock = threading.RLock()
        self.default_config: Dict[str, Any] = {"project_path": self.project_path}

    def create_session(self, config: Dict[str, Any]) -> AgentSession:
        cfg = dict(self.default_config)
        cfg.update(config or {})
        cfg["project_path"] = cfg.get("project_path") or self.project_path
        cancel_existing = bool(cfg.get("cancel_existing", True))
        session = AgentSession(cfg)
        with self.lock:
            if cancel_existing:
                for old in self.sessions.values():
                    if not old.finished:
                        old.cancel()
            self.sessions[session.id] = session
        session.start()
        return session

    def cancel_all(self) -> int:
        count = 0
        with self.lock:
            for session in self.sessions.values():
                if not session.finished and not session.cancelled:
                    session.cancel()
                    count += 1
        return count

    def get_session(self, session_id: str):
        with self.lock:
            return self.sessions.get(session_id)


class AgentServiceHandler(BaseHTTPRequestHandler):
    state: AgentServiceState = None  # type: ignore

    def log_message(self, fmt, *args):
        return

    def _json_response(self, status: int, payload: Dict[str, Any]) -> None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _read_json(self) -> Dict[str, Any]:
        length = int(self.headers.get("Content-Length") or 0)
        if length <= 0:
            return {}
        raw = self.rfile.read(length).decode("utf-8")
        return json.loads(raw) if raw else {}

    def do_GET(self):
        try:
            parsed = urlparse(self.path)
            path = parsed.path
            if path == "/health":
                self._json_response(200, {"Success": True, "Message": "python-agent-service-ok", "project_path": self.state.project_path})
                return
            if path == "/config":
                self._json_response(200, {"Success": True, "Config": self.state.default_config})
                return
            if path.startswith("/sessions/"):
                parts = [p for p in path.split("/") if p]
                if len(parts) >= 3:
                    session_id = parts[1]
                    action = parts[2]
                    session = self.state.get_session(session_id)
                    if not session:
                        self._json_response(404, {"Success": False, "Message": "session not found"})
                        return
                    if action == "events":
                        query = parse_qs(parsed.query)
                        after = int((query.get("after") or ["0"])[0])
                        events = self.state.history.events_after(after, session_id=session_id)
                        self._json_response(200, {"Success": True, "Events": events, "Finished": session.finished, "Result": session.result})
                        return
                    if action == "messages":
                        self._json_response(200, {"Success": True, "Messages": self.state.history.load_messages(), "Finished": session.finished})
                        return
                    if action == "status":
                        self._json_response(200, {"Success": True, "Finished": session.finished, "Cancelled": session.cancelled, "Result": session.result})
                        return
            self._json_response(404, {"Success": False, "Message": "not found"})
        except Exception as exc:
            self._json_response(500, {"Success": False, "Message": str(exc), "Traceback": traceback.format_exc()})

    def do_POST(self):
        try:
            path = urlparse(self.path).path
            payload = self._read_json()
            if path == "/config":
                self.state.default_config.update(payload or {})
                self._json_response(200, {"Success": True, "Config": self.state.default_config})
                return
            if path == "/sessions/cancel_all":
                count = self.state.cancel_all()
                self._json_response(200, {"Success": True, "CancelledCount": count})
                return
            if path == "/sessions/start":
                session = self.state.create_session(payload or {})
                self._json_response(200, {"Success": True, "SessionId": session.id})
                return
            if path.startswith("/sessions/") and path.endswith("/cancel"):
                parts = [p for p in path.split("/") if p]
                session_id = parts[1] if len(parts) >= 3 else ""
                session = self.state.get_session(session_id)
                if not session:
                    self._json_response(404, {"Success": False, "Message": "session not found"})
                    return
                session.cancel()
                self._json_response(200, {"Success": True, "Message": "session cancellation requested"})
                return
            self._json_response(404, {"Success": False, "Message": "not found"})
        except Exception as exc:
            self._json_response(500, {"Success": False, "Message": str(exc), "Traceback": traceback.format_exc()})


def run_service(project_path: str, host: str = "127.0.0.1", port: int = None) -> None:
    project_path = os.path.abspath(project_path)
    port = port or get_service_port(project_path)
    AgentServiceHandler.state = AgentServiceState(project_path)
    server = ThreadingHTTPServer((host, port), AgentServiceHandler)
    print(f"[PythonAgentService] listening on http://{host}:{port} for project {project_path}", flush=True)
    server.serve_forever()


def main() -> None:
    parser = argparse.ArgumentParser(description="Python Unity AI Agent Service")
    parser.add_argument("--project-path", required=True)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=0)
    args = parser.parse_args()
    run_service(args.project_path, args.host, args.port or None)


if __name__ == "__main__":
    main()
