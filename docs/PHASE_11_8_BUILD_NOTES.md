# Phase 11.8 Build Notes — Deep Active App Context Resolver

This build turns the active-app resolver from a basic target/session helper into the first real context-awareness layer for ThreadlineAI.

## Delivered

- Added process intelligence as a first-class resolver concept.
- Added confidence labels for captured context: `HIGH`, `MEDIUM`, `LOW`, and `NONE`.
- Added resolver diagnostics so capture behavior can be debugged without flooding the default UI.
- Formalized the capture ladder:
  1. Provider
  2. UI Automation
  3. Document/File Resolver
  4. Screenshot fallback
- Added a screenshot fallback seam that reports low/no confidence unless OCR or vision extraction is available.
- Added a Current Context panel contract for source, confidence, summary, and diagnostics.

## Validation Targets

Use these first:

- ChatGPT desktop app
- Chrome
- Notepad
- Outlook
- Word
- PDF viewer
- Power BI

## Rebuild

```powershell
git pull
dotnet restore ThreadlineAI.sln
dotnet build ThreadlineAI.sln
./eng/build-windows.ps1
```

## Verification Checklist

1. Start the local service.
2. Launch the Windows sidecar.
3. Click Refresh in the app/window picker.
4. Select Notepad, Chrome, ChatGPT desktop, Outlook, and Word one at a time.
5. Click Use.
6. Confirm the Current Context panel reports:
   - Source
   - Confidence
   - Summary
   - Capture method
7. Open Diagnostics and confirm it shows:
   - Window title
   - HWND
   - Process name
   - PID
   - Executable path
   - Capture method used
   - Characters extracted
   - Summary size
   - Confidence
8. Ask a question and confirm Threadline states what it can see before answering.

## Known Limits

Screenshot OCR and image layout analysis are still dependency-gated. The fallback path now exists and will report honestly when OCR/vision extraction is unavailable instead of pretending the visible document was understood.
