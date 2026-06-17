# Phase 9 — PowerShell and Terminal adapter

Phase 9 adds a service-backed PowerShell adapter for explicit terminal context capture and action tracking.

## Completed in this phase

- Added `adapters/powershell/Threadline.PowerShell.psm1`.
- Added local service configuration helpers.
- Added active-session lookup and session creation helpers.
- Added adapter registration for PowerShell.
- Added explicit terminal context send support.
- Added transcript capture helpers.
- Added command-output capture support.
- Added command action proposal and completion support.
- Added smoke script.

## Smoke test

Start Threadline.Service first, then run:

```powershell
./eng/smoke-powershell-adapter.ps1 -BaseUrl http://localhost:5057
```

## Manual use pattern

1. Import the module from `adapters/powershell`.
2. Point it at the local Threadline service.
3. Start or reuse a Threadline session.
4. Send explicit terminal notes, transcript excerpts, or command output into the active session.
5. Propose command actions through the service action lifecycle.
6. Mark actions complete or failed after the user performs the work.

## Safety notes

The adapter does not silently execute AI-generated commands. Commands are either run explicitly by the user or tracked as proposed actions through the service action lifecycle. Future phases can add richer approval UX before execution.

## Next work

- PowerShell profile opt-in helper.
- Terminal session identity and heartbeat.
- Incremental output streaming.
- Policy-based risk detection for destructive commands.
- Windows companion UX for terminal actions.
