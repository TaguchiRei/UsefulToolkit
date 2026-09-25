#ifndef SANDBOX_TOON_REALISTIC_FORWARD_PASS_INCLUDED
#define SANDBOX_TOON_REALISTIC_FORWARD_PASS_INCLUDED

#include "ToonRealisticLighting.hlsl"

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    // ToonSmoothNormalBaker が UV8 に焼き込んだスムース法線（接空間）
    float3 smoothNormalTS : TEXCOORD7;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    half3 normalWS : TEXCOORD2;
#if defined(_NORMALMAP)
    half4 tangentWS : TEXCOORD3;
#endif
    half fogFactor : TEXCOORD4;
    half3 smoothNormalWS : TEXCOORD5;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings RealisticForwardVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

    output.positionCS = positionInputs.positionCS;
    output.positionWS = positionInputs.positionWS;
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
    output.normalWS = normalInputs.normalWS;
    output.smoothNormalWS = TransformObjectToWorldNormal(
        ReconstructSmoothNormalOS(input.normalOS, input.tangentOS, input.smoothNormalTS));
#if defined(_NORMALMAP)
    output.tangentWS = half4(normalInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());
#endif
    output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
    return output;
}

half4 RealisticForwardFragment(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    half4 base = SampleRealisticBase(input.uv);

    // マスクマップは R = Metallic、G = Occlusion、A = Smoothness
#if defined(_MASKMAP)
    half4 mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, input.uv);
    half metallic = mask.r * _Metallic;
    half occlusion = lerp(1.0h, mask.g, _OcclusionStrength);
    half smoothness = mask.a * _Smoothness;
#else
    half metallic = _Metallic;
    half occlusion = 1.0h;
    half smoothness = _Smoothness;
#endif

#if defined(_NORMALMAP)
    half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
    half3 bitangentWS = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
    half3 normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangentWS, input.normalWS));
#else
    half3 normalWS = input.normalWS;
#endif

    RealisticSurface surface;
    surface.albedo = base.rgb;
    surface.metallic = metallic;
    surface.smoothness = smoothness;
    surface.occlusion = occlusion;
    surface.normalWS = NormalizeNormalPerPixel(normalWS);
    surface.smoothNormalWS = NormalizeNormalPerPixel(input.smoothNormalWS);
    surface.viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    surface.positionWS = input.positionWS;
    surface.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

    half3 color = RealisticFragmentLighting(surface);

#if defined(_EMISSION)
    color += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb * _EmissionColor.rgb;
#endif

    color = MixFog(color, input.fogFactor);
    return half4(color, 1.0h);
}

#endif
