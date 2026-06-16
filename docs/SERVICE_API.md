# Local service API

Threadline.Service is the local context broker. It is intentionally local-first and should be protected before production release.

## Health

```http
GET /health
```

Returns service status and storage backend.

## Sessions

```http
POST /sessions
```

```json
{
  "name": "Debug build issue",
  "provider": "OpenAI"
}
```

```http
GET /sessions/active
GET /sessions/{sessionId}/events/recent?take=20
POST /sessions/{sessionId}/summaries
```

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

## Prompt composition

```http
POST /sessions/{sessionId}/prompt
```

Composes structured LLM messages using recent approved session events and the latest summary.

## Providers

```http
GET /providers
GET /providers/{providerName}
POST /providers
```

Provider records store credential references, not raw secrets. Actual secret material must be stored in Windows Credential Manager, DPAPI-protected storage, or an enterprise vault in later phases.

## Audit

```http
GET /audit/recent?sessionId={sessionId}&take=50
```

Returns recent audit events. Audit records must not include raw captured content.
