// Replay of the captured combine draw (shader.asm), not L2Yebis_Grade.
// s0 goes through the first matrix and filmic. s1 is the glare texture for c7-c9.
// A missing glare is black, so the matrix bias 0.005 still remains.
// c15 in constants.txt is ignored: the bytecode defines its own c15.
Shader "Hidden/L2/YebisCaptureReplay"
{
    Properties
    {
        _MainTex ("s0", 2D) = "black" {}
        _GlareTex ("s1", 2D) = "black" {}
        _DitherTex ("s3", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _GlareTex;
            sampler2D _DitherTex;
            float _HasDither;
            float _GammaPow;
            float4 _Dither;
            float4 _CapC4;
            float4 _CapC5;
            float4 _CapC6;
            float4 _CapC7;
            float4 _CapC8;
            float4 _CapC9;
            float _FilmicA;
            float _FilmicB;

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(appdata_img input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.texcoord;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                // def c15.z in the bytecode. Used as the floor before exp and log.
                const float kFloor = 5.96046448e-8;
                float3 scene = tex2D(_MainTex, input.uv).rgb;
                float3 glare = tex2D(_GlareTex, input.uv).rgb;

                float4 sceneH = float4(scene, 1.0);
                float3 x;
                x.r = dot(sceneH, _CapC4);
                x.g = dot(sceneH, _CapC5);
                x.b = dot(sceneH, _CapC6);
                x = max(x, kFloor);

                float3 e = exp(-_FilmicA * x);
                float3 oneMinusBe = 1.0 - _FilmicB * e;
                float3 filmic = saturate((1.0 - e) * oneMinusBe * oneMinusBe);

                float4 glareH = float4(glare, 1.0);
                float3 g;
                g.r = saturate(dot(glareH, _CapC7));
                g.g = saturate(dot(glareH, _CapC8));
                g.b = saturate(dot(glareH, _CapC9));
                float3 combined = filmic + (1.0 - filmic) * g;

                float3 safe = max(combined, kFloor);
                float3 gamma = exp2(log2(safe) * _GammaPow);
                float3 output = gamma;
                if (_HasDither > 0.5)
                {
                    float d = tex2D(_DitherTex, input.uv).r;
                    output = gamma + gamma * _Dither.y + d * _Dither.x + _Dither.z;
                }
                return float4(saturate(output), 1.0);
            }
            ENDHLSL
        }
    }
}
