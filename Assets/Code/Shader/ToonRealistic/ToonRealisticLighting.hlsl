#ifndef SANDBOX_TOON_REALISTIC_LIGHTING_INCLUDED
#define SANDBOX_TOON_REALISTIC_LIGHTING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// リアル調トゥーンの計算に使う表面情報
struct RealisticSurface
{
    half3 albedo;
    half metallic;
    half smoothness;
    half occlusion;
    half3 normalWS;
    // リムライト用のスムース法線（平面でも面内で変化するので、リムが縁に帯状に出る）
    half3 smoothNormalWS;
    half3 viewDirWS;
    float3 positionWS;
    float2 normalizedScreenSpaceUV;
};

// threshold を中心に ±softness の幅で 0→1 に切り替える
half RealisticStep(half value, half threshold, half softness)
{
    half s = max(softness, 1e-4h);
    return smoothstep(threshold - s, threshold + s, value);
}

// 輝度を保ったまま彩度を amount だけ持ち上げる（0 で元の色）
half3 BoostSaturation(half3 color, half amount)
{
    half luma = dot(color, half3(0.2126h, 0.7152h, 0.0722h));
    return max(lerp(luma.xxx, color, 1.0h + amount), 0.0h);
}

// 鏡面反射に掛ける色。_SpecularAlbedoTint でアルベドの色相へ寄せる（最大成分で割って明るさは保つ）
half3 SpecularTint(half3 albedo)
{
    half3 hue = BoostSaturation(albedo, 0.5h);
    hue /= max(max(hue.r, max(hue.g, hue.b)), 1e-3h);
    return lerp(half3(1.0h, 1.0h, 1.0h), hue, _SpecularAlbedoTint);
}

// 1つのライトの直接光（拡散＋鏡面）
// 拡散はセル調の2トーン（明部／影色）で塗り分け、明部の中にだけ N・L のグラデーションを残す
// directOcclusion は SSAO の直接光遮蔽、shadowFill は影側に影色と境界の帯を入れる量（メインライトのみ 1）
half3 RealisticDirectLight(BRDFData brdfData, RealisticSurface s, half3 lightColor, half3 lightDirectionWS,
                           half shadowAttenuation, half distanceAttenuation, half directOcclusion, half shadowFill)
{
    half nDotL = dot(s.normalWS, lightDirectionWS);

    // 受光マスク：N・L・受け影・SSAO をそれぞれ段階化して掛ける
    half shadeStep = RealisticStep(nDotL, _ShadeThreshold, _ShadeSoftness);
    half castShadow = RealisticStep(shadowAttenuation, 0.5h, _ShadowSoftness);
    half occlusionStep = RealisticStep(directOcclusion, 0.5h, 0.1h);
    half lit = shadeStep * castShadow * occlusionStep;

    half litShade = lerp(1.0h, saturate(nDotL), _LitGradient);
    half3 saturatedDiffuse = BoostSaturation(brdfData.diffuse, _ShadowSaturation);
    half3 litDiffuse = brdfData.diffuse * litShade;
    half3 shadowDiffuse = saturatedDiffuse * _ShadowColor.rgb * shadowFill;

    // 明暗境界の影側に、彩度を上げたアルベドの細い帯を入れる（受け影の縁にも出る）
    half softLit = RealisticStep(nDotL, _ShadeThreshold, _TerminatorWidth) * saturate(shadowAttenuation);
    half band = saturate(4.0h * softLit * (1.0h - softLit)) * (1.0h - lit);
    half3 diffuse = lerp(shadowDiffuse, litDiffuse, lit) + band * _TerminatorColor.rgb * saturatedDiffuse * shadowFill;

    // 鏡面：GGX を、形のはっきりしたハイライトへ _SpecStylization で寄せる
    // 誘電体は F0 が 0.04 と小さくトゥーンのハイライトが見えないため、_ToonSpecularBase を下限にする
    half physicalSpec = DirectBRDFSpecular(brdfData, s.normalWS, lightDirectionWS, s.viewDirWS);
    half toonMask = RealisticStep(physicalSpec / (1.0h + physicalSpec), _SpecThreshold, _SpecSoftness);
    half3 toonSpec = max(brdfData.specular, _ToonSpecularBase.xxx) * _ToonSpecIntensity * toonMask;
    half3 specular = lerp(brdfData.specular * physicalSpec, toonSpec, _SpecStylization) * SpecularTint(s.albedo) * lit;

    return (diffuse + specular) * lightColor * distanceAttenuation;
}

// 環境光（SH）と環境反射（リフレクションプローブ）
half3 RealisticIndirect(BRDFData brdfData, RealisticSurface s, half occlusion)
{
    // 法線を縮めると SH の方向成分が弱まり、環境光が平坦になる（影の中を一様に保つ）
    half3 bakedGI = SampleSH(s.normalWS * (1.0h - _AmbientFlatten)) * _AmbientStrength;

    half3 reflectVector = reflect(-s.viewDirWS, s.normalWS);
    half fresnelTerm = Pow4(1.0h - saturate(dot(s.normalWS, s.viewDirWS)));
    half3 indirectSpecular = GlossyEnvironmentReflection(reflectVector, s.positionWS, brdfData.perceptualRoughness,
                                                         1.0h, s.normalizedScreenSpaceUV)
                              * _EnvironmentReflectionStrength * SpecularTint(s.albedo);

    return EnvironmentBRDF(brdfData, bakedGI, indirectSpecular, fresnelTerm) * occlusion;
}

// 段階化したフレネルリム。_RimLightSideMask でメインライト側に寄せる
half3 RealisticRim(Light mainLight, RealisticSurface s)
{
#if defined(_REALISTIC_RIM)
    half fresnel = 1.0h - saturate(dot(s.smoothNormalWS, s.viewDirWS));
    half rim = RealisticStep(fresnel, _RimThreshold, _RimSoftness);
    half lightSide = saturate(dot(s.smoothNormalWS, mainLight.direction) * 0.5h + 0.5h);
    rim *= lerp(1.0h, lightSide, _RimLightSideMask);
    return rim * _RimColor.rgb * mainLight.color;
#else
    return half3(0.0h, 0.0h, 0.0h);
#endif
}

// 全ライトを合成した最終色（フォグ・エミッション以外）
half3 RealisticFragmentLighting(RealisticSurface s)
{
    half alpha = 1.0h;
    BRDFData brdfData;
    InitializeBRDFData(s.albedo, s.metallic, half3(0.0h, 0.0h, 0.0h), s.smoothness, alpha, brdfData);

    half4 shadowMask = half4(1.0h, 1.0h, 1.0h, 1.0h);
    float4 shadowCoord = TransformWorldToShadowCoord(s.positionWS);
    AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(s.normalizedScreenSpaceUV);
    half directOcclusion = lerp(1.0h, aoFactor.directAmbientOcclusion, _SSAOStrength);
    half indirectOcclusion = lerp(1.0h, aoFactor.indirectAmbientOcclusion, _SSAOStrength) * s.occlusion;

    Light mainLight = GetMainLight(shadowCoord, s.positionWS, shadowMask);

    half3 color = RealisticIndirect(brdfData, s, indirectOcclusion);

#if defined(_LIGHT_LAYERS)
    uint meshRenderingLayers = GetMeshRenderingLayer();
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
#endif
    {
        color += RealisticDirectLight(brdfData, s, mainLight.color, mainLight.direction,
                                      mainLight.shadowAttenuation, mainLight.distanceAttenuation, directOcclusion, 1.0h);
        color += RealisticRim(mainLight, s);
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
            color += RealisticDirectLight(brdfData, s, light.color, light.direction,
                                          light.shadowAttenuation, light.distanceAttenuation, 1.0h, 0.0h);
        }
    }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, s.positionWS, shadowMask);
        #if defined(_LIGHT_LAYERS)
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
        #endif
        {
            color += RealisticDirectLight(brdfData, s, light.color, light.direction,
                                          light.shadowAttenuation, light.distanceAttenuation, 1.0h, 0.0h);
        }
    LIGHT_LOOP_END
#endif

    return color;
}

#endif
