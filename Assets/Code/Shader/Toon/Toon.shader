Shader "Sandbox/Toon"
{
    Properties
    {
        [Header(Base)]
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [Toggle(_NORMALMAP)] _UseNormalMap("Use Normal Map", Float) = 0
        [NoScaleOffset][Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2

        [Header(Toon Shading)]
        _ShadeColor("1st Shade Color", Color) = (0.62, 0.62, 0.78, 1)
        _ShadeThreshold("1st Shade Threshold", Range(0, 1)) = 0.5
        _ShadeSoftness("1st Shade Softness", Range(0, 0.5)) = 0.015
        _Shade2Color("2nd Shade Color", Color) = (0.36, 0.34, 0.52, 1)
        _Shade2Threshold("2nd Shade Threshold", Range(0, 1)) = 0.28
        _Shade2Softness("2nd Shade Softness", Range(0, 0.5)) = 0.015
        [Toggle(_TOON_RAMP_MAP)] _UseRampMap("Use Ramp Map (replaces shade colors)", Float) = 0
        [NoScaleOffset] _RampMap("Ramp Map (U = Half Lambert)", 2D) = "white" {}

        [Header(Lighting)]
        _LightColorInfluence("Light Color Influence", Range(0, 1)) = 1
        _ReceiveShadowStrength("Receive Shadow Strength", Range(0, 1)) = 1
        _SSAOStrength("SSAO Strength", Range(0, 1)) = 0.5
        _AmbientStrength("Ambient Strength", Range(0, 2)) = 0.35
        _AdditionalLightStrength("Additional Light Strength", Range(0, 2)) = 1

        [Header(Specular)]
        [Toggle(_TOON_SPECULAR)] _UseSpecular("Enable Specular", Float) = 1
        [HDR] _ToonSpecColor("Specular Color", Color) = (0.9, 0.9, 1, 1)
        _SpecPower("Specular Power", Range(1, 256)) = 48
        _SpecThreshold("Specular Threshold", Range(0, 1)) = 0.5
        _SpecSoftness("Specular Softness", Range(0, 0.5)) = 0.02

        [Header(Rim Light)]
        [Toggle(_TOON_RIM)] _UseRim("Enable Rim Light", Float) = 1
        [HDR] _RimColor("Rim Color", Color) = (0.45, 0.6, 1, 1)
        _RimThreshold("Rim Threshold", Range(0, 1)) = 0.72
        _RimSoftness("Rim Softness", Range(0, 0.5)) = 0.02
        _RimLightSideMask("Rim Light Side Mask", Range(0, 1)) = 0.6

        [Header(Emission)]
        [Toggle(_EMISSION)] _UseEmission("Enable Emission", Float) = 0
        [NoScaleOffset] _EmissionMap("Emission Map", 2D) = "white" {}
        [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 1)

        [Header(Outline)]
        [ToggleUI] _OutlineEnabled("Enable Outline", Float) = 1
        _OutlineColor("Outline Color", Color) = (0.06, 0.05, 0.09, 1)
        _OutlineWidth("Outline Width (px at 1080p)", Range(0, 10)) = 2.5
        _OutlineBaseColorBlend("Outline Base Color Blend", Range(0, 1)) = 0.35
        _OutlineFadeStart("Outline Fade Start Distance", Float) = 15
        _OutlineFadeEnd("Outline Fade End Distance", Float) = 60
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
            Name "ToonForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ToonForwardVertex
            #pragma fragment ToonForwardFragment

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _TOON_RAMP_MAP
            #pragma shader_feature_local_fragment _TOON_SPECULAR
            #pragma shader_feature_local_fragment _TOON_RIM
            #pragma shader_feature_local_fragment _EMISSION

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "ToonInput.hlsl"
            #include "ToonForwardPass.hlsl"
            ENDHLSL
        }

        // 背面法アウトライン。ShaderGUI が _OutlineEnabled に応じてこのパス自体を有効／無効にする
        Pass
        {
            Name "ToonOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ToonOutlineVertex
            #pragma fragment ToonOutlineFragment

            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "ToonOutlinePass.hlsl"
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
            #pragma vertex ToonShadowCasterVertex
            #pragma fragment ToonShadowCasterFragment

            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "ToonDepthPasses.hlsl"
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
            #pragma vertex ToonDepthVertex
            #pragma fragment ToonDepthOnlyFragment

            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "ToonDepthPasses.hlsl"
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
            #pragma vertex ToonDepthVertex
            #pragma fragment ToonDepthNormalsFragment

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "ToonDepthPasses.hlsl"
            ENDHLSL
        }
    }

    CustomEditor "Sandbox.Toon.ToonShaderGUI"
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
