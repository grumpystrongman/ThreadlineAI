# Architecture

```mermaid
flowchart LR
    A[Windows Shell] --> B[Local Threadline Service]
    C[Browser Extension] --> B
    D[PowerShell Adapter] --> B
    E[Active Window Monitor] --> B
    B --> G[Session Store]
    B --> H[Prompt Composer]
    H --> I[LLM Provider Gateway]
```

ThreadlineAI has five layers: Windows shell, local service, context adapters, session store, and provider gateway.

The Windows shell owns the side panel, provider picker, session picker, context preview, timeline, and pause controls. The local service is the trusted boundary for adapters. It accepts context events, evaluates capture policy, redacts secrets, stores session data, and composes prompts.

The context broker normalizes browser context, PowerShell transcript output, active window metadata, future UI Automation text, user-selected text, and approved screenshots into a single `ContextEvent` shape.
