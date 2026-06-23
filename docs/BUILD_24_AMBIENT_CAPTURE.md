# Build 24: Ambient Capture

Build 24 adds the first ThreadlineAI ambient capture tool under the existing **Tools** drawer.

## What is included

- A visible Ambient Capture panel in the Tools menu.
- Microphone/input capture using the current Windows capture endpoint.
- System audio capture using WASAPI loopback from the current Windows render endpoint.
- Device detection metadata for microphone, output endpoint, Bluetooth/headset/headphone inference, and default communications status.
- Local capture folders under `%LOCALAPPDATA%\ThreadlineAI\AmbientCapture`.
- Per-session `manifest.json`, `microphone.wav`, `system-loopback.wav`, `transcript.md`, and `handoff.md` files.
- Share-safe handoff generation with explicit consent/redaction reminders.

## Important architecture note

Ambient capture runs in the Windows sidecar app, not in the Windows service. A Windows service is not the right place to capture a user's interactive microphone/output devices. The sidecar owns recording, while future builds can sync finalized transcript/handoff artifacts into the local service and Work Thread memory.

## Headphones and Bluetooth behavior

The tool does not try to capture directly from headphones. Instead, it captures:

1. the default microphone/capture endpoint for the user's voice, and
2. the default render endpoint through WASAPI loopback for whatever the user hears.

That means speakers, wired headphones, Bluetooth headphones, and headset output all flow through the same render-loopback architecture.

## Current limitation

This build records and stores audio plus capture metadata. The transcript and translation path is intentionally marked as provider-pending rather than pretending speech-to-text has completed. The next step is to add a pluggable transcription provider behind the same session model.

## Manual validation

```powershell
dotnet restore ThreadlineAI.sln
dotnet build ThreadlineAI.sln
./eng/build-windows.ps1
```

Then validate:

1. Open the ThreadlineAI sidecar.
2. Open **Tools**.
3. Confirm the Ambient Capture section appears.
4. Click **Refresh Devices** and confirm mic/output names appear.
5. Start capture with microphone and system enabled.
6. Play system audio and speak briefly.
7. Stop capture.
8. Open the session folder and confirm `microphone.wav`, `system-loopback.wav`, `manifest.json`, `transcript.md`, and `handoff.md` are created.
9. Repeat after switching from speakers to Bluetooth/headphones.
