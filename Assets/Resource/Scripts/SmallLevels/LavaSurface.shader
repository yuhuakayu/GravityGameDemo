Shader "Gravity Game/Small Levels/Lava"
{
    Properties
    {
        [PerRendererData] _MainTex ("Current", 2D) = "white" {}
        _PreviousTex ("Previous", 2D) = "white" {}
        _Interpolation ("Interpolation", Range(0,1)) = 1
        _Color ("Tint", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend", Float) = 10
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="False" }
        Cull Off
        ZWrite Off
        Blend [_SrcBlend] [_DstBlend]
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_PreviousTex); SAMPLER(sampler_PreviousTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Interpolation;
            CBUFFER_END
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                output.color = input.color * _Color;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                half4 current = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 previous = SAMPLE_TEXTURE2D(_PreviousTex, sampler_PreviousTex, input.uv);
                return lerp(previous, current, _Interpolation) * input.color;
            }
            ENDHLSL
        }
    }
}
