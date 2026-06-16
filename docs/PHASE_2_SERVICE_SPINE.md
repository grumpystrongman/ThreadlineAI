# Phase 2 — Core domain and local service spine

Phase 2 makes ThreadlineAI durable enough to build real adapters and UI on top of it.

## Completed in this phase

- Added stabilized domain models for:
  - capture modes
  - provider connections
  - session artifacts
  - audit events
  - context previews
- Added `ContextPreviewBuilder` so preview and storage are separate concepts.
- Added provider connection repository contracts.
- Added audit repository contracts.
- Added SQLite storage using `Microsoft.Data.Sqlite`.
- Added service startup initialization for the SQLite schema.
- Replaced default service storage with SQLite instead of in-memory storage.
- Added endpoints for:
  - active session lookup
  - context preview
  - context storage
  - summaries
  - prompt composition
  - provider connection records
  - audit events
- Strengthened context ingestion so blocked context is rejected and approval-required context must be explicitly approved.
- Added tests for context preview, provider connections, SQLite sessions, summaries, providers, and audit records.

## Explicitly deferred

- Local API authentication/token enforcement.
- Provider secret encryption implementation.
- Real LLM provider dispatch from the service.
- SQLite migrations beyond initial schema creation.
- Windows shell wiring to the service.
- Browser native messaging host implementation.

## Exit criteria

Phase 2 is complete when the local service can persist sessions, context, summaries, provider connection records, and audit events in SQLite; can preview context before storage; and has tests around those behaviors.
