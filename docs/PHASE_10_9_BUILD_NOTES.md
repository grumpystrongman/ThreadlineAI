# Phase 10.9 Build Notes — First Active Window Resolver Slice

This build introduces the first active-window content resolution pipeline.

## Added

- `ActiveWindowContentResolver`
- Selected target preview now routes through the resolver instead of hard-coded browser/native branches.
- Browser targets continue to use the browser extension provider.
- Notepad tab targets are routed through a dedicated Notepad-needed response instead of unsafe generic native body capture.
- Generic readable targets still use native UI summarization as fallback.

## Why this matters

Threadline is moving from one-off capture buttons toward a provider ladder:

1. App-specific provider
2. Document/file provider
3. UI Automation provider
4. Native control provider
5. Approved screenshot/vision fallback
6. Safety/confidence layer

## Current limitation

This first slice does not yet implement active Notepad document body extraction. It prevents wrong-tab capture and establishes the resolver seam where the Notepad active-document provider will plug in next.

## Next concrete work

- Add active window diagnostics for selected targets.
- Add provider confidence display.
- Add Notepad active editor inspection.
- Add saved-file resolution where a selected tab maps to a known local file.
- Add screenshot/vision fallback behind explicit approval.
