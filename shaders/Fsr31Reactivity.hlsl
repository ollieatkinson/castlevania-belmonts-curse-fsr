// Copyright (c) 2026 Oliver Atkinson. MIT licence, see LICENSE.
// Reject unreliable temporal history without reducing accumulation everywhere.
cbuffer cbFsr31Reactivity : register(b0)
{
    float4 ReactivityParams; // inverse size, threshold, gain
    float4 HistoryParams; // previous-current jitter in pixels, reset, unused
};
Texture2D<float4> r_current_color : register(t0);
Texture2D<float4> r_previous_color : register(t1);
Texture2D<float2> r_motion : register(t2);
SamplerState sampler_LinearClamp : register(s0);
RWTexture2D<float> rw_reactive : register(u0);

[numthreads(8, 8, 1)]
void CS(uint3 id : SV_DispatchThreadID)
{
    uint width, height;
    rw_reactive.GetDimensions(width, height);
    if (id.x >= width || id.y >= height) return;
    if (HistoryParams.z > .5) { rw_reactive[id.xy] = 0; return; }
    int2 p = int2(id.xy);
    float2 uv = (float2(p) + .5) * ReactivityParams.xy;
    // URP supplies forward, non-jittered motion in screen UV space.
    float2 previousUv = uv - r_motion.Load(int3(p, 0)) + HistoryParams.xy * ReactivityParams.xy;
    if (any(previousUv <= 0) || any(previousUv >= 1)) { rw_reactive[p] = 1; return; }
    float3 previous = r_previous_color.SampleLevel(sampler_LinearClamp, previousUv, 0).rgb;
    float3 minimum = r_current_color.Load(int3(p, 0)).rgb;
    float3 maximum = minimum;
    // A small neighbourhood permits normal subpixel changes along static edges.
    [unroll] for (int y = -1; y <= 1; ++y)
    [unroll] for (int x = -1; x <= 1; ++x)
    {
        int2 q = clamp(p + int2(x, y), int2(0, 0), int2(width - 1, height - 1));
        float3 c = r_current_color.Load(int3(q, 0)).rgb;
        minimum = min(minimum, c); maximum = max(maximum, c);
    }
    float3 error = abs(previous - clamp(previous, minimum, maximum));
    float difference = max(error.r, max(error.g, error.b));
    rw_reactive[p] = saturate((difference - ReactivityParams.z) * ReactivityParams.w);
}
