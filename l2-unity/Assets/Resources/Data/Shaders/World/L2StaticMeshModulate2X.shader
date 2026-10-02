// D3D9 FF Modulate2X + linear fog (mosque / terrain draws).
// Building: mesh UV, _L2StaticMeshAmbient.
// Floor:   world XZ / 128 UU, _L2TerrainAmbient.
// OB_Masked: _AlphaClip + _Cutoff. TwoSided: Cull Off.
// DepthPriming: DepthOnly / DepthNormalsOnly / ForwardOnly share GetVertexPositionInputs.
Shader "L2/World/StaticMeshModulate2X"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Color", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Toggle] _L2FloorMode ("Floor (world UV + TerrainAmbient)", Float) = 0
        _L2WorldUvPerTileUu ("World UV tile size (UU)", Float) = 128
        [Toggle] _AlphaClip ("Alpha Clip (OB_Masked)", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [HideInInspector] _L2HasSunMask ("L2 Sun Mask", Float) = 0
        [HideInInspector] _MainTex ("BaseMap", 2D) = "white" {}
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "L2Modulate2XLighting.hlsl"

    TEXTURE2D(_BaseMap);
    SAMPLER(sampler_BaseMap);

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        float4 _BaseColor;
        float _Cull;
        float _L2FloorMode;
        float _L2WorldUvPerTileUu;
        float _AlphaClip;
        float _Cutoff;
        float _L2HasSunMask;
    CBUFFER_END

    void L2_AlphaClip(float2 uv)
    {
        if (_AlphaClip > 0.5)
            clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a - _Cutoff);
    }

    float2 L2_MeshUv(float2 uv)
    {
        float4 st = _BaseMap_ST;
        if (abs(st.x) + abs(st.y) < 0.0001)
            st = float4(1, 1, 0, 0);
        return uv * st.xy + st.zw;
    }
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
            Name "L2StaticMeshForwardOnly"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
                float4 sunMask : TEXCOORD3;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float viewAbsZ : TEXCOORD1;
                float3 sun : TEXCOORD2;
                float3 sunDebug : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = posInputs.positionCS;

                if (_L2FloorMode > 0.5)
                {
                    float tileUu = max(_L2WorldUvPerTileUu, 1.0);
                    float2 worldUu = posInputs.positionWS.xz * 52.5;
                    OUT.uv = worldUu / tileUu;
                }
                else
                {
                    OUT.uv = L2_MeshUv(IN.uv);
                }

                float3 vtx = IN.color.rgb;
                if (max(max(vtx.r, vtx.g), vtx.b) < 0.001)
                    vtx = float3(1, 1, 1);
                OUT.color = float4(vtx, IN.color.a);
                OUT.sun = L2_StaticMeshMaskedSun(
                    IN.sunMask.x,
                    TransformObjectToWorldNormal(IN.normalOS));
                OUT.sunDebug = L2_StaticMeshSunDebugColor(IN.sunMask.x, OUT.sun);
                OUT.viewAbsZ = abs(posInputs.positionVS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                L2_AlphaClip(IN.uv);

                float3 rgb;
                if (_L2FloorMode > 0.5)
                {
                    rgb = L2_Modulate2XFog(tex.rgb * _BaseColor.rgb, IN.color.rgb, _L2TerrainAmbient.rgb, IN.viewAbsZ, _L2TerrainIntensity);
                }
                else
                {
                    float3 ambient = L2_OrWhite(_L2StaticMeshAmbient.rgb);
                    float3 color0 = _L2HasSunMask > 0.5
                        ? saturate(ambient * 0.5 + IN.sun)
                        : saturate(IN.color.rgb * ambient * 0.5);
                    rgb = L2_LinearFog(L2_Modulate2X(tex.rgb * _BaseColor.rgb, color0), IN.viewAbsZ);
                    if (_L2SunMaskDebug > 0.5 && _L2HasSunMask > 0.5)
                        rgb = lerp(IN.sunDebug, tex.rgb * _BaseColor.rgb, 0.18);
                }

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
            #pragma target 2.0
            #pragma multi_compile_instancing

            struct DepthAttrs
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DepthVaryings DepthVert(DepthAttrs IN)
            {
                DepthVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionCS = GetVertexPositionInputs(IN.positionOS.xyz).positionCS;
                OUT.uv = L2_MeshUv(IN.uv);
                return OUT;
            }

            half DepthFrag(DepthVaryings IN) : SV_Target
            {
                L2_AlphaClip(IN.uv);
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
            #pragma target 2.0
            #pragma multi_compile_instancing

            struct DNAttrs
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DNVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DNVaryings DepthNormalsVert(DNAttrs IN)
            {
                DNVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionCS = GetVertexPositionInputs(IN.positionOS.xyz).positionCS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = L2_MeshUv(IN.uv);
                return OUT;
            }

            half4 DepthNormalsFrag(DNVaryings IN) : SV_Target
            {
                L2_AlphaClip(IN.uv);
                float3 n = normalize(IN.normalWS);
                return half4(n * 0.5 + 0.5, 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
