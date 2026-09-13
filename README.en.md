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
3. Ensures the upscaler is set to DLSS (runtime value 2)
4. Triggers an engine render-target rebuild — this is what re-creates the upscaler and actually engages DLSS (and lets tools like OptiScaler hook in)
5. A window guard runs throughout: it blocks the game from dropping `WS_CAPTION` during the rebuild, so the window border and resolution stay intact

> Note: the upscaler type is not changed via `IGameConfig.Set` — that fires the game's config-change callback, which re-applies graphics settings and corrupts the window in windowed mode (border lost, bogus resolution; see goatcorp/Dalamud#2964). The plugin uses a direct FFXIVClientStructs write to `GraphicsConfig` plus a guarded engine rebuild instead.
>
> Also note: **the runtime enum differs from the config file** — cfg uses `0=FSR, 1=DLSS`, the runtime struct uses `0=none, 1=FSR, 2=DLSS`. Writing a wrong value fails silently and leaves the game on FSR.

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
