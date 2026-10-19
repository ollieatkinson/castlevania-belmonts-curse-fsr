using System.Reflection;
using FidelityFX;
using FidelityFX.FSR3;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Hook;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace BelmontsCurseGraphics;

// Replace URP's temporal resolve at its existing injection point. URP continues
// to produce depth, motion vectors and jittered geometry, then performs its normal
// post-processing and HUD composition after the FSR resolve.
internal static class Fsr31Prototype
{
    private static AssetBundle? bundle;
    private static readonly List<ComputeShader> ownedShaders = new();
    private static Fsr3UpscalerShaders? shaders;
    private static Fsr3UpscalerContext? context;
    private static RenderTexture? output;
    private static ComputeShader? reactivityShader;
    private static RenderTexture? previousColour, reactiveMask;
    private static Vector2 previousJitter;
    private static bool previousColourValid;
    private static RTHandle? outputHandle;
    private static int cameraId, width, height, lastFrame = -1;
    private static Vector3 lastPosition;
    private static float lastSize;
    private static bool faulted;
    private static bool requestedLast;
    private static INativeDetour? resolveHook;
    private static ResolveDelegate originalResolve = null!;
    private static readonly ResolveDelegate resolveCallback = ResolveNative;

    // CameraData is a non-blittable IL2CPP value type; Harmony's bridge cannot
    // marshal it by ref. Retain the native pointer and box a read-only copy.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ResolveDelegate(IntPtr cmd, IntPtr material, IntPtr cameraData,
        IntPtr source, IntPtr destination, IntPtr motionVectors, IntPtr method);
    internal static bool Available => shaders != null && !faulted;
    internal static bool Dispatching => Available && lastFrame >= 0 && Time.frameCount - lastFrame <= 1;
    internal static string UnavailableReason { get; private set; } = "Shader package not loaded.";

    internal static void Load()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11 || !SystemInfo.supportsComputeShaders || !SystemInfo.usesReversedZBuffer)
                throw new NotSupportedException("This prototype requires DX11 compute shaders and reversed depth.");
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BelmontsCurseGraphics.Fsr31Shaders")
                ?? throw new FileNotFoundException("FSR 3.1 shader resource is missing.");
            using var memory = new MemoryStream(); stream.CopyTo(memory);
            bundle = AssetBundle.LoadFromMemory(new Il2CppStructArray<byte>(memory.ToArray()));
            if (!bundle) throw new InvalidOperationException("Unity rejected the FSR 3.1 shader bundle.");
            ComputeShader Get(string name)
            {
                var asset = bundle!.LoadAsset<ComputeShader>("assets/fpsdoctorshaders/ffx_fsr3upscaler_" + name + "_pass.compute");
                if (!asset || !asset.HasKernel("CS") || !asset.IsSupported(asset.FindKernel("CS")))
                    throw new NotSupportedException($"FSR 3.1 shader unavailable: {name}");
                // CLR references are not native Unity asset roots in IL2CPP.
                // Own a persistent copy, independent of the bundle and its unload.
                var shader = Object.Instantiate(asset);
                shader.name = "Belmont FSR31 " + name;
                shader.hideFlags = HideFlags.HideAndDontSave;
                ownedShaders.Add(shader);
                return shader;
            }
            shaders = new Fsr3UpscalerShaders
            {
                prepareInputsPass = Get("prepare_inputs"), lumaPyramidPass = Get("luma_pyramid"),
                shadingChangePyramidPass = Get("shading_change_pyramid"), shadingChangePass = Get("shading_change"),
                prepareReactivityPass = Get("prepare_reactivity"), lumaInstabilityPass = Get("luma_instability"),
                accumulatePass = Get("accumulate"), sharpenPass = Get("rcas"),
                autoGenReactivePass = Get("autogen_reactive"), tcrAutoGenPass = Get("tcr_autogen"), debugViewPass = Get("debug_view")
            };
            bundle!.Unload(true); bundle = null;
            LoadReactivityShader();
            ValidateShaders();
            var info = Compatibility.StaticMethod(typeof(TemporalAA), nameof(TemporalAA.ExecutePass), typeof(void),
                typeof(CommandBuffer), typeof(Material), typeof(CameraData).MakeByRefType(), typeof(RTHandle), typeof(RTHandle), typeof(RenderTexture));
            InstallResolve(info);
            UnavailableReason = "";
            Plugin.Logger.LogInfo("FSR 3.1 Native AA prototype: all 11 FSR compute shaders and the custom reactivity shader cloned, persistent and supported; source bundles unloaded.");
        }
        catch (Exception e)
        {
            shaders = null;
            ReleaseShaders();
            if (bundle) bundle!.Unload(true);
            bundle = null;
            UnavailableReason = e.Message;
            Plugin.Logger.LogWarning($"FSR 3.1 prototype unavailable; existing AA modes remain available: {e.Message}");
        }
    }

    private static void LoadReactivityShader()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BelmontsCurseGraphics.Fsr31Reactivity")
            ?? throw new FileNotFoundException("FSR reactivity shader resource is missing.");
        using var memory = new MemoryStream(); stream.CopyTo(memory);
        bundle = AssetBundle.LoadFromMemory(new Il2CppStructArray<byte>(memory.ToArray()));
        if (!bundle) throw new InvalidOperationException("Unity rejected the reactivity shader bundle.");
        var asset = bundle!.LoadAsset<ComputeShader>("assets/belmont_fsr31_reactivity.compute");
        if (!asset || !asset.HasKernel("CS") || !asset.IsSupported(asset.FindKernel("CS")))
            throw new NotSupportedException("Custom FSR reactivity kernel is unavailable.");
        reactivityShader = Object.Instantiate(asset);
        reactivityShader.name = "Belmont FSR31 reactivity";
        reactivityShader.hideFlags = HideFlags.HideAndDontSave;
        ownedShaders.Add(reactivityShader);
        bundle.Unload(true); bundle = null;
    }

    internal static void Retry()
    {
        if (!faulted || shaders == null) return;
        ReleaseContext(); faulted = false; UnavailableReason = "";
    }

    internal static void Tick(bool requested)
    {
        if (!requested)
        {
            if (context != null) ReleaseContext();
        }
        else if (!requestedLast) faulted = false;
        requestedLast = requested;
    }

    private static bool Wants(ref CameraData data)
    {
        var owner = Fsr31Controller.Instance;
        return Available && owner != null && owner.Fsr31Active && owner.Current.Enabled
            && data.camera && data.camera.gameObject.scene.name == "Player" && !data.camera.targetTexture
            && data.cameraTargetDescriptor.msaaSamples == 1 && !data.isHdrEnabled && data.renderType == CameraRenderType.Base
            && data.antialiasing == AntialiasingMode.TemporalAntiAliasing && !data.camera.allowDynamicResolution;
    }

    private static Vector2 JitterForFrame(CameraData data)
    {
        var matrix = data.m_JitterMatrix;
        var jitter = Fsr31Coordinates.JitterPixels(matrix.m03, matrix.m13,
            data.cameraTargetDescriptor.width, data.cameraTargetDescriptor.height, SystemInfo.graphicsUVStartsAtTop);
        return new Vector2(jitter.X, jitter.Y);
    }

    private static unsafe void InstallResolve(IntPtr info)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64) throw new PlatformNotSupportedException("FSR render hook requires x64.");
        if (!IL2CPP.il2cpp_class_is_valuetype(Il2CppClassPointerStore<CameraData>.NativeClassPtr))
            throw new InvalidOperationException("CameraData is no longer an IL2CPP value type.");
        var address = UnityVersionHandler.Wrap((Il2CppMethodInfo*)info).MethodPointer;
        if (address == IntPtr.Zero) throw new MissingMethodException("No native temporal resolve address.");
        resolveHook = INativeDetour.CreateAndApply<ResolveDelegate>(address, resolveCallback, out originalResolve);
    }

    private static unsafe void ResolveNative(IntPtr cmd, IntPtr material, IntPtr data, IntPtr source, IntPtr destination, IntPtr motion, IntPtr method)
    {
        try
        {
            if (Available && Fsr31Controller.Instance?.Fsr31Active == true)
            {
                var boxed = IL2CPP.il2cpp_value_box(Il2CppClassPointerStore<CameraData>.NativeClassPtr, data);
                var cameraData = new CameraData(boxed);
                if (Resolve(new CommandBuffer(cmd), ref cameraData, new RTHandle(source), new RTHandle(destination),
                    motion == IntPtr.Zero ? null! : new RenderTexture(motion))) return;
                if (faulted)
                {
                    // Propagate the fallback's history reset into the original
                    // unboxed argument, without hard-coded field offsets.
                    uint alignment = 0;
                    var size = IL2CPP.il2cpp_class_value_size(Il2CppClassPointerStore<CameraData>.NativeClassPtr, ref alignment);
                    if (size is > 0 and < 4096)
                        Buffer.MemoryCopy((void*)IL2CPP.il2cpp_object_unbox(boxed), (void*)data, size, size);
                }
            }
        }
        catch (Exception e)
        {
            faulted = true; UnavailableReason = e.Message;
            Plugin.Logger.LogError($"FSR native resolve wrapper stopped; using stock TAA: {e}");
        }
        originalResolve(cmd, material, data, source, destination, motion, method);
    }

    internal static bool Resolve(CommandBuffer cmd, ref CameraData data, RTHandle source, RTHandle destination, RenderTexture motionVectors)
    {
        if (!Wants(ref data)) return false;
        try
        {
            var camera = data.camera;
            var desc = data.cameraTargetDescriptor;
            if (desc.width != camera.pixelWidth || desc.height != camera.pixelHeight)
                throw new NotSupportedException("Native AA requires the camera to render at 100% scale.");
            if (!motionVectors) throw new InvalidOperationException("The camera supplied no motion-vector texture.");
            // _CameraDepthTexture is a shader-global alias, not a temporary RT ID.
            // Bind the renderer's actual copied depth texture and its depth plane.
            var depth = data.renderer?.TryCast<UniversalRenderer>()?.m_DepthTexture?.rt;
            if (!depth || !depth!.IsCreated() || depth.width != desc.width || depth.height != desc.height)
                throw new InvalidOperationException("The renderer supplied no full-resolution depth texture.");
            var depthPlane = depth.graphicsFormat == UnityEngine.Experimental.Rendering.GraphicsFormat.None
                ? RenderTextureSubElement.Depth : RenderTextureSubElement.Color;
            if (context == null || cameraId != camera.GetInstanceID() || width != desc.width || height != desc.height)
            {
                ReleaseContext();
                ValidateShaders();
                cameraId = camera.GetInstanceID(); width = desc.width; height = desc.height;
                output = new RenderTexture(width, height, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat)
                { name = "FSR31 Native AA output", hideFlags = HideFlags.HideAndDontSave,
                    enableRandomWrite = true, useMipMap = false, autoGenerateMips = false };
                if (!output.Create()) throw new InvalidOperationException("Could not allocate FSR 3.1 output.");
                previousColour = CreateTexture("FSR31 previous input", source.rt.graphicsFormat);
                reactiveMask = CreateTexture("FSR31 reactivity", UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm);
                outputHandle = RTHandles.Alloc(output);
                context = new Fsr3UpscalerContext();
                context.Create(new Fsr3Upscaler.ContextDescription
                {
                    MaxUpscaleSize = new Vector2Int(width, height), MaxRenderSize = new Vector2Int(width, height),
                    Flags = Fsr3Upscaler.InitializationFlags.EnableDepthInverted | Fsr3Upscaler.InitializationFlags.EnableAutoExposure,
                    Shaders = shaders
                });
                Plugin.Logger.LogInfo($"FSR 3.1 Native AA context created: {width}x{height}, 100%, reversed depth, FP32.");
                static string Describe(RenderTexture? texture) => texture
                    ? $"{texture!.name} {texture.width}x{texture.height} {texture.graphicsFormat}, depth={texture.depthStencilFormat}"
                    : "none";
                Plugin.Logger.LogInfo($"FSR inputs: colour={Describe(source.rt)}, motion={Describe(motionVectors)}, depth={Describe(depth)}, " +
                    $"RT scale={source.rtHandleProperties.rtHandleScale}, UV top={SystemInfo.graphicsUVStartsAtTop}, jitter matrix=({data.m_JitterMatrix.m03}, {data.m_JitterMatrix.m13}), FSR jitter={JitterForFrame(data)}");
            }
            if (lastFrame == Time.frameCount)
            {
                Blitter.BlitCameraTexture(cmd, outputHandle, destination);
                return true;
            }
            var position = camera.transform.position;
            var size = camera.orthographic ? camera.orthographicSize : camera.fieldOfView;
            var reset = lastFrame < 0 || data.taaSettings.resetHistoryFrames > 0 || Time.frameCount > lastFrame + 1 || Mathf.Abs(size - lastSize) > .01f
                || Vector3.Distance(position, lastPosition) > Math.Max(1, camera.orthographicSize * .5f);
            var options = Fsr31Controller.Instance!.Current;
            var jitter = JitterForFrame(data);
            var useMask = options.ReactiveMask;
            if (useMask) PrepareReactivity(cmd, source, motionVectors, jitter, reset);
            else previousColourValid = false;
            var dispatch = new Fsr3Upscaler.DispatchDescription
            {
                Color = new ResourceView(source.nameID), Depth = new ResourceView(new RenderTargetIdentifier(depth), depthPlane),
                MotionVectors = new ResourceView(new RenderTargetIdentifier(motionVectors)),
                Output = new ResourceView(new RenderTargetIdentifier(output)),
                Reactive = useMask ? new ResourceView(new RenderTargetIdentifier(reactiveMask)) : ResourceView.Unassigned,
                TransparencyAndComposition = useMask ? new ResourceView(new RenderTargetIdentifier(reactiveMask)) : ResourceView.Unassigned,
                JitterOffset = jitter, MotionVectorScale = new Vector2(-width, -height),
                RenderSize = new Vector2Int(width, height), UpscaleSize = new Vector2Int(width, height),
                PreExposure = 1, FrameTimeDelta = Time.unscaledDeltaTime,
                CameraNear = camera.nearClipPlane, CameraFar = camera.farClipPlane,
                CameraFovAngleVertical = camera.fieldOfView * Mathf.Deg2Rad, ViewSpaceToMetersFactor = 1,
                Reset = reset, EnableSharpening = options.Sharpness > 0, Sharpness = options.Sharpness,
                EnableAutoReactive = false
            };
            context!.Dispatch(dispatch, cmd);
            Blitter.BlitCameraTexture(cmd, outputHandle, destination);
            if (lastFrame < 0) Plugin.Logger.LogInfo($"FSR 3.1 Native AA is dispatching in place of the stock TAA resolve; motion-aware reactivity={useMask}.");
            lastFrame = Time.frameCount; lastPosition = position; lastSize = size;
            return true;
        }
        catch (Exception e)
        {
            faulted = true; UnavailableReason = e.Message;
            var nativeTaa = data.taaSettings;
            nativeTaa.resetHistoryFrames = 1;
            data.taaSettings = nativeTaa;
            Plugin.Logger.LogError($"FSR 3.1 prototype stopped; falling back to native TAA: {e}");
            return false;
        }
    }

    private static RenderTexture CreateTexture(string name, UnityEngine.Experimental.Rendering.GraphicsFormat format)
    {
        var texture = new RenderTexture(width, height, 0, format)
        { name = name, hideFlags = HideFlags.HideAndDontSave, enableRandomWrite = name == "FSR31 reactivity", useMipMap = false,
            autoGenerateMips = false, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        if (texture.Create()) return texture;
        Object.Destroy(texture);
        throw new InvalidOperationException($"Could not allocate {name}.");
    }

    private static void PrepareReactivity(CommandBuffer cmd, RTHandle colour, RenderTexture motion, Vector2 jitter, bool reset)
    {
        var shader = reactivityShader!;
        var kernel = shader.FindKernel("CS");
        cmd.SetComputeVectorParam(shader, Shader.PropertyToID("ReactivityParams"), new Vector4(1f / width, 1f / height, .04f, 8));
        cmd.SetComputeVectorParam(shader, Shader.PropertyToID("HistoryParams"), new Vector4(previousJitter.x - jitter.x,
            previousJitter.y - jitter.y, reset || !previousColourValid ? 1 : 0, 0));
        cmd.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("r_current_color"), colour.nameID);
        cmd.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("r_previous_color"), previousColour);
        cmd.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("r_motion"), motion);
        cmd.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("rw_reactive"), reactiveMask);
        cmd.DispatchCompute(shader, kernel, (width + 7) / 8, (height + 7) / 8, 1);
        cmd.CopyTexture(colour.nameID, new RenderTargetIdentifier(previousColour));
        previousJitter = jitter; previousColourValid = true;
    }

    private static void ReleaseContext()
    {
        try { context?.Destroy(); }
        finally
        {
            context = null; lastFrame = -1;
            previousColourValid = false;
            outputHandle?.Release(); outputHandle = null;
            if (output) { output!.Release(); Object.Destroy(output); }
            output = null;
            ReleaseTexture(ref previousColour); ReleaseTexture(ref reactiveMask);
        }
    }

    private static void ReleaseTexture(ref RenderTexture? texture)
    {
        if (texture) { texture!.Release(); Object.Destroy(texture); }
        texture = null;
    }

    internal static void Shutdown()
    {
        resolveHook?.Dispose(); resolveHook = null;
        ReleaseContext(); shaders = null;
        ReleaseShaders();
        if (bundle) bundle!.Unload(true);
        bundle = null;
    }

    private static void ReleaseShaders()
    {
        foreach (var shader in ownedShaders)
            if (shader) Object.Destroy(shader);
        ownedShaders.Clear();
        reactivityShader = null;
    }

    private static void ValidateShaders()
    {
        if (ownedShaders.Count != 12 || ownedShaders.Any(shader => !shader))
            throw new InvalidOperationException("A persistent FSR 3.1 shader was unloaded.");
    }
}
