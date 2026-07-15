# -*- coding: utf-8 -*-
"""Prompt construction for the Python-side Agent.

2026-06-16 refactor: Split into engine-agnostic base rules (prompts_base.py) and
engine-specific context modules (prompts_unity.py, prompts_autocad.py, etc.).

This file remains the single entry point — callers continue to use:
    from .prompts import build_system_prompt
"""

from .prompts_base import BASE_AGENT_RULES
from .prompts_unity import UNITY_CONTEXT, UNITY_ENGINE_NAME


# ── Engine prompt registry ────────────────────────────────────────────────────
# Maps engine names to their context strings.
# When a new engine adapter is created, register it here.
_ENGINE_CONTEXTS = {
    "unity": UNITY_CONTEXT,
    # Future:
    # "autocad": AUTOCAD_CONTEXT,
    # "tekla": TEKLA_CONTEXT,
    # "ue4": UE4_CONTEXT,
}

_ENGINE_DISPLAY_NAMES = {
    "unity": UNITY_ENGINE_NAME,
    # Future:
    # "autocad": "AutoCAD",
    # "tekla": "Tekla Structures",
    # "ue4": "Unreal Engine",
}


def build_system_prompt(config: dict) -> str:
    """Build the full system prompt for the Agent.

    Composes:
    1. Base agent rules (engine-agnostic)
    2. Engine-specific context (Unity / AutoCAD / Tekla / ...)
    3. Environment info (engine version, project path)
    4. User-provided extra instructions

    The engine is determined by config["engine"] (default: "unity").
    """
    engine = (config.get("engine") or "unity").lower()
    engine_display = _ENGINE_DISPLAY_NAMES.get(engine, engine.title())
    engine_context = _ENGINE_CONTEXTS.get(engine, "")

    # 1. Base rules with engine name injected
    base = BASE_AGENT_RULES.format(engine_name=engine_display)

    parts = [base]

    # 2. Engine-specific context
    if engine_context:
        parts.append(engine_context)

    # 3. Environment info
    engine_version = config.get("unity_version") or config.get("engine_version") or "unknown"
    parts.append(f"当前 {engine_display} 版本: {engine_version}")

    project_path = config.get("project_path") or ""
    if project_path:
        parts.append(f"当前项目路径: {project_path}")

    # 4. User extra instructions
    user_extra = (config.get("user_system_prompt") or "").strip()
    if user_extra:
        parts.append("用户额外指令：\n" + user_extra)

    return "\n\n".join(parts)
