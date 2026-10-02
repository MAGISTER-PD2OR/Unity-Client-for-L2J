// L2 terrain base: opaque layer 0 only. D3D9 FF saturate(t0 * Color0 * 2) + linear fog.
// Overlays: L2/World/TerrainLayerModulate2X, L2TerrainOverlayRenderPass after opaques.
// Color UV = worldXZ_UU / 128 / _Layer_0_UV. DepthPriming: DepthOnly / DepthNormalsOnly.
Shader "L2/World/TerrainBaseModulate2X"
{
    Properties
    {
        _Layer_0 ("Layer 0 Base", 2D) = "white" {}
        _Layer_1 ("Layer 1", 2D) = "white" {}
        _Layer_2 ("Layer 2", 2D) = "white" {}
        _Layer_3 ("Layer 3", 2D) = "white" {}
        _Layer_4 ("Layer 4", 2D) = "white" {}
        _Layer_5 ("Layer 5", 2D) = "white" {}
        _Layer_6 ("Layer 6", 2D) = "white" {}
        _Layer_7 ("Layer 7", 2D) = "white" {}
        _Layer_8 ("Layer 8", 2D) = "white" {}
        _Layer_9 ("Layer 9", 2D) = "white" {}
        [NoScaleOffset] _LayerMask_1 ("Mask 1 (AlphaMap)", 2D) = "black" {}
        [NoScaleOffset] _LayerMask_2 ("Mask 2 (AlphaMap)", 2D) = "black" {}
        [NoScaleOffset] _LayerMask_3 ("Mask 3 (AlphaMap)", 2D) = "black" {}
        [NoScaleOffset] _LayerMask_4 ("Mask 4 (AlphaMap)", 2D) = "black" {}
        [NoScaleOffset] _LayerMask_5 ("Mask 5 (AlphaMap)", 2D) = "black" {}
        [NoScaleOffset] _LayerMask_6 ("Mask 6 (AlphaMap)", 2D) = "black" {}
        [NoScaleOffset] _LayerMask_7 ("Mask 7 (AlphaMap)", 2D) = "black" {}
        [NoScaleOffset] _LayerMask_8 ("Mask 8 (AlphaMap)", 2D) = "black" {}
        [NoScaleOffset] _LayerMask_9 ("Mask 9 (AlphaMap)", 2D) = "black" {}
        [NoScaleOffset] _Splatmaps ("Splatmaps (array, slice i = layer i+1)", 2DArray) = "black" {}
        [Toggle] _Layer_1_Enabled ("Layer 1 Enabled", Float) = 1
        [Toggle] _Layer_2_Enabled ("Layer 2 Enabled", Float) = 1
        [Toggle] _Layer_3_Enabled ("Layer 3 Enabled", Float) = 1
        [Toggle] _Layer_4_Enabled ("Layer 4 Enabled", Float) = 1
        [Toggle] _Layer_5_Enabled ("Layer 5 Enabled", Float) = 1
        [Toggle] _Layer_6_Enabled ("Layer 6 Enabled", Float) = 1
        [Toggle] _Layer_7_Enabled ("Layer 7 Enabled", Float) = 1
        [Toggle] _Layer_8_Enabled ("Layer 8 Enabled", Float) = 1
        [Toggle] _Layer_9_Enabled ("Layer 9 Enabled", Float) = 1
        _Layer_0_UV ("Layer 0 UScale (TerrainInfo)", Vector) = (1, 1, 0, 0)
        _Layer_1_UV ("Layer 1 UScale", Vector) = (1, 1, 0, 0)
        _Layer_2_UV ("Layer 2 UScale", Vector) = (1, 1, 0, 0)
        _Layer_3_UV ("Layer 3 UScale", Vector) = (2, 2, 0, 0)
        _Layer_4_UV ("Layer 4 UScale", Vector) = (1, 1, 0, 0)
        _Layer_5_UV ("Layer 5 UScale", Vector) = (1, 1, 0, 0)
        _Layer_6_UV ("Layer 6 UScale", Vector) = (1, 1, 0, 0)
        _Layer_7_UV ("Layer 7 UScale", Vector) = (1.5, 1.5, 0, 0)
        _Layer_8_UV ("Layer 8 UScale", Vector) = (1, 1, 0, 0)
        _Layer_9_UV ("Layer 9 UScale", Vector) = (1.5, 1.5, 0, 0)
        _L2WorldUvPerTileUu ("World UV tile (UU)", Float) = 128
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [HideInInspector] _BaseMap ("BaseMap", 2D) = "white" {}
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "L2Modulate2XLighting.hlsl"
    #include "L2TerrainCommon.hlsl"

    TEXTURE2D(_Layer_0);
    SAMPLER(sampler_Layer_0);
    TEXTURE2D(_Layer_1);
    TEXTURE2D(_Layer_2);
    TEXTURE2D(_Layer_3);
    TEXTURE2D(_Layer_4);
    TEXTURE2D(_Layer_5);
    TEXTURE2D(_Layer_6);
    TEXTURE2D(_Layer_7);
    TEXTURE2D(_Layer_8);
    TEXTURE2D(_Layer_9);
    TEXTURE2D(_LayerMask_1);
    TEXTURE2D(_LayerMask_2);
    TEXTURE2D(_LayerMask_3);
    TEXTURE2D(_LayerMask_4);
    TEXTURE2D(_LayerMask_5);
    TEXTURE2D(_LayerMask_6);
    TEXTURE2D(_LayerMask_7);
    TEXTURE2D(_LayerMask_8);
    TEXTURE2D(_LayerMask_9);

    CBUFFER_START(UnityPerMaterial)
        float4 _Layer_0_ST;
        float4 _Layer_1_ST;
        float4 _Layer_2_ST;
        float4 _Layer_3_ST;
        float4 _Layer_4_ST;
        float4 _Layer_5_ST;
        float4 _Layer_6_ST;
        float4 _Layer_7_ST;
        float4 _Layer_8_ST;
        float4 _Layer_9_ST;
        float4 _Layer_0_UV;
        float4 _Layer_1_UV;
        float4 _Layer_2_UV;
        float4 _Layer_3_UV;
        float4 _Layer_4_UV;
        float4 _Layer_5_UV;
        float4 _Layer_6_UV;
        float4 _Layer_7_UV;
        float4 _Layer_8_UV;
        float4 _Layer_9_UV;
        float _Layer_1_Enabled;
        float _Layer_2_Enabled;
        float _Layer_3_Enabled;
        float _Layer_4_Enabled;
        float _Layer_5_Enabled;
        float _Layer_6_Enabled;
        float _Layer_7_Enabled;
        float _Layer_8_Enabled;
        float _Layer_9_Enabled;
        float _L2WorldUvPerTileUu;
        float _Cull;
    CBUFFER_END
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "UniversalMaterialType" = "Unlit"
            "IgnoreProjector" = "True"
        }

        Cull [_Cull]
        ZWrite On
        ZTest LEqual
        Blend One Zero

        Pass
        {
            Name "L2TerrainBaseForwardOnly"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 worldUu : TEXCOORD0;
                float3 color0 : TEXCOORD1;
                float viewAbsZ : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = posInputs.positionCS;
                OUT.worldUu = posInputs.positionWS.xz * 52.5;
                float3 vtx = IN.color.rgb;
                if (max(max(vtx.r, vtx.g), vtx.b) < 0.001)
                    vtx = float3(1, 1, 1);
                float intensity = L2_TerrainIntensityAt(OUT.worldUu);
                OUT.color0 = L2_TerrainColor0(vtx, _L2TerrainAmbient.rgb, intensity);
                OUT.viewAbsZ = abs(posInputs.positionVS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                if (_L2TerrainLightDebug > 0.5)
                    return half4(L2_TerrainLightDebugColor(IN.worldUu), 1);
                float2 uv = L2_TerrainLayerUv(IN.worldUu, _Layer_0_UV, _L2WorldUvPerTileUu);
                half3 albedo = SAMPLE_TEXTURE2D(_Layer_0, sampler_Layer_0, uv).rgb;
                float3 rgb = L2_LinearFog(L2_Modulate2X(albedo, IN.color0), IN.viewAbsZ);
                return half4(rgb, 1);
            }
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
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma target 4.5
            #pragma multi_compile_instancing

            struct DepthAttrs
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 DepthVert(DepthAttrs IN) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                return GetVertexPositionInputs(IN.positionOS.xyz).positionCS;
            }

            half DepthFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma target 4.5
            #pragma multi_compile_instancing

            struct DNAttrs
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DNVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DNVaryings DepthNormalsVert(DNAttrs IN)
            {
                DNVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionCS = GetVertexPositionInputs(IN.positionOS.xyz).positionCS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 DepthNormalsFrag(DNVaryings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);
                return half4(n * 0.5 + 0.5, 0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "L2TerrainRetainOverlays"
            Tags { "LightMode" = "L2TerrainRetainOverlays" }
            ColorMask 0
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex RetainVert
            #pragma fragment RetainFrag
            #pragma target 4.5

            float4 RetainVert(float4 positionOS : POSITION) : SV_POSITION
            {
                return GetVertexPositionInputs(positionOS.xyz).positionCS;
            }

            half4 RetainFrag() : SV_Target
            {
                float2 uv = float2(0.5, 0.5);
                half4 c = 0;
                c += SAMPLE_TEXTURE2D(_Layer_1, sampler_Layer_0, uv);
                c += SAMPLE_TEXTURE2D(_Layer_2, sampler_Layer_0, uv);
                c += SAMPLE_TEXTURE2D(_Layer_3, sampler_Layer_0, uv);
                c += SAMPLE_TEXTURE2D(_Layer_4, sampler_Layer_0, uv);
                c += SAMPLE_TEXTURE2D(_Layer_5, sampler_Layer_0, uv);
                c += SAMPLE_TEXTURE2D(_Layer_6, sampler_Layer_0, uv);
                c += SAMPLE_TEXTURE2D(_Layer_7, sampler_Layer_0, uv);
                c += SAMPLE_TEXTURE2D(_Layer_8, sampler_Layer_0, uv);
                c += SAMPLE_TEXTURE2D(_Layer_9, sampler_Layer_0, uv);
                c += SAMPLE_TEXTURE2D(_LayerMask_1, sampler_Layer_0, uv);
                c += SAMPLE_TEXTURE2D(_LayerMask_2, sampler_Layer_0, uv);
                c += SAMPLE_TEXTURE2D(_LayerMask_3, sampler_Layer_0, uv);
                c += SAMPLE_TEXTURE2D(_LayerMask_4, sampler_Layer_0, uv);
                c += SAMPLE_TEXTURE2D(_LayerMask_5, sampler_Layer_0, uv);
                c += SAMPLE_TEXTURE2D(_LayerMask_6, sampler_Layer_0, uv);
                c += SAMPLE_TEXTURE2D(_LayerMask_7, sampler_Layer_0, uv);
                c += SAMPLE_TEXTURE2D(_LayerMask_8, sampler_Layer_0, uv);
                c += SAMPLE_TEXTURE2D(_LayerMask_9, sampler_Layer_0, uv);
                return c * 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
