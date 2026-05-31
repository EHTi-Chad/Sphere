Shader "Custom/Starfield"
{
    SubShader
    {
        Tags { "RenderType"="Background" "Queue"="Background" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        Cull Front

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            // hash functions for procedural stars
            float hash(float3 p)
            {
                p = frac(p * float3(443.897, 441.423, 437.195));
                p += dot(p, p.yzx + 19.19);
                return frac((p.x + p.y) * p.z);
            }

            float3 hash3(float3 p)
            {
                return float3(hash(p), hash(p + 47.1), hash(p + 91.3));
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.direction);

                float3 bg = float3(0.0, 0.0, 0.0);

                // single sparse star layer
                float scale = 60.0;
                float3 cell = floor(dir * scale);
                float3 rnd = hash3(cell);

                float3 starPos = (cell + rnd) / scale;
                float dist = length(dir - normalize(starPos));

                float brightness = rnd.z;
                float size = 0.0005 + rnd.y * 0.0003;

                if (dist < size && brightness > 0.85)
                {
                    float intensity = (1.0 - dist / size) * brightness;
                    intensity = pow(intensity, 3.0);
                    float3 starColor = lerp(float3(0.85, 0.88, 1.0), float3(1.0, 0.95, 0.8), rnd.x);
                    bg += starColor * intensity * 1.2;
                }

                return half4(bg, 1.0);
            }
            ENDHLSL
        }
    }
}
