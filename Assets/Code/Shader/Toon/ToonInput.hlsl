#ifndef SANDBOX_TOON_INPUT_INCLUDED
#define SANDBOX_TOON_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// 全パスで同一の UnityPerMaterial を宣言する（SRP Batcher 互換の条件）
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half _BumpScale;

    half4 _ShadeColor;
    half _ShadeThreshold;
    half _ShadeSoftness;
    half4 _Shade2Color;
    half _Shade2Threshold;
    half _Shade2Softness;

    half _LightColorInfluence;
    half _ReceiveShadowStrength;
    half _SSAOStrength;
    half _AmbientStrength;
    half _AdditionalLightStrength;

    half4 _ToonSpecColor;
    half _SpecPower;
    half _SpecThreshold;
    half _SpecSoftness;

    half4 _RimColor;
    half _RimThreshold;
    half _RimSoftness;
    half _RimLightSideMask;

    half4 _EmissionColor;

    half4 _OutlineColor;
    half _OutlineWidth;
    half _OutlineBaseColorBlend;
    float _OutlineFadeStart;
    float _OutlineFadeEnd;
    float _OutlineZOffset;
CBUFFER_END

TEXTURE2D(_BaseMap);        SAMPLER(sampler_BaseMap);
TEXTURE2D(_BumpMap);        SAMPLER(sampler_BumpMap);
TEXTURE2D(_RampMap);
TEXTURE2D(_EmissionMap);    SAMPLER(sampler_EmissionMap);

// ベースカラー（テクスチャ × カラー）を取得する
half4 SampleToonBase(float2 uv)
{
    return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
}

// ToonSmoothNormalBaker が UV8 に焼き込んだ接空間のスムース法線を、オブジェクト空間へ戻す
// UV8 が焼かれていないメッシュ（入力がほぼ 0 ベクトル）は頂点法線をそのまま返す
float3 ReconstructSmoothNormalOS(float3 normalOS, float4 tangentOS, float3 smoothNormalTS)
{
    if (dot(smoothNormalTS, smoothNormalTS) < 0.25)
    {
        return normalOS;
    }

    // ToonSmoothNormalBaker と同じく、接線を法線に直交化してから基底を作る
    float3 n = normalize(normalOS);
    float3 t = normalize(tangentOS.xyz - n * dot(n, tangentOS.xyz));
    float3 b = cross(n, t) * (tangentOS.w < 0.0 ? -1.0 : 1.0);
    return normalize(t * smoothNormalTS.x + b * smoothNormalTS.y + n * smoothNormalTS.z);
}

#endif
