using BepInEx.Configuration;

namespace BelmontsCurseGraphics;

internal sealed class Settings
{
    private readonly ConfigFile config;
    private readonly ConfigEntry<bool> enabled, reactive;
    private readonly ConfigEntry<float> sharpness;

    internal Settings(ConfigFile config)
    {
        this.config = config;
        config.SaveOnConfigSet = false;
        enabled = config.Bind("FSR31", "Enabled", false, "Select FSR 3.1 Native AA in Settings > Video > Anti-aliasing. Other AA choices return control to the game.");
        sharpness = config.Bind("FSR31", "Sharpness", .2f, new ConfigDescription("FSR RCAS sharpening, 0 to 1. Also available in the Video menu.", new AcceptableValueRange<float>(0, 1)));
        reactive = config.Bind("FSR31", "ReactiveMask", true, "Experimental motion-aware colour-change mask to reduce unreliable temporal history. Disable for comparison.");
        config.Save();
    }

    internal Fsr31Options Read()
    {
        var options = new Fsr31Options(enabled.Value, sharpness.Value, reactive.Value);
        options.Validate();
        return options;
    }

    internal void Save(Fsr31Options options)
    {
        options.Validate();
        enabled.Value = options.Enabled; sharpness.Value = options.Sharpness; reactive.Value = options.ReactiveMask;
        config.Save();
    }
}
