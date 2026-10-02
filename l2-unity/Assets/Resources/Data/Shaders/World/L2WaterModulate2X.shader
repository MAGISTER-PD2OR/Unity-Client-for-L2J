// D3D9 FF water sheet. Blend SrcAlpha/InvSrcAlpha, AlphaTest GREATER 0.
// rgb = saturate(stages * BSPAmbient * 2). a = base1.a * BSPAmbient.a (not *2).
// ZWrite was not in the capture. Cull defaults off so the sheet is visible from above.
Shader "L2/World/WaterModulate2X"
{
    Properties
    {
        [MainTexture] _WaterMap ("Ocean atlas (20 frames)", 2D) = "white" {}
        _DetailMap ("base1", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0
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
            Name "L2WaterForwardOnly"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0
            #include "L2WaterCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 sheetUv : TEXCOORD0;
                float viewAbsZ : TEXCOORD1;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = posInputs.positionCS;
                float2 uv = L2_WaterSheetUv(posInputs.positionWS);
                OUT.sheetUv = uv * _WaterMap_ST.xy + _WaterMap_ST.zw;
                OUT.viewAbsZ = abs(posInputs.positionVS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // Each Ertheia sheet owns UV 0-1. Pan and the 22.5 deg rotator
                // run inside that tile. Rotating the raw world UV spins every
                // wave around one point and draws the petal across the sea.
                float time = _Time.y;
                float2 local = IN.sheetUv - floor(IN.sheetUv);
                float2 uv0 = L2_WaterAtlasUv(L2_WaterRotate(L2_WaterOceanS(local, time)), time);
                float2 uv1 = L2_WaterAtlasUv(L2_WaterOscillator0(local, time), time);
                half4 c0 = SAMPLE_TEXTURE2D(_WaterMap, sampler_WaterMap, uv0);
                half4 c1 = SAMPLE_TEXTURE2D(_WaterMap, sampler_WaterMap, uv1);
                half4 baseMap = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, float2(0.5, 0.5));
                half4 water = L2_WaterCombine(c0, c1, baseMap, c1, L2_WaterFactor());
                clip(water.a - (half)_Cutoff - 1e-5);
                float3 rgb = L2_LinearFog(water.rgb, IN.viewAbsZ);
                return half4(rgb, water.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
