using Dalamud.Configuration;
using System;

namespace GraphicsUpscalerToggle;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool Enabled { get; set; } = true;
    public float LoginDelaySeconds { get; set; } = 0.5f;
    public float ToggleIntervalSeconds { get; set; } = 3.0f;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
