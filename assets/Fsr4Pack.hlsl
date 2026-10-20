// Copy Unity's temporal inputs into textures with explicit bridge formats.
Texture2D<float4> r_color : register(t0);
Texture2D<float> r_depth : register(t1);
Texture2D<float2> r_motion : register(t2);
RWTexture2D<float4> rw_color : register(u0);
RWTexture2D<float> rw_depth : register(u1);
RWTexture2D<float2> rw_motion : register(u2);
[numthreads(8, 8, 1)]
void CS(uint3 id : SV_DispatchThreadID)
{
    uint width, height;
    rw_color.GetDimensions(width, height);
    if (id.x >= width || id.y >= height) return;
    rw_color[id.xy] = r_color[id.xy];
    rw_depth[id.xy] = r_depth[id.xy];
    rw_motion[id.xy] = r_motion[id.xy];
}
