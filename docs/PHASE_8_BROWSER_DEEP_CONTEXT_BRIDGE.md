# Phase 8 — Browser deep context bridge

Phase 8 makes the Chrome/Edge extension usable against the local Threadline service.

## Completed in this phase

- Updated Manifest V3 extension permissions for local Threadline service calls.
- Replaced native-message-only background behavior with direct local service bridge calls.
- Added local service client for:
  - settings
  - health checks
  - active session lookup
  - context preview
  - approved context storage
- Added browser context capture for:
  - page title
  - page URL
  - selected text
  - visible page text capped by configured limit
- Added context menu actions:
  - Send selection to ThreadlineAI
  - Send page to ThreadlineAI
- Added popup actions:
  - Test connection
  - Send page
  - Send selection
- Added options page for:
  - local service URL
  - optional local access token
  - max captured characters
- Added extension build script.

## Manual test path

1. Start Threadline.Service on `http://localhost:5057`.
2. Start or use a Threadline session.
3. Build the browser extension:

```powershell
./eng/build-browser-extension.ps1
```

4. Open Edge or Chrome extensions page.
5. Enable developer mode.
6. Load unpacked extension from:

```text
adapters/browser-extension
```

7. Open a web page.
8. Click the ThreadlineAI extension button.
9. Click **Test connection**.
10. Click **Send page**.
11. Select some text on the page.
12. Click **Send selection** or use the right-click context menu.
13. Check Threadline Windows companion or service audit/session events.

## Safety notes

The extension only sends context on user action. It does not continuously scrape pages. The local service still applies capture policy, redaction, consent metadata, and audit handling before context is stored or used.

## Next work

- Native messaging host for stronger local integration.
- Tab/session relationship tracking.
- Browser-side preview before sending.
- URL/domain denylist synced from Threadline policy.
- Richer page extraction for structured content.
