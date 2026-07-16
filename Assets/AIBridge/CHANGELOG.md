# Changelog

## 2.0.0

- Replaced the legacy Python service, package installation, port discovery, and proxy process with an Editor-only pure C# Agent.
- Added durable Domain Reload recovery, bounded LLM retries, and non-replay handling for uncertain ordinary tools.
- Fixed LitJson treating small JSON integers such as `0` as `Int32`, which prevented `Int64` state fields from loading and stopped the Agent after a successful compile-triggered Domain Reload.
- Corrupt state files are now preserved and retried at a low frequency with an explicit UI error instead of being reparsed and logged every editor frame.
- Authentication, billing, and invalid-request failures now stop immediately; only timeouts, 429, and 5xx failures use bounded retries.
- Added versioned AES API-key storage before session start so Domain Reload can recover credentials without ever sending undecryptable legacy ciphertext.
- Added transactional generated-source compilation with backups, hashes, full-cycle errors, rollback compilation, and post-reload target validation.
- Separated compilation from execution and disabled automatic execution of generated code by default.
- Added dangerous-source checks and an integrated pure C# environment check in the assistant window.
- Limited automatic Agent initialization to the main Editor process so Asset Import Workers cannot load sessions, scan commands, or advance compile transactions.
- Stale active sessions are now stopped after a five-minute interruption instead of replaying old operations against an unknown project state.
- Command execution now accepts only public static methods marked with `[AgentCommand]`; removed unused 1.x server/port/batch APIs, the test API, and the unrelated generic `GameUtility` command library.
- Cached and validated `AgentTools.json`, removed the obsolete model-facing compile polling tool, and made the host the single owner of compile transaction polling.
- Sanitized unsupported decorative symbols in rendered chat content and removed them from controls to prevent per-repaint missing-glyph warnings from flooding `Editor.log`.
- Added a persistent chat scroll lock toggle: unlocked conversations follow the latest message, while locked conversations preserve manual scrolling.
- Simplified the operation window to one contextual send/stop action, one command execution path, integrated environment validation, dynamic command categories, and protected controls while an Agent session is running.
- Removed an ineffective method-level CLS attribute from the bundled LitJson source to eliminate its package-originated `CS3021` warning.
- Removed the redundant standalone environment wizard; the main window owns the pure C# environment check, with Unity 2018.4 as the minimum supported version.
- Moved the window to `Tools/AIBridge/AI Assistant` and removed import-time creation of user files outside the package root.
- Made successful command-registry scans silent by default; define `AIBRIDGE_VERBOSE_LOGS` when diagnostic scan logs are required.
