# Phase 10 — Native Windows UI Automation

Phase 10 adds the first native Windows UI Automation capture path to the Windows companion.

## Completed in this phase

- Added a Windows UI Automation reader in the Windows companion.
- Added foreground-window UI text extraction with caps on element count and character count.
- Added safe failure handling for inaccessible or volatile UI elements.
- Added a **Preview Native UI** button to the Windows companion.
- Added native UI preview output into the companion transcript.
- Included native UI preview context in prompt composition when available.

## Manual test path

1. Start Threadline.Service.
2. Start the Windows companion.
3. Start or use an active Threadline session.
4. Open a normal Windows app such as Notepad, Calculator, Settings, or File Explorer.
5. Bring that app to the foreground.
6. Return to ThreadlineAI Companion.
7. Click **Refresh Foreground** if needed.
8. Click **Preview Native UI**.
9. Review the captured text in the transcript panel.
10. Ask a question using **Ask / Compose Prompt** and confirm the native UI context appears in the composed prompt.

## Safety notes

This phase only previews native UI text inside the local companion UI. It does not silently scrape apps in the background, does not continuously monitor app content, and does not execute UI actions. The user triggers capture directly.

## Known limitations

- Some apps expose little or no UI Automation text.
- Elevated/admin windows may require Threadline to run elevated to read them.
- Browser content is better handled by the Phase 8 browser extension.
- Office/document capture will get a dedicated workflow in Phase 11.

## Next work

- Store native UI preview as approved session context.
- Add per-app allow/block policy in the Windows UI.
- Add UI Automation tree diagnostics.
- Add richer control-type filtering and table/list extraction.
- Add approved UI actions later through the action policy layer.
