Shader "Childhood/PortalSpark"
{
    Properties
    {
        _Color ("Tint", Color) = (1.0, 0.86, 0.58, 1)
        _Boost ("Boost", Range(0.2, 4)) = 1.4
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+45" "RenderType"="Transparent" }

        Pass
        {
            Name "PortalSpark"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            ZWrite Off
            Blend SrcAlpha One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            float4 _Color;
            float _Boost;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 圆形柔边：不用贴图，UV 直接算
                float2 p = input.uv * 2.0 - 1.0;
                float r = saturate(1.0 - dot(p, p));
                float a = r * r;
                float3 col = _Color.rgb * input.color.rgb * _Boost;
                float alpha = a * _Color.a * input.color.a;
                return half4(col * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
