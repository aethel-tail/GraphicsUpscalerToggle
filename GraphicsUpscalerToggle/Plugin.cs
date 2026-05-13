using Dalamud.Game.Command;
using Dalamud.Game.Config;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using System.Threading.Tasks;

namespace GraphicsUpscalerToggle;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IGameConfig GameConfig { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;

    private const string CommandName = "/pupscaler";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("GraphicsUpscalerToggle");
    private ConfigWindow ConfigWindow { get; init; }

    private bool wasLoggedIn;
    private bool hasToggledThisSession;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        ConfigWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(ConfigWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle upscaler auto-switch on/off or show status"
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        Framework.Update += OnFrameworkUpdate;

        Log.Information($"GraphicsUpscalerToggle loaded. Enabled={Configuration.Enabled}");
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        CommandManager.RemoveHandler(CommandName);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!Configuration.Enabled)
            return;

        bool isLoggedIn = ClientState.IsLoggedIn;

        if (isLoggedIn && !wasLoggedIn && !hasToggledThisSession)
        {
            Log.Information("Login detected. Starting upscaler toggle sequence...");
            hasToggledThisSession = true;
            Task.Run(() => DoToggleSequence());
        }
        else if (!isLoggedIn)
        {
            wasLoggedIn = false;
            hasToggledThisSession = false;
        }

        wasLoggedIn = isLoggedIn;
    }

    private async Task DoToggleSequence()
    {
        int loginDelay = (int)(Configuration.LoginDelaySeconds * 1000);
        int toggleInterval = (int)(Configuration.ToggleIntervalSeconds * 1000);

        Log.Information($"Waiting {Configuration.LoginDelaySeconds}s before toggling...");
        await Task.Delay(loginDelay);

        // Read current upscaling value
        if (!TryGetUpscaleType(out uint originalValue))
        {
            Log.Error("Failed to read current GraphicsRezoUpscaleType. Aborting toggle.");
            return;
        }

        Log.Information($"Current GraphicsRezoUpscaleType = {originalValue} (0=FSR, 1=DLSS)");

        // Set to FSR (0)
        Log.Information("Switching to AMD FSR...");
        SetUpscaleType(0);

        await Task.Delay(toggleInterval);

        // Set to DLSS (1)
        Log.Information("Switching to NVIDIA DLSS...");
        SetUpscaleType(1);

        Log.Information("Upscaler toggle sequence complete.");
    }

    private bool TryGetUpscaleType(out uint value)
    {
        return GameConfig.TryGet(SystemConfigOption.GraphicsRezoUpscaleType, out value);
    }

    private void SetUpscaleType(uint value)
    {
        GameConfig.Set(SystemConfigOption.GraphicsRezoUpscaleType, value);
    }

    private void OnCommand(string command, string args)
    {
        args = args.Trim().ToLower();

        if (args == "on" || args == "enable")
        {
            Configuration.Enabled = true;
            Configuration.Save();
            ChatGui.Print("[UpscalerToggle] Auto-toggle enabled.");
        }
        else if (args == "off" || args == "disable")
        {
            Configuration.Enabled = false;
            Configuration.Save();
            ChatGui.Print("[UpscalerToggle] Auto-toggle disabled.");
        }
        else if (args == "status")
        {
            ChatGui.Print($"[UpscalerToggle] Auto-toggle is {(Configuration.Enabled ? "enabled" : "disabled")}");
        }
        else
        {
            ChatGui.Print("[UpscalerToggle] Usage: /pupscaler on|off|status");
        }
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
}
