# Build 12.1 — Chat Sidecar and Edge Handle

Build 12.1 changes the Windows sidecar product shape from a mini hub into a context-aware chat shell.

## Product direction

The sidecar should feel closer to Gemini, Copilot, or a lightweight AI companion panel:

- It is primarily a chat window.
- It understands the app, tab, document, or workflow it is attached to.
- It does not expose the full hub/debug/admin surface by default.
- Advanced controls are kept behind the overflow menu.

## Visible sidecar surface

The default visible UI now focuses on:

- Current attached context
- Context confidence/status
- Conversation transcript
- Prompt box
- Ask / Copy / Clear
- Attach toggle
- Hide button
- Minimal overflow menu

The previous hub-style controls are no longer part of the main visible layout. They remain available behind the overflow menu only because the current code-behind still depends on those controls while the app is being simplified.

## Edge handle behavior

The sidecar now supports a collapsed edge-handle mode:

1. Click **Hide** in the sidecar header.
2. The sidecar collapses into a narrow handle near the attached target window edge.
3. Hover over or press the handle to restore the full chat sidecar.

When attached to a target, the handle tries to stay near the target window. If the target cannot be located, it falls back to the screen edge.

## Follow-up cleanup

Later builds should remove the remaining hidden hub dependencies from the sidecar code-behind and split the app into clearer surfaces:

- Hub / settings / diagnostics surface
- Context chat sidecar surface
- Edge handle / invocation surface
