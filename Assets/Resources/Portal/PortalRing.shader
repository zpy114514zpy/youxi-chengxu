Shader "Childhood/PortalRing"
{
    Properties
    {
        _ColorA ("Color A", Color) = (1.0, 0.72, 0.30, 1)
        _ColorB ("Color B", Color) = (1.0, 0.93, 0.74, 1)
        _Speed ("Pulse Speed", Range(0, 8)) = 2.2
        _Intensity ("Intensity", Range(0, 4)) = 1.0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+35" "RenderType"="Transparent" }

        Pass
        {
            Name "PortalRing"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            ZWrite Off
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            float4 _ColorA, _ColorB;
            float _Speed, _Intensity;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float u = input.uv.x;      // 绕环一周
                float v = input.uv.y;      // 管子截面/环带径向

                // 两道沿环行进的亮带，方向相反，看起来像能量在环里流动
                float a = 0.5 + 0.5 * sin(u * 6.2831853 * 2.0 - _Time.y * _Speed);
                float b = 0.5 + 0.5 * sin(u * 6.2831853 * 3.0 + _Time.y * _Speed * 0.7);
                float pulse = saturate(a * 0.65 + b * 0.45);

                // 管子朝向玩家的那一面最亮（v=0.25 是 +Z 方向），两侧渐暗，光环才有体积
                float edge = 0.30 + 0.70 * saturate(sin(v * 6.2831853) * .5 + .5);
                float3 col = lerp(_ColorA.rgb, _ColorB.rgb, pulse);
                float amp = (0.45 + 0.85 * pulse) * edge * _Intensity;
                return half4(col * amp, amp);
            }
            ENDHLSL
        }
    }
}
