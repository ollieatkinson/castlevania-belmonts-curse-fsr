namespace BelmontsCurseGraphics;

internal sealed record Fsr31Options(bool Enabled = false, float Sharpness = .2f, bool ReactiveMask = true)
{
    internal void Validate()
    {
        if (!float.IsFinite(Sharpness) || Sharpness is < 0 or > 1)
            throw new ArgumentException("FSR sharpening must be between 0 and 1.");
    }
}

internal static class Fsr31MenuModel
{
    internal static bool IsFsrChoice(int index, int nativeCount) => nativeCount > 0 && index == nativeCount;
    internal static bool IsNativeChoice(int index, int nativeCount) => index >= 0 && index < nativeCount;
    internal static int DisplayIndex(bool enabled, int nativeValue, int[] nativeValues)
        => enabled ? nativeValues.Length : Math.Max(0, Array.IndexOf(nativeValues, nativeValue));
    internal static int SharpnessIndex(float value) => Math.Clamp((int)MathF.Round(value * 10), 0, 10);
}

internal sealed record Fsr31Requirements(bool Backend, bool PostProcessing, bool TemporalShader, bool MotionVectors,
    bool Stacked, bool DynamicResolution, bool Hdr)
{
    internal string? Failure => !Backend ? "FSR shader package or resolve hook is unavailable."
        : !PostProcessing ? "Post-processing is disabled."
        : !TemporalShader ? "The renderer's temporal shader is unavailable."
        : !MotionVectors ? "The renderer cannot produce motion vectors."
        : Stacked ? "Camera stacking is unsupported."
        : DynamicResolution ? "Dynamic resolution is unsupported."
        : Hdr ? "This prototype supports LDR rendering only." : null;
}
