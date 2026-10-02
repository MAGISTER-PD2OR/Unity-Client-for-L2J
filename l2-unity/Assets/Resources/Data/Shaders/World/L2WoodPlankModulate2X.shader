// Wooden planks (world_bridge01). L2 fixed-function draw:
//   vertex: Color0 = saturate(in_Color1 * GlobalAmbient + in_Color0)
//   pixel:  rgb = saturate(tex * Color0 * 2)
// in_Color1 on this mesh is white. GlobalAmbient is the static ambient already halved (noon 60/255).
// Unity's _L2StaticMeshAmbient is the full byte (noon 120/255), so the add uses ambient * 0.5.
// HSVStaticMeshSunLight is trunc(plane * 32) on the same byte.
// _L2UseVertexLight stays off until the captured in_Color0 stream is on the mesh.
Shader "L2/World/WoodPlankModulate2X"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Color", Color) = (1, 1, 1, 1)
        [Toggle] _L2UseVertexLight ("L2 vertex light", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
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
        float _L2UseVertexLight;
        float _Cull;
        float _L2HasSunMask;
    CBUFFER_END

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
            Name "L2WoodPlankForwardOnly"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 sunMask : TEXCOORD3;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float viewAbsZ : TEXCOORD1;
                float4 color : TEXCOORD2;
                float3 sun : TEXCOORD3;
                float3 sunDebug : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = posInputs.positionCS;
                OUT.uv = L2_MeshUv(IN.uv);
                OUT.viewAbsZ = abs(posInputs.positionVS.z);
                OUT.color = IN.color;
                OUT.sun = L2_StaticMeshMaskedSun(
                    IN.sunMask.x,
                    TransformObjectToWorldNormal(IN.normalOS));
                OUT.sunDebug = L2_StaticMeshSunDebugColor(IN.sunMask.x, OUT.sun);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                float3 ambient = L2_OrWhite(_L2StaticMeshAmbient.rgb);
                float3 color0 = ambient;
                if (_L2HasSunMask > 0.5)
                    color0 = saturate(ambient * 0.5 + IN.sun);
                else if (_L2UseVertexLight > 0.5)
                    color0 = L2_StaticMeshColor0(IN.color.rgb, ambient);
                float3 rgb = L2_LinearFog(L2_Modulate2X(tex.rgb * _BaseColor.rgb, color0), IN.viewAbsZ);
                if (_L2SunMaskDebug > 0.5 && _L2HasSunMask > 0.5)
                    rgb = lerp(IN.sunDebug, tex.rgb * _BaseColor.rgb, 0.18);
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DepthVaryings DepthVert(DepthAttrs IN)
            {
                DepthVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionCS = GetVertexPositionInputs(IN.positionOS.xyz).positionCS;
                return OUT;
            }

            half DepthFrag(DepthVaryings IN) : SV_Target
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
            #pragma target 2.0
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
    }
    FallBack Off
}
