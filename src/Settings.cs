using BepInEx.Configuration;

namespace BelmontsCurseGraphics;

internal sealed class Settings
{
    private readonly ConfigFile config;
    private readonly ConfigEntry<bool> enabled;
    private readonly ConfigEntry<float> sharpness;

    internal Settings(ConfigFile config)
    {
        this.config = config;
        config.SaveOnConfigSet = false;
        enabled = config.Bind("FSR4", "Enabled", false, "Select FSR 4.1.1 Native AA in Settings > Video > Anti-aliasing.");
        sharpness = config.Bind("FSR4", "Sharpness", .2f, new ConfigDescription("FSR sharpening, 0 to 1. Also available in the Video menu.", new AcceptableValueRange<float>(0, 1)));
        config.Save();
    }

    internal Fsr4Options Read()
    {
        var options = new Fsr4Options(enabled.Value, sharpness.Value);
        options.Validate();
        return options;
    }

    internal void Save(Fsr4Options options)
    {
        options.Validate();
        enabled.Value = options.Enabled; sharpness.Value = options.Sharpness;
        config.Save();
    }
}
