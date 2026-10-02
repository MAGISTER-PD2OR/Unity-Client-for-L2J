// D3D9 FF tree leaves: Blend SrcAlpha/InvSrcAlpha, AlphaTest GREATER 0, ZWrite off.
// Original VK Cull Front + clockwise front matches Unity Cull Back.
// Cull Front shows the opposite shell: its normals point away from the camera,
// so the whole crown is one brightness.
// rgb = saturate(tex * Color0 * 2); a = tex.a * Color0.a (not *2).
Shader "L2/World/TreeLeafModulate2X"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Color", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0
        [HideInInspector] _L2HasSunMask ("L2 Sun Mask", Float) = 0
        [HideInInspector] _MainTex ("BaseMap", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "UniversalMaterialType" = "Unlit"
            "IgnoreProjector" = "True"
        }

        Cull [_Cull]
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask RGBA

        Pass
        {
            Name "L2TreeLeafForwardOnly"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0
            #include "L2TreeCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 sunMask : TEXCOORD3;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float viewAbsZ : TEXCOORD1;
                float3 sun : TEXCOORD2;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = posInputs.positionCS;
                OUT.uv = L2_TreeUv(IN.uv);
                OUT.color = L2_TreeVertexColor(IN.color);
                OUT.sun = L2_StaticMeshDirectSun(TransformObjectToWorldNormal(IN.normalOS));
                OUT.viewAbsZ = abs(posInputs.positionVS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                float3 rgb = L2_TreeColorRgb(tex.rgb, IN.color.rgb, IN.sun, IN.viewAbsZ);
                float a = saturate(tex.a * IN.color.a * _BaseColor.a);
                clip(a - _Cutoff);
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
