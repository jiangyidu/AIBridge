# Contributing

AI Bridge 2.x is a pure C#, Editor-only package with Unity 2018.4 as its minimum supported version.

- Do not add external processes, local listeners, installers, or bundled runtime dependencies.
- Guard APIs introduced after Unity 2018.4 and provide an older-version path.
- Durable Agent work must survive Domain Reload through serializable state under `Library/AIBridge`.
- Persist intent before Assets side effects and provide hashes, timeouts, conflict handling, and recovery.
- Use Undo for scene changes and never overwrite user assets silently.
- Keep generated-code compilation separate from execution; do not weaken the default-off execution gate.
- Never persist API keys or tokens in Agent state.

Validate compilation, successful generated-source reload, failed-source rollback, interrupted request recovery, ordinary-tool uncertainty, cancellation, and dangerous-source rejection.
