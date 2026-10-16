// Upscaling filters for the world picture (MapGame, F2). Pixel shaders only: SpriteBatch keeps its vertex shader.
// Both filters read the 4x4 texels around the sample point (point sampling) and weight them per axis.
// Compiled to Upscale.mgfx with tools/compile-shaders.sh (mgfxc needs Wine on Linux); the .mgfx is embedded.

#if OPENGL
#define PS_SHADERMODEL ps_3_0
#else
#define PS_SHADERMODEL ps_4_0_level_9_3
#endif

Texture2D SpriteTexture;
sampler2D SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
    MinFilter = Point;
    MagFilter = Point;
    AddressU = Clamp;
    AddressV = Clamp;
};

// Width and height of the texture in texels.
float2 TextureSize;

struct PixelInput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TexCoord : TEXCOORD0;
};

// Catmull-Rom spline weights for the taps at -1, 0, 1, 2 around the fraction t.
float4 CatmullRom(float t)
{
    float t2 = t * t, t3 = t2 * t;
    return float4(
        -0.5 * t3 + t2 - 0.5 * t,
        1.5 * t3 - 2.5 * t2 + 1,
        -1.5 * t3 + 2 * t2 + 0.5 * t,
        0.5 * t3 - 0.5 * t2);
}

// Lanczos window with a = 2: sinc(x) * sinc(x / 2), normalized.
float4 Lanczos2(float t)
{
    float4 x = float4(1 + t, t, 1 - t, 2 - t);
    float4 px = max(abs(x), 1e-4) * 3.14159265;
    float4 w = sin(px) * sin(px * 0.5) / (px * px * 0.5);
    return w / dot(w, 1);
}

float4 Filter(float2 uv, bool lanczos)
{
    float2 position = uv * TextureSize - 0.5;
    float2 fraction = frac(position);
    float2 texel = 1 / TextureSize;
    float2 first = (floor(position) - 0.5) * texel;
    float4 wx = lanczos ? Lanczos2(fraction.x) : CatmullRom(fraction.x);
    float4 wy = lanczos ? Lanczos2(fraction.y) : CatmullRom(fraction.y);
    float4 sum = 0;
    [unroll] for (int row = 0; row < 4; row++)
    {
        float v = first.y + row * texel.y;
        float4 taps = wx.x * tex2D(SpriteTextureSampler, float2(first.x, v))
                      + wx.y * tex2D(SpriteTextureSampler, float2(first.x + texel.x, v))
                      + wx.z * tex2D(SpriteTextureSampler, float2(first.x + 2 * texel.x, v))
                      + wx.w * tex2D(SpriteTextureSampler, float2(first.x + 3 * texel.x, v));
        sum += wy[row] * taps;
    }
    // The negative lobes overshoot at hard edges.
    return saturate(sum);
}

float4 CatmullRomPS(PixelInput input) : COLOR
{
    return Filter(input.TexCoord, false) * input.Color;
}

float4 Lanczos2PS(PixelInput input) : COLOR
{
    return Filter(input.TexCoord, true) * input.Color;
}

technique CatmullRom
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL CatmullRomPS();
    }
};

technique Lanczos2
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL Lanczos2PS();
    }
};
