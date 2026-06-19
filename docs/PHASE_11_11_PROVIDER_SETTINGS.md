# Phase 11.11 Build Notes — Provider Settings Flyout

Build 11.11 moves provider configuration into the Windows sidecar so setup does not require copying PowerShell snippets into a terminal.

## Delivered

- Added a **Settings** button next to the provider picker.
- Added a provider settings flyout with:
  - provider selector
  - base URL
  - default model
  - API key field
  - provider-specific defaults
  - save status text
- Added **Use Defaults** to fill common provider URL/model values.
- Added **Save Provider** to persist settings through the local Threadline service.
- Saved API-key providers through `POST /providers/{providerName}/credential` so secrets flow through Threadline local secret storage.
- Saved the `Local` provider through `POST /providers` using `LocalEndpoint`, because local OpenAI-compatible runtimes should not require an API key.
- Kept the main provider picker synchronized with the provider selected in the settings flyout.
- Kept OpenAI as the default new-session provider.

## User flow

1. Start `Threadline.Service`.
2. Open the Windows sidecar.
3. Choose **OpenAI** in the top-right provider picker.
4. Click **Settings**.
5. Confirm or edit the base URL and model.
6. Enter the API key.
7. Click **Save Provider**.
8. Start a new session.
9. Ask against browser, Notepad, or another resolved context.

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
2. Click **Settings** and confirm the flyout opens.
3. Select OpenAI and click **Use Defaults**.
4. Confirm base URL/model fields populate.
5. Enter a test API key and click **Save Provider**.
6. Confirm the transcript records that provider settings were saved without displaying the key.
7. Start a new session and confirm it uses OpenAI.
8. Ask a question and confirm failures fall back to a local visibility report rather than raw service text.

## Known limitations

- This is a first-pass flyout, not a full settings page.
- It does not yet list existing saved provider status from the service.
- It does not yet validate model access before saving.
- Non-OpenAI hosted providers are only useful when they expose an OpenAI-compatible chat completions endpoint or when Threadline adds a provider-specific adapter.
