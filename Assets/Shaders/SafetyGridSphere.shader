Shader "UR3eVR/SafetyGridSphere"
{
    Properties
    {
        [MainColor] _BaseColor ("Base Color", Color) = (1.0, 0.15, 0.15, 1.0)
        _GridDensity ("Grid Density", Float) = 24.0
        _LineWidth ("Line Width", Range(0.01, 0.3)) = 0.08
        _Alpha ("Alpha", Range(0.0, 1.0)) = 0.95
        _Pulse ("Pulse", Range(0.2, 2.5)) = 1.0
        _HandPosWS ("Hand Position World", Vector) = (0, 0, 0, 0)
        _FocusRadius ("Focus Radius", Float) = 0.38
        _ApproachAlpha ("Approach Alpha", Range(0.0, 1.0)) = 0.0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-20"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SafetyGrid"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual

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
                float2 uv           : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 positionOS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _HandPosWS;
                float _GridDensity;
                float _LineWidth;
                float _Alpha;
                float _Pulse;
                float _FocusRadius;
                float _ApproachAlpha;
                float _Cull;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInputs = GetVertexPositionInputs(input.positionOS.xyz);

                output.positionCS = vertexInputs.positionCS;
                output.positionWS = vertexInputs.positionWS;
                output.positionOS = input.positionOS.xyz;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // หากมืออยู่ไกล หรือยังไม่เข้าใกล้ขอบเขต ให้ไม่แสดงผลเลยเพื่อประหยัดการ Render
                if (_ApproachAlpha <= 0.001) discard;

                // คำนวณระยะห่างระหว่างจุดบน Barrier กับมือ (World Space)
                float distToHand = length(input.positionWS - _HandPosWS.xyz);

                // จุดเรืองแสงรอบมือ (Focus Spot) แบบ Smooth Hermite falloff
                float focus = saturate(1.0 - distToHand / max(_FocusRadius, 0.05));
                float focusWeight = focus * focus * (3.0 - 2.0 * focus);

                // ให้มี Ambient Visibility จางๆ ทั่วทั้งทรงกลมเมื่อมือเริ่มเข้าใกล้ เพื่อให้มองเห็นโครงร่าง Barrier ได้ชัดเจน
                // และสว่างจ้าที่สุด (100%) ตรงบริเวณรอบมือ
                float overallVisibility = lerp(0.14, 1.0, focusWeight);

                // พิกัดทรงกลม Latitude / Longitude ใน Object Space (Symmetric & Uniform)
                float3 p = normalize(input.positionOS);
                float phi = asin(clamp(p.y, -1.0, 1.0)); // [-pi/2, pi/2]
                float theta = atan2(p.z, p.x);            // [-pi, pi]

                float latNorm = (phi / 3.14159265 + 0.5) * _GridDensity;
                float lonNorm = (theta / 6.2831853 + 0.5) * (_GridDensity * 2.0);

                float latDist = abs(frac(latNorm) - 0.5);
                float lonDist = abs(frac(lonNorm) - 0.5);

                // 1. เส้นกริดคมชัด (Core Laser Line)
                float latCore = 1.0 - smoothstep(_LineWidth * 0.25, _LineWidth * 0.75, latDist);
                float lonCore = 1.0 - smoothstep(_LineWidth * 0.25, _LineWidth * 0.75, lonDist);
                float coreGrid = max(latCore, lonCore);

                // 2. แสงเรืองรอบเส้น (Emissive Halo Glow)
                float latGlow = 1.0 - smoothstep(_LineWidth * 0.5, _LineWidth * 2.2, latDist);
                float lonGlow = 1.0 - smoothstep(_LineWidth * 0.5, _LineWidth * 2.2, lonDist);
                float haloGrid = max(latGlow, lonGlow) * 0.55;

                // 3. เส้นวงแหวนหลัก (Equator & Meridians)
                float eqRing = 1.0 - smoothstep(0.006, 0.028, abs(p.y));
                float merRing1 = 1.0 - smoothstep(0.006, 0.028, abs(p.x));
                float merRing2 = 1.0 - smoothstep(0.006, 0.028, abs(p.z));
                float majorRings = max(eqRing, max(merRing1, merRing2)) * 1.3;

                // 4. จุดตัดกริดเรืองแสง (Intersection Nodes)
                float nodeDots = latCore * lonCore * 1.8;

                float finalPattern = max(coreGrid + haloGrid, majorRings) + nodeDots;

                // Final Alpha: สว่างชัดเจน เห็น Barrier ง่ายและสวยงามมากใน VR
                float finalAlpha = saturate(finalPattern * overallVisibility * _ApproachAlpha * _Pulse * _Alpha * _BaseColor.a);
                if (finalAlpha < 0.005) discard;

                // Emissive Boost: เพิ่มความสว่างเรืองแสง (Sci-Fi Neon Glow)
                float3 finalColor = _BaseColor.rgb * (1.35 + focusWeight * 1.8) * _Pulse;

                return half4(finalColor, finalAlpha);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
