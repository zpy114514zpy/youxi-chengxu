Shader "Childhood/PortalSurface"
{
    Properties
    {
        _ColorCore ("Core Color", Color) = (1.0, 0.96, 0.82, 1)
        _ColorMid ("Mid Color", Color) = (1.0, 0.62, 0.22, 1)
        _ColorRim ("Rim Color", Color) = (0.45, 0.82, 0.88, 1)
        _Intensity ("Intensity", Range(0, 4)) = 1.0
        _Speed ("Swirl Speed", Range(0, 3)) = 0.5
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+40" "RenderType"="Transparent" }

        Pass
        {
            Name "PortalSwirl"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            ZWrite Off
            Blend SrcAlpha One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            float4 _ColorCore, _ColorMid, _ColorRim;
            float _Intensity, _Speed;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float noise2(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float r = length(p);
                if (r > 1.0) discard;

                float ang = atan2(p.y, p.x);
                float t = _Time.y * _Speed;

                // 螺旋：角度随半径扭转，再叠一层噪声，像被卷进去的光
                float s = ang * 2.0 + t * 1.6 - r * 4.5;
                float n = noise2(float2(cos(s), sin(s)) * 1.5 + float2(r * 3.0 - t * 0.7, t * 0.45));
                float bands = smoothstep(0.20, 0.95, 0.5 + 0.5 * sin(s * 2.0 + n * 4.5 + r * 8.0));

                float core = pow(saturate(1.0 - r), 1.5);          // 中心亮
                float rim  = smoothstep(0.70, 0.97, r) * smoothstep(1.0, 0.94, r);   // 边缘环带

                float3 col = _ColorCore * core * 1.15
                           + _ColorMid  * bands * core * 1.05
                           + _ColorRim  * rim * 1.25;

                float alpha = (core * 0.42 + bands * core * 0.55 + rim * 0.95) * _Intensity;
                return half4(col * alpha, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
