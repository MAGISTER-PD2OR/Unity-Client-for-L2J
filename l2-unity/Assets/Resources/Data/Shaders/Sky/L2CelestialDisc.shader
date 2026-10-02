// L2 ANSun / ANMoon: unlit textured billboard, SrcAlpha/One additive (PTDS_Brighten).
Shader "L2/Sky/CelestialDisc"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "CelestialDisc"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off
            Lighting Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
#if UNITY_REVERSED_Z
                o.positionCS.z = 0.0;
#else
                o.positionCS.z = o.positionCS.w;
#endif
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                return tex * _Color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "BloomExclude"
            Tags { "LightMode" = "L2BloomExclude" }

            Blend Off
            ZWrite Off
            ZTest LEqual
            Cull Off
            ColorMask RGB
            Lighting Off

            HLSLPROGRAM
            #pragma vertex vertExclude
            #pragma fragment fragExclude
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vertExclude(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
#if UNITY_REVERSED_Z
                o.positionCS.z = 0.0;
#else
                o.positionCS.z = o.positionCS.w;
#endif
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float4 fragExclude(Varyings i) : SV_Target
            {
                float a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a * _Color.a;
                clip(a - 0.02);
                return float4(1, 1, 1, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "BloomInclude"
            Tags { "LightMode" = "L2BloomInclude" }

            Blend Off
            ZWrite Off
            ZTest LEqual
            Cull Off
            ColorMask RGB
            Lighting Off

            HLSLPROGRAM
            #pragma vertex vertInclude
            #pragma fragment fragInclude
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vertInclude(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
#if UNITY_REVERSED_Z
                o.positionCS.z = 0.0;
#else
                o.positionCS.z = o.positionCS.w;
#endif
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float4 fragInclude(Varyings i) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _Color;
                float a = max(tex.a, max(tex.r, max(tex.g, tex.b)));
                clip(a - 0.02);
                return float4(0, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
