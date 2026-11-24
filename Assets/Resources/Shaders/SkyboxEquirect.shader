// 第二关的夜空：一张经纬展开（2:1）的天空图铺到整个天球上。
// 之所以自己写一个而不是用内置 Skybox/Panoramic：内置那个在 URP 里对贴图
// 导入设置（Latitude-Longitude 映射）有要求，运行时从 Resources 加载的图
// 不一定符合，容易变成一片灰。这里直接按方向算 UV，贴图什么设置都能用。
Shader "House/SkyboxEquirect"
{
    Properties
    {
        _MainTex ("Sky (Equirect 2:1)", 2D) = "grey" {}
        _Exposure ("Exposure", Range(0, 3)) = 1
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Rotation ("Rotation (deg)", Range(0, 360)) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            Name "Sky"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Tint;
                float _Exposure;
                float _Rotation;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 dirOS : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                // 天空盒的网格就是一个内翻的立方体，顶点坐标本身就是方向。
                o.dirOS = v.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dirOS);

                // 经度 → u，纬度 → v。天顶 v=0、地平线 v=.5、天底 v=1。
                float yaw = atan2(d.z, d.x) + radians(_Rotation);
                float2 uv = float2(yaw * (1.0 / 6.28318530718) + 0.5,
                                   acos(clamp(d.y, -1.0, 1.0)) * (1.0 / 3.14159265359));

                half3 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                c *= _Tint.rgb * _Exposure;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
