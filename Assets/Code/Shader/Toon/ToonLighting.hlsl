#ifndef SANDBOX_TOON_LIGHTING_INCLUDED
#define SANDBOX_TOON_LIGHTING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// トゥーン計算に使う表面情報
struct ToonSurface
{
    half3 albedo;
    half3 normalWS;
    // リムライト用のスムース法線（平面でも面内で変化するので、リムが面全体ではなく縁に帯状に出る）
    half3 smoothNormalWS;
    half3 viewDirWS;
    float3 positionWS;
    float2 normalizedScreenSpaceUV;
};

// threshold を中心に ±softness の幅で 0→1 に切り替える段階関数
half ToonStep(half value, half threshold, half softness)
{
    half s = max(softness, 1e-4h);
    return smoothstep(threshold - s, threshold + s, value);
}

// ハーフランバート値から段階陰影の拡散色を求める
// ランプマップ有効時はランプの色をアルベドに乗算し、無効時はベース／1影／2影の3段階で塗り分ける
half3 ToonDiffuse(half term, half3 albedo)
{
#if defined(_TOON_RAMP_MAP)
    half3 ramp = SAMPLE_TEXTURE2D(_RampMap, sampler_LinearClamp, half2(saturate(term), 0.5h)).rgb;
    return albedo * ramp;
#else
    half lit = ToonStep(term, _ShadeThreshold, _ShadeSoftness);
    half firstShade = ToonStep(term, _Shade2Threshold, _Shade2Softness);
    half3 shade = lerp(_Shade2Color.rgb, _ShadeColor.rgb, firstShade) * albedo;
    return lerp(shade, albedo, lit);
#endif
}

// ライト色の影響度を反映したライト色を返す（0 でライト色・強度を無視して白として扱う）
half3 ToonLightColor(half3 lightColor)
{
    return lerp(half3(1.0h, 1.0h, 1.0h), lightColor, _LightColorInfluence);
}

// 段階化したハイライト。lit は明部マスク（影側にハイライトを出さないために使う）
half3 ToonSpecular(Light light, ToonSurface s, half lit)
{
#if defined(_TOON_SPECULAR)
    half3 halfDir = SafeNormalize(light.direction + s.viewDirWS);
    half nDotH = saturate(dot(s.normalWS, halfDir));
    half spec = ToonStep(pow(nDotH, _SpecPower), _SpecThreshold, _SpecSoftness);
    return spec * lit * _ToonSpecColor.rgb;
#else
    return half3(0.0h, 0.0h, 0.0h);
#endif
}

// メインライトの寄与（段階陰影＋受け影＋SSAO＋ハイライト）
half3 ToonMainLight(Light light, ToonSurface s, half directOcclusion)
{
    half nDotL = dot(s.normalWS, light.direction);
    half term = nDotL * 0.5h + 0.5h;

    // 受け影は2影まで落とさず、1影と2影の中間へ押し込む
    half shadow = lerp(1.0h, light.shadowAttenuation, _ReceiveShadowStrength);
    half shadowClamp = (_ShadeThreshold + _Shade2Threshold) * 0.5h;
    term = lerp(min(term, shadowClamp), term, shadow);
    term *= lerp(1.0h, directOcclusion, _SSAOStrength);

    half3 lightColor = ToonLightColor(light.color);
    half3 color = ToonDiffuse(term, s.albedo) * lightColor;

    half lit = ToonStep(term, _ShadeThreshold, _ShadeSoftness);
    color += ToonSpecular(light, s, lit) * lightColor;
    return color;
}

// 追加ライト（ポイント／スポット等）の寄与。明暗は段階化し、距離減衰は滑らかなまま使う
half3 ToonAdditionalLight(Light light, ToonSurface s)
{
    half nDotL = dot(s.normalWS, light.direction);
    half lit = ToonStep(nDotL * 0.5h + 0.5h, _ShadeThreshold, _ShadeSoftness);
    half attenuation = light.distanceAttenuation * light.shadowAttenuation;
    half3 lightColor = light.color * attenuation * _AdditionalLightStrength;

    half3 color = s.albedo * lit * lightColor;
    color += ToonSpecular(light, s, lit) * lightColor;
    return color;
}

// 段階化したリムライト。スムース法線でフレネルを取り、_RimLightSideMask でメインライト側に寄せる
half3 ToonRim(Light mainLight, ToonSurface s)
{
#if defined(_TOON_RIM)
    half fresnel = 1.0h - saturate(dot(s.smoothNormalWS, s.viewDirWS));
    half rim = ToonStep(fresnel, _RimThreshold, _RimSoftness);
    half lightSide = ToonStep(dot(s.smoothNormalWS, mainLight.direction) * 0.5h + 0.5h, 0.5h, 0.1h);
    rim *= lerp(1.0h, lightSide, _RimLightSideMask);
    return rim * _RimColor.rgb;
#else
    return half3(0.0h, 0.0h, 0.0h);
#endif
}

// 全ライトを合成したトゥーンの最終色（フォグ・エミッション以外）
half3 ToonFragmentLighting(ToonSurface s)
{
    half4 shadowMask = half4(1.0h, 1.0h, 1.0h, 1.0h);
    float4 shadowCoord = TransformWorldToShadowCoord(s.positionWS);
    AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(s.normalizedScreenSpaceUV);

    Light mainLight = GetMainLight(shadowCoord, s.positionWS, shadowMask);

    // 環境光は法線に依存しない SH の定数項だけを使い、陰影の段階を崩さない
    half3 ambient = SampleSH(half3(0.0h, 0.0h, 0.0h)) * s.albedo * _AmbientStrength
                    * lerp(1.0h, aoFactor.indirectAmbientOcclusion, _SSAOStrength);
    half3 color = ambient;

#if defined(_LIGHT_LAYERS)
    uint meshRenderingLayers = GetMeshRenderingLayer();
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
#endif
    {
        color += ToonMainLight(mainLight, s, aoFactor.directAmbientOcclusion);
    }

#if defined(_ADDITIONAL_LIGHTS)
    // LIGHT_LOOP_BEGIN（クラスターループ）が inputData という名前の変数を参照するため、この名前で宣言する
    InputData inputData = (InputData)0;
    inputData.positionWS = s.positionWS;
    inputData.normalizedScreenSpaceUV = s.normalizedScreenSpaceUV;

    uint pixelLightCount = GetAdditionalLightsCount();

    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, s.positionWS, shadowMask);
        #if defined(_LIGHT_LAYERS)
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
        #endif
        {
            color += ToonAdditionalLight(light, s);
        }
    }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, s.positionWS, shadowMask);
        #if defined(_LIGHT_LAYERS)
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
        #endif
        {
            color += ToonAdditionalLight(light, s);
        }
    LIGHT_LOOP_END
#endif

    color += ToonRim(mainLight, s);
    return color;
}

#endif
