# Build 23: Testing and Release Confidence

This is the build where ThreadlineAI stops hoping and starts proving. Build 23 adds automated tests, smoke validation, and release validation gates around the service, provider path, browser extension bridge, session bootstrap, geometry state, SQLite storage, context classification, UI Automation fakes, registered artifact actions, and the local API contract.

## Automated coverage added

| Area | Coverage |
| --- | --- |
| Capability/action registry | Unique action ids, required capability coverage, case-insensitive capability replacement, destructive-action guardrails. |
| Provider success/failure | OpenAI-compatible chat-completions success, OpenAI responses success, HTTP failure path, provider probe pass/fail audit recording. |
| Browser extension reachable/unreachable | Doctor warning when missing, pass when registered/fresh/compatible, degraded when stale. |
| Session bootstrap | Registered `work.resume` action creates a Work Thread when none exists. |
| Geometry save/restore | JSON sidecar geometry save/restore and minimum-size clamping. |
| SQLite writable/migration | Idempotent initialization, writable audit probe, expected tables and indexes. |
| Context source classification | Browser, PowerShell, terminal, screenshot, selected text, active window, and UI Automation metadata classification. |
| UI Automation fake-window | Deterministic fake UIA window snapshot and context event content. |
| Artifact action no-crash | Summary creation, copy, export, and regenerate paths succeed and preserve artifact history. |
| Local service API contract | `/health`, `/doctor`, `/capabilities`, `/actions`, session create, context preview/store, and prompt composition contracts. |

## Commands

Run the standard build and automated tests:

```powershell
./eng/build.ps1
./eng/test.ps1
```

Run the Build 23 service smoke test after starting the local service:

```powershell
dotnet run --project src/Threadline.Service/Threadline.Service.csproj --urls "http://localhost:5057"
./eng/smoke-build23.ps1 -BaseUrl http://localhost:5057
```

If local API token protection is enabled, pass the token explicitly:

```powershell
./eng/smoke-build23.ps1 -BaseUrl http://localhost:5057 -LocalAccess '<local-token>'
```

Run full release validation:

```powershell
./eng/release-validate.ps1
```

On a non-Windows CI host or a workstation without WinUI build tools, run the non-Windows validation slice:

```powershell
./eng/release-validate.ps1 -SkipWindows
```

On a machine without Node/npm extension tooling, skip the browser build only when you are intentionally validating service/core changes:

```powershell
./eng/release-validate.ps1 -SkipBrowserExtension
```

## Manual QA checklist

### Local service

- [ ] Start the service with `dotnet run --project src/Threadline.Service/Threadline.Service.csproj --urls "http://localhost:5057"`.
- [ ] Confirm `GET /health` returns `status = ok` and the expected API compatibility.
- [ ] Confirm `GET /doctor` includes checks for service running, SQLite writable, provider configured, provider test, active session, active Work Thread, browser extension reachable, current context source, last provider error, and sidecar geometry state.
- [ ] Confirm `GET /capabilities` includes provider, active-window context, Work Thread memory, Work Artifact, and browser-extension bridge capabilities.
- [ ] Confirm `GET /actions` includes provider test, resume work, clear context, clear conversation, clear memory, and all artifact actions.

### Provider path

- [ ] Configure a provider in the sidecar Settings flyout or through `/providers`.
- [ ] Run provider test from the Windows Tools panel or `POST /providers/{providerName}/test`.
- [ ] Confirm Doctor reports the provider test as pass when credentials/base URL/model are valid.
- [ ] Intentionally break the provider base URL or credential and confirm Doctor reports provider failure without crashing the service.

### Browser extension bridge

- [ ] Load or reload the Chrome/Edge extension from `adapters/browser-extension/dist`.
- [ ] Register the extension and send a heartbeat.
- [ ] Confirm Doctor moves `browser-extension.reachable` from warning to pass.
- [ ] Send page context and selected text from the extension.
- [ ] Confirm Doctor identifies browser-provided page context instead of title-only browser window metadata.

### Session and context

- [ ] Start a new session from the Windows sidecar.
- [ ] Use Follow/Lock against a normal app window.
- [ ] Preview context before storing it.
- [ ] Store approved context and confirm prompt composition includes the stored context.
- [ ] Try a blocked/sensitive app or password window and confirm capture is blocked or requires approval.

### Geometry and sidecar behavior

- [ ] Move and resize the sidecar to a normal attached position.
- [ ] Restart the sidecar and confirm it restores a usable position.
- [ ] Move the target app between monitors and confirm placement fallback does not crash the sidecar.
- [ ] Force an invalid/small geometry state if possible and confirm the sidecar clamps or resets rather than disappearing.

### UI Automation and active-window context

- [ ] Open Notepad or another simple desktop app with visible text.
- [ ] Use the sidecar native context preview.
- [ ] Confirm captured context identifies the UI Automation/native provider and includes visible text.
- [ ] Confirm a browser title-only active window tells the user to use the browser extension for deeper page text.

### Artifact actions

- [ ] Run Summary, Handoff, Decisions, Risks, and Next Actions from the Tools/action surface.
- [ ] Confirm artifacts save against the active Work Thread.
- [ ] Copy/export an artifact.
- [ ] Regenerate an artifact and confirm history is retained.
- [ ] Run a destructive action only after intentional approval and confirm it does not delete unrelated state.

### Release validation

- [ ] Run `./eng/release-validate.ps1` on a Windows release machine.
- [ ] Confirm `artifacts/release-validation/release-validation.json` is produced.
- [ ] Confirm service publish output includes `Threadline.Service.dll`.
- [ ] Confirm browser extension `dist` exists.
- [ ] Confirm Windows companion Release build completes on a workstation with Visual Studio/Windows App SDK build tools.
- [ ] Run `./eng/smoke-build23.ps1` against the built service.

## Release confidence rule

A Build 23 release candidate should not be tagged unless all required automated tests pass, the Build 23 smoke script passes against a running service, the release validation script produces a manifest, and the manual checklist is completed for any surface that changed.
