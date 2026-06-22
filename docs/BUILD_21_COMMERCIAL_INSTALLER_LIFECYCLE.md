# Build 21: Commercial Installer and Service Lifecycle

Build 21 moves ThreadlineAI from a developer-run alpha toward commercial Windows software. The goal is simple: a normal user should be able to install it, launch it from the Start menu, have the local service recover, connect the browser extension, export diagnostics, and remove local data without understanding ports or developer commands.

## What changed

### Commercial packaging

- Added `eng/package-commercial.ps1` to publish the Windows sidecar, local service, and browser extension into a commercial staging folder.
- Added `installer/wix/ThreadlineAI.wxs` as the first MSI definition.
- Added signing hooks through `signtool.exe` using either `THREADLINE_SIGN_CERT_SHA1` or `THREADLINE_SIGN_PFX` / `THREADLINE_SIGN_PFX_PASSWORD`.
- Added a `-RequireSigning` switch so release packaging can fail closed when signing credentials are missing.

### Windows service lifecycle

- The local service now calls `UseWindowsService`, so it can run correctly under the Windows Service Control Manager.
- Added `eng/install-service.ps1` to install `ThreadlineAIService`, set delayed auto-start, configure service description, and apply crash recovery.
- Added `eng/uninstall-service.ps1` to stop and remove the service cleanly.
- The packaged service runs on `http://127.0.0.1:5057` by default.

### Version and diagnostics endpoints

The service now exposes authenticated lifecycle endpoints:

- `GET /version`
- `GET /diagnostics/manifest`
- `POST /diagnostics/export`
- `GET /local-data/clear-plan`
- `POST /local-data/clear`

The public `/health` response now includes product/service version metadata and the expected browser-extension version.

### Diagnostics package

`POST /diagnostics/export` creates a local zip package with:

- `manifest.json`
- `local-data-clear-plan.json`
- `service-health.txt`
- recent files from the local logs folder, if present

The manifest intentionally reports whether the local API token exists, but never includes the token value or provider secrets.

### Clear local data

Build 21 adds two clear-data paths:

1. `POST /local-data/clear`, guarded by the exact confirmation phrase `CLEAR THREADLINE LOCAL DATA`.
2. `eng/clear-local-data.ps1`, which stops the service first and verifies database, token, secret store, logs, diagnostics, settings, first-run state, and auto-start entry removal.

The script is the safer commercial support path because it can stop the service before deleting SQLite files.

### First-run setup wizard

The Windows sidecar now shows a first-run setup prompt that explains:

- local service state
- version/API compatibility
- browser extension setup
- local token location
- privacy/local data behavior
- where to find service and diagnostics tools

Completion is stored at `%LOCALAPPDATA%\ThreadlineAI\first-run-complete.json`.

### Browser extension guidance

The first-run setup explains that the extension should be installed from the packaged `browser-extension` folder and paired using the local token from `%LOCALAPPDATA%\ThreadlineAI\service-token.txt`.

### Automated security coverage

A new `tests/Threadline.Service.Tests` project covers:

- local API auth bypass attempts
- malformed/missing token rejection
- remote/non-loopback rejection even with a valid token
- browser-extension token generation and reuse
- retention fallback edge case
- diagnostics manifest redaction of token material
- clear-local-data confirmation and removal verification

`eng/test.ps1` now runs the new service security tests.

## Packaging commands

Stage a commercial package:

```powershell
./eng/package-commercial.ps1 -Version 21.0.0
```

Require a signed MSI during release packaging:

```powershell
$env:THREADLINE_SIGN_CERT_SHA1 = '<certificate thumbprint>'
./eng/package-commercial.ps1 -Version 21.0.0 -RequireSigning
```

Install the service from a staged or installed root:

```powershell
./eng/install-service.ps1 -InstallRoot 'C:\Program Files\ThreadlineAI' -Start
```

Export diagnostics:

```powershell
./eng/export-diagnostics.ps1
```

Clear local data with verification:

```powershell
./eng/clear-local-data.ps1
```

## Still not complete

This build lands the commercial lifecycle foundation. It does not yet finish:

- marketplace browser extension publishing
- enterprise deployment / MDM policy
- full automatic update feed
- SOC 2 or third-party penetration test
- cloud diagnostics upload
- polished installer UI screens

Those are later commercial hardening passes. Build 21 establishes the install, service, diagnostics, clear-data, versioning, and security-test base that those passes need.
