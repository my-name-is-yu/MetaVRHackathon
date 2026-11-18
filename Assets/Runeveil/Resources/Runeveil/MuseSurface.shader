Shader "Runeveil/Muse Surface"
{
    Properties
    {
        _Front ("Front artwork", 2D) = "white" {}
        _Back ("Back artwork", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Front);
            SAMPLER(sampler_Front);
            TEXTURE2D(_Back);
            SAMPLER(sampler_Back);

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 frontUV : TEXCOORD0;
                float2 backUV : TEXCOORD1;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 frontUV : TEXCOORD0;
                float2 backUV : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half backWeight : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.frontUV = input.frontUV;
                output.backUV = input.backUV;
                output.backWeight = input.color.a;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 front = SAMPLE_TEXTURE2D(_Front, sampler_Front, input.frontUV);
                half4 back = SAMPLE_TEXTURE2D(_Back, sampler_Back, input.backUV);
                // Keep the contours opaque and use the transparent border only to choose the facing artwork.
                half backWeight = input.backWeight * smoothstep(0.03h, 0.76h, back.a);
                half frontWeight = (1.0h - input.backWeight) * smoothstep(0.03h, 0.76h, front.a);
                half3 plum = half3(0.093h, 0.039h, 0.075h);
                half amount = max(frontWeight + backWeight, 0.002h);
                half3 painted = (front.rgb * frontWeight + back.rgb * backWeight) / amount;
                half fill = 0.88h + 0.12h * saturate(dot(normalize(input.normalWS), half3(-0.43h, 0.79h, -0.43h)));
                return half4(lerp(plum, painted, saturate(amount * 2.0h)) * fill, 1.0h);
            }
            ENDHLSL
        }
    }
}
