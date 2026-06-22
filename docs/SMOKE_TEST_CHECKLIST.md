# Smoke Test Checklist

Follow this checklist to perform a basic smoke test before each release or after major changes to ensure core functionality works as expected.

1. **Build and Launch**
   - Ensure the project builds without errors on Windows.
   - Launch the ThreadlineAI sidecar application. Verify the window appears and attaches to the active window when commanded.

2. **Provider Setup**
   - Open the provider settings panel.
   - Use "Defaults" to auto-fill a provider (e.g. OpenAI) base URL and model.
   - Enter an API key if required and click **Save Provider**. Verify the provider saves without resizing the sidecar.
   - (Optional) Clear the API key and run **Test Provider** via Diagnostics. Confirm success or see a clear failure message describing what to fix.

3. **Sidecar Attachment**
   - Open the target picker, select an open window, and click **Use**. Confirm the sidecar attaches and shows context summary.
   - Verify the sidecar geometry remains stable when switching targets or saving settings.

4. **Browser Extension**
   - In Chrome with the extension installed, navigate to a web page and click the extension button to send context.
   - Ask a question using the sidecar. Confirm the answer includes page-level context. If no extension context is provided, the sidecar should display a guidance message inviting you to send the page.

5. **Asking Without Session**
   - Without manually creating a session, click **Ask** and confirm the sidecar automatically creates or resumes a session. There should be no "Start or load an active session first" error.

6. **AI Hover Trigger**
   - Hide the sidecar. Verify the floating AI hover trigger appears and does not flicker.
   - Show the sidecar and ensure the trigger hides.

7. **Artifacts and Buttons**
   - After receiving a response, click each artifact button (Summary, Handoff, Decisions, Risks, Next Actions). Confirm that none of the buttons crash the application and the UI remains responsive.

8. **Sidecar Docking**
   - Save provider settings or toggle settings panels. Verify the sidecar remains docked to the selected target window and does not resize unexpectedly.

9. **Diagnostics Panel**
   - Open the diagnostics panel. Ensure product diagnostics show service status, provider configuration, provider test result, session/work-thread status, browser connection, context source, and last provider error.

Document any failures or unexpected behaviors for further investigation.
