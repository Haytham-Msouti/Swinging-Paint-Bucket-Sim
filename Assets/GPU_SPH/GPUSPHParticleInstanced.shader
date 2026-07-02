Shader "Custom/GPUSPHParticleInstanced"
{
    Properties
    {
        _Color ("Base Fluid Color", Color) = (1, 0.4117647, 0.7058824, 1)
        _PressureTint ("Pressure / Density Highlight", Color) = (1, 0.85, 1, 1)

        _Size ("Rendered Particle Size", Float) = 0.018

        _RestDensity ("SPH Rest Density", Float) = 120
        _PressureScale ("Pressure Visualization Scale", Float) = 0.04
        _DensityScale ("Density Visualization Scale", Float) = 0.015

        _EdgeSoftness ("Particle Edge Softness", Range(0.5, 4)) = 1.5
        _AlphaMultiplier ("Alpha Multiplier", Range(0, 1)) = 0.85
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

            /*
             * Academic GPU SPH particle visualization shader.
             *
             * This shader does not solve the SPH equations. The physical simulation
             * is executed in the Compute Shader. Here, the shader receives the final
             * particle data through a StructuredBuffer and visualizes each particle
             * using GPU instancing and camera-facing billboards.
             */

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
            };

            v2f vert(appdata v)
            {
                v2f o;

                Particle particle = _Particles[v.instanceID];

                /*
                 * Billboard rendering:
                 * The mesh is a small quad. It is expanded in world space using the
                 * camera right and up vectors so each particle always faces the camera.
                 */
                float3 cameraRight = UNITY_MATRIX_I_V._m00_m10_m20;
                float3 cameraUp    = UNITY_MATRIX_I_V._m01_m11_m21;

                float3 worldPosition =
                    particle.position +
                    cameraRight * (v.vertex.x * _Size) +
                    cameraUp    * (v.vertex.y * _Size);

                o.positionCS = mul(UNITY_MATRIX_VP, float4(worldPosition, 1.0));
                o.uv = v.uv;

                /*
                 * Normalize pressure and density only for visual feedback.
                 * The original physical values remain unchanged.
                 */
                o.pressure01 = saturate(particle.pressure * _PressureScale);
                o.density01 = saturate((particle.density - _RestDensity) * _DensityScale);

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                /*
                 * Convert the quad into a circular soft particle.
                 */
                float2 centeredUV = i.uv * 2.0 - 1.0;
                float radiusSquared = dot(centeredUV, centeredUV);

                clip(1.0 - radiusSquared);

                float softMask = saturate(1.0 - radiusSquared);
                softMask = pow(softMask, _EdgeSoftness);

                fixed4 color = _Color;

                /*
                 * Higher pressure/density means stronger compression in SPH.
                 * The shader brightens these particles slightly to make compressed
                 * fluid regions visible in the final rendering.
                 */
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
