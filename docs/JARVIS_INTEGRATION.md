# ThreadlineAI + PersonalJarvis operating layer

ThreadlineAI is the Windows-native shell, context/memory/privacy engine, and owner-control surface. PersonalJarvis is the local agentic runtime for Missions, workers, critic review, tool approvals, and long-running execution. AIKA / JARVIS is the user-facing assistant surface.

## Architecture

```text
AIKA / JARVIS (WinUI)
        |
        +--> Threadline Direct Ask
        |
        +--> PersonalJarvis Mission
                  |
                  +--> threadline-windows-device MCP
                  |       +--> files / PowerShell
                  |       +--> Win32 process + window control
                  |       +--> Windows UI Automation
                  |       +--> screenshot/OCR observation
                  |       +--> keyboard/mouse fallback
                  |
                  +--> threadline-browser MCP
                  |       +--> authenticated local WebSocket
                  |       +--> Threadline browser extension
                  |       +--> real logged-in tabs + DOM
                  |
                  +--> threadline-privileged MCP
                          +--> Jarvis risk tier: ask
                          +--> per-tool AIKA approval
                          +--> normal Windows UAC
                          +--> allowlisted elevated broker
```

The system prefers direct, inspectable mechanisms over simulated clicks. Files and shell work use native file/PowerShell capabilities. Browser work uses the browser extension and semantic DOM operations. Generic Windows UI Automation remains the broad fallback for desktop apps; OCR and keyboard/mouse are later fallbacks.

## Upstream pin and local compatibility patch

- Upstream: `PersonalJarvis/PersonalJarvis`
- License: MIT
- Pinned revision: `b85535a50a3cece8cd269325b6b3ea2cb900e43f`
- Threadline compatibility patch: `eng/patches/personal-jarvis-mcp-risk.patch`

The pinned PersonalJarvis revision normalizes MCP tools without retaining MCP annotations and creates every MCP adapter at the `monitor` risk tier. Threadline's small local compatibility patch preserves MCP annotations and maps `destructiveHint=true` tools to PersonalJarvis's existing `ask` tier. This lets protected MCP tools flow through the normal Jarvis approval workflow rather than trusting a model-supplied confirmation argument.

The bootstrap always checks out/reset the managed runtime to the exact pin before applying the patch, making repeat installs deterministic.

## Bootstrap

Run from PowerShell:

```powershell
./eng/bootstrap-personal-jarvis.ps1 -Start
```

The bootstrap:

1. clones or resets the managed PersonalJarvis runtime to the pinned revision;
2. verifies and applies the MCP risk patch;
3. compiles the patched MCP modules;
4. creates/updates the isolated PersonalJarvis Python environment;
5. publishes self-contained Windows executables for Device MCP, Browser MCP, Privileged MCP, and the Privileged Broker;
6. writes the Jarvis MCP configuration under `%LOCALAPPDATA%\ThreadlineAI\jarvis\mcp.json`;
7. registers `threadline-windows-device`, `threadline-browser`, and `threadline-privileged`;
8. optionally starts `jarvis serve`.

Managed runtime locations:

```text
%LOCALAPPDATA%\ThreadlineAI\runtimes\PersonalJarvis
%LOCALAPPDATA%\ThreadlineAI\runtimes\DeviceMcp
%LOCALAPPDATA%\ThreadlineAI\runtimes\BrowserMcp
%LOCALAPPDATA%\ThreadlineAI\runtimes\PrivilegedMcp
%LOCALAPPDATA%\ThreadlineAI\runtimes\PrivilegedBroker
```

## Direct Ask vs Mission routing

The unified entry point is:

```text
POST /sessions/{sessionId}/agent
```

`route` accepts `Auto`, `Direct`, or `Mission`. `Auto` uses an inspectable deterministic classifier. Ordinary conversational/explanatory requests stay on the direct provider path; operational, coding, research, install, computer-use, and multi-step requests route to a Mission.

When a Mission is built, Threadline provides only approved/redacted context. Captured pages, documents, terminal output, OCR, and other external material are explicitly marked as evidence/data, not authority or hidden instructions.

A meaningful action is expected to follow a closed loop:

```text
observe -> choose strongest capability -> act -> verify
   ^                                           |
   +-------- inspect/recover/re-plan <---------+
```

Partial/failed verification is a signal to inspect current state and choose another capability, not blindly repeat the same interaction.

## Windows Device Agent

`threadline-windows-device` exposes bounded tools for:

- desktop/window observation;
- UI Automation inspection;
- screenshot/OCR observation;
- application launch/focus;
- UI control invocation and text setting;
- direct file read/write;
- PowerShell execution with captured output;
- keyboard/mouse fallback.

Owner Authority is persisted locally and visible in the AIKA/JARVIS window. The model may read the active grant but cannot expand it. Protected operations remain outside normal Owner Authority.

## Browser Agent

`threadline-browser` controls the user's existing logged-in browser through the Threadline browser extension, rather than starting a separate automation browser.

Semantic tools include:

```text
browser_state
browser_navigate
browser_new_tab
browser_activate_tab
browser_close_tab
browser_inspect_dom
browser_click
browser_fill
browser_read_text
browser_scroll
```

There is intentionally no arbitrary JavaScript-execution tool. DOM/page content remains untrusted evidence.

The service-to-extension command channel is local and authenticated: the extension obtains a short-lived socket ticket through Threadline's existing local API token boundary and then connects to the loopback WebSocket command hub.

## Protected Windows operations

`threadline-privileged` advertises only protected tools and marks every one with MCP `destructiveHint=true`:

```text
protected_install_package
protected_service_start
protected_service_stop
protected_service_restart
protected_registry_set_hklm_software
```

The approval chain is intentionally layered:

```text
Mission proposes protected tool
        -> PersonalJarvis ask-tier approval gate
        -> AIKA displays tool / risk / reason / secret-free args preview
        -> owner approves exactly that trace ID
        -> Windows UAC elevation
        -> allowlisted broker executes
        -> result returns to Mission for verification
```

Approval applies only to the paused trace ID. It does not expand Owner Authority or pre-authorize future protected actions.

The elevated broker has **no arbitrary elevated shell**. It currently allows only:

- exact `winget` package installation;
- start/stop/restart of non-blocked Windows services;
- string/DWORD writes beneath `HKLM\SOFTWARE`.

The broker validates request/response paths, rejects expired requests, requires an elevated token, validates package/service identifiers, and blocks a set of security-critical services. UAC is never bypassed.

## Jarvis Mission bridge

Threadline proxies the Mission control surface behind its existing local-access guard:

```text
GET  /v1/jarvis/status
POST /v1/jarvis/missions
GET  /v1/jarvis/missions/{missionId}
GET  /v1/jarvis/missions/{missionId}/result
GET  /v1/jarvis/missions/{missionId}/changes
POST /v1/jarvis/missions/{missionId}/cancel
GET  /v1/jarvis/missions/{missionId}/tool-approvals
POST /v1/jarvis/missions/{missionId}/tool-approvals/{traceId}/approve
POST /v1/jarvis/missions/{missionId}/tool-approvals/{traceId}/deny
```

Dispatch-level destructive confirmation and per-tool approval are separate gates and both remain active.

## Runtime networking

Default PersonalJarvis address:

```text
http://127.0.0.1:47821/
```

Threadline rejects non-loopback Jarvis targets unless `Threadline:Jarvis:AllowRemoteRuntime=true` is explicitly configured.

The browser command hub also remains loopback/local-token protected.

## Validation

The branch validation workflow (`.github/workflows/jarvis-bridge-validation.yml`) is designed to prove the non-interactive parts of the integration on `windows-latest`:

- parse the bootstrap PowerShell script;
- clone the pinned PersonalJarvis revision and prove the risk patch applies cleanly;
- Python-compile the patched MCP modules;
- build Threadline.Service;
- build Device MCP, Browser MCP, Privileged MCP, and Privileged Broker;
- MCP protocol-smoke-test all three servers and require `destructiveHint=true` on every privileged tool;
- publish the same self-contained runtime shapes used by bootstrap;
- install/build the browser extension from its lockfile;
- restore/build the WinUI application with Visual Studio MSBuild;
- run Threadline.Core and Threadline.Service tests.

Local `./eng/build-windows.ps1` now builds the same Threadline agent stack plus browser extension before the WinUI application.

Hosted CI cannot prove real interactive UI Automation, a signed-in browser session, or the UAC consent desktop. `eng/test-device-agent.ps1` remains the interactive Windows smoke path for those device-level checks.

## Product boundary

The intended system is one assistant surface with replaceable subsystems:

- **Threadline**: Windows context, memory, privacy, routing, owner controls, local service and capability bridges.
- **PersonalJarvis**: Mission orchestration, workers, critic, tool execution and approval workflow.
- **AIKA / JARVIS**: user-facing personal assistant experience and approval surface.
- **Future adapters**: companion/personality memory, local creative models, ComfyUI, voice/wake word, and scheduled automations can attach behind the same capability boundaries without turning one model into an all-powerful monolith.
