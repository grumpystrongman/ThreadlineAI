# Phase 11.8 Build Notes — Deep Active App Context Resolver

Build 11.8 turns the Windows sidecar resolver into an explicit confidence-based active-app context pipeline.

## Delivered

- Added first-class resolver confidence levels: `High`, `Medium`, `Low`, and `None`.
- Added capture method taxonomy for provider, UI Automation, file/document, screenshot, OCR, image extraction, and layout analysis.
- Added process intelligence output for selected targets:
  - Window title
  - HWND
  - PID
  - Executable path
  - Parent process
  - App type
  - Available capture methods
- Added resolver diagnostics output for:
  - Window
  - Process
  - PID
  - Capture method used
  - Characters extracted
  - Images found
  - Summary size
  - Confidence
- Added screenshot fallback as an explicit placeholder route with `Low` confidence and visible caveats. It does not pretend OCR/vision is implemented yet.
- Updated prompt context formatting so Threadline states what it can actually see before answering.

## Pipeline order

The resolver now follows this ladder:

1. App/provider context
2. UI Automation
3. File/document resolver
4. Screenshot/OCR fallback placeholder

Screenshot is intentionally the last resort.

## Validation targets

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

## Verification checklist

1. Start the local service.
2. Start the Windows sidecar.
3. Click **Refresh** in Open Apps and Tabs.
4. Select each validation target and click **Use**.
5. Confirm the Current Context panel shows source, confidence, and summary.
6. Ask a simple question and confirm the answer includes the resolved context path.
7. Use Diagnostics to confirm method, extracted character count, and confidence.

## Known limitations

- Screenshot OCR, image extraction, and layout analysis are not fully implemented in this build. The resolver exposes the fallback route honestly instead of fabricating content.
- Word, Outlook, PDF, and Power BI still depend mostly on UI Automation until app-specific providers are added.
