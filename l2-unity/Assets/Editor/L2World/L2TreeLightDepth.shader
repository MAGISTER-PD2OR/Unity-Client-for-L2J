// Editor-only. Draws a mesh into a depth image from the sun's point of view.
Shader "Hidden/L2/TreeLightDepth"
{
    Properties
    {
        _Ray ("Ray", Vector) = (0, -1, 0, 0)
        _CamPos ("Camera", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForwardOnly" }
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float3 _Ray;
            float3 _CamPos;

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float depth : TEXCOORD0;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS);
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.depth = dot(positionWS - _CamPos, _Ray);
                return OUT;
            }

            float4 Frag(Varyings IN) : SV_Target
            {
                float depth = max(IN.depth, 0);
                uint packed = (uint)(depth * 64.0 + 0.5);
                return float4(
                    (packed & 255) / 255.0,
                    ((packed >> 8) & 255) / 255.0,
                    ((packed >> 16) & 255) / 255.0,
                    1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
