Shader "UR3eVR/URPHandOutline"
{
    Properties
    {
        [Header(Colors)]
        _ColorPrimary ("Glow Primary Color", Color) = (0.25, 0.70, 1.0, 1.0)
        _ColorTop ("Top Gradient Color", Color) = (0.08, 0.12, 0.20, 1.0)
        _ColorBottom ("Bottom Gradient Color", Color) = (0.02, 0.04, 0.08, 1.0)

        [Header(Fresnel Edge Glow)]
        _RimFactor ("Rim Intensity", Range(0.1, 5.0)) = 1.8
        _FresnelPower ("Fresnel Exponent", Range(0.1, 5.0)) = 1.6
        _HandAlpha ("Hand Alpha", Range(0.0, 1.0)) = 0.90
        _BaseAlpha ("Minimum Visible Alpha", Range(0.0, 1.0)) = 0.40
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent" 
            "RenderPipeline" = "UniversalPipeline" 
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 normalWS     : TEXCOORD0;
                float3 viewDirWS    : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorPrimary;
                float4 _ColorTop;
                float4 _ColorBottom;
                float  _RimFactor;
                float  _FresnelPower;
                float  _HandAlpha;
                float  _BaseAlpha;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = vertexInputs.positionCS;
                output.normalWS = normalInputs.normalWS;
                output.viewDirWS = GetWorldSpaceViewDir(vertexInputs.positionWS);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(input.viewDirWS);

                // Compute Fresnel edge glow for both eyes in VR Single Pass Instanced
                float NdotV = saturate(dot(normalWS, viewDirWS));
                float fresnel = pow(1.0 - NdotV, _FresnelPower);

                // Vertical gradient across the hand
                float grad = normalWS.y * 0.5 + 0.5;
                half3 bodyColor = lerp(_ColorBottom.rgb, _ColorTop.rgb, grad);

                // Combine rim glow with base body color
                half3 finalColor = bodyColor + _ColorPrimary.rgb * (fresnel * _RimFactor);

                // Alpha blending: edge is more opaque, center is sleek translucent
                float alpha = saturate(_HandAlpha * (_BaseAlpha + fresnel * (1.0 - _BaseAlpha)));

                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
