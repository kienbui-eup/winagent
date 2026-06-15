# Troly Win Agent — Native Shell (`winagent/shell`)

Native **WinUI 3 / .NET 8** desktop shell that supervises the existing winagent
Node/TS runtime as a headless sidecar and drives it over the runtime's loopback
HTTP API + bearer token. See [`../docs/ADR-0002-winui-shell-node-sidecar.md`](../docs/ADR-0002-winui-shell-node-sidecar.md)
for the architecture decision (supersedes ADR-0001's Electron-shell assumption).

## Projects

| Project | TFM | Purpose |
|---|---|---|
| `src/Troly.WinAgent.Core` | `net8.0` | Shell-agnostic runtime supervision: discovery/health handshake, HTTP client, process launcher, Win32 Job Object. No UI dependency, so it builds and unit-tests anywhere on Windows. |
| `tests/Troly.WinAgent.Core.Tests` | `net8.0` | xUnit tests for the Core (state reader, HTTP handshake, supervisor lifecycle) using a stub `HttpMessageHandler` + fake launcher — no real Node/sockets needed. |
| `src/Troly.WinAgent.App` | `net8.0-windows10.0.19041.0` | WinUI 3 app (unpackaged). Phase B2: single-instance entry (`AppInstance`), Troly login UI, runtime supervision + auth/key-sync wiring. **Intentionally NOT in `Troly.WinAgent.sln`** (see build note). Floating bar / voice / TTS land in Phases D–E. |

### Core types (Phase A)

- `RuntimePaths` — resolves the state dir + `state.json`/`token.txt` (mirrors `state.mjs`).
- `RuntimeStateReader` — loads a `RuntimeConnection` from the discovery files (mirrors `mcp-lib.loadConnection`).
- `RuntimeClient` — the `/health` (+ serverId) and authenticated `/status` handshake (mirrors `mcp-lib.validateConn`).
- `Win32JobObject` — `KILL_ON_JOB_CLOSE` job so the runtime (and its Chrome) is reaped with the shell.
- `IRuntimeProcessLauncher` / `ProcessRuntimeProcessLauncher` — spawns the runtime headless (`TROLY_HEADLESS=1`); injectable for tests.
- `RuntimeSupervisor` — reuse-or-launch + poll-until-healthy, mirroring `mcp-lib.ensureDesktopRunning`.

## Prerequisites

- .NET 8 SDK (pinned via `global.json`).
- The runtime side: Node 20+ and the winagent `.mjs` tree in the parent directory.

## Build & test

```powershell
dotnet build  shell/Troly.WinAgent.sln
dotnet test   shell/Troly.WinAgent.sln
```

(If the `.sln` is absent, target the test project directly:
`dotnet test shell/tests/Troly.WinAgent.Core.Tests/Troly.WinAgent.Core.Tests.csproj`.)

### Building the WinUI App (`Troly.WinAgent.App`)

The App is **not** part of `Troly.WinAgent.sln` because the bare .NET 8 SDK cannot
complete the build: the MRT/PRI resource step needs the MSIX/Appx MSBuild tasks
(`Microsoft.Build.Packaging.Pri.Tasks.dll`) that ship with **Visual Studio 2022**
(or the "Windows application packaging" build tooling), not the SDK alone. On a box
without it the build fails with `MSB4062 … ExpandPriContent`. The C#/XAML itself
compiles fine (verified headlessly via `XamlCompiler`).

Build/run on a dev box that has VS 2022 (with the Windows App SDK C# templates):

```powershell
dotnet build shell/src/Troly.WinAgent.App/Troly.WinAgent.App.csproj -r win-x64
```

For a from-source dev run (before packaging in Phase G bundles `node.exe` + sidecar),
point the supervisor at your runtime via env vars:

```powershell
$env:TROLY_RUNTIME_EXE  = "<path to electron.cmd or electron.exe>"
$env:TROLY_RUNTIME_ARGS = "<path to winagent>\main.mjs"   # runtime-host.mjs after Phase F
```

App pieces (Phase B2): `Program.cs` (single-instance via `AppInstance`, per research),
`MainWindow` (email/password login → `TrolyAuthClient` → DPAPI `TokenStore`, with
runtime health from `/runtime/status`). Floating bar / voice / TTS arrive in Phases D–E.

## Running against the real runtime (manual, Phase A smoke)

The supervisor launches the runtime headless. Until `runtime-host.mjs` lands (Phase F),
point it at the Electron entry with `TROLY_HEADLESS=1`:

```csharp
var supervisor = new RuntimeSupervisor(new RuntimeSupervisorOptions
{
    Executable = /* path to node or the bundled electron */,
    Args = new[] { "main.mjs" },        // runtime-host.mjs after Phase F
    AppVersion = "0.1.2",
});
var conn = await supervisor.EnsureRunningAsync();   // -> http://127.0.0.1:<port>
```

## Next phases (see `../docs/IMPLEMENTATION_BACKLOG.md` and the repo plan)

- **B** — Troly auth + key sync: `/troly/*` runtime proxy + login UI + DPAPI token storage.
- **C** — core chat (RuntimeClient `/query` + `/status` polling).
- **D** — floating bar + tray + global hotkey + launch-at-login.
- **E** — voice PTT + TTS (+ `/events` WebSocket).
- **F** — extract pure-Node `runtime-host.mjs`; drop Electron from the default path.
- **G** — Velopack packaging + Authenticode signing + auto-update + telemetry.
