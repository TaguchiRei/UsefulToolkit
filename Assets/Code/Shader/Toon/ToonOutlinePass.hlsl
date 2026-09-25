#ifndef SANDBOX_TOON_OUTLINE_PASS_INCLUDED
#define SANDBOX_TOON_OUTLINE_PASS_INCLUDED

#include "ToonInput.hlsl"

// アウトライン幅（ピクセル）の基準とする画面の高さ
#define TOON_OUTLINE_REFERENCE_HEIGHT 1080.0

struct OutlineAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    // ToonSmoothNormalBaker が UV8 に焼き込んだ平均化法線（接空間）
    float3 smoothNormalTS : TEXCOORD7;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct OutlineVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    half fogFactor : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

// 背面法アウトライン。法線方向へ画面上で一定ピクセル幅だけ押し出し、距離に応じて細くする
OutlineVaryings ToonOutlineVertex(OutlineAttributes input)
{
    OutlineVaryings output = (OutlineVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 normalOS = ReconstructSmoothNormalOS(input.normalOS, input.tangentOS, input.smoothNormalTS);
    float3 normalWS = TransformObjectToWorldNormal(normalOS);

    float3 positionVS = TransformWorldToView(positionWS);
    float viewDepth = -positionVS.z;
    positionVS.z -= _OutlineZOffset;
    float4 positionCS = TransformWViewToHClip(positionVS);

    float3 normalVS = TransformWorldToViewDir(normalWS, true);
    float2 normalCS = mul((float3x3)UNITY_MATRIX_P, normalVS).xy;

    // 画面ピクセル空間で方向を正規化し、縦横比に関係なく均一な幅にする
    float2 directionPx = normalCS * _ScreenParams.xy;
    float directionLength = length(directionPx);
    float2 direction = directionLength > 1e-5 ? directionPx / directionLength : float2(0.0, 0.0);

    float fadeRange = max(_OutlineFadeEnd - _OutlineFadeStart, 1e-3);
    float fade = 1.0 - saturate((viewDepth - _OutlineFadeStart) / fadeRange);
    float widthPx = _OutlineWidth * (_ScreenParams.y / TOON_OUTLINE_REFERENCE_HEIGHT) * fade;

    positionCS.xy += direction * (widthPx * 2.0 / _ScreenParams.xy) * positionCS.w;

    output.positionCS = positionCS;
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
    output.fogFactor = ComputeFogFactor(positionCS.z);
    return output;
}

// アウトライン色。_OutlineBaseColorBlend で表面色（2影色で暗くしたもの）へ寄せる
half4 ToonOutlineFragment(OutlineVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    half3 base = SampleToonBase(input.uv).rgb;
    half3 tinted = base * _Shade2Color.rgb * 0.5h;
    half3 color = lerp(_OutlineColor.rgb, tinted, _OutlineBaseColorBlend);

    color = MixFog(color, input.fogFactor);
    return half4(color, 1.0h);
}

#endif
