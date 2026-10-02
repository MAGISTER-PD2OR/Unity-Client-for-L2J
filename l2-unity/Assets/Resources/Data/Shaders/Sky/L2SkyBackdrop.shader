// Original colour-pass backdrop.
// Upper sky is the blue vault (depth-tested so mountains stay in front).
// Hard haze (EID 1475 WhiteRing card) then fills every pixel below the
// horizon, including the whole black lower buffer; later world drawing
// is what covers it in the original client. Soft WhiteRing is a later fade.
Shader "Hidden/L2/SkyBackdrop"
{
    Properties
    {
        _SkyColor ("Sky Color", Vector) = (0.384314, 0.478431, 0.72549, 1)
        _LowerHazeColor ("Lower Haze Color", Vector) = (0.6, 0.498039, 0.627451, 1)
    }

    HLSLINCLUDE
    #pragma target 3.0
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    float4 _SkyColor;
    float4 _LowerHazeColor;

    // ~2.3° shared band at the horizon; WhiteRing softens it.
    static const float kHorizonOverlap = 0.04;

    struct Attributes
    {
        uint vertexID : SV_VertexID;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
#if UNITY_REVERSED_Z
        output.positionCS.z = UNITY_RAW_FAR_CLIP_VALUE;
#else
        output.positionCS.z = output.positionCS.w;
#endif
        return output;
    }

    // Normalize per pixel. Interpolating a normalized Y across the fullscreen
    // triangle only produced a thin horizon wedge.
    float ViewRayY(float4 positionCS)
    {
        float2 uv = GetNormalizedScreenSpaceUV(positionCS);
        float3 farWS = ComputeWorldSpacePosition(uv, UNITY_RAW_FAR_CLIP_VALUE, UNITY_MATRIX_I_VP);
        float3 ray = farWS - GetCameraPositionWS();
        float len = length(ray);
        return len > 1e-6 ? ray.y / len : 1.0;
    }

    float4 FragUpperSky(Varyings input) : SV_Target
    {
        clip(ViewRayY(input.positionCS) + kHorizonOverlap);
        return float4(_SkyColor.rgb, 1);
    }

    float4 FragLowerHaze(Varyings input) : SV_Target
    {
        clip(kHorizonOverlap - ViewRayY(input.positionCS));
        return float4(_LowerHazeColor.rgb, 1);
    }

    float4 FragUpperSkyExclude(Varyings input) : SV_Target
    {
        clip(ViewRayY(input.positionCS) + kHorizonOverlap);
        return float4(1, 1, 1, 1);
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Background"
            "RenderType" = "Opaque"
        }

        Pass
        {
            Name "UpperSky"
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragUpperSky
            ENDHLSL
        }

        Pass
        {
            Name "LowerHazeFill"
            // Original EID 1475 paints the whole lower sky buffer before world
            // exists. Unity already has deferred depth there, so LEqual only
            // kept the thin horizon overlap and left the rest black.
            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragLowerHaze
            ENDHLSL
        }

        Pass
        {
            Name "BloomExclude"
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend Off
            ColorMask RGB
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragUpperSkyExclude
            ENDHLSL
        }
    }
}
