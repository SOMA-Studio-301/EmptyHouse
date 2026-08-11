Shader "Custom/Outline"
{
    Properties
    {
        [HDR] _OutlineColor ("외곽선 컬러", Color) = (1, 0, 0, 1)
        _OutlineWidth ("외곽선 두께 (픽셀)", Range(0, 20)) = 3
        // 각진 메시(큐브 등)는 면마다 법선이 쪼개져 있어 법선 확장 시 코너가 갈라진다.
        // 피벗이 메시 중심에 있는 볼록한 물체라면 방사 확장이 더 깔끔하다
        [Toggle(_RADIAL_EXTRUDE)] _RadialExtrude ("피벗 기준 방사 확장 (각진 메시용)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry+1"
            // 정점을 오브젝트 공간에서 확장하므로 동적 배칭에 묶이면 안 된다
            // (배칭되면 정점이 월드 공간으로 합쳐져 법선 확장이 어긋난다)
            "DisableBatching" = "True"
        }

        Pass
        {
            Name "Outline"
            // URP 는 LightMode 를 인식하지 못하는 패스를 에러 없이 건너뛴다.
            // 빌트인 RP 의 "Always" 는 URP 에서 아무것도 그리지 않는다
            Tags { "LightMode" = "SRPDefaultUnlit" }

            // 뒷면만 그린다 — 확장된 뒷면이 원본 실루엣 밖으로 삐져나온 부분이 외곽선이 된다
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #pragma multi_compile_instancing
            #pragma shader_feature_local_vertex _RADIAL_EXTRUDE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 정점 확장 계산. Shader Graph 로 갈아타도 이 파일을 Custom Function 으로 그대로 재사용한다
            #include "InvertedHullOutline.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            // 법선 방향으로 확장한 정점을 클립 공간으로 변환한다.
            // 확장량은 카메라 거리에 비례하므로 화면상 두께는 거리와 무관하게 일정하다
            Varyings OutlineVert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // 확장 방향만 갈아 끼운다. 오프셋 계산은 두 모드가 동일하다
            #ifdef _RADIAL_EXTRUDE
                float3 extrudeDirOS = normalize(input.positionOS.xyz);
            #else
                float3 extrudeDirOS = input.normalOS;
            #endif

                float3 expandedOS;
                OutlineOffsetOS_float(input.positionOS.xyz, extrudeDirOS, _OutlineWidth, expandedOS);

                output.positionCS = TransformObjectToHClip(expandedOS);
                return output;
            }

            // 외곽선은 단색이라 조명 계산이 없다
            half4 OutlineFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return _OutlineColor;
            }
            ENDHLSL
        }
    }

    // 빌트인 RP 의 Fallback "Diffuse" 는 URP 에 존재하지 않는다
    FallBack Off
}
