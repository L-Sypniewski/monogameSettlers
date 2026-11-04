// The water lab's pixelsurf: the water pass shaded per pixel, so no mesh lighting can facet it. The
// geometry stays flat (the triangles carry no lift); the pixel shader builds a sum-of-sines normal in
// the same screen field as the swell, shades the sampled water texture with it and adds glints. Per
// vertex the pass packs the water ring into color.r ((ring+1)/8, 0 = no ring) and the shore distance
// strength into color.g. Compiled to Water.mgfx with tools/compile-shaders.sh (mgfxc needs Wine).

#if OPENGL
#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0
#else
#define VS_SHADERMODEL vs_4_0_level_9_3
#define PS_SHADERMODEL ps_4_0_level_9_3
#endif

float4x4 WorldViewProjection;
float Time;

Texture2D WaterTexture;
sampler2D WaterSampler = sampler_state
{
    Texture = <WaterTexture>;
    MinFilter = Point;
    MagFilter = Point;
    AddressU = Clamp;
    AddressV = Clamp;
};

struct VertexInput
{
    float4 Position : POSITION;
    float4 Color : COLOR0;
    float2 TexCoord : TEXCOORD0;
};

struct PixelInput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TexCoord : TEXCOORD0;
    float2 World : TEXCOORD1;
};

PixelInput WaterVertex(VertexInput vertex)
{
    PixelInput output;
    output.Position = mul(vertex.Position, WorldViewProjection);
    output.Color = vertex.Color;
    output.TexCoord = vertex.TexCoord;
    output.World = vertex.Position.xy;
    return output;
}

// Grid to screen field, as WaveLayer sees it: rows shifted half a tile, 9 px of 16 wide.
float2 ScreenField(float2 world)
{
    float y = world.y / 9;
    float x = (world.x + y * 8) / 16;
    return float2(x - y * 0.5, y * 9.0 / 16.0);
}

// Analytic normal of a sum of sines over the field: the main swell running diagonally (length 18
// fields, speed 1.8), a shorter ripple across it and a long variation along the crests. The gradient
// of each sine is its wave vector times the cosine of its phase, so no finite differences are needed.
float3 WaterNormal(float2 field, float calm)
{
    float2 heading = normalize(float2(-0.5, 0.87));
    float2 across = float2(-heading.y, heading.x);
    float2 k1 = heading * (6.2831853 / 18.0);
    float2 k2 = (heading * 0.8 + across * 0.6) * (6.2831853 / 5.0);
    float2 k3 = across * (6.2831853 / 43.0);
    float2 gradient = k1 * cos(dot(field, k1) - Time * 1.8 * 6.2831853 / 18.0)
        + 0.5 * k2 * cos(dot(field, k2) - Time * 1.7)
        + 0.25 * k3 * cos(dot(field, k3) + Time * 0.2);
    gradient *= 1.2 * calm;
    return normalize(float3(-gradient.x, -gradient.y, 1));
}

// Depth tint like the CPU ramp (WaterLabTint): red falls most, blue least, cooler towards Water8.
float3 RingTint(float4 color)
{
    float depth = saturate((color.r * 8.0 - 1.0) / 7.0);
    return lerp(float3(1, 1, 1), float3(0.88, 0.90, 0.96), depth);
}

float4 WaterPixel(PixelInput pixel) : COLOR0
{
    float3 normal = WaterNormal(ScreenField(pixel.World), pixel.Color.g);
    // Slopes facing the viewer are lit, as the terrain's relief light does.
    float3 light = normalize(float3(0.0, -0.6, 0.8));
    float diffuse = saturate(dot(normal, light));
    float3 halfway = normalize(light + float3(0, 0, 1));
    float glint = pow(saturate(dot(normal, halfway)), 32) * 0.5 * pixel.Color.g;

    float4 texel = tex2D(WaterSampler, pixel.TexCoord);
    float3 color = texel.rgb * (0.85 + 0.25 * diffuse) * RingTint(pixel.Color) + glint;
    return float4(color, 1);
}

technique Water
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL WaterVertex();
        PixelShader = compile PS_SHADERMODEL WaterPixel();
    }
}
