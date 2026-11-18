Shader "Runeveil/Arena Surface"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _BaseMap ("Texture", 2D) = "white" {}
        _Shading ("Shading", Range(0, 1)) = 1
        _Fog ("Distance Fog", Range(0, 1)) = 1
        _Spot ("Soft Glow", Range(0, 1)) = 0
        _Rim ("Ward Rim", Range(0, 1)) = 0
        [HideInInspector] _SrcBlend ("Source Blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination Blend", Float) = 0
        [HideInInspector] _ZWrite ("Depth Write", Float) = 1
        [HideInInspector] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Shading;
                half _Fog;
                half _Spot;
                half _Rim;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.color = input.color;
                #ifndef UNITY_COLORSPACE_GAMMA
                    output.color.rgb = SRGBToLinear(input.color.rgb);
                #endif
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 color = input.color * _BaseColor * SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 normal = SafeNormalize(float3(input.normalWS));
                half key = saturate(dot(normal, half3(-0.49, 0.74, -0.46)));
                half rim = saturate(dot(normal, half3(0.52, 0.44, 0.73)));
                half3 lighting = half3(0.49, 0.44, 0.68)
                    + key * half3(0.64, 0.50, 0.33)
                    + rim * half3(0.20, 0.14, 0.29)
                    + max(0.0, normal.y) * 0.045;
                float3 view = GetCameraPositionWS() - input.positionWS;
                half stoneGrain = 0.97 + 0.03 * sin(input.positionWS.x * 29.0 + input.positionWS.y * 41.0)
                    * cos(input.positionWS.z * 36.0 - input.positionWS.y * 23.0);
                color.rgb *= lerp(1.0, lighting * stoneGrain, _Shading);
                half fog = saturate((length(view) - 7.0) * 0.020) * _Fog;
                color.rgb = lerp(color.rgb, half3(0.22, 0.125, 0.218), fog);
                half2 radial = input.uv * 2.0 - 1.0;
                half soft = saturate(1.0 - dot(radial, radial));
                color.a *= lerp(1.0, soft * soft * soft, _Spot);
                half edge = 1.0 - abs(dot(normal, SafeNormalize(view)));
                color.a *= lerp(1.0, 0.10 + edge * edge * 0.90, _Rim);
                return color;
            }
            ENDHLSL
        }
    }
}
