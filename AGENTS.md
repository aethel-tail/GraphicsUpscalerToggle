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

**Core flow**: `Framework.Update` detects login → `DoToggleSequence()` runs async → starts the window guard → `IGameConfig.Set(GraphicsRezoUpscaleType, 0)` (FSR) → waits 3s → `Set(..., 1)` (DLSS) → waits for the guard to settle → dumps state. The config path makes the game run its display apply, and that is what re-creates the DLSS feature — i.e. what re-engages DLSS and lets OptiScaler take over.

**Why the config path (hard-won)**: verified in-game that a direct `GraphicsConfig->GraphicsRezoUpscaleType = 2` write *and* a soft `Device->RequestResolutionChange` both left the game at ~60 fps while the config/UI apply reached ~84 fps — with byte-identical `GraphicsConfig`/`Device`/cfg state in both cases. The difference is internal: only the game's own display apply re-creates the DLSS feature. Do not "optimise" this back into a struct write.

**Window guard (why it exists)**: the display apply drops `WS_CAPTION` and then sizes its render targets off the now-captionless client rect (3840x1080 client ⇒ 3862x1136 render ⇒ blurry). `GuardWindowAsync` snapshots the style and, while active, blocks the caption drop synchronously through hooks on `user32!SetWindowLongPtrW/A`, `SetWindowLongW/A` and `win32u!NtUserSetWindowLongPtr`/`NtUserSetWindowLong` (2ms polling alone loses the race — verified in-game). The guard must be up *before* the first `Set` and stay up for the whole apply (the apply is slow, hence the raised stability window).

**Two enums, do not mix them**: config file / `IGameConfig` is `0=FSR, 1=DLSS`; the runtime `GraphicsConfig->GraphicsRezoUpscaleType` is `0=Linear/none, 1=FSR, 2=DLSS` (confirmed via DP-CustomResolution's mapping, verified on CN 7.55). Writing one into the other fails silently on the wrong upscaler.

**Diagnostics**: `/pupscaler sizes` (device/client/outer/style), `get` (struct vs cfg dump incl. dynamic-resolution fields), `set <n>` (raw runtime value), `cfgseq` (config-path toggle with the guard — the current login mechanism), `cfg <n>` (unguarded config write, reproduces the window bug), `scale <pct>` (live 3D resolution), `reset [w h]` (soft rebuild).

**Key files**:
- `Plugin.cs` — plugin lifecycle, command handler (`/pupscaler`), framework update hook, toggle logic
- `Configuration.cs` — `IPluginConfiguration`: `Enabled`, `LoginDelaySeconds`
- `Windows/ConfigWindow.cs` — ImGui config window using Dalamud `Window` base class

**Dalamud API Level**: 15. Services used: `IDalamudPluginInterface`, `IClientState`, `IFramework`, `ICommandManager`, `IPluginLog`, `IChatGui`, `IGameInteropProvider` (user32/win32u style hooks). Game memory access via FFXIVClientStructs (`FFXIV.Client.Graphics.Render.GraphicsConfig`, `FFXIV.Client.Graphics.Kernel.Device`).

**Important**: The toggle sequence (`DoToggleSequence`) runs via `Task.Run` — all game access (`GraphicsConfig` struct read/write) is marshaled to the framework thread via `Framework.RunOnFrameworkThread`; only logging happens off-thread.
