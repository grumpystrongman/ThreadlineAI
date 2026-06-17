# Phase 11 — Office, OneNote, and document workflow plan

Phase 11 should add document-centric workflows without assuming that every Microsoft Office app is fully licensed or editable on the local machine.

## Constraint

The validation machine may have Microsoft Office applications installed but not licensed for full editing. OneNote is available and usable. Therefore, Phase 11 should treat Office integration as capability-building, not as a single dependency on a licensed Word/Excel/PowerPoint desktop install.

## Capability goals

Threadline should be able to help with document workflows across several access levels:

- OneNote notes and pages.
- Visible/read-only Office windows.
- User-selected text.
- Clipboard-approved text.
- Local document files where available.
- Future Microsoft Graph or Office add-in paths.

## Implementation tiers

### Tier 1 — OneNote and visible document context

- Treat OneNote as the first real Office-family validation target.
- Capture visible OneNote context through the native/UI path and summarizer.
- Add OneNote-specific cleanup rules if the native output is noisy.
- Support user questions, summarization, rewriting, and draft generation.

### Tier 2 — Clipboard and selected text

- Add an explicit paste-from-clipboard / selected-text capture flow.
- Use this as the fallback for read-only Office apps.
- Make the capture previewable and auditable.

### Tier 3 — File import

- Add local file import for document formats where feasible.
- Start with plain text and markdown.
- Add docx/xlsx/pptx parsing later through dedicated document libraries.

### Tier 4 — Microsoft Graph / Office add-in path

- Add future support for Microsoft Graph or Office add-ins when credentials, tenant policy, and licensing allow it.
- Keep this optional so Threadline remains useful without a licensed local Office install.

## UX goal

The user should not care which technical path was used. They should experience a single behavior:

```text
Ask Threadline about the document or note I am working on.
```

Threadline should choose the best available capture method and summarize context before composing the prompt.
