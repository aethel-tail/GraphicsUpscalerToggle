# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Development

```bash
dotnet build                # Debug build (x64)
dotnet build -c Release     # Release build (x64)
```

Solution: `DalamudPlugins.sln`, single project: `GraphicsUpscalerToggle`.

Dalamud SDK 15.0.0, targets .NET 10, x64 only. Dalamud dev hooks point to XIVLauncherCN (Chinese client): `%AppData%\XIVLauncherCN\addon\Hooks\dev`.

## Architecture

Standard Dalamud plugin pattern. Entry point is `Plugin.cs` implementing `IDalamudPlugin`. Services are injected via `[PluginService]` attributes on static properties.

**Core flow**: `Framework.Update` detects login → `DoToggleSequence()` runs async → starts the window guard → ensures `GraphicsConfig->GraphicsRezoUpscaleType` is DLSS (2) → triggers an engine render-target rebuild (`Device->RequestResolutionChange` at the current client size) → waits 1s → logs `device/client/outer/style`. The rebuild is what re-creates the upscaler, which is what re-engages DLSS at login and lets OptiScaler hook in. A plain struct write only changes the stored value — the renderer never notices it.

**Window guard (why it exists)**: the engine's rebuild drops `WS_CAPTION` for ~30ms and then sizes its render targets off the now-captionless client rect (3840x1080 client ⇒ 3862x1136 render ⇒ blurry). `GuardWindowAsync` snapshots the style and, while active, synchronously blocks the caption drop via hooks on `user32!SetWindowLongPtrW/A`, `SetWindowLongW/A` and `win32u!NtUserSetWindowLongPtr`/`NtUserSetWindowLong` (2ms polling alone loses the race — verified in-game). The guard must be up before any rebuild is triggered.

**Do not use `IGameConfig.Set` for this**: it fires the game's config-change callback, which re-applies graphics settings and corrupts the window in windowed mode (goatcorp/Dalamud#2964). The struct write + guarded rebuild replaces that path.

**Runtime enum**: `GraphicsConfig->GraphicsRezoUpscaleType` is NOT the config-file enum (cfg: `0=FSR, 1=DLSS`; runtime: `0=Linear/none, 1=FSR, 2=DLSS` — from DP-CustomResolution's mapping, verified on CN 7.55). Writing a config-enum value into the struct silently leaves the game on FSR.

**Diagnostics**: `/pupscaler sizes` (device/client/outer/style), `get` (struct vs cfg dump), `set <n>` (raw runtime value), `scale <pct>` (live 3D resolution), `reset [w h]` (manual guarded rebuild), `cfg <n>` (config path — reproduces the window bug, kept for reference).

**Key files**:
- `Plugin.cs` — plugin lifecycle, command handler (`/pupscaler`), framework update hook, toggle logic
- `Configuration.cs` — `IPluginConfiguration`: `Enabled`, `LoginDelaySeconds`
- `Windows/ConfigWindow.cs` — ImGui config window using Dalamud `Window` base class

**Dalamud API Level**: 15. Services used: `IDalamudPluginInterface`, `IClientState`, `IFramework`, `ICommandManager`, `IPluginLog`, `IChatGui`, `IGameInteropProvider` (user32/win32u style hooks). Game memory access via FFXIVClientStructs (`FFXIV.Client.Graphics.Render.GraphicsConfig`, `FFXIV.Client.Graphics.Kernel.Device`).

**Important**: The toggle sequence (`DoToggleSequence`) runs via `Task.Run` — all game access (`GraphicsConfig` struct read/write) is marshaled to the framework thread via `Framework.RunOnFrameworkThread`; only logging happens off-thread.
