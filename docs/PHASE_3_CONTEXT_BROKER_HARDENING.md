# Phase 3 — Local service and context broker hardening

Phase 3 turns the local service from a simple persistence API into a safer context broker that adapters can call.

## Completed in this phase

- Added optional protected local access for service endpoints.
- Kept `/health` open so local scripts can detect whether the service is alive.
- Added service options for:
  - local API access requirement
  - local API access value
  - max context size
  - max session name size
- Added centralized request validation for sessions, context events, prompts, summaries, providers, and adapters.
- Added adapter domain model:
  - adapter kind
  - permissions
  - registration identity
  - last-seen timestamp
- Added an in-memory adapter registry.
- Added endpoints for:
  - listing adapters
  - registering adapters
  - adapter heartbeat
- Added audit records for adapter registration and heartbeat.
- Split service contracts and endpoint mappings out of `Program.cs`.
- Added `eng/smoke.ps1` for repeatable local service validation.

## Explicitly deferred

- Durable adapter registry storage.
- Per-adapter secrets.
- Windows Credential Manager / DPAPI-backed secret storage.
- Full local caller identity binding.
- Browser native messaging host.
- Windows shell service client.

## Exit criteria

Phase 3 is complete when the local service can optionally require local access credentials, reject malformed requests, register local adapters, receive adapter heartbeat calls, and pass the smoke test from a developer workstation.
