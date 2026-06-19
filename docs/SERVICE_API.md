# Local service API

Threadline.Service is the local context broker. It is local-first, pausable, and preview-driven.

## Health

```http
GET /health
```

Returns service status, storage backend, whether protected local access is enabled, and configured context-size limits. Health remains open so scripts can tell whether the broker is alive.

## Protected local access

Development mode is open by default. Protected local access turns on automatically when a local access value is configured through `THREADLINE_API_TOKEN` or `Threadline:ApiToken`. When enabled, service callers must include the configured value with their local requests.

## Sessions

```http
POST /sessions
GET /sessions/active
GET /sessions/{sessionId}/events/recent?take=20
POST /sessions/{sessionId}/summaries
```

Session names are required and capped by `Threadline:MaxSessionNameCharacters`.

## Context preview

```http
POST /sessions/{sessionId}/events/preview
```

Preview applies capture policy and redaction without storing the event. This is the endpoint the UI should call before sending context to storage or a provider.

## Context storage

```http
POST /sessions/{sessionId}/events
```

Stores an approved context event. If the capture policy blocks the event, the service rejects it. If the event requires explicit approval, `userApproved` must be `true`.

Context content is required and capped by `Threadline:MaxContextCharacters`, which defaults to 200,000 characters.

## Prompt composition

```http
POST /sessions/{sessionId}/prompt
```

Composes structured LLM messages using recent approved session events and the latest summary.

## Ask response path

```http
POST /sessions/{sessionId}/ask
```

Build 11.7 exposes this as the service-side real answer path. The request mirrors prompt composition so the service resolves the same approved context, builds the same structured messages, resolves the active session provider, calls the configured OpenAI-compatible provider, and returns the assistant answer.

Request body:

```json
{
  "question": "What should I do next?",
  "currentWindow": "Optional resolved current-window context",
  "takeRecentEvents": 20
}
```

Expected response body:

```json
{
  "answer": "Provider response text",
  "messages": [
    { "role": "system", "content": "..." },
    { "role": "user", "content": "..." }
  ],
  "providerName": "OpenAI",
  "model": "gpt-4.1-mini",
  "durationMs": 742
}
```

The Windows sidecar appends the user question and assistant answer as separate transcript messages. Provider/configuration failures return a non-success status with a useful error body so the UI can show a readable failure instead of crashing. Provider calls are audit-logged with metadata such as provider, model, duration, message count, and answer length; secrets and prompt content are not written to audit metadata.

## Providers

```http
GET /providers
GET /providers/{providerName}
POST /providers
POST /providers/{providerName}/credential
```

Provider records store credential references, not raw secrets. Actual secret material must be stored in Windows Credential Manager, DPAPI-protected storage, or an enterprise vault in later phases.

## Adapters

```http
GET /adapters
POST /adapters
POST /adapters/{adapterId}/heartbeat
```

Adapters identify the local component calling the service, such as the Windows shell, browser extension, PowerShell adapter, terminal adapter, or test harness.

Adapter registration is in-memory in Phase 3. Durable adapter identity and secure adapter secrets are deferred until the credential/security phase.

## Audit

```http
GET /audit/recent?sessionId={sessionId}&take=50
```

Returns recent audit events. Audit records must not include raw captured content.

## Smoke test

With the service running:

```powershell
./eng/smoke.ps1 -BaseUrl http://localhost:5057
```

With protected local access enabled:

```powershell
./eng/smoke.ps1 -BaseUrl http://localhost:5057 -LocalAccess $env:THREADLINE_API_TOKEN
```
