Shader "AeroFlow/StreamlineFlowURP"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.5, 0.9, 1.0, 0.9)
        _DashCount ("Dash Count", Range(2, 80)) = 24
        _DashRatio ("Dash Fill Ratio", Range(0.2, 0.95)) = 0.55
        _FlowSpeed ("Flow Speed", Range(0, 8)) = 2.5
        _GlowIntensity ("Glow Intensity", Range(0, 3)) = 0.8
        _FadeStart ("Tail Fade Start", Range(0.2, 1.0)) = 0.5
        _FadeEnd ("Tail Fade End Alpha", Range(0.0, 0.5)) = 0.05
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+15"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "StreamlineFlow"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float  fogFactor  : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _DashCount;
                float  _DashRatio;
                float  _FlowSpeed;
                float  _GlowIntensity;
                float  _FadeStart;
                float  _FadeEnd;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(posWS);
                o.color = input.color;
                o.uv = input.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float u = i.uv.x; // 0..1 along the line

                // Scrolling dash pattern
                float scrolled = u * _DashCount - _Time.y * _FlowSpeed;
                float dashPattern = frac(scrolled);

                // Smooth dash edges for anti-aliasing
                float dashWidth = _DashRatio;
                float edge = 0.06;
                float dash = smoothstep(0.0, edge, dashPattern)
                           * smoothstep(dashWidth + edge, dashWidth, dashPattern);

                // Secondary subtle pulse wave
                float pulse = 0.85 + 0.15 * sin(u * 6.2832 * 3.0 - _Time.y * _FlowSpeed * 1.7);

                // Tail fade: alpha decreases along the line
                float tailFade = lerp(1.0, _FadeEnd, smoothstep(_FadeStart, 1.0, u));

                // Base color from vertex color (set by LineRenderer per-vertex colors)
                half4 col = i.color * _BaseColor;

                // Apply glow boost
                col.rgb *= 1.0 + _GlowIntensity * dash * pulse;

                // Combine alpha: vertex alpha * dash * tail fade
                col.a *= lerp(0.15, 1.0, dash) * tailFade * pulse;

                // Apply fog
                col.rgb = MixFog(col.rgb, i.fogFactor);

                return col;
            }
            ENDHLSL
        }
    }

    // Fallback for built-in render pipeline
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+15"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos    : SV_POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
                UNITY_FOG_COORDS(1)
            };

            float4 _BaseColor;
            float  _DashCount;
            float  _DashRatio;
            float  _FlowSpeed;
            float  _GlowIntensity;
            float  _FadeStart;
            float  _FadeEnd;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float u = i.uv.x;

                float scrolled = u * _DashCount - _Time.y * _FlowSpeed;
                float dashPattern = frac(scrolled);

                float dashWidth = _DashRatio;
                float edge = 0.06;
                float dash = smoothstep(0.0, edge, dashPattern)
                           * smoothstep(dashWidth + edge, dashWidth, dashPattern);

                float pulse = 0.85 + 0.15 * sin(u * 6.2832 * 3.0 - _Time.y * _FlowSpeed * 1.7);
                float tailFade = lerp(1.0, _FadeEnd, smoothstep(_FadeStart, 1.0, u));

                fixed4 col = i.color * _BaseColor;
                col.rgb *= 1.0 + _GlowIntensity * dash * pulse;
                col.a *= lerp(0.15, 1.0, dash) * tailFade * pulse;

                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }

    FallBack "Sprites/Default"
}
