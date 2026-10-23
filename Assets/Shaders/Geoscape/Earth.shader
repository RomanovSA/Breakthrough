// The Earth of the strategic map: the day map where the sun shines, the city lights where it does not, a soft blue glow at the rim and a glint on the sea.
Shader "Geoscape/Earth"
{
    Properties
    {
        _DayMap ("Day map", 2D) = "white" {}
        _NightMap ("Night lights", 2D) = "black" {}
        _NightColor ("Night light tint", Color) = (1, 0.82, 0.55, 1)
        _NightStrength ("Night light strength", Float) = 1.8
        _AtmosphereColor ("Rim glow", Color) = (0.35, 0.6, 1, 1)
        _RimPower ("Rim power", Float) = 3
        _RimStrength ("Rim strength", Float) = 1.1
        _Ambient ("Ambient light on the day map", Range(0, 1)) = 0.16
        _GlintStrength ("Sea glint", Float) = 0.6
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_DayMap);   SAMPLER(sampler_DayMap);
            TEXTURE2D(_NightMap); SAMPLER(sampler_NightMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _DayMap_ST;
                float4 _NightColor;
                float _NightStrength;
                float4 _AtmosphereColor;
                float _RimPower;
                float _RimStrength;
                float _Ambient;
                float _GlintStrength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; float2 uv : TEXCOORD2; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = input.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                Light sun = GetMainLight();
                float3 L = normalize(sun.direction);
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));

                float ndl = dot(N, L);
                float day = smoothstep(-0.12, 0.30, ndl);

                half3 dayCol = SAMPLE_TEXTURE2D(_DayMap, sampler_DayMap, i.uv).rgb;
                half3 lights = SAMPLE_TEXTURE2D(_NightMap, sampler_NightMap, i.uv).rgb * _NightColor.rgb * _NightStrength;

                half3 lit = dayCol * (_Ambient + saturate(ndl) * sun.color.rgb);

                // the sea shines a little (the sea is the blue part of the map)
                float sea = saturate((dayCol.b - dayCol.r) * 4.0);
                float3 H = normalize(L + V);
                lit += pow(saturate(dot(N, H)), 90.0) * sea * _GlintStrength * sun.color.rgb;

                half3 col = lerp(lights + dayCol * _Ambient * 0.3, lit, day);

                float rim = pow(1.0 - saturate(dot(N, V)), _RimPower);
                col += _AtmosphereColor.rgb * rim * _RimStrength * (0.3 + 0.7 * day);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
