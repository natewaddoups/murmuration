cbuffer Frame : register(b0)
{
    row_major float4x4 ViewProj;
};

struct VSIn
{
    float3 LocalPos : POSITION;
    float3 Color : COLOR;
    float3 InstPos : IPOS;
    float3 InstDir : IDIR;
    float3 InstUp : IUP;
};

struct VSOut
{
    float4 Pos : SV_Position;
    float3 Color : COLOR;
};

// Local axes: x forward, y left, z up.
VSOut VSMain(VSIn i)
{
    float3 fwd = normalize(i.InstDir);
    float3 up = normalize(i.InstUp);
    float3 left = cross(up, fwd);

    float3 world = i.InstPos + fwd * i.LocalPos.x + left * i.LocalPos.y + up * i.LocalPos.z;

    VSOut o;
    o.Pos = mul(float4(world, 1.0), ViewProj);
    o.Color = i.Color;
    return o;
}

float4 PSMain(VSOut i) : SV_Target
{
    return float4(i.Color, 1.0);
}
