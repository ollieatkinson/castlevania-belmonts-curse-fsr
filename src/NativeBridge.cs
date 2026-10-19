using System.Runtime.InteropServices;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.Rendering;

namespace BelmontsCurseGraphics;

internal static class NativeBridge
{
    private const string Library = "BelmontFsr4Bridge";
    internal static IntPtr RenderEvent;
    private static bool initialized;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Frame
    {
        internal IntPtr Color, Depth, Motion, Output;
        internal uint Width, Height, Reset, Index;
        internal float JitterX, JitterY, DeltaMs, Near, Far, Fov, Sharpness;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Status
    {
        internal int Failed;
        internal uint Frames, Width, Height;
        internal double Milliseconds;
    }

    internal static void Load()
    {
        if (Marshal.SizeOf<Frame>() != 80 || Marshal.SizeOf<Status>() != 24)
            throw new InvalidOperationException("FSR bridge ABI layout mismatch.");
        var directory = Path.Combine(Paths.BepInExRootPath, "FSR4");
        NativeLibrary.SetDllImportResolver(typeof(Plugin).Assembly, (name, _, _) => name == Library
            ? NativeLibrary.Load(Path.Combine(directory, Library + ".dll")) : IntPtr.Zero);
        if (LoadNative(directory) == 0) throw new InvalidOperationException(ReadStatus().Message);
        RenderEvent = GetRenderEvent();
        if (RenderEvent == IntPtr.Zero) throw new InvalidOperationException("FSR bridge has no render callback.");
        initialized = true;
    }

    internal static (Status State, string Message) ReadStatus()
    {
        var message = new StringBuilder(512);
        GetStatus(out var status, message, 512);
        return (status, message.ToString());
    }

    internal static bool Failed => initialized && ReadStatus().State.Failed != 0;

    internal static void ReleaseContext(bool retry = false)
    {
        if (!initialized) return;
        var command = new CommandBuffer { name = "FSR4 context cleanup" };
        try
        {
            command.IssuePluginEventAndData(RenderEvent, retry ? 3 : 2, IntPtr.Zero);
            Graphics.ExecuteCommandBuffer(command);
        }
        finally { command.Release(); }
    }


    [DllImport(Library, EntryPoint = "Fsr4_Load", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int LoadNative(string directory);
    [DllImport(Library, EntryPoint = "Fsr4_GetRenderEvent", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr GetRenderEvent();
    [DllImport(Library, EntryPoint = "Fsr4_Queue", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr Queue(ref Frame frame);
    [DllImport(Library, EntryPoint = "Fsr4_GetStatus", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern void GetStatus(out Status status, StringBuilder message, uint capacity);
}
