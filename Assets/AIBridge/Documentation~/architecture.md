# Pure C# Architecture and Domain Reload Recovery

## Durable Agent loop

Unity recompilation rebuilds the AppDomain, so a task, thread, static field, editor window, or request object cannot own an end-to-end Agent run. `PureCSharpAgent` instead advances one persisted phase on each editor update:

```text
RequestingLlm -> WaitingLlm -> ExecutingTools
                                  |-> ExecutingPreparedTool
                                  `-> StartingCompile -> WaitingCompile -> restored domain
```

Intent is saved to `Library/AIBridge` before a side effect. `[InitializeOnLoad]` restores update and compilation callbacks in the next domain.

## LLM and ordinary tools

An interrupted request is retried for the same logical decision with a maximum of three attempts. Network retries do not consume extra Agent steps.

Before an ordinary tool runs, a `Prepared` execution record is saved. If a reload occurs before the result is committed, the host reports `Uncertain` instead of replaying the tool. The model must inspect current scene or asset state before compensating.

## Generated-source transaction

1. Validate that the path is under `Assets` and reject dangerous source.
2. Hash and back up the original file.
3. Persist `Prepared` before writing source.
4. Atomically write no-BOM UTF-8 source, persist `AwaitingCompilation`, then refresh assets.
5. Accumulate errors across the complete compilation cycle.
6. On success, wait for a new domain token and verify the target type/method.
7. On failure, preserve original errors, restore/delete the generated source, and wait for recovery compilation plus reload.
8. Stop automatic overwrite on any hash conflict.

Compilation never implicitly executes generated code.

## Cancellation and bounds

Cancellation stops Agent scheduling and network work, while an already-started compile transaction is allowed to validate or roll back safely. Agent steps, request attempts, and compile waits are bounded. Rollback source is never written while Unity is compiling.

Static source inspection is not a complete sandbox. Generated-code execution remains opt-in.
