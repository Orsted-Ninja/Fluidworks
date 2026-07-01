Shader "AeroFlow/VertexColorLitURP"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "RenderType"="Opaque" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionHCS = positionInputs.positionCS;
                output.normalWS = normalInputs.normalWS;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half lighting = 0.24h + ndotl * 0.76h;
                half3 litColor = input.color.rgb * lighting;
                return half4(litColor, input.color.a);
            }
            ENDHLSL
        }
    }
}
