# Build 11.7 - Real Ask Pipeline

Build 11.7 closes the gap between the Windows sidecar Ask button and the local service provider execution path.

## Goal

A Windows sidecar Ask request should be able to move through the full local pipeline:

1. Validate the session and question.
2. Resolve recent approved session context and the latest summary.
3. Compose Threadline LLM messages through `PromptComposer`.
4. Resolve the active session provider from stored provider connections.
5. Resolve the provider credential through the protected secret service when required.
6. Call the OpenAI-compatible provider implementation.
7. Return the assistant answer to the Windows transcript.
8. Audit provider call start/completion/failure without storing prompt content or secrets.

## Added

- `ThreadlineAskService`
  - Owns service-side Ask orchestration.
  - Uses `PromptComposer` for consistency with `/sessions/{sessionId}/prompt`.
  - Resolves provider configuration from `ProviderConnectionService`.
  - Resolves credentials through `SecretService`.
  - Calls `OpenAiCompatibleProvider`.
  - Records safe audit metadata.

- `POST /sessions/{sessionId}/ask`
  - Request body mirrors `ComposePromptRequest`.
  - Returns `AskResponse` with `answer`, composed `messages`, `providerName`, `model`, and `durationMs`.
  - Returns a readable non-success response for missing or incomplete provider configuration.

- Provider execution polish
  - Local/OpenAI-compatible providers can be used without an API key when configured with `AuthType.None` or `AuthType.LocalEndpoint`.
  - Bearer authorization is only attached when an API key is present.

## Not yet included

Streaming responses are intentionally out of scope for this build. That belongs in a later streaming/transcript refinement phase.

Tool calling, action execution from model output, and provider-specific adapters beyond the OpenAI-compatible path are also deferred.

## Completion check

Build 11.7 is complete when:

- The service exposes `/sessions/{sessionId}/ask`.
- A configured active provider can return a real answer.
- The Windows client can display that answer through its existing Ask path.
- Missing provider configuration returns a readable error instead of a missing endpoint fallback.
- Provider calls produce safe audit metadata without prompt text, captured context, credentials, or secret values.
