# Phase 4 — Privacy, redaction, and consent engine hardening

Phase 4 strengthens Threadline's privacy layer so the local broker can explain what happened to captured context before it is stored or sent to a model.

## Completed in this phase

- Expanded redaction coverage for:
  - named secrets
  - API keys
  - bearer-style tokens
  - JWT-style tokens
  - private-key blocks
  - connection strings
  - URL secret parameters
  - email addresses
  - phone numbers
  - social-security-number style identifiers
  - MRN / patient-id style markers
- Added structured redaction findings:
  - kind
  - label
  - match location
  - match length
- Added consent states for context preview:
  - not required
  - required
  - approved
  - blocked
  - stored
- Added rule-source metadata for capture rules:
  - default
  - user
  - organization
  - runtime
- Added privacy metadata to previews and stored context metadata.
- Added privacy-safe audit records for preview, blocking, redaction, and storage decisions.
- Added tests for expanded redaction and consent-state behavior.

## Design rule

Audit records must never store raw captured sensitive content. They may store counts, types, rule IDs, rule sources, consent state, source type, and context type.

## Still deferred

- Durable user/org policy storage.
- Policy UI.
- User-managed blocklist editor.
- DPAPI / Windows Credential Manager storage for local secrets.
- Per-adapter consent and trust scopes.
- Enterprise policy import/export.

## Exit criteria

Phase 4 is complete when previews identify redactions, assign consent state, report policy source metadata, store only redacted context, and write privacy-safe audit metadata.
