namespace BelmontsCurseGraphics;

internal static class Fsr4Coordinates
{
    // URP applies its jitter matrix after converting to GPU projection space.
    // FSR expects offsets in input texture pixels, whose Y axis on DX11 is down.
    internal static (float X, float Y) JitterPixels(float clipX, float clipY, int width, int height, bool uvStartsAtTop)
        => (clipX * width * .5f, clipY * height * (uvStartsAtTop ? -.5f : .5f));
}
