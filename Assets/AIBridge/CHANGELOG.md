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
- Added dangerous-source checks and a pure C# environment wizard.
- Declared Unity 2018.4 minimum compatibility.
