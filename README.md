# ThreadlineAI

ThreadlineAI is a Windows-first, session-aware AI companion framework. It is designed to let a user start a named work session, move across browser, terminal, and desktop applications, and ask a chosen LLM about the approved context of that session.

The project is provider-agnostic. OpenAI, Anthropic Claude, Gemini, DeepSeek, OpenRouter, and local OpenAI-compatible runtimes should sit behind the same provider interface.

## Current status

ThreadlineAI is in early alpha engineering.

- Phase 0/1: scaffold and engineering foundation are complete.
- Phase 2: core domain and local service spine are complete.
- Phase 3: local service/context broker hardening is complete enough for adapter and UI wiring.
- Phase 4: privacy, redaction, consent, and privacy-safe audit metadata are in place.
- Phase 5: secure local provider credential storage is in place.
- Phase 6: window attachment and action proposal service APIs are in place.
- Phase 7: first Windows companion UI is in place for service connection, session start/use, foreground-window attachment, preview/store context, prompt composition, and action proposal tracking.
- Phase 8: browser extension bridge is in place for user-triggered page/selection capture into the active Threadline session through the local service.
- Phase 9: PowerShell terminal adapter is in place for explicit terminal context capture, command-output capture, and command action tracking.
- Build 11.5: Windows Ask routes to the provider-response contract and appends assistant answers into a stable chat transcript when the local service exposes `/sessions/{sessionId}/ask`; older service builds fall back to prompt composition with a readable transcript message.
- Build 11.7: the local service now exposes `/sessions/{sessionId}/ask`, resolves the active configured provider, calls the OpenAI-compatible provider path, returns answer metadata, and audit-logs provider call start/completion/failure without storing secrets or prompt content.
- Build 11.8: the Windows sidecar now has a confidence-based deep active-app resolver with process intelligence, provider/UIA/file/screenshot pipeline ordering, a Current Context panel, and hidden diagnostics for selected targets.
- Build 11.9: the sidecar now surfaces a visible context status pill, carries that status into Ask timeline/transcript messages, and resets bound transcript/context state safely when starting a new chat or clearing context.
- Build 11.9.1: missing-Ask-endpoint fallback now reports the local resolved context visibility instead of only showing a service plumbing message.
- Build 11.10: the sidecar moves Current Context above the conversation, frees the left column for Open Apps and Tabs, defaults the provider picker to OpenAI, and falls back to local visibility when Ask provider calls fail.

## What is in this scaffold

- Core domain model for sessions, context events, capture rules, prompt composition, privacy rules, provider abstraction, provider connections, artifacts, audit events, context preview, adapter registration, secure secret references, window attachment, and window actions.
- Infrastructure for SQLite persistence, in-memory adapter registration, in-memory testing, secure local secret storage, in-memory window attachment runtime state, and OpenAI-compatible HTTP providers.
- Local service API for adapters, the Windows shell, and provider-backed Ask execution.
- Windows companion UI scaffold wired to the local service for session, window attachment, context preview/storage, Ask response routing, prompt composition fallback, confidence-scored context resolution, visible context status, diagnostics, and action proposal flows.
- Browser extension bridge for Chrome/Edge user-triggered page and selected-text capture into the active Threadline session.
- PowerShell terminal adapter module for user-triggered terminal notes, transcript excerpts, command-output capture, and action tracking.
- Privacy/security design notes and implementation roadmap.

## Target MVP

The first useful MVP should let a Windows user:

1. Install ThreadlineAI.
2. Configure an LLM provider.
3. Start or resume a named session.
4. Attach Threadline to the current Windows foreground window.
5. Capture approved context from Chrome/Edge selected text, active window metadata, PowerShell transcript output, UI Automation text, and file-backed document resolvers.
6. Preview the context that will be sent or stored.
7. Ask questions about the current window or full active session.
8. Approve safe actions such as inserting text, pasting generated content, or running supported app-specific actions.
9. Pause capture, block apps/domains, and export a session summary.

## Repository layout

```text
src/
  Threadline.Core/           Domain model, abstractions, prompt composition, privacy rules
  Threadline.Infrastructure/ Storage and provider implementations
  Threadline.Service/        Local HTTP API for adapters and app shell
  Threadline.Windows/        Windows companion shell/panel scaffold
adapters/
  browser-extension/         Chrome/Edge extension scaffold
  powershell/                PowerShell terminal adapter module
docs/
  Architecture, provider, privacy, roadmap, and service API notes
tests/
  Threadline.Core.Tests/
  Threadline.Infrastructure.Tests/
```

## Development prerequisites

- Windows 11
- .NET 8 SDK or newer
- Visual Studio 2022 with Windows App SDK / WinUI workload
- Node.js 20+ for the browser extension
- PowerShell 7+

## Build and test

```powershell
./eng/build.ps1
./eng/test.ps1
```

Build the Windows companion UI separately:

```powershell
./eng/build-windows.ps1
```

Build the browser extension:

```powershell
./eng/build-browser-extension.ps1
```

Run the PowerShell adapter smoke test after starting the local service:

```powershell
./eng/smoke-powershell-adapter.ps1 -BaseUrl http://localhost:5057
```

Run the local service:

```powershell
dotnet run --project src/Threadline.Service/Threadline.Service.csproj --urls "http://localhost:5057"
```

Then run the local smoke test:

```powershell
./eng/smoke.ps1 -BaseUrl http://localhost:5057
```

See `docs/SERVICE_API.md` for local API details.

## Privacy-first defaults

ThreadlineAI should never behave like invisible spyware. Capture should be visible, pausable, previewable, and rule-driven. Sensitive applications, private browsing windows, credentials, and private records must be excluded or redacted before any provider call.

## License

Business Source License 1.1

Parameters:
- Licensor: Jeff Barnes
- Software: ThreadlineAI
- Change Date: January 1, 2030
- Change License: Apache License, Version 2.0
- Additional Use Grant: You may make non-commercial use of the Licensed Work. 
  Production use, commercial use, or embedding this code into any paid or enterprise 
  offering is strictly prohibited without a separate commercial license from the Licensor.

Licensed Work is all files in this repository.

Subject to the terms hereof, Licensor hereby grants you a non-exclusive, worldwide, 
non-transferable, non-sublicensable, royalty-free license to use, copy, and modify 
the Licensed Work solely for your non-commercial purposes. 

On the Change Date, or upon an earlier public announcement by Licensor, the Licensed 
Work converts automatically to the Change License.
