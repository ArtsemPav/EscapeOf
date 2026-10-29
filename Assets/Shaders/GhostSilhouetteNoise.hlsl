#ifndef GHOST_SILHOUETTE_NOISE_INCLUDED
#define GHOST_SILHOUETTE_NOISE_INCLUDED

// Dissolve uniforms (_DISSOLVE_ON, _Dissolve, _DissolveEdgeWidth,
// _DissolveEdgeColor) are declared in the material's UnityPerMaterial
// CBUFFER in the .shader file — the SRP Batcher only uploads per-material
// values for CBUFFER members, so they must live there.

float2 GhostHash2(float2 p)
{
    p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
    return frac(sin(p) * 43758.5453123) * 2.0 - 1.0;
}

// Two-octave gradient noise; the octaves drift independently so the dark
// flow inside the silhouette never looks like one repeating sheet.
float GhostNoise(float2 uv)
{
    float2 cell = floor(uv);
    float2 f = frac(uv);
    float2 u = f * f * (3.0 - 2.0 * f);
    float2 a = GhostHash2(cell);
    float2 b = GhostHash2(cell + float2(1.0, 0.0));
    float2 c = GhostHash2(cell + float2(0.0, 1.0));
    float2 d = GhostHash2(cell + float2(1.0, 1.0));
    float2 ab = lerp(a, b, u.x);
    float2 cd = lerp(c, d, u.x);
    float n1 = lerp(dot(ab, f), dot(cd, f - float2(0.0, 1.0)), u.y);

    float2 cell2 = cell * 2.13 + float2(3.7, 1.9);
    float2 f2 = frac(uv * 2.13);
    float2 u2 = f2 * f2 * (3.0 - 2.0 * f2);
    float2 a2 = GhostHash2(cell2);
    float2 b2 = GhostHash2(cell2 + float2(1.0, 0.0));
    float2 c2 = GhostHash2(cell2 + float2(0.0, 1.0));
    float2 d2 = GhostHash2(cell2 + float2(1.0, 1.0));
    float2 ab2 = lerp(a2, b2, u2.x);
    float2 cd2 = lerp(c2, d2, u2.x);
    float n2 = lerp(dot(ab2, f2), dot(cd2, f2 - float2(0.0, 1.0)), u2.y);

    return 0.5 + (n1 * 0.6 + n2 * 0.4);
}

// Discards fragments once dissolving; include AFTER the material CBUFFER.
void GhostDissolveClip(float3 positionWS)
{
    if (_DISSOLVE_ON > 0.5 && _Dissolve > 0.0)
    {
        float2 dUV = positionWS.xy * _NoiseScale + float2(_TimeParameters.x * _NoiseSpeed * 0.5, 0.0);
        clip(GhostNoise(dUV) - _Dissolve + 0.5);
    }
}

#endif
