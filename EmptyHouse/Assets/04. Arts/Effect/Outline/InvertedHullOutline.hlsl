#ifndef EMPTYHOUSE_INVERTED_HULL_OUTLINE_INCLUDED
#define EMPTYHOUSE_INVERTED_HULL_OUTLINE_INCLUDED

// 인버티드 헐 외곽선의 정점 확장.
// 법선 방향으로 정점을 밀어낸 뒷면(Render Face = Back)을 원본 위에 겹쳐 그리면
// 실루엣만 삐져나와 외곽선이 된다.
//
// 확장량을 카메라 거리에 비례시켜 화면상 두께를 픽셀 단위로 고정한다.
// (원점 기준 스케일 방식은 피벗이 치우치면 외곽선도 같이 쏠리므로 쓰지 않는다)
//
// 그래프 설정: Unlit 타깃 + Render Face = Back + Depth Write On

// Shader Graph 노드 프리뷰는 URP 파이프라인 컨텍스트 없이 따로 컴파일된다.
// SHADERGRAPH_PREVIEW 는 프리뷰 셰이더에서만 정의되므로 실제 패스에는 영향이 없다.
#ifndef SHADERGRAPH_PREVIEW
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#endif

// positionOS    : 오브젝트 공간 정점 위치 (Position 노드, Object 공간)
// normalOS      : 오브젝트 공간 법선 (Normal Vector 노드, Object 공간)
// widthPixels   : 외곽선 두께(화면 픽셀). 카메라 거리와 무관하게 일정하다
// OutPositionOS : 확장된 오브젝트 공간 위치. Vertex 블록의 Position 에 연결한다
void OutlineOffsetOS_float(
    float3 positionOS,
    float3 normalOS,
    float widthPixels,
    out float3 OutPositionOS)
{
#ifdef SHADERGRAPH_PREVIEW
    OutPositionOS = positionOS;
#else
    float3 positionWS = TransformObjectToWorld(positionOS);
    float3 normalWS = TransformObjectToWorldNormal(normalOS);
    float4 positionCS = TransformWorldToHClip(positionWS);

    // positionCS.w 가 뷰 공간 깊이다. 이 깊이에서 화면 1픽셀이 차지하는 월드 거리.
    // 원근 투영이 아니면 w 가 1 로 고정돼 거리 보정이 사라진다(직교 카메라에서는 두께 고정)
    //
    // _m11 에 abs 필수: 렌더 텍스처로 그릴 때(게임 뷰) Y 가 뒤집혀 음수가 된다.
    // 부호를 그대로 쓰면 정점이 안쪽으로 수축해 오브젝트에 파묻히고 외곽선이 통째로 사라진다.
    // 여기서 필요한 건 크기뿐이고 방향은 월드 공간 법선이 이미 갖고 있다
    float worldPerPixel = 2.0 * positionCS.w / (_ScreenParams.y * abs(UNITY_MATRIX_P._m11));

    float3 offsetWS = normalWS * widthPixels * worldPerPixel;
    OutPositionOS = TransformWorldToObject(positionWS + offsetWS);
#endif
}

// Shader Graph 노드 정밀도가 Half 일 때 쓰이는 변종. 내부 연산은 float 로 유지한다
// (위치 변환을 half 로 하면 원점에서 먼 오브젝트가 떨린다)
void OutlineOffsetOS_half(
    half3 positionOS,
    half3 normalOS,
    half widthPixels,
    out half3 OutPositionOS)
{
    float3 positionOSFloat;
    OutlineOffsetOS_float(positionOS, normalOS, widthPixels, positionOSFloat);
    OutPositionOS = (half3)positionOSFloat;
}

#endif // EMPTYHOUSE_INVERTED_HULL_OUTLINE_INCLUDED
