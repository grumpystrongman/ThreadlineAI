# Build 15: Agent Reliability Core

Build 15 adds the first reliability foundation for ThreadlineAI's agent workflow. The goal is not more UI surface area; it is a trustworthy service-side core that the Windows sidecar and future Hermes-style skill system can depend on.

## Threadline Doctor

The local service now exposes a Doctor report that returns a readiness state and structured checks for:

- Service running
- SQLite writable
- Provider configured
- Provider test
- Active session
- Active Work Thread
- Browser extension reachable
- Current context source
- Last provider error
- Sidecar geometry state

Readiness values are:

- `Ready`
- `NeedsSetup`
- `Degraded`

The main endpoint is:

```http
GET /doctor
```

The report includes checks, capabilities, and registered actions so the sidecar can explain what is working, what needs setup, and what is degraded.

## Capability Registry

Build 15 introduces typed capability metadata without overbuilding a full skill runtime yet:

- `ProviderCapability`
- `ContextCapability`
- `MemoryCapability`
- `ArtifactCapability`
- `BrowserExtensionCapability`

The registry is available through:

```http
GET /capabilities
```

This gives future skills a stable way to ask whether Threadline can use a provider, context source, memory, artifacts, or browser-extension bridge before attempting work.

## First Action Registry

The first action catalog defines the small set of operations the sidecar should treat as registered actions:

- Summary
- Handoff
- Decisions
- Risks
- Next actions
- Provider test
- Resume work
- Clear context

The service exposes action definitions through:

```http
GET /actions
```

The Windows sidecar also has a local `ThreadlineUiActionRegistry` so button handlers can route through registered action IDs instead of growing more scattered one-off handlers.

## Provider Test

Provider test runs through the service path and records an audit event so Doctor can report the last provider test as pass/fail instead of leaving provider health ambiguous.

```http
POST /providers/{providerName}/test
```

## Onboarding / Setup Readiness

Doctor provides guidance for first-run and degraded states, including:

- Provider setup required
- Chrome/Edge extension guidance
- Local service status
- Browser-title-only versus extension page-context guidance
- Session and Work Thread setup state

## Tests

Build 15 adds core tests for the reliability catalog and readiness contracts. These protect the first capability/action registry from accidental drift while keeping the implementation intentionally small.
