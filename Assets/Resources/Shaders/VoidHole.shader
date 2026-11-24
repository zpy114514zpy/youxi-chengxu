// 墙上的黑洞：一个带噪声软边的椭圆面片。
// 中心死黑、靠近边缘渗一点烧焦的暖褐、边缘外 12% 渐隐，贴到墙上就像墙体被烧穿了一个洞。
Shader "House/VoidHole"
{
    Properties
    {
        [HDR] _EmberColor ("Ember Color", Color) = (1, .55, .22, 1)
        _EmberStrength ("Ember Strength", Range(0, 1)) = .12
        _NoiseScale ("Noise Scale", Float) = 9
        _EdgeSoftness ("Edge Softness", Range(.02, .30)) = .13
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+60"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Void"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _EmberColor;
                float _EmberStrength;
                float _NoiseScale;
                float _EdgeSoftness;
            CBUFFER_END

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1.0, 0.0));
                float c = hash21(i + float2(0.0, 1.0));
                float d = hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float d = length(p);

                // 边缘不规则：低频噪声让轮廓像被烧穿，而不是一个规整的椭圆。
                float n = vnoise(i.uv * _NoiseScale) * .085
                        + vnoise(i.uv * _NoiseScale * 2.7) * .038
                        + vnoise(i.uv * _NoiseScale * 6.1) * .014;
                d += n - .068;

                float mask = 1.0 - smoothstep(1.0 - _EdgeSoftness, 1.0, d);
                if (mask <= .001) discard;

                // 中心死黑 → 越靠边缘越偏焦褐，制造“洞有深度”的错觉。
                float depth = saturate(d);
                float3 col = lerp(float3(.0015, .0015, .0025), float3(.040, .024, .015), depth * depth * .9);

                // 内侧最后 8% 渗一圈很淡的暖光（呼应“另一边有东西在吸引我”）。
                float rim = smoothstep(1.0 - _EdgeSoftness * 2.4, 1.0, d) * mask;
                col += _EmberColor.rgb * rim * _EmberStrength;

                return half4(col, mask);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
