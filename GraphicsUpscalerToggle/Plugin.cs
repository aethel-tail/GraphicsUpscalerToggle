using Dalamud.Game.Command;
using Dalamud.Game.Config;
using Dalamud.Hooking;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace GraphicsUpscalerToggle;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IGameConfig GameConfig { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;

    private const string CommandName = "/pupscaler";

    // ponytail: fixed 1s settle; make configurable only if a rebuild ever needs longer
    private const int RebuildSettleMs = 1000;

    // Runtime enum in GraphicsConfig — NOT the config-file enum (cfg file uses 0=FSR, 1=DLSS).
    // Runtime: 0=Linear/none, 1=FSR, 2=DLSS (confirmed via DP-CustomResolution's mapping + CN 7.55 test).
    private const byte DlssRuntimeValue = 2;

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("GraphicsUpscalerToggle");
    private ConfigWindow ConfigWindow { get; init; }

    private bool wasLoggedIn;
    private bool hasToggledThisSession;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate long SetWindowLongPtrDelegate(IntPtr hWnd, int nIndex, long dwNewLong);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetWindowLongWDelegate(IntPtr hWnd, int nIndex, int dwNewLong);

    // win32u is where user32's wrappers end up; the game appears to call this layer directly.
    // Declaring an extra trailing "ansi" flag is safe either way (x64 ignores unused register args).
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr NtUserSetWindowLongPtrDelegate(IntPtr hWnd, int nIndex, IntPtr dwNewLong, uint ansi);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int NtUserSetWindowLongDelegate(IntPtr hWnd, int nIndex, int dwNewLong, uint ansi);

    private static Hook<SetWindowLongPtrDelegate>? setWindowLongPtrHook;
    private static Hook<SetWindowLongWDelegate>? setWindowLongHook;
    private static Hook<SetWindowLongPtrDelegate>? setWindowLongPtrAHook;
    private static Hook<SetWindowLongWDelegate>? setWindowLongAHook;
    private static Hook<NtUserSetWindowLongPtrDelegate>? ntUserSetWindowLongPtrHook;
    private static Hook<NtUserSetWindowLongDelegate>? ntUserSetWindowLongHook;
    private static IntPtr guardedHwnd;
    private static long guardedStyle;
    private static volatile bool guardActive;

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

        // The game's rebuild path drops WS_CAPTION, then immediately reads the (now borderless) client
        // rect to size its render targets — polling can't win that race, so intercept style changes
        // synchronously at every layer. Only active while the window guard is running.
        setWindowLongPtrHook = HookSymbol<SetWindowLongPtrDelegate>("user32.dll", "SetWindowLongPtrW", SetWindowLongPtrDetour);
        setWindowLongHook = HookSymbol<SetWindowLongWDelegate>("user32.dll", "SetWindowLongW", SetWindowLongDetour);
        setWindowLongPtrAHook = HookSymbol<SetWindowLongPtrDelegate>("user32.dll", "SetWindowLongPtrA", SetWindowLongPtrDetour);
        setWindowLongAHook = HookSymbol<SetWindowLongWDelegate>("user32.dll", "SetWindowLongA", SetWindowLongDetour);
        ntUserSetWindowLongPtrHook = HookSymbol<NtUserSetWindowLongPtrDelegate>("win32u.dll", "NtUserSetWindowLongPtr", NtUserSetWindowLongPtrDetour);
        ntUserSetWindowLongHook = HookSymbol<NtUserSetWindowLongDelegate>("win32u.dll", "NtUserSetWindowLong", NtUserSetWindowLongDetour);

        Log.Information($"GraphicsUpscalerToggle loaded. Enabled={Configuration.Enabled}");
    }

    public void Dispose()
    {
        guardActive = false;
        setWindowLongPtrHook?.Dispose();
        setWindowLongHook?.Dispose();
        setWindowLongPtrAHook?.Dispose();
        setWindowLongAHook?.Dispose();
        ntUserSetWindowLongPtrHook?.Dispose();
        ntUserSetWindowLongHook?.Dispose();

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
        var loginDelay = (int)(Configuration.LoginDelaySeconds * 1000);
        Log.Information($"Waiting {Configuration.LoginDelaySeconds}s before toggling...");
        await Task.Delay(loginDelay);

        // The engine's rebuild drops WS_CAPTION for a moment and then sizes its render targets off the
        // (captionless) client rect, so the guard and the style hooks must be up before we trigger it.
        var guard = GuardWindowAsync(Process.GetCurrentProcess().MainWindowHandle, TimeSpan.FromSeconds(20));

        var originalValue = await Framework.RunOnFrameworkThread(GetUpscaleType);
        Log.Information($"Current GraphicsRezoUpscaleType = {originalValue} (runtime enum: 0=Linear, 1=FSR, 2=DLSS)");
        if (originalValue != DlssRuntimeValue)
        {
            await Framework.RunOnFrameworkThread(() => SetUpscaleType(DlssRuntimeValue));
            Log.Information($"Set GraphicsRezoUpscaleType = {DlssRuntimeValue} (DLSS)");
        }

        // Making the engine rebuild its render targets is what re-creates the upscaler, which is what
        // actually re-engages DLSS after login (and lets OptiScaler hook in). A plain struct write only
        // changes the stored value — the renderer never notices.
        var client = await Framework.RunOnFrameworkThread(GetClientSize);
        Log.Information($"Requesting render rebuild at {client.Width}x{client.Height} (current client size)");
        await Framework.RunOnFrameworkThread(() => RequestRenderReset(true, client.Width, client.Height));
        await Task.Delay(RebuildSettleMs);
        await Framework.RunOnFrameworkThread(() => RequestRenderReset(false, null, null));

        await guard;
        await Framework.RunOnFrameworkThread(LogSizes);
        Log.Information("Upscaler toggle sequence complete.");
    }

    private static (uint Width, uint Height) GetClientSize()
    {
        NativeMethods.GetClientRect(Process.GetCurrentProcess().MainWindowHandle, out var client);
        return ((uint)client.Right, (uint)client.Bottom);
    }

    private static unsafe byte GetUpscaleType()
    {
        return GraphicsConfig.Instance()->GraphicsRezoUpscaleType;
    }

    private static unsafe void SetUpscaleType(byte value)
    {
        GraphicsConfig.Instance()->GraphicsRezoUpscaleType = value;
    }

    private static unsafe void SetRezoScale(float scale)
    {
        var g = GraphicsConfig.Instance();
        var old = g->GraphicsRezoScale;
        g->GraphicsRezoScale = scale;
        Log.Information($"[scale] GraphicsRezoScale {old} -> {g->GraphicsRezoScale} (upscaleType={g->GraphicsRezoUpscaleType})");
    }

    private static unsafe void RequestRenderReset(bool start, uint? width, uint? height)
    {
        var dev = Device.Instance();
        if (start)
        {
            var w = width ?? dev->Width;
            var h = height ?? dev->Height;
            Log.Information($"[reset] device {dev->Width}x{dev->Height}, requesting {w}x{h}");
            dev->NewWidth = w;
            dev->NewHeight = h;
            dev->RequestResolutionChange = 1;
        }
        else
        {
            dev->RequestResolutionChange = 0;
        }
    }

    /// <summary>
    /// Restores the window frame style whenever the game's rebuild path drops it, until the window has
    /// been stable for a while. Fixes the border instantly (instead of seconds later) so the game keeps
    /// sizing its render targets against the correct client area.
    /// </summary>
    private static Hook<T>? HookSymbol<T>(string module, string symbol, T detour) where T : Delegate
    {
        try
        {
            var hook = GameInteropProvider.HookFromSymbol(module, symbol, detour);
            hook.Enable();
            Log.Information($"[hook] {module}!{symbol} hooked");
            return hook;
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"[hook] failed to hook {module}!{symbol}");
            return null;
        }
    }

    private static long SetWindowLongPtrDetour(IntPtr hWnd, int nIndex, long dwNewLong)
    {
        if (guardActive && hWnd == guardedHwnd && nIndex == NativeMethods.GWL_STYLE &&
            (dwNewLong & NativeMethods.WS_CAPTION) == 0)
        {
            var patched = dwNewLong | (guardedStyle & NativeMethods.WS_CAPTION);
            Log.Information($"[hook] blocked style drop 0x{dwNewLong:X} -> 0x{patched:X}");
            dwNewLong = patched;
        }

        return setWindowLongPtrHook!.Original(hWnd, nIndex, dwNewLong);
    }

    private static int SetWindowLongDetour(IntPtr hWnd, int nIndex, int dwNewLong)
    {
        if (guardActive && hWnd == guardedHwnd && nIndex == NativeMethods.GWL_STYLE &&
            (dwNewLong & (int)NativeMethods.WS_CAPTION) == 0)
        {
            var patched = dwNewLong | (int)(guardedStyle & NativeMethods.WS_CAPTION);
            Log.Information($"[hook] blocked style drop (A/W) 0x{dwNewLong:X} -> 0x{patched:X}");
            dwNewLong = patched;
        }

        return setWindowLongHook!.Original(hWnd, nIndex, dwNewLong);
    }

    private static IntPtr NtUserSetWindowLongPtrDetour(IntPtr hWnd, int nIndex, IntPtr dwNewLong, uint ansi)
    {
        var value = dwNewLong.ToInt64();
        if (guardActive && hWnd == guardedHwnd && nIndex == NativeMethods.GWL_STYLE &&
            (value & NativeMethods.WS_CAPTION) == 0)
        {
            var patched = value | (guardedStyle & NativeMethods.WS_CAPTION);
            Log.Information($"[hook] blocked style drop (win32u/ptr) 0x{value:X} -> 0x{patched:X} ansi={ansi}");
            dwNewLong = (IntPtr)patched;
        }

        return ntUserSetWindowLongPtrHook!.Original(hWnd, nIndex, dwNewLong, ansi);
    }

    private static int NtUserSetWindowLongDetour(IntPtr hWnd, int nIndex, int dwNewLong, uint ansi)
    {
        if (guardActive && hWnd == guardedHwnd && nIndex == NativeMethods.GWL_STYLE &&
            (dwNewLong & (int)NativeMethods.WS_CAPTION) == 0)
        {
            var patched = dwNewLong | (int)(guardedStyle & NativeMethods.WS_CAPTION);
            Log.Information($"[hook] blocked style drop (win32u) 0x{dwNewLong:X} -> 0x{patched:X} ansi={ansi}");
            dwNewLong = patched;
        }

        return ntUserSetWindowLongHook!.Original(hWnd, nIndex, dwNewLong, ansi);
    }

    private static async Task GuardWindowAsync(IntPtr hwnd, TimeSpan maxDuration)
    {
        var savedStyle = NativeMethods.GetWindowLongPtrW(hwnd, NativeMethods.GWL_STYLE);
        Log.Information($"[guard] start: style=0x{savedStyle:X}");

        guardedHwnd = hwnd;
        guardedStyle = savedStyle;
        guardActive = true;

        var started = Environment.TickCount64;
        var lastChange = started;
        var repairs = 0;

        while (Environment.TickCount64 - started < maxDuration.TotalMilliseconds &&
               Environment.TickCount64 - lastChange < 3000)
        {
            var style = NativeMethods.GetWindowLongPtrW(hwnd, NativeMethods.GWL_STYLE);
            if (style != savedStyle)
            {
                NativeMethods.SetWindowLongPtrW(hwnd, NativeMethods.GWL_STYLE, savedStyle);
                NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_FRAMECHANGED);
                repairs++;
                lastChange = Environment.TickCount64;
                Log.Information($"[guard] style 0x{style:X} -> restored 0x{savedStyle:X} (#{repairs} after {lastChange - started}ms) {SizeSummary()}");
            }

            await Task.Delay(2);
        }

        Log.Information($"[guard] done after {Environment.TickCount64 - started}ms, {repairs} repair(s)");
        guardActive = false;
    }

    private static unsafe string SizeSummary()
    {
        var dev = Device.Instance();
        var hwnd = Process.GetCurrentProcess().MainWindowHandle;
        NativeMethods.GetClientRect(hwnd, out var client);
        NativeMethods.GetWindowRect(hwnd, out var outer);
        return $"[sizes] device {dev->Width}x{dev->Height} | client {client.Right}x{client.Bottom} | " +
               $"outer {outer.Right - outer.Left}x{outer.Bottom - outer.Top} | style=0x{NativeMethods.GetWindowLongPtrW(hwnd, NativeMethods.GWL_STYLE):X}";
    }

    private static unsafe void LogSizes()
    {
        Log.Information(SizeSummary());
    }

    /// <summary>
    /// Dumps GraphicsConfig fields next to their config-file counterparts so the runtime enum and the
    /// struct offsets can be verified against a known-good cfg.
    /// </summary>
    private static unsafe void DumpState()
    {
        var g = GraphicsConfig.Instance();
        Log.Information($"[dump] struct: RezoType={g->GraphicsRezoUpscaleType} RezoScale={g->GraphicsRezoScale} " +
                        $"ReflectionType={g->ReflectionType} ShadowLOD={g->ShadowLOD} Tessellation={g->Tessellation} " +
                        $"GlareRepr={g->GlareRepresentation} DynRezoThr={g->DynamicRezoThreshold} " +
                        $"GrassDynInterf={g->GrassEnableDynamicInterference} DynRezoCutScene={g->DynamicRezoEnableCutScene} Gamma={g->Gamma}");

        string[] keys =
        [
            "GraphicsRezoUpscaleType", "GraphicsRezoScale", "ReflectionType_DX11", "ShadowLOD_DX11",
            "Tessellation_DX11", "GlareRepresentation_DX11", "DynamicRezoThreshold",
            "GrassEnableDynamicInterference", "DynamicRezoEnableCutScene", "Gamma",
        ];
        foreach (var key in keys)
        {
            Log.Information(GameConfig.System.TryGetUInt(key, out var v)
                ? $"[dump] cfg {key} = {v}"
                : $"[dump] cfg {key} = <missing>");
        }
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
        else if (args == "toggle")
        {
            ChatGui.Print("[UpscalerToggle] Manual toggle sequence started.");
            Task.Run(() => DoToggleSequence());
        }
        else if (args.StartsWith("scale "))
        {
            // Changing GraphicsRezoScale makes the engine resize its render targets, which is a
            // candidate for re-creating the upscaler (and thus letting OptiScaler hook in) without
            // going through the config apply that breaks the window.
            if (float.TryParse(args[6..].Trim(), out var percent) && percent is >= 1 and <= 200)
            {
                Framework.RunOnFrameworkThread(() => SetRezoScale(percent / 100f));
                ChatGui.Print($"[UpscalerToggle] GraphicsRezoScale = {percent}%");
            }
            else
            {
                ChatGui.Print("[UpscalerToggle] Usage: /pupscaler scale <1-200>  (3D resolution %)");
            }
        }
        else if (args == "reset" || args.StartsWith("reset "))
        {
            // Soft render-target rebuild: this is what makes the game re-create its upscaler (and thus
            // lets OptiScaler hook in). Optional explicit target size bypasses the game's own (broken)
            // client-size read: /pupscaler reset 3840 1080
            var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            uint? targetW = null, targetH = null;
            if (parts.Length >= 3 && uint.TryParse(parts[1], out var pw) && uint.TryParse(parts[2], out var ph))
            {
                targetW = pw;
                targetH = ph;
            }

            Task.Run(async () =>
            {
                var hwnd = Process.GetCurrentProcess().MainWindowHandle;
                var guard = GuardWindowAsync(hwnd, TimeSpan.FromSeconds(20));

                await Framework.RunOnFrameworkThread(() => RequestRenderReset(true, targetW, targetH));
                await Task.Delay(1000);
                await Framework.RunOnFrameworkThread(() => RequestRenderReset(false, null, null));

                await guard;
                await Framework.RunOnFrameworkThread(LogSizes);
                ChatGui.Print("[UpscalerToggle] reset finished — check window");
            });
            ChatGui.Print(targetW is null
                ? "[UpscalerToggle] Requested resolution change (window guard active)"
                : $"[UpscalerToggle] Requested resolution change to {targetW}x{targetH} (window guard active)");
        }
        else if (args == "sizes")
        {
            LogSizes();
            ChatGui.Print("[UpscalerToggle] Sizes written to /xllog");
        }
        else if (args is "get" or "dump")
        {
            DumpState();
            ChatGui.Print("[UpscalerToggle] Dumped struct + cfg values to /xllog");
        }
        else if (args.StartsWith("set "))
        {
            if (byte.TryParse(args[4..].Trim(), out var value))
            {
                Framework.RunOnFrameworkThread(() =>
                {
                    SetUpscaleType(value);
                    Log.Information($"[set] wrote GraphicsRezoUpscaleType={value}, reads back {GetUpscaleType()}");
                });
                ChatGui.Print($"[UpscalerToggle] Set GraphicsRezoUpscaleType = {value} (check /xllog)");
            }
            else
            {
                ChatGui.Print("[UpscalerToggle] Usage: /pupscaler set <0-255>");
            }
        }
        else if (args.StartsWith("cfg "))
        {
            // Config path (cfg enum: 0=FSR, 1=DLSS) — this is what triggers the game's apply/re-init
            // that makes DLSS/OptiScaler engage, but it can break the window in windowed mode.
            if (uint.TryParse(args[4..].Trim(), out var value))
            {
                Framework.RunOnFrameworkThread(() =>
                {
                    GameConfig.Set(SystemConfigOption.GraphicsRezoUpscaleType, value);
                    Log.Information($"[cfg] IGameConfig.Set(GraphicsRezoUpscaleType, {value}) — struct now {GetUpscaleType()}");
                });
                ChatGui.Print($"[UpscalerToggle] cfg GraphicsRezoUpscaleType = {value}");
            }
            else
            {
                ChatGui.Print("[UpscalerToggle] Usage: /pupscaler cfg <0|1>  (0=FSR, 1=DLSS)");
            }
        }
        else if (args.StartsWith("mode "))
        {
            // Test whether the game's own display-mode apply repairs the window cleanly when driven
            // through IGameConfig (instead of poking window styles with Win32).
            if (uint.TryParse(args[5..].Trim(), out var value))
            {
                Framework.RunOnFrameworkThread(() =>
                {
                    GameConfig.Set(SystemConfigOption.ScreenMode, value);
                    Log.Information($"[mode] IGameConfig.Set(ScreenMode, {value})");
                });
                ChatGui.Print($"[UpscalerToggle] ScreenMode = {value}");
            }
            else
            {
                ChatGui.Print("[UpscalerToggle] Usage: /pupscaler mode <0|1|2>  (0=windowed, 1=borderless, 2=fullscreen)");
            }
        }
        else
        {
            ChatGui.Print("[UpscalerToggle] Usage: on|off|status|toggle|get|set <n>|cfg <n>|mode <n>|scale <pct>|reset [w h]|sizes");
        }
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
}

internal static class NativeMethods
{
    public const int GWL_STYLE = -16;
    public const uint WS_CAPTION = 0x00C00000;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_FRAMECHANGED = 0x0020;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    public static extern long GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern long SetWindowLongPtrW(IntPtr hWnd, int nIndex, long dwNewLong);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
