Shader "Childhood/ProximityOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0.22, 0.9, 1, 0.9)
        _OutlineWidth ("Outline Width", Range(0.002, 0.05)) = 0.018
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+50" "RenderType"="Transparent" }
        Pass
        {
            Name "GlowingSilhouette"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            float4 _OutlineColor;
            float _OutlineWidth;
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                float3 normal = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(worldPos + normalize(normal) * _OutlineWidth);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target { return _OutlineColor; }
            ENDHLSL
        }
    }
}
