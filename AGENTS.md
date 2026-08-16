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

**Core flow**: `Framework.Update` detects login → `DoToggleSequence()` runs async (background task, not on game thread) → reads `GraphicsRezoUpscaleType` via `GameConfig.TryGet`, sets FSR (0), delays, sets DLSS (1). Purpose: work around a bug where DLSS doesn't engage on initial game load — the FSR→DLSS toggle forces it.

**Key files**:
- `Plugin.cs` — plugin lifecycle, command handler (`/pupscaler`), framework update hook, toggle logic
- `Configuration.cs` — `IPluginConfiguration`, three settings: `Enabled`, `LoginDelaySeconds`, `ToggleIntervalSeconds`
- `Windows/ConfigWindow.cs` — ImGui config window using Dalamud `Window` base class

**Dalamud API Level**: 15. Services used: `IDalamudPluginInterface`, `IGameConfig`, `IClientState`, `IFramework`, `ICommandManager`, `IPluginLog`, `IChatGui`.

**Important**: The toggle sequence (`DoToggleSequence`) runs via `Task.Run` — it must NOT touch game thread-only APIs. Only `GameConfig.Set`/`TryGet` and logging are used there.
