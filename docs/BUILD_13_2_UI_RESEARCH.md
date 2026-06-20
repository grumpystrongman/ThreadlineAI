# Build 13.2 UI Research

Threadline's main sidecar should stay focused on current work status, transcript, and compose. Session management and provider settings should be secondary surfaces.

Guidance used:

- Windows command bar guidance: keep the most important commands visible and move secondary commands to overflow when space is limited.
- Windows navigation guidance: preserve screen real estate on smaller windows and use adaptive navigation.

Decision for Build 13.2A:

- Hide the session manager from the default main surface.
- Use sessions as a secondary panel opened intentionally.
- Keep settings out of the normal transcript flow.
- Keep pending window binding visible only when there is an actual pending target.
