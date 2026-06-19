# Phase 11.12 Build Notes — OpenAI Responses API

Build 11.12 moves Threadline's first-class OpenAI execution path from Chat Completions to the Responses API while preserving the existing chat-completions path for local and third-party OpenAI-compatible providers.

## Delivered

- Changed the OpenAI provider execution path to call `POST /responses` when:
  - the selected provider name is `OpenAI`; or
  - the configured base URL host is `api.openai.com`.
- Preserved `POST /chat/completions` for `Local`, DeepSeek, OpenRouter, Gemini-compatible, and other non-OpenAI-compatible endpoints.
- Normalized provider endpoint construction so base URLs work with or without a trailing slash.
- Added Responses API request/response DTOs.
- Preserved Threadline's existing `LlmRequest` contract so the app-side Ask flow does not need to change.
- Extracted answer text from either `output_text` or the standard Responses `output[].content[].text` shape.
- Added provider response metadata showing whether the executed provider path was `responses` or `chat/completions`.
- Updated the OpenAI settings hint to clarify that OpenAI now uses the Responses API and stores the key through local Threadline secret storage.

## Why this build matters

The older OpenAI-compatible adapter appended `chat/completions` to the configured base URL. That was fine for older model families and for local runtimes, but it blocked clean use of current OpenAI Responses API models. Build 11.12 keeps the local-provider safety of the old adapter but gives OpenAI a first-class path for newer model access.

## User flow

1. Start `Threadline.Service`.
2. Open the Windows sidecar.
3. Choose **OpenAI** in the provider picker.
4. Open **Settings**.
5. Click **Use Defaults** or enter:
   - base URL: `https://api.openai.com/v1/`
   - model: `gpt-4.1-mini` or another model available to the active OpenAI project
6. Enter the OpenAI API key.
7. Click **Save Provider**.
8. Start a new session using OpenAI.
9. Ask against resolved browser, Notepad, or app context.

## Validation

Run from the repository root on Windows:

```powershell
git pull
dotnet restore ThreadlineAI.sln
dotnet build ThreadlineAI.sln
./eng/build-windows.ps1
./eng/run-windows.ps1
```

Manual checks:

1. Confirm the top-right provider defaults to OpenAI.
2. Open Settings, select OpenAI, and click **Use Defaults**.
3. Confirm the OpenAI settings hint mentions the Responses API.
4. Save a valid OpenAI API key and model.
5. Start a new session with OpenAI selected.
6. Ask: `what can you actually see`.
7. Confirm the response comes back from OpenAI or the existing local visibility fallback still replaces provider failures cleanly.
8. Switch to `Local`, save a local OpenAI-compatible endpoint, and confirm Local still uses the chat-completions-compatible path.

## Known limitations

- Build 11.12 does not add streaming.
- Build 11.12 does not add vision or screenshot upload to the provider request.
- OpenAI still requires a valid API credential and a model available to the active OpenAI project.
- Non-OpenAI hosted providers are still handled as OpenAI-compatible chat-completions providers unless a later provider-specific adapter is added.
