# -*- coding: utf-8 -*-
"""Starts the Python-side Unity AI Agent HTTP service."""

import os
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", line_buffering=True)
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", line_buffering=True)

from python_agent.service import main

if __name__ == "__main__":
    main()
