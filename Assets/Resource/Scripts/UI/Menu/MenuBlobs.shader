Shader "Hidden/GravityGame/MenuBlobs"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            ZTest Always Cull Off ZWrite Off

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            int _BlobCount;
            float4 _Blobs[256];
            float _BlobLayers[256];
            float4 _BaseColor;
            float4 _BackColor;
            float4 _FrontColor;
            float4 _Glows[4];
            float4 _GlowParams[4];
            float _Threshold;

            float4 frag(v2f_img input) : SV_Target
            {
                float2 gridPosition = float2(input.uv.x, 1.0 - input.uv.y) * float2(480.0, 270.0);
                float2 field = 0.0;
                [loop]
                for (int i = 0; i < _BlobCount; i++)
                {
                    float4 blob = _Blobs[i];
                    float2 delta = gridPosition - blob.xy;
                    float radius = 1.4142 * blob.z;
                    float q = saturate(1.0 - dot(delta, delta) / (radius * radius));
                    float contribution = blob.w * q * q;
                    float layer = _BlobLayers[i];
                    field += float2(1.0 - layer, layer) * contribution;
                    if (field.x > _Threshold && field.y > _Threshold) break;
                }
                float3 color = field.y > _Threshold ? _FrontColor.rgb : field.x > _Threshold ? _BackColor.rgb : _BaseColor.rgb;
                [unroll]
                for (int lamp = 0; lamp < 4; lamp++)
                {
                    float2 delta = gridPosition - _Glows[lamp].xy;
                    float distanceSquared = dot(delta, delta);
                    float flicker = _GlowParams[lamp].x;
                    float strength = distanceSquared < 64.0 * flicker ? 0.26 :
                        distanceSquared < 196.0 * flicker ? 0.15 :
                        distanceSquared < 484.0 * flicker ? 0.08 :
                        distanceSquared < 900.0 * flicker ? 0.04 : 0.0;
                    color = lerp(color, float3(255.0, 186.0, 104.0) / 255.0, strength * _GlowParams[lamp].y);
                }
                // Palette interpolation and stepped light mixing use the prototype's sRGB values.
                #ifndef UNITY_COLORSPACE_GAMMA
                color = GammaToLinearSpace(color);
                #endif
                return float4(color, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
