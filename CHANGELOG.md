# Changelog

## Unreleased

### Fixed
- Cancelling a generation now works at every stage of a chat turn, not only while the backend is streaming. `LLMEngine.CancelGeneration()` stops the preparation steps too (context plugins, memory handling, prompt building) instead of cancelling a backend call that might not exist yet, after which the turn used to start anyway.
- `LLMEngine.Status` is now `Busy` for the whole of a chat turn, preparation included. Before, it only became `Busy` after the plugin pre-pass, so a second send or reroll could start a parallel turn, and nested plugin queries made the status flicker `Busy` / `Ready` / `Busy`.
- Agent and plugin queries (`SimpleQuery`, `SimpleQueryStreaming`) now honor their `CancellationToken`, which used to be ignored. They are also cancelled when the chat turn they run in is cancelled. A disposed caller token can no longer leave the engine stuck.
- A cancelled `SimpleQuery` returns an empty string instead of the backend's error text.
- A refused `RerollLastMessage` no longer deletes the last message from the history.
- An exception thrown while preparing a turn (prompt building, plugins) could leave `Status` stuck on `Busy`. It is now reported as an error completion and the engine returns to `Ready`.
- `Connect()` no longer overwrites a `Busy` status while a turn is running.
- A throwing `OnStatusChanged` handler can no longer leave the engine half-transitioned.
- Cancelling no longer injects a fake "The response was manually interrupted or cancelled" token into the response (OpenAI and llama.cpp chat), and llama.cpp text completion no longer ends silently without a completion. Timeouts and real errors are still reported.
- Cancelling an OpenAI / llama.cpp chat completion no longer starts another tool-call or retry round.
- Backend chunks arriving after a cancel are dropped instead of leaking into the next response.

### Added
- `InferenceResult.WasCancelled`, and the `"cancelled"` finish reason, to tell the application that a response was abandoned. `Response` and `ThinkingContent` hold the partial text streamed so far.
- Completion contract: every chat turn that starts raises exactly one `OnInferenceCompleted`, whether it ended normally, with an error (`FinishReason == "error"`, details in `Response`), or was cancelled. If the backend stops without sending a final event, the engine finalizes the response with what was received.
- `CancellationToken` parameter on `ILLMServiceClient.GenerateText` and `GenerateTextStreaming` (default value, optional for callers). It is linked to each adapter's own abort mechanism so a cancel requested just before a call starts cannot be lost.
- "Cancelling a Generation" section in `Docs/LLMSYSTEM.md`.

### Changed
- `SendMessageToBot`, `AddBotMessage`, `ImpersonateUser` and `RerollLastMessage` now return `Task<bool>`: `true` if the turn ran, `false` if it could not start (engine busy or not connected). A refused call raises no event and leaves the chat history untouched. Callers that ignore the result keep compiling.
- The model slot (`LLMBusy`) is now claimed atomically, and only around the backend call. A turn that reaches its backend call while an agent query holds the slot waits for it (cancellable) instead of silently dropping the turn.
- `OnStatusChanged` is raised only when the status actually changes.
- The user message sent with `SendMessageToBot` is always logged to the chat history, even if the turn is cancelled or fails before reaching the model. The bot's response is never logged by the engine, as before.
- Streaming cancellation is quiet in all adapters: a cancelled call ends without raising tokens, the engine reports the cancellation. The LlamaSharp adapter no longer emits its own `"cancel"` finish reason.

### Notes for implementers of `ILLMServiceClient`
- Update the signatures of `GenerateText` / `GenerateTextStreaming` to accept the new `CancellationToken`.
- A cancelled streaming call should simply return, without raising error or completion tokens.
- KoboldCpp only received the token plumbing and was not tested.
