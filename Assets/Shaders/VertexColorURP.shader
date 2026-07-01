Shader "AeroFlow/VertexColorURP"
{
    Properties
    {
        _Brightness ("Brightness", Range(0.25, 2.0)) = 1.0
        _Saturation ("Saturation", Range(0.0, 2.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                half4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                half _Brightness;
                half _Saturation;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                output.normalWS = NormalizeNormalPerVertex(TransformObjectToWorldNormal(input.normalOS));
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 color = input.color.rgb;
                half luminance = dot(color, half3(0.2126h, 0.7152h, 0.0722h));
                color = lerp(luminance.xxx, color, _Saturation);

                half3 lightDir = normalize(half3(0.35h, 0.85h, 0.35h));
                half ndotl = saturate(dot(normalize(input.normalWS), lightDir));
                half lighting = 0.35h + ndotl * 0.65h;

                color *= lighting * _Brightness;
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
}
