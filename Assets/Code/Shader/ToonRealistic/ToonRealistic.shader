Shader "Sandbox/ToonRealistic"
{
    Properties
    {
        [Header(Base)]
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _Metallic("Metallic", Range(0, 1)) = 0
        _Smoothness("Smoothness", Range(0, 1)) = 0.5
        [Toggle(_MASKMAP)] _UseMaskMap("Use Mask Map", Float) = 0
        [NoScaleOffset] _MaskMap("Mask Map (R Metallic, G Occlusion, A Smoothness)", 2D) = "white" {}
        _OcclusionStrength("Occlusion Strength", Range(0, 1)) = 1
        [Toggle(_NORMALMAP)] _UseNormalMap("Use Normal Map", Float) = 0
        [NoScaleOffset][Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2

        [Header(Cel Shading)]
        _ShadeThreshold("Shade Threshold (N dot L)", Range(-1, 1)) = 0
        _ShadeSoftness("Shade Edge Softness", Range(0, 0.5)) = 0.04
        _ShadowSoftness("Cast Shadow Edge Softness", Range(0, 0.5)) = 0.05
        _LitGradient("Lit Area Gradient", Range(0, 1)) = 0.3
        _ShadowColor("Shadow Color", Color) = (0.55, 0.5, 0.72, 1)
        _ShadowSaturation("Shadow Saturation Boost", Range(0, 2)) = 0.8
        _TerminatorColor("Terminator Color", Color) = (0.6, 0.25, 0.2, 1)
        _TerminatorWidth("Terminator Width", Range(0.01, 1)) = 0.25

        [Header(Specular)]
        _SpecStylization("Specular Stylization", Range(0, 1)) = 0.8
        _SpecThreshold("Stylized Specular Threshold", Range(0, 1)) = 0.5
        _SpecSoftness("Stylized Specular Edge Softness", Range(0, 0.5)) = 0.04
        _ToonSpecIntensity("Stylized Specular Intensity", Range(0, 8)) = 1.5
        _ToonSpecularBase("Stylized Specular Base (dielectric)", Range(0, 1)) = 0.3
        _SpecularAlbedoTint("Specular Albedo Tint", Range(0, 1)) = 0.5

        [Header(Indirect)]
        _AmbientStrength("Ambient Strength", Range(0, 2)) = 0.6
        _AmbientFlatten("Ambient Flatten", Range(0, 1)) = 0.85
        _EnvironmentReflectionStrength("Environment Reflection Strength", Range(0, 2)) = 1
        _SSAOStrength("SSAO Strength", Range(0, 1)) = 0.8

        [Header(Rim Light)]
        [Toggle(_REALISTIC_RIM)] _UseRim("Enable Rim Light", Float) = 1
        [HDR] _RimColor("Rim Color", Color) = (0.4, 0.4, 0.46, 1)
        _RimThreshold("Rim Threshold", Range(0, 1)) = 0.72
        _RimSoftness("Rim Edge Softness", Range(0, 0.5)) = 0.02
        _RimLightSideMask("Rim Light Side Mask", Range(0, 1)) = 0.8

        [Header(Emission)]
        [Toggle(_EMISSION)] _UseEmission("Enable Emission", Float) = 0
        [NoScaleOffset] _EmissionMap("Emission Map", 2D) = "white" {}
        [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 1)

        [Header(Outline)]
        [ToggleUI] _OutlineEnabled("Enable Outline", Float) = 1
        _OutlineColor("Outline Color", Color) = (0.05, 0.04, 0.06, 1)
        _OutlineWidth("Outline Width (px at 1080p)", Range(0, 10)) = 1.5
        _OutlineBaseColorBlend("Outline Base Color Blend", Range(0, 1)) = 0.8
        _OutlineFadeStart("Outline Fade Start Distance", Float) = 10
        _OutlineFadeEnd("Outline Fade End Distance", Float) = 40
        _OutlineZOffset("Outline Z Offset (view space)", Range(0, 0.1)) = 0.002
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ToonRealisticForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex RealisticForwardVertex
            #pragma fragment RealisticForwardFragment

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _MASKMAP
            #pragma shader_feature_local_fragment _REALISTIC_RIM
            #pragma shader_feature_local_fragment _EMISSION

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "ToonRealisticInput.hlsl"
            #include "ToonRealisticForwardPass.hlsl"
            ENDHLSL
        }

        // 背面法アウトライン。ShaderGUI が _OutlineEnabled に応じてこのパス自体を有効／無効にする
        Pass
        {
            Name "ToonRealisticOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex RealisticOutlineVertex
            #pragma fragment RealisticOutlineFragment

            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "ToonRealisticOutlinePass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex RealisticShadowCasterVertex
            #pragma fragment RealisticShadowCasterFragment

            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "ToonRealisticDepthPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex RealisticDepthVertex
            #pragma fragment RealisticDepthOnlyFragment

            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "ToonRealisticDepthPasses.hlsl"
            ENDHLSL
        }

        // SSAO などが使う法線テクスチャ用
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex RealisticDepthVertex
            #pragma fragment RealisticDepthNormalsFragment

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "ToonRealisticDepthPasses.hlsl"
            ENDHLSL
        }
    }

    // アウトラインパスの ON/OFF は Sandbox/Toon と共通の ShaderGUI で行う
    CustomEditor "Sandbox.Toon.ToonShaderGUI"
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
