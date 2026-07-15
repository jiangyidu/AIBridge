# -*- coding: utf-8 -*-
"""Python-side AI Agent package.

All LLM interaction, prompt construction, tool orchestration and Agent loop logic
lives in this package. The engine-side (Unity / AutoCAD / Tekla / ...) exposes
executable capabilities through its local AgentBridge HTTP endpoint.

2026-06-16 refactor: Engine-agnostic architecture.
  - engine_client.py  : base class for all engine clients (HTTP protocol)
  - unity_client.py   : Unity-specific engine client
  - prompts_base.py   : engine-agnostic prompt rules
  - prompts_unity.py  : Unity-specific prompt context
  - prompts.py        : prompt composition entry point
"""

__all__ = [
    "agent_core",
    "engine_client",
    "history_store",
    "prompts",
    "prompts_base",
    "prompts_unity",
    "providers",
    "service",
    "unity_client",
]
