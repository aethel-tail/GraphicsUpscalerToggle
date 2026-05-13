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
| FSR Delay (s) | 0.5 | Seconds to wait after login before toggling |
| DLSS Delay (s) | 3.0 | Seconds to wait after switching to FSR before switching back to DLSS |

## How It Works

1. Detects player login
2. Waits `FSR Delay` seconds
3. Reads current `GraphicsRezoUpscaleType` value
4. Sets to FSR (0)
5. Waits `DLSS Delay` seconds
6. Sets to DLSS (1)

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
