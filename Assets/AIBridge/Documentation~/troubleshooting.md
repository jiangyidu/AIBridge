# Troubleshooting

## Agent did not continue after compilation

Check Unity Console first, then inspect `Library/AIBridge/agent-state.json` and `compile-state.json`. `FailedConflict` means another operation changed the source during the transaction; compare it with the backup manually and do not force an overwrite.

## Generated source failed to compile

The transaction preserves the original errors, restores the prior source, and waits for recovery compilation. Do not delete the transaction file while rollback is active. `FailedRollback` means the original project still fails, the backup is unavailable, or recovery timed out.

## Request was interrupted by script reload

This is expected. A request object cannot survive an AppDomain rebuild. The host retries the same logical decision up to three times, then stops rather than loop forever.

## HTTP 401 / 403 authentication failure

Authentication failures are permanent for the current configuration, so the Agent stops immediately without retrying. Open the AI Assistant configuration, verify that the provider and endpoint match, remove the old value, and paste a valid key issued by that provider. Do not include an API URL, a `Bearer ` prefix, quotes, or a key from another provider.

Before a session starts, the key is stored in EditorPrefs using versioned AES encryption so it can be recovered after Domain Reload. Legacy ciphertext that cannot be decrypted safely is never sent and must be re-entered. The key is not written to the Agent state under `Library/AIBridge`.

HTTP 402 and invalid request/model errors also stop immediately. Only timeouts, 429 responses, and server-side 5xx failures use bounded backoff retries.

## Tool returned `Uncertain`

The reload occurred across an ordinary tool's execution/commit boundary. The tool is not replayed. Inspect current scene or asset state before compensating.

## Generated code was rejected or not executed

Prefer fixed tools and remove process, network, native, reflection-loading, filesystem, initialization-hook, static-constructor, and persistent editor-event behavior. Successful compilation does not execute code. Review the source before enabling the generated-code execution setting.

Only clear `Library/AIBridge` after the Agent is stopped and no compile/rollback transaction is active.
