# Build 16 — Context Capture Core

This build turns context capture into an explicit provider ladder with a receipt for every resolved result.

## Provider ladder

Threadline now evaluates context in this order:

1. Browser extension provider
2. File/document provider
3. UI Automation provider
4. Clipboard/selection provider, blocked unless explicitly allowed
5. Screenshot/OCR/vision provider, blocked unless explicitly allowed
6. Title/process fallback

The ladder records every provider attempt as captured, skipped, missed, blocked, or failed.

## Context Receipt

Every resolved `SummarizedContext` can now include a `ContextReceipt` showing:

- Source used
- Confidence
- Capture kind
- What was captured
- What was not captured
- Whether the result is page text, selected text, title-only, OCR, file-backed, or UI Automation
- Whether Threadline is missing the real working content
- Provider ladder attempts

When Threadline only has metadata, the receipt message is explicit:

> I only have the window title. I do not have the page/body content.

## Trust behavior

Browser targets no longer fall through to UI Automation and pretend browser chrome is page text. If the extension does not provide page or selected text, the resolver falls back to title/process metadata and marks real working content as missing.

Modern Notepad tab targets still avoid unsafe UI Automation body capture. They can resolve through a unique saved-file match; otherwise Threadline reports missing document body content instead of guessing.

Clipboard/selection and screenshot/OCR/vision are represented in the ladder, but blocked by default. This keeps the product honest until explicit user consent and provider wiring are added.

## UI changes

The sidecar receipt panel now shows the capture kind, source used, trust level, and whether real working content is missing. Ask context messages also include the receipt message so the chat transcript records what Threadline actually had.
