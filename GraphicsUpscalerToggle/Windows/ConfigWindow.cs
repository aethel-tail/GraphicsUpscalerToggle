using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace GraphicsUpscalerToggle;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin) : base("Graphics Upscaler Toggle Settings###GUTConfig")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(300, 155);
        SizeCondition = ImGuiCond.Always;

        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var enabled = configuration.Enabled;
        if (ImGui.Checkbox("Enable Auto-Toggle", ref enabled))
        {
            configuration.Enabled = enabled;
            configuration.Save();
        }

        var loginDelay = configuration.LoginDelaySeconds;
        if (ImGui.SliderFloat("FSR Delay (s)", ref loginDelay, 0.1f, 5.0f, "%.1f"))
        {
            configuration.LoginDelaySeconds = loginDelay;
            configuration.Save();
        }

        var toggleInterval = configuration.ToggleIntervalSeconds;
        if (ImGui.SliderFloat("DLSS Delay (s)", ref toggleInterval, 0.5f, 10.0f, "%.1f"))
        {
            configuration.ToggleIntervalSeconds = toggleInterval;
            configuration.Save();
        }

        ImGui.Spacing();
        ImGui.Text("/pupscaler on|off|status|check");
    }
}
