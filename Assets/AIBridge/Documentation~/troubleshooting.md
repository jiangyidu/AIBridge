# Troubleshooting

## Agent did not continue after compilation

Check Unity Console first, then inspect `Library/AIBridge/agent-state.json` and `compile-state.json`. `FailedConflict` means another operation changed the source during the transaction; compare it with the backup manually and do not force an overwrite.

## Generated source failed to compile

The transaction preserves the original errors, restores the prior source, and waits for recovery compilation. Do not delete the transaction file while rollback is active. `FailedRollback` means the original project still fails, the backup is unavailable, or recovery timed out.

## Request was interrupted by script reload

This is expected. A request object cannot survive an AppDomain rebuild. The host retries the same logical decision up to three times, then stops rather than loop forever.

## Tool returned `Uncertain`

The reload occurred across an ordinary tool's execution/commit boundary. The tool is not replayed. Inspect current scene or asset state before compensating.

## Generated code was rejected or not executed

Prefer fixed tools and remove process, network, native, reflection-loading, filesystem, initialization-hook, static-constructor, and persistent editor-event behavior. Successful compilation does not execute code. Review the source before enabling the generated-code execution setting.

Only clear `Library/AIBridge` after the Agent is stopped and no compile/rollback transaction is active.
