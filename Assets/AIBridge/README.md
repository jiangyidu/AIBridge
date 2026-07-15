# AI Bridge 2.0

AI Bridge is a pure C#, Editor-only Unity Agent compatible with Unity 2018.4+. It calls OpenAI-compatible, Claude, or user-provided Ollama endpoints through `UnityWebRequest` and invokes Unity tools in-process.

No Python runtime, package installer, proxy process, listening port, or bundled executable is required. The package is suitable for classic `.unitypackage` distribution.

## Highlights

- `PureCSharpAgent` is an `EditorApplication.update`-driven persistent state machine, not one long-lived task or thread.
- Session and compilation intent are saved under `Library/AIBridge` before side effects.
- Interrupted LLM requests use bounded retries for the same logical decision.
- Non-compilation tools are not replayed when their commit status is uncertain after a Domain Reload.
- Generated source uses isolated files and a two-phase compile transaction with hashes, backups, full-cycle error collection, rollback compilation, and post-reload target validation.
- Compilation and execution are separate. Automatic execution of AI-generated code is off by default.
- API keys are not written to the durable Agent state.

## Install

1. Import the `.unitypackage`, or copy `Assets/AIBridge` into a Unity 2018.4+ project.
2. Wait for script compilation.
3. Run `AIBridge > Environment Setup Wizard`.
4. Open `AIBridge > AI Assistant` and configure the model endpoint and API key.

Ollama mode assumes the user has chosen to run Ollama as the model endpoint; it is not an internal AI Bridge dependency.

## Domain Reload behavior

An AppDomain rebuild destroys static fields, window instances, tasks, callbacks, and in-flight request objects. AI Bridge persists phases such as `WaitingLlm`, `ExecutingPreparedTool`, and `WaitingCompile`, then restores hooks from `[InitializeOnLoad]`.

Generated-code compilation only succeeds after a new domain token is observed and the expected type or method is loaded. Failed source is restored before a second compilation/reload cycle completes.

## Safety boundary

Generated source is rejected when it contains process, network, native, dynamic assembly, raw filesystem, editor initialization hook, persistent event, or destructive asset operations. Identifiers, target declarations, and braces are also checked.

This is risk reduction, not a complete C# sandbox. Review generated files before enabling automatic execution, use version control, and keep custom `[AgentCommand]` methods minimal, undoable, idempotent, and parameter-validated.

## Runtime state

`Library/AIBridge` contains session state, compilation transactions, chat history, and temporary backups. It must not be committed or exported.

See `Documentation~/architecture.md` and `Documentation~/troubleshooting.md` for details.
