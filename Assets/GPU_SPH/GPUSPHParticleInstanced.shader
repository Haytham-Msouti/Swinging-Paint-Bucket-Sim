Shader "Custom/GPUSPHParticleInstanced"
{
    Properties
    {
        _Color ("Base Fluid Color", Color) = (1, 0.4117647, 0.7058824, 1)
        _PressureTint ("Pressure / Density Highlight", Color) = (1, 0.85, 1, 1)

        _Size ("Rendered Particle Size", Float) = 0.020

        _RestDensity ("SPH Rest Density", Float) = 120
        _PressureScale ("Pressure Visualization Scale", Float) = 0.04
        _DensityScale ("Density Visualization Scale", Float) = 0.015

        _EdgeSoftness ("Particle Edge Softness", Range(0.5, 4)) = 1.5
        _AlphaMultiplier ("Alpha Multiplier", Range(0, 1)) = 0.85

        // Rainbow / batch coloring
        _UseRainbowParticles ("Use Rainbow Particles", Float) = 1
        _UseColorBatches ("Use Color Batches", Float) = 1
        _ParticlesPerColorBatch ("Particles Per Color Batch", Float) = 10000
        _RedBlueBatchesOnly ("Red Blue Batches Only", Float) = 1
        _BatchColorA ("Batch Color A", Color) = (1, 0, 0, 1)
        _BatchColorB ("Batch Color B", Color) = (0, 0.25, 1, 1)
        _RainbowHueScale ("Rainbow Hue Scale", Float) = 0.12
        _RainbowScrollSpeed ("Rainbow Scroll Speed", Float) = 0.0
        _RainbowSaturation ("Rainbow Saturation", Range(0, 1)) = 1
        _RainbowValue ("Rainbow Value", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct Particle
            {
                float3 position;
                float3 velocity;
                float density;
                float pressure;
            };

            StructuredBuffer<Particle> _Particles;

            float4 _Color;
            float4 _PressureTint;

            float _Size;
            float _RestDensity;
            float _PressureScale;
            float _DensityScale;
            float _EdgeSoftness;
            float _AlphaMultiplier;

            float _UseRainbowParticles;
            float _UseColorBatches;
            float _ParticlesPerColorBatch;
            float _RedBlueBatchesOnly;
            float4 _BatchColorA;
            float4 _BatchColorB;
            float _RainbowHueScale;
            float _RainbowScrollSpeed;
            float _RainbowSaturation;
            float _RainbowValue;

            struct appdata
            {
                float3 vertex : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct v2f
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float pressure01 : TEXCOORD1;
                float density01 : TEXCOORD2;
                float4 particleColor : TEXCOORD3;
                float active : TEXCOORD4;
            };

            float3 HSVToRGB(float3 hsv)
            {
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(hsv.xxx + K.xyz) * 6.0 - K.www);
                return hsv.z * lerp(K.xxx, saturate(p - K.xxx), hsv.y);
            }

            float4 GetBatchColor(uint instanceID)
            {
                float safeBatchSize = max(1.0, _ParticlesPerColorBatch);
                uint batchIndex = (uint)floor((float)instanceID / safeBatchSize);

                // 1000 red, 1000 blue, 1000 red, 1000 blue...
                if (_RedBlueBatchesOnly > 0.5)
                {
                    return (batchIndex % 2 == 0) ? _BatchColorA : _BatchColorB;
                }

                // 1000 red, 1000 orange, 1000 yellow, 1000 green, 1000 blue...
                float hue = frac((float)batchIndex * _RainbowHueScale + _Time.y * _RainbowScrollSpeed);
                float3 rgb = HSVToRGB(float3(hue, saturate(_RainbowSaturation), max(0.0, _RainbowValue)));
                return float4(rgb, _Color.a);
            }

            v2f vert(appdata v)
            {
                v2f o;

                Particle particle = _Particles[v.instanceID];
                o.active = particle.density > 0.0 ? 1.0 : 0.0;

                float3 cameraRight = UNITY_MATRIX_I_V._m00_m10_m20;
                float3 cameraUp    = UNITY_MATRIX_I_V._m01_m11_m21;

                float3 worldPosition =
                    particle.position +
                    cameraRight * (v.vertex.x * _Size) +
                    cameraUp    * (v.vertex.y * _Size);

                o.positionCS = mul(UNITY_MATRIX_VP, float4(worldPosition, 1.0));
                o.uv = v.uv;

                o.pressure01 = saturate(particle.pressure * _PressureScale);
                o.density01 = saturate((particle.density - _RestDensity) * _DensityScale);

                if (_UseRainbowParticles > 0.5 && _UseColorBatches > 0.5)
                {
                    o.particleColor = GetBatchColor(v.instanceID);
                }
                else
                {
                    o.particleColor = _Color;
                }

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                clip(i.active - 0.5);
                float2 centeredUV = i.uv * 2.0 - 1.0;
                float radiusSquared = dot(centeredUV, centeredUV);

                clip(1.0 - radiusSquared);

                float softMask = saturate(1.0 - radiusSquared);
                softMask = pow(softMask, _EdgeSoftness);

                fixed4 color = i.particleColor;

                float physicalHighlight = saturate(i.pressure01 * 0.65 + i.density01 * 0.35);
                color.rgb = lerp(color.rgb, _PressureTint.rgb, physicalHighlight * 0.45);
                color.a *= softMask * _AlphaMultiplier;

                return color;
            }
            ENDCG
        }
    }

    FallBack Off
}
