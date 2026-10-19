using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine.Rendering.Universal;

namespace BelmontsCurseGraphics;

[BepInPlugin(Id, "FSR 3.1 Native AA", BuildInfo.Version)]
public sealed class Plugin : BasePlugin
{
    // Preserve the development plugin identity and installation path for upgrades.
    public const string Id = "ollieatkinson.belmontscursegraphics";
    internal static ManualLogSource Logger = null!;
    internal static Settings Settings = null!;
    private Harmony? harmony;

    public override void Load()
    {
        Logger = Log; Settings = new Settings(Config);
        Compatibility.Method(typeof(CameraVideoSettings), nameof(CameraVideoSettings.OnAntiAliasingChanged), typeof(void), typeof(Setting<int>));
        Compatibility.Field(typeof(CameraVideoSettings), "_cameraData", typeof(UniversalAdditionalCameraData));
        Compatibility.Field(typeof(UniversalRenderer), "m_DepthTexture", typeof(UnityEngine.Rendering.RTHandle));
        AddComponent<Fsr31Controller>();
        harmony = new Harmony(Id); harmony.PatchAll(typeof(Plugin).Assembly);
        Fsr31Prototype.Load();
        Log.LogInfo($"FSR 3.1 Native AA {BuildInfo.Version} loaded. Select Settings > Video > Anti-aliasing > FSR 3.1 Native AA.");
        Log.LogInfo($"Graphics device: {UnityEngine.SystemInfo.graphicsDeviceName}; API={UnityEngine.SystemInfo.graphicsDeviceType}");
    }

    public override bool Unload()
    {
        Fsr31Controller.Instance?.Shutdown();
        harmony?.UnpatchSelf();
        return true;
    }
}

[HarmonyPatch(typeof(CameraVideoSettings), nameof(CameraVideoSettings.OnAntiAliasingChanged))]
[HarmonyWrapSafe]
internal static class GameAaChanged
{
    private static void Postfix(CameraVideoSettings __instance) => Fsr31Controller.Instance?.RecordGameAa(__instance._cameraData);
}
