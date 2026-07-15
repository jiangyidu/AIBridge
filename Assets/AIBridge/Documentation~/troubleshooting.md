# AI Bridge Troubleshooting Guide

This document lists common issues, setup failures, and their corresponding resolutions.

---

## 1. Python Environment Check Failures
**Issue**: The Setup Wizard reports that Python or pip cannot be found.
* **Resolution**:
  1. Ensure Python 3.8+ is installed on your operating system.
  2. During Python installation, make sure to check the option **"Add Python to PATH"** (on Windows).
  3. Re-run the Setup Wizard in Unity (`AIBridge -> Environment Setup Wizard`). If it still fails, you can manually enter the absolute path to your `python.exe` in the wizard text field.

---

## 2. API Key Issues and Storage
**Issue**: Unauthorized errors or incorrect API Key warnings.
* **Resolution**:
  * Open `AIBridge -> AI Assistant`, click the **配置 (Configuration)** panel, and double-check your API key and provider endpoint.
  * *Note*: Keys are stored in `EditorPrefs`. Make sure you don't share your project settings or preferences files publicly if they contain sensitive keys.

---

## 3. Dynamic Compilation Failures
**Issue**: The LLM Agent tries to generate code, but Unity logs compilation errors, causing the agent loop to freeze or retry indefinitely.
* **Resolution**:
  * Check the **Unity Console** panel to see the exact compilation error.
  * The Python backend automatically attempts to rollback bad generated code using `RollbackPendingGeneratedSource`. However, if compilation is broken by manual changes, fix the syntax errors manually so Unity can re-compile.
  * Look into `Assets/Editor/AITempCommands.cs` or scripts under `Assets/Scripts/AITemp/` to verify what the agent wrote.

---

## 4. Forbidden Code Fragments (Security Exception)
**Issue**: The agent command is blocked and raises a security validation warning.
* **Resolution**:
  * AI Bridge restricts code execution to prevent destructive commands (such as deleting operating system files, opening external processes, or scanning user directories).
  * If the LLM generates code containing keywords like `Process.Start`, `File.Delete`, or namespace imports outside Unity standard packages, the compiler watcher blocks it. Adjust your prompt to ensure the AI does not attempt system-level operations.
