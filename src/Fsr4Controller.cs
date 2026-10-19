using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using MotherBase.ApplicationFlowManager;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BelmontsCurseGraphics;

public sealed class Fsr4Controller : MonoBehaviour
{
    internal static Fsr4Controller? Instance;
    internal Fsr4Options Current { get; private set; } = new();
    internal bool Fsr4Active { get; private set; }
    internal string Status { get; private set; } = "Game AA";
    internal string? UnavailableReason { get; private set; }
    private Snapshot? snapshot;
    private bool failed;
    private float nextMenuRefresh;

    public Fsr4Controller(IntPtr pointer) : base(pointer) { }
    public void Awake()
    {
        Instance = this;
        try { Current = Plugin.Settings.Read(); }
        catch (Exception e) { Plugin.Logger.LogWarning($"Invalid FSR settings; using game AA: {e.Message}"); }
    }

    public void Update()
    {
        if (Time.unscaledTime < nextMenuRefresh) return;
        nextMenuRefresh = Time.unscaledTime + .25f;
        try { NativeVideoMenu.Tick(); }
        catch (Exception e) { Plugin.Logger.LogWarning($"FSR Video menu refresh failed: {e.Message}"); }
    }

    public void LateUpdate()
    {
        try
        {
            var camera = Camera.main;
            if (!Current.Enabled || failed || ApplicationFlowManager.IsLoading || !camera
                || camera.gameObject.scene.name != "Player" || camera.targetTexture)
            {
                Deactivate(Current.Enabled ? "FSR selected; waiting for the gameplay camera" : "Game AA");
                return;
            }
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            var pipeline = GraphicsSettings.currentRenderPipeline?.TryCast<UniversalRenderPipelineAsset>();
            if (!data || !pipeline)
            {
                UnavailableReason = "Unsupported renderer.";
                Deactivate("FSR unavailable: unsupported renderer; using game AA"); return;
            }
            var failure = Requirements(camera, data!, pipeline!).Failure;
            if (failure != null)
            {
                UnavailableReason = Fsr4Backend.Available ? failure : Fsr4Backend.UnavailableReason;
                Deactivate("FSR unavailable; using game AA. " + UnavailableReason);
                return;
            }
            if (snapshot == null || !snapshot.Matches(camera, pipeline!))
            {
                Restore();
                snapshot = new Snapshot(camera, data!, pipeline!);
            }
            UnavailableReason = null;
            Fsr4Backend.Tick(true);
            snapshot.Apply();
            Fsr4Active = true;
            Report(Fsr4Backend.Dispatching ? $"FSR 4.1.1 Native AA dispatching at {camera.pixelWidth}x{camera.pixelHeight}, 100%"
                : "FSR 4.1.1 Native AA selected; awaiting first frame");
        }
        catch (Exception e)
        {
            failed = true; UnavailableReason = e.Message;
            Deactivate("FSR stopped; using game AA. See BepInEx log.");
            Plugin.Logger.LogError($"FSR camera overrides restored after an error: {e}");
        }
    }

    [HideFromIl2Cpp]
    private static Fsr4Requirements Requirements(Camera camera, UniversalAdditionalCameraData data, UniversalRenderPipelineAsset pipeline)
    {
        var index = data.m_RendererIndex < 0 ? pipeline.m_DefaultRendererIndex : data.m_RendererIndex;
        var renderers = pipeline.m_RendererDataList;
        var rendererData = index >= 0 && index < renderers.Length ? renderers[index]?.TryCast<UniversalRendererData>() : null;
        var shader = rendererData?.postProcessData?.shaders?.temporalAntialiasingPS;
        var renderer = data.scriptableRenderer?.TryCast<UniversalRenderer>();
        return new(Fsr4Backend.Available, data.renderPostProcessing, shader && shader!.isSupported,
            renderer != null && renderer.m_MotionVectorPass != null,
            data.renderType != CameraRenderType.Base || (data.cameraStack?.Count ?? 0) > 0,
            camera.allowDynamicResolution, camera.allowHDR && pipeline.supportsHDR);
    }

    [HideFromIl2Cpp]
    internal void Apply(Fsr4Options options)
    {
        options.Validate();
        Plugin.Settings.Save(options);
        if (options != Current)
            Plugin.Logger.LogInfo($"FSR settings: enabled={options.Enabled}, sharpening={options.Sharpness:F2}");
        if (options.Enabled && !Current.Enabled) Fsr4Backend.Retry();

        Current = options; failed = false; UnavailableReason = null;
        if (!options.Enabled) Deactivate("Game AA");
    }

    [HideFromIl2Cpp]
    internal void RecordGameAa(UniversalAdditionalCameraData? data)
    {
        var state = snapshot;
        if (data && state != null && state.Data == data) state.NativeAa = data!.antialiasing;
    }

    [HideFromIl2Cpp]
    private void Deactivate(string status)
    {
        Fsr4Active = false;
        Restore();
        Fsr4Backend.Tick(false);
        Report(status);
    }

    [HideFromIl2Cpp]
    private void Report(string status)
    {
        if (status == Status) return;
        Status = status; Plugin.Logger.LogInfo(status);
    }

    [HideFromIl2Cpp]
    private void Restore()
    {
        try { snapshot?.Restore(); }
        finally { snapshot = null; }
    }

    [HideFromIl2Cpp]
    internal void Shutdown()
    {
        Fsr4Active = false;
        try { Restore(); }
        finally { NativeVideoMenu.Shutdown(); Fsr4Backend.Shutdown(); }
    }

    public void OnDestroy() { Shutdown(); if (Instance == this) Instance = null; }

    private sealed class Snapshot
    {
        private readonly Camera camera;
        internal readonly UniversalAdditionalCameraData Data;
        private readonly UniversalRenderPipelineAsset pipeline;
        internal AntialiasingMode NativeAa;
        private readonly bool allowMsaa;
        private readonly float scale, jitter, sharpening;
        private readonly int msaa;
        private readonly UpscalingFilterSelection filter;
        private readonly CameraOverrideOption depthOption;
        private bool applied;

        internal Snapshot(Camera camera, UniversalAdditionalCameraData data, UniversalRenderPipelineAsset pipeline)
        {
            this.camera = camera; Data = data; this.pipeline = pipeline;
            NativeAa = data.antialiasing; allowMsaa = camera.allowMSAA;
            depthOption = data.requiresDepthOption;
            jitter = data.m_TaaSettings.jitterScale; sharpening = data.m_TaaSettings.contrastAdaptiveSharpening;
            scale = pipeline.renderScale; msaa = pipeline.msaaSampleCount; filter = pipeline.upscalingFilter;
        }

        internal bool Matches(Camera other, UniversalRenderPipelineAsset asset) => camera == other && pipeline == asset;

        internal void Apply()
        {
            var reset = !applied || Data.antialiasing != AntialiasingMode.TemporalAntiAliasing
                || pipeline.renderScale != 1 || pipeline.msaaSampleCount != 1;
            pipeline.renderScale = 1; pipeline.msaaSampleCount = 1; pipeline.upscalingFilter = UpscalingFilterSelection.Auto;
            camera.allowMSAA = false;
            Data.requiresDepthOption = CameraOverrideOption.On;
            // URP still produces jitter, depth and motion; FSR replaces only its resolve.
            Data.antialiasing = AntialiasingMode.TemporalAntiAliasing;
            var tuning = Data.m_TaaSettings;
            tuning.jitterScale = 1; tuning.contrastAdaptiveSharpening = 0;
            Data.m_TaaSettings = tuning;
            if (reset) Data.resetHistory = true;
            applied = true;
        }

        internal void Restore()
        {
            if (pipeline) { pipeline.renderScale = scale; pipeline.msaaSampleCount = msaa; pipeline.upscalingFilter = filter; }
            if (!camera || !Data || !applied) return;
            Data.antialiasing = NativeAa; camera.allowMSAA = allowMsaa;
            Data.requiresDepthOption = depthOption;
            var tuning = Data.m_TaaSettings;
            tuning.jitterScale = jitter; tuning.contrastAdaptiveSharpening = sharpening;
            Data.m_TaaSettings = tuning;
            Data.resetHistory = true;
            applied = false;
        }
    }
}
