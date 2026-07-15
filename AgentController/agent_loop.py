# -*- coding: utf-8 -*-
"""Compatibility entry point.

The old Unity window launched this file directly. The implementation now delegates
to python_agent.agent_core so all LLM / Agent orchestration stays in Python.
"""

import json
import os
import sys

# Ensure stdout is UTF-8 and line-buffered for Unity's Process output reader.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", line_buffering=True)
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", line_buffering=True)

from python_agent.agent_core import run_from_config_file


def main() -> int:
    config_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "agent_config.json")
    if len(sys.argv) > 1:
        config_path = sys.argv[1]
    result = run_from_config_file(config_path)
    print(json.dumps(result, ensure_ascii=False, indent=2), flush=True)
    return 0 if result.get("Success") else 1


if __name__ == "__main__":
    raise SystemExit(main())
