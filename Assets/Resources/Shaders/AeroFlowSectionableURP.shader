Shader "AeroFlow/SectionableURP"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _UsePressureMap ("Use Pressure Map", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0;
                float4 pressureData : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionHCS  : SV_POSITION;
                float3 positionWS   : TEXCOORD1;
                float2 uv           : TEXCOORD0;
                float3 normalWS     : NORMAL;
                float fogCoord      : TEXCOORD3;
                float pressureScalar : TEXCOORD4;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _MainTex_ST;
                float _UsePressureMap;
            CBUFFER_END

            // These are now global shader properties set by SurfacePressureVisualizer
            float _PressureMin;
            float _PressureMax;

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            float4 _AeroFlowClipPlane; // Globally set via Shader.SetGlobalVector
            int _AeroFlowClipEnabled;  // Globally set via Shader.SetGlobalInt

            half3 TurboColormap(float t)
            {
                t = saturate(t);

                const float4 red4 = float4(0.13572138, 4.61539260, -42.66032258, 132.13108234);
                const float2 red2 = float2(-152.94239396, 59.28637943);
                const float4 green4 = float4(0.09140261, 2.19418839, 4.84296658, -14.18503333);
                const float2 green2 = float2(4.27729857, 2.82956604);
                const float4 blue4 = float4(0.10667330, 12.64194608, -60.58204836, 110.36276771);
                const float2 blue2 = float2(-89.90310912, 27.34824973);

                float2 tt = float2(t * t, t * t * t);
                float4 v4 = float4(1.0, t, tt.x, tt.y);
                float2 v2 = float2(tt.x * tt.x, tt.x * tt.y);

                return saturate(half3(
                    dot(v4, red4) + dot(v2, red2),
                    dot(v4, green4) + dot(v2, green2),
                    dot(v4, blue4) + dot(v2, blue2)
                ));
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.fogCoord = ComputeFogFactor(output.positionHCS.z);
                output.pressureScalar = input.pressureData.x;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (_AeroFlowClipEnabled > 0)
                {
                    float d = dot(input.positionWS, _AeroFlowClipPlane.xyz) + _AeroFlowClipPlane.w;
                    clip(d);
                }

                half4 baseColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _Color;
                if (_UsePressureMap > 0.5)
                {
                    float range = max(_PressureMax - _PressureMin, 1e-5);
                    float normalizedPressure = saturate((input.pressureScalar - _PressureMin) / range);
                    baseColor = half4(TurboColormap(normalizedPressure), _Color.a);
                }
                
                // Very simple Lambert lighting
                Light mainLight = GetMainLight();
                half NdotL = saturate(dot(normalize(input.normalWS), mainLight.direction));
                half3 lighting = mainLight.color * NdotL + half3(0.2, 0.2, 0.2); // ambient

                baseColor.rgb *= lighting;
                baseColor.rgb = MixFog(baseColor.rgb, input.fogCoord);
                
                return baseColor;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
