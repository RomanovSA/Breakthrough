// A thin glowing shell around the Earth: invisible in the middle, brightest at the rim, added to what is behind it.
Shader "Geoscape/Atmosphere"
{
    Properties
    {
        _Color ("Colour", Color) = (0.3, 0.55, 1, 1)
        _Power ("Falloff", Float) = 3.5
        _Intensity ("Intensity", Float) = 1.4
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Power;
                float _Intensity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                float rim = pow(saturate(dot(N, V)), _Power);          // 1 in the middle of the shell, 0 at its edge
                float glow = pow(1.0 - saturate(dot(N, V)), 1.5) * rim * 4.0;
                return half4(_Color.rgb * glow * _Intensity, 1.0);
            }
            ENDHLSL
        }
    }
}
