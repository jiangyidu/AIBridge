# -*- coding: utf-8 -*-
"""Unity-specific engine client.

Inherits from EngineClient and overrides only the Unity-specific behavior:
  - Agent URL resolution (port file + hash port with Unity path convention)
  - Tools schema fallback path (Assets/Editor/AgentTools.json)
  - Compile tool names (compile_temp_method, compile_script)

All HTTP protocol logic (ping, call_tool, compile polling) lives in the
engine-agnostic base class EngineClient.

2026-06-16 refactor: Extracted base class to engine_client.py.
Previous behavior is preserved 100% — only the code structure changed.
"""

import os
from typing import Any, List

from .engine_client import (
    EngineClient,
    normalize_csharp_source,
    project_hash_port,
    get_service_port,
    get_service_url,
)

# ── Re-export for backward compatibility ──────────────────────────────────────
# Modules that were importing these from unity_client will continue to work.
normalize_generated_source = normalize_csharp_source
__all__ = [
    "UnityClient",
    "normalize_generated_source",
    "project_hash_port",
    "get_agent_url",
    "get_service_port",
    "get_service_url",
]


def get_agent_url(project_path: str) -> str:
    """Resolve the Unity AgentBridge HTTP URL.

    Reads AgentController/agent_port.txt if available, otherwise falls back
    to the deterministic hash port.
    """
    port_file = os.path.join(project_path, "AgentController", "agent_port.txt")
    if os.path.exists(port_file):
        try:
            with open(port_file, "r", encoding="utf-8") as f:
                port = int(f.read().strip())
                if port > 0:
                    return f"http://127.0.0.1:{port}/agent"
        except Exception:
            pass
    return f"http://127.0.0.1:{project_hash_port(project_path)}/agent"


class UnityClient(EngineClient):
    """Unity-specific engine client.

    Inherits all HTTP protocol logic from EngineClient.
    Only overrides Unity-specific agent URL resolution, tool schema fallback,
    and compile tool names.
    """

    engine_name = "Unity"

    def _resolve_agent_url(self) -> str:
        return get_agent_url(self.project_path)

    def _tools_schema_fallback_paths(self) -> List[str]:
        return [
            os.path.join(self.project_path, "Assets", "AIBridge", "Editor", "AgentTools.json"),
            os.path.join(self.project_path, "Assets", "Editor", "AgentTools.json"),
            os.path.join(self.project_path, "Editor", "AgentTools.json"),
        ]

    def _compile_tool_names(self) -> set:
        return {"compile_temp_method", "compile_script"}

    def _normalize_source(self, code: Any) -> Any:
        return normalize_csharp_source(code)

    # ── Backward-compatible aliases ───────────────────────────────────────────

    def wait_for_unity(self, timeout: float = 8.0, interval: float = 0.5) -> bool:
        """Backward-compatible alias for wait_for_engine()."""
        return self.wait_for_engine(timeout=timeout, interval=interval)
