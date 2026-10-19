using System.Reflection;
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
internal static class Fsr4Backend
{
    private static ComputeShader? pack;
    private static RenderTexture? colorInput, depthInput, motionInput, output;
    private static RTHandle? outputHandle;
    private static int cameraId, width, height, lastFrame = -1;
    private static Vector3 lastPosition;
    private static float lastSize;
    private static bool faulted;
    private static INativeDetour? resolveHook;
    private static ResolveDelegate originalResolve = null!;
    private static readonly ResolveDelegate resolveCallback = ResolveNative;

    // CameraData is a non-blittable IL2CPP value type; Harmony's bridge cannot
    // marshal it by ref. Retain the native pointer and box a read-only copy.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ResolveDelegate(IntPtr cmd, IntPtr material, IntPtr cameraData,
        IntPtr source, IntPtr destination, IntPtr motionVectors, IntPtr method);
    internal static bool Available => pack != null && !faulted && !NativeBridge.Failed;
    internal static bool Dispatching => Available && lastFrame >= 0 && Time.frameCount - lastFrame <= 1 && NativeBridge.ReadStatus().State.Frames > 0;
    private static string reason = "Bridge not loaded.";
    internal static string UnavailableReason => NativeBridge.Failed ? NativeBridge.ReadStatus().Message : reason;

    internal static void Load()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11 || !SystemInfo.supportsComputeShaders || !SystemInfo.usesReversedZBuffer)
                throw new NotSupportedException("FSR 4.1.1 requires DX11 compute shaders and reversed depth.");
            NativeBridge.Load();
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BelmontsCurseGraphics.Fsr4Pack")
                ?? throw new FileNotFoundException("FSR bridge pack shader is missing.");
            using var memory = new MemoryStream(); stream.CopyTo(memory);
            var bundle = AssetBundle.LoadFromMemory(new Il2CppStructArray<byte>(memory.ToArray()));
            if (!bundle) throw new InvalidOperationException("Unity rejected the FSR pack bundle.");
            try
            {
                var asset = bundle.LoadAsset<ComputeShader>("assets/belmont_fsr4_bridge_pack.compute");
                if (!asset || !asset.HasKernel("CS") || !asset.IsSupported(asset.FindKernel("CS")))
                    throw new NotSupportedException("FSR pack kernel is unavailable.");
                pack = Object.Instantiate(asset);
                pack.hideFlags = HideFlags.HideAndDontSave;
            }
            finally { bundle.Unload(true); }
            var info = Compatibility.StaticMethod(typeof(TemporalAA), nameof(TemporalAA.ExecutePass), typeof(void),
                typeof(CommandBuffer), typeof(Material), typeof(CameraData).MakeByRefType(), typeof(RTHandle), typeof(RTHandle), typeof(RenderTexture));
            InstallResolve(info);
            reason = "";
            Plugin.Logger.LogInfo("FSR 4.1.1 DX11/DX12 bridge and Unity pack shader loaded.");
        }
        catch (Exception e)
        {
            if (pack) Object.Destroy(pack);
            pack = null;
            reason = e.Message;
            Plugin.Logger.LogWarning($"FSR 4.1.1 unavailable; existing AA modes remain available: {e.Message}");
        }
    }

    internal static void Retry()
    {
        if (NativeBridge.Failed) return;
        ReleaseContext(); faulted = false; reason = "";
    }

    internal static void Tick(bool requested)
    {
        if (!requested && output) ReleaseContext();
    }

    private static bool Wants(ref CameraData data)
    {
        var owner = Fsr4Controller.Instance;
        return Available && owner != null && owner.Fsr4Active && owner.Current.Enabled
            && data.camera && data.camera.gameObject.scene.name == "Player" && !data.camera.targetTexture
            && data.cameraTargetDescriptor.msaaSamples == 1 && !data.isHdrEnabled && data.renderType == CameraRenderType.Base
            && data.antialiasing == AntialiasingMode.TemporalAntiAliasing && !data.camera.allowDynamicResolution;
    }

    private static Vector2 JitterForFrame(CameraData data)
    {
        var matrix = data.m_JitterMatrix;
        var jitter = Fsr4Coordinates.JitterPixels(matrix.m03, matrix.m13,
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
            if (Available && Fsr4Controller.Instance?.Fsr4Active == true)
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
            faulted = true; reason = e.Message;
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
            if (!output || cameraId != camera.GetInstanceID() || width != desc.width || height != desc.height)
            {
                ReleaseContext();
                cameraId = camera.GetInstanceID(); width = desc.width; height = desc.height;
                colorInput = CreateTexture("FSR4 colour input", UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);
                depthInput = CreateTexture("FSR4 depth input", UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat);
                motionInput = CreateTexture("FSR4 motion input", UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16_SFloat);
                output = CreateTexture("FSR4 output", UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);
                outputHandle = RTHandles.Alloc(output);
                Plugin.Logger.LogInfo($"FSR 4.1.1 bridge inputs allocated: {width}x{height}, 100%, depth={depth.depthStencilFormat}, reversed Z.");
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
            var options = Fsr4Controller.Instance!.Current;
            var jitter = JitterForFrame(data);
            var shader = pack!;
            var kernel = shader.FindKernel("CS");
            cmd.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("r_color"), source.nameID);
            cmd.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("r_depth"), new RenderTargetIdentifier(depth), 0, depthPlane);
            cmd.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("r_motion"), motionVectors);
            cmd.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("rw_color"), colorInput);
            cmd.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("rw_depth"), depthInput);
            cmd.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("rw_motion"), motionInput);
            cmd.DispatchCompute(shader, kernel, (width + 7) / 8, (height + 7) / 8, 1);
            var frame = new NativeBridge.Frame
            {
                Color = colorInput!.GetNativeTexturePtr(), Depth = depthInput!.GetNativeTexturePtr(),
                Motion = motionInput!.GetNativeTexturePtr(), Output = output!.GetNativeTexturePtr(),
                Width = (uint)width, Height = (uint)height, Reset = reset ? 1u : 0u, Index = (uint)Time.frameCount,
                JitterX = jitter.x, JitterY = jitter.y, DeltaMs = Time.unscaledDeltaTime * 1000,
                Near = camera.nearClipPlane, Far = camera.farClipPlane, Fov = camera.fieldOfView * Mathf.Deg2Rad,
                Sharpness = options.Sharpness
            };
            var job = NativeBridge.Queue(ref frame);
            if (job == IntPtr.Zero) throw new InvalidOperationException("FSR bridge rejected the frame.");
            cmd.IssuePluginEventAndData(NativeBridge.RenderEvent, 1, job);
            Blitter.BlitCameraTexture(cmd, outputHandle, destination);
            if (lastFrame < 0) Plugin.Logger.LogInfo("FSR 4.1.1 Native AA frame queued for the DX12 render callback.");
            lastFrame = Time.frameCount; lastPosition = position; lastSize = size;
            return true;
        }
        catch (Exception e)
        {
            faulted = true; reason = e.Message;
            var nativeTaa = data.taaSettings;
            nativeTaa.resetHistoryFrames = 1;
            data.taaSettings = nativeTaa;
            Plugin.Logger.LogError($"FSR 4.1.1 stopped; falling back to native TAA: {e}");
            return false;
        }
    }

    private static RenderTexture CreateTexture(string name, UnityEngine.Experimental.Rendering.GraphicsFormat format)
    {
        var texture = new RenderTexture(width, height, 0, format)
        { name = name, hideFlags = HideFlags.HideAndDontSave, enableRandomWrite = true, useMipMap = false,
            autoGenerateMips = false, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        if (texture.Create()) return texture;
        Object.Destroy(texture);
        throw new InvalidOperationException($"Could not allocate {name}.");
    }

    private static void ReleaseContext()
    {
        lastFrame = -1;
        outputHandle?.Release(); outputHandle = null;
        ReleaseTexture(ref output); ReleaseTexture(ref colorInput); ReleaseTexture(ref depthInput); ReleaseTexture(ref motionInput);
    }

    private static void ReleaseTexture(ref RenderTexture? texture)
    {
        if (texture) { texture!.Release(); Object.Destroy(texture); }
        texture = null;
    }

    internal static void Shutdown()
    {
        resolveHook?.Dispose(); resolveHook = null;
        ReleaseContext();
        if (pack) Object.Destroy(pack);
        pack = null;
    }
}
