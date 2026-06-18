# Phase 10.9 — Active Window Content Resolution

Phase 10.9 generalizes context capture beyond browser tabs and beyond Notepad.

Threadline should resolve usable content from any active window by routing the target through the best available provider instead of treating every app as a generic accessibility tree.

## Goal

For any active app, tab, document, or pane, Threadline should attempt to collect:

- text
- selected text
- document structure / headings
- links
- images and image metadata
- tables
- formatting hints
- file path or document identity when available
- provider confidence
- warnings and ambiguity notes

The result must be previewable, clearable, and safe. Threadline must not silently summarize content if it cannot prove the captured body belongs to the selected target.

## Provider ladder

1. App-specific provider
   - Browser / Google Docs / Gmail
   - Notepad active document
   - Office documents
   - Terminal / shell panes
   - PDF viewers

2. Document/file provider
   - Resolve an active document to a local file or cloud document identity.
   - Read through an approved provider, not blind scraping.

3. UI Automation provider
   - Prefer focused element and supported text/value/selection patterns.
   - Avoid whole-window text dumps when tabs are involved.

4. Native control provider
   - Use app-specific native/window-control strategies where available.

5. Screenshot / vision fallback
   - Explicit approval only.
   - Used for visible text, layout, images, and non-exposed UI.

6. Safety layer
   - Confidence score.
   - Source label.
   - Ambiguity warning.
   - User preview and approval.
   - Clear/delete controls.

## Notepad as first concrete 10.9 target

Modern Notepad exposes tab titles and body text in ways that can be ambiguous through generic native UI capture. The first 10.9 app-specific provider should target Notepad, but only as one implementation of the broader active-window resolver.

The Notepad provider should:

- detect the active tab title
- inspect focused/editor-like controls
- read only active editor text when it can be isolated
- warn when body text cannot be safely mapped to the active tab
- optionally resolve saved file-backed tabs after user approval

## Definition of done

- The selected target displays which provider was used.
- Browser pages continue to use browser-extension / document export paths.
- Notepad no longer relies on whole-window native UI for body content.
- Generic apps route through the active-window content resolver.
- Threadline shows text/links/images/tables/formatting hints where available.
- Threadline refuses uncertain body text instead of hallucinating certainty.
