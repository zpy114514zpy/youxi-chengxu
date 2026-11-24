// 第一关结尾的“门”：不再把墙烧穿成一个黑洞，而是在墙面上勾出一道暖黄色的门框光边。
// 中心完全透明，只有一圈带宽有限的框线，外加框内一点点辉光，中等明显。
Shader "House/PortalFrame"
{
    Properties
    {
        [HDR] _FrameColor ("Frame Color", Color) = (1, .78, .30, 1)
        _FrameStrength ("Strength", Range(0, 3)) = 1.05
        _BandCenter ("Band Center (uv)", Range(.60, .99)) = .90
        _BandWidth ("Band Width (uv)", Range(.004, .12)) = .038
        _Softness ("Softness", Range(.001, .06)) = .010
        _InnerGlow ("Inner Glow", Range(0, 1)) = .22
        _Corner ("Corner Rounding", Range(2, 12)) = 5
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
            Name "Frame"
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
                float4 _FrameColor;
                float _FrameStrength;
                float _BandCenter;
                float _BandWidth;
                float _Softness;
                float _InnerGlow;
                float _Corner;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // 0 在中心，1 在面片边缘。
                float2 p = abs(i.uv * 2.0 - 1.0);

                // 超椭圆距离：指数越大越接近直角矩形，5 左右是“门框”那种微圆角。
                float e = pow(pow(p.x, _Corner) + pow(p.y, _Corner), 1.0 / _Corner);

                // 框线本身
                float halfBand = _BandWidth * .5;
                float d = abs(e - _BandCenter);
                float band = 1.0 - smoothstep(halfBand - _Softness, halfBand + _Softness, d);

                // 框内一层很淡的辉光，让门看起来“被点亮”而不是画了一条线
                float inner = saturate(1.0 - (_BandCenter - e) / .22) * step(e, _BandCenter) * _InnerGlow;

                float a = saturate(band + inner * .55);
                if (a <= .002) discard;

                float3 col = _FrameColor.rgb * _FrameStrength;
                return half4(col, a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
