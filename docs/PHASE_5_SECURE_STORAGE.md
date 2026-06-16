# Phase 5 — Secure storage

Phase 5 introduces a local secret boundary so provider credentials are not stored directly in provider records, logs, or normal service responses.

## Completed in this phase

- Added secret-store abstractions in the core layer:
  - `ISecretStore`
  - `SecretDescriptor`
  - `SecretProtectionKind`
- Added a Windows local protected secret store:
  - stores encrypted secret envelopes under local app data
  - uses Windows current-user protected-data APIs
  - returns `secret://local/...` references
  - hashes on-disk filenames so secret names are not directly visible from file names
- Added an in-memory secret store for tests.
- Added `SecretService` for safe audit events around storing, resolving, and deleting local secrets.
- Added provider credential endpoint:
  - stores the raw credential in the secret store
  - saves provider records with only a credential reference
  - returns a safe descriptor, not the secret value
- Added secret descriptor and delete endpoints.
- Added smoke-test coverage for protected provider credential storage.
- Added tests proving secret service audit records do not include raw secret values.

## Design rule

Provider records must store credential references only. Actual secret values must only pass through the secret store and must never be returned by provider-list, provider-detail, audit, or descriptor endpoints.

## API shape

```http
POST /providers/{providerName}/credential
GET /secrets/{reference}
DELETE /secrets/{reference}
```

`GET /secrets/{reference}` returns metadata only. It does not return the stored secret value.

## Still deferred

- Windows Credential Manager integration.
- Secret rotation lifecycle.
- Provider credential validation against real provider APIs.
- Per-adapter secret access scopes.
- Enterprise vault adapters.
- Backup/export policy for encrypted local secret envelopes.

## Exit criteria

Phase 5 is complete when a provider credential can be stored locally through the secret store, referenced from a provider connection, described without exposing the value, deleted, audited safely, and validated through the service smoke test.
