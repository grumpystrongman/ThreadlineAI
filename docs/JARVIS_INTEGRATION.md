# ThreadlineAI + PersonalJarvis runtime bridge

This integration keeps ThreadlineAI as the Windows-native context shell while using PersonalJarvis as an optional local agent runtime for Missions, worker/critic execution, computer-use, and future tool routing.

## Why this shape

Threadline already owns Windows attachment, approved context capture, provider setup, Work Threads, artifacts, privacy rules, and the native sidecar. PersonalJarvis already owns a mature mission subsystem with isolated workers, critic review, destructive-action confirmation, tool approvals, and computer-use. The bridge joins those systems without replacing either one.

```text
Threadline.Windows
      |
      v
Threadline.Service  -- local token / origin protection
      |
      +--> /sessions/{id}/agent  -- Direct Ask vs Mission router
      |             |
      |             +--> configured Threadline provider (fast conversational path)
      |             |
      |             +--> PersonalJarvis Mission (agentic execution path)
      |
      +--> /v1/jarvis/*  -- explicit Mission control surface
                    |
                    v
          PersonalJarvis server
          http://127.0.0.1:47821
                    |
                    +--> Missions / Workers / Critic / Computer Use
```

Threadline does not silently bypass PersonalJarvis safety gates. A destructive mission can return HTTP 409 and require the caller to resubmit with `confirmed: true`. Mission-level tool approvals are also proxied explicitly so the Threadline UI can present approve/deny controls.

## Upstream

- Repository: https://github.com/PersonalJarvis/PersonalJarvis
- License: MIT
- Pinned integration commit: `b85535a50a3cece8cd269325b6b3ea2cb900e43f`

The bootstrap script clones the upstream repository directly rather than copying its full source into Threadline. This makes the upstream license boundary clear and allows us to advance the pin intentionally after compatibility testing.

## Install the runtime

On Windows PowerShell:

```powershell
./eng/bootstrap-personal-jarvis.ps1 -Start
```

This clones the pinned upstream revision under:

```text
%LOCALAPPDATA%\ThreadlineAI\runtimes\PersonalJarvis
```

It creates an isolated Python virtual environment and installs the `full` PersonalJarvis extra. Use `-Headless` if you intentionally want the smaller server-only dependency set.

## Threadline configuration

Defaults are intentionally useful for a local install:

```text
Threadline:Jarvis:Enabled = true
Threadline:Jarvis:BaseAddress = http://127.0.0.1:47821/
Threadline:Jarvis:RequestTimeoutSeconds = 30
Threadline:Jarvis:AllowRemoteRuntime = false
```

Environment-variable equivalents:

```powershell
$env:Threadline__Jarvis__Enabled = 'true'
$env:Threadline__Jarvis__BaseAddress = 'http://127.0.0.1:47821/'
$env:Threadline__Jarvis__RequestTimeoutSeconds = '30'
$env:Threadline__Jarvis__AllowRemoteRuntime = 'false'
```

The runtime address is restricted to loopback by default. A non-loopback HTTP/HTTPS address is rejected unless `AllowRemoteRuntime` is explicitly enabled. That keeps the first version local-first and avoids turning Threadline into an accidental network proxy.

## Unified Agent Ask

The canonical future entry point is:

```text
POST /sessions/{sessionId}/agent
```

Example:

```json
{
  "question": "Fix this code and run the tests.",
  "currentWindow": "approved resolved context",
  "takeRecentEvents": 20,
  "route": "Auto",
  "confirmed": false
}
```

`route` can be `Auto`, `Direct`, or `Mission`.

`Auto` uses a small deterministic classifier rather than another LLM call. Conversational/explanatory requests stay on Threadline's configured provider path. Operational requests such as implementation, debugging, file changes, research jobs, installs, computer-use commands, or multi-step work are escalated to a Jarvis Mission. The response includes the selected route, reason, and confidence so the decision is inspectable.

If Auto selects a Mission but Jarvis is unavailable (502/503/504), Threadline falls back to the direct provider path and labels the route `DirectFallback`. It does **not** fall back around a Jarvis HTTP 409 destructive-action confirmation because doing so would bypass the safety gate.

For Missions, Threadline packages the redacted current resolved context, session summary, and recent approved context events. Captured page/document content is explicitly marked as **evidence/data only** so prompt-like instructions inside external content do not gain authority over the Mission.

## Explicit Jarvis bridge endpoints

All endpoints live under Threadline's existing local-access guard.

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

Direct Mission dispatch example:

```json
{
  "prompt": "Research the current project and produce a tested implementation plan.",
  "language": "en",
  "confirmed": false
}
```

## Validation

The integration branch contains `.github/workflows/jarvis-bridge-validation.yml`, scoped to `feature/jarvis-runtime-bridge`. It restores/builds the .NET service on `windows-latest` and runs the Threadline.Core test suite, including Ask-vs-Mission routing tests.

## Next integration stages

1. Surface Mission progress, critic verdicts, artifacts, and destructive/tool approvals in the WinUI transcript.
2. Register Jarvis Missions and computer-use as first-class Threadline capabilities/actions.
3. Add PersonalJarvis lifecycle management to Threadline Doctor and the Windows service startup path.
4. Connect MyAika as an optional companion/personality/memory provider behind the same capability interface.
5. Add local creative-model and ComfyUI adapters as separate capabilities so fiction and image workflows do not depend on one hosted provider.
6. Add scheduler/trigger routing for recurring personal automations.

The intended long-term product shape is one assistant surface with separate, replaceable subsystems: Threadline for native Windows context, PersonalJarvis for agentic execution, and AIKA for companion/personality workflows where desired.
