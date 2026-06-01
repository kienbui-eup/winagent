# ADR-0002: Native WinUI 3 Shell + Node/TS Runtime Sidecar for Troly Win Agent

## Status
Accepted - 2026-06-01

**Supersedes [ADR-0001](ADR-0001-base-platform.md)** with respect to the desktop **shell/UI layer**. ADR-0001's decision to fork Agentify Desktop and reuse its runtime still stands; what changes is that the **Electron control-center window is no longer the product UI**.

## Context
The product target moved from "a local control center with MCP" to a **Windows desktop client at parity with the macOS agent (`macagent`/"Fazm")** for the scope *MVP + signature desktop UX*. The signature experience — an always-on-top floating bar, voice push-to-talk (STT), text-to-speech, system tray, global hotkey, launch-at-login — requires OS integration that Electron does not expose well:

- Always-on-top frameless floating window with custom drag/resize, acrylic/Mica, hide-from-Alt-Tab, click-through overlays.
- Global push-to-talk hotkey while the app is unfocused.
- Native microphone capture and low-latency audio playback.
- True system-tray and launch-at-login behavior.

`macagent` delivers these with a native SwiftUI shell that owns the UI and supervises a Node ACP-bridge subprocess. The same split applies cleanly on Windows because **winagent's runtime is already shell-agnostic**: `http-api.mjs`, `state.mjs`, `chrome-cdp-backend.mjs`, `tab-manager.mjs`, `context-packer.mjs`, the artifact/bundle/watch stores, and the MCP server have no Electron dependency. Electron is concentrated almost entirely in `main.mjs` (the control-center `BrowserWindow`, `ipcMain`, `Menu`, `dialog`, `Notification`).

## Decision
Build the Troly Win Agent UI as a **native WinUI 3 / .NET 8 shell** (unpackaged, full-trust) that **supervises the existing winagent Node/TS runtime as a headless sidecar** and drives it over the runtime's existing **loopback HTTP API (`127.0.0.1`) + bearer token**.

- **Shell (new, `winagent/shell/`):** all signature UX (floating bar, chat UI, tray, global hotkey, voice/STT, TTS, launch-at-login) and the Troly login UI. Mirrors `macagent`'s `Desktop/Sources` responsibilities.
- **Runtime (existing winagent `.mjs`):** Chrome-CDP browser automation, MCP server, context packing, artifact/bundle/watch stores, and the **single HTTPS egress** to the Troly backend's reused `/v1/macos-client/*` endpoints.
- **Boot:** the runtime runs headless (`TROLY_HEADLESS=1`); no Electron window is ever shown. A new pure-Node `runtime-host.mjs` (extracted from `main.mjs`) becomes the default entrypoint, removing Electron from the default path; the Electron entry remains only as an optional embedded-browser fallback.
- **Discovery/health:** the shell reuses the exact handshake `mcp-lib.mjs` already implements — read `state.json` (`{port, serverId}`) + `token.txt`, then `GET /health` and an authenticated `GET /status`.

## Why This Decision
1. **OS fidelity:** floating bar, global hotkey, WASAPI audio, tray, and launch-at-login map directly to Win32/WinUI APIs and match `macagent`'s native quality — which Electron cannot reach without heavy native modules.
2. **Maximum reuse, minimal rewrite:** ~40% of `macagent`'s logic already exists, cross-platform, inside the winagent runtime. The shell talks to it over an API that already exists; no runtime rewrite is needed.
3. **Clean separation:** the runtime owns automation + the only outbound HTTPS path; the shell owns UI + login. This matches the proven `macagent` process model (native UI owns a Node subprocess).
4. **Forward-compatible:** the loopback contract is stable; token-streaming (Anthropic/ACP/Dify SSE) can be added later as a `GET /events` WebSocket channel without changing the shell contract.

## Consequences
**Positive**
- Native-quality signature UX on Windows; parity path with `macagent`.
- Runtime stays reusable and independently testable (`node --test`); MCP server keeps working for external clients.
- Zero backend work: reuse the existing `/v1/macos-client/*` blueprint.

**Negative / costs**
- Adds a .NET 8 / Windows App SDK toolchain and C#/XAML skill surface alongside the Node tree.
- Packaging must bundle a pinned `node.exe` + the `.mjs` tree + `node_modules`; signing/auto-update move to **Authenticode (EV) + Velopack** (MSIX is rejected because the app spawns Node, drives external Chrome over CDP, and installs a global keyboard hook — all fighting the MSIX sandbox).
- **MPL-2.0 obligation:** keep the proprietary .NET shell as a separate project tree; preserve MPL headers/NOTICE on the Node tree; add a license check before release.
- No Microsoft Store / MDM distribution under the unpackaged + Velopack path.

## Scope (Phase A)
- Record this ADR; scaffold `winagent/shell/Troly.WinAgent.sln` (App / Core / Tests).
- `RuntimeSupervisor`: spawn the runtime headless (`TROLY_HEADLESS=1`), Win32 Job Object (kill-on-job-close), single-instance Mutex, `state.json` + `token.txt` + `/health` + `/status` handshake, restart with backoff.
- Gate the Electron control center (`showControlCenter()`) and `window-all-closed` behind `TROLY_HEADLESS`.
- Point `troly-config.mjs` at the reused `/v1/macos-client/*` endpoints; send `X-Client-Id` (persisted per-install GUID) + `X-App-Version`.
- Begin EV code-signing certificate procurement and CI scaffolding.

## Out of Scope for This ADR
- Final Troly auth/key-sync request shapes (Phase B) and the `/troly/*` runtime proxy contract.
- The `/events` WebSocket streaming protocol details (designed in Phase E).
- Whether to hard-rename `agentify_*` MCP tools to `trolywin_*` vs ship temporary back-compat aliases. **Resolved (2026-06-01):** ship `trolywin_*` as primary names with `agentify_*` registered as deprecated back-compat aliases.
- The pure-Node `runtime-host.mjs` extraction that drops Electron from the default path (Phase F).
