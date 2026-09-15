# Graphics Upscaler Toggle

[中文](README.md)

A FFXIV Dalamud plugin that automatically toggles graphics upscaling from DLSS to FSR and back on login, working around a bug where DLSS does not properly engage on initial game load.

## Installation

Copy the `GraphicsUpscalerToggle` folder into Dalamud's `installedPlugins` directory.

## Usage

Use the `/pupscaler` command:

| Command | Description |
|---------|-------------|
| `/pupscaler on` / `enable` | Enable auto-toggle |
| `/pupscaler off` / `disable` | Disable auto-toggle |
| `/pupscaler status` | Show current status |

## Configuration

Open via `/pupscaler` → settings window, or through Dalamud plugin settings.

| Setting | Default | Description |
|---------|---------|-------------|
| Enable Auto-Toggle | On | Whether to run the toggle sequence on login |
| Login Delay (s) | 0.5 | Seconds to wait after login before triggering the rebuild |

## How It Works

1. Detects player login
2. Waits `Login Delay` seconds
3. Starts the window guard (blocks the game from dropping the window frame style while it re-applies display settings)
4. Sets the upscaler to FSR (config enum 0) through `IGameConfig.Set`, waits 3 seconds
5. Sets it back to DLSS (config enum 1) — this is what makes the game re-apply display settings and **re-create the DLSS feature**, which is what actually engages DLSS (and lets tools like OptiScaler take over)

> Why the config path is required: measured in-game, both a direct `GraphicsConfig` struct write and a soft `RequestResolutionChange` settle at ~60 fps while the config path reaches ~84 fps — with byte-identical `GraphicsConfig`/`Device`/cfg state in both cases. The difference is the renderer's internal DLSS feature creation, which a struct write never triggers.
>
> Also note: **the two enums must not be mixed** — config file / `IGameConfig` uses `0=FSR, 1=DLSS`, the runtime `GraphicsConfig` uses `0=none, 1=FSR, 2=DLSS`. Writing one into the other fails silently on the wrong upscaler.

## Build

```bash
dotnet build                 # Debug
dotnet build -c Release      # Release
```

Requires Dalamud SDK 15.0.0, targets .NET 10, x64.

## License

AGPL-3.0-or-later

## Acknowledgments

This plugin was developed with assistance from DeepSeek V4 Pro.
