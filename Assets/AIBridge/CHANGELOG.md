# Changelog

## 2.0.0

- Replaced the legacy Python service, package installation, port discovery, and proxy process with an Editor-only pure C# Agent.
- Added durable Domain Reload recovery, bounded LLM retries, and non-replay handling for uncertain ordinary tools.
- Added transactional generated-source compilation with backups, hashes, full-cycle errors, rollback compilation, and post-reload target validation.
- Separated compilation from execution and disabled automatic execution of generated code by default.
- Added dangerous-source checks and a pure C# environment wizard.
- Declared Unity 2018.4 minimum compatibility.
