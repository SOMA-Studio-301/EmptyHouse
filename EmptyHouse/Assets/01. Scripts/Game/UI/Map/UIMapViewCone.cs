using Border.Core;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 안내도 방향 밝힘(EH-62, 요구 5) — 전면 어둠 오버레이 + 플레이어 위치 중심 부채꼴이 카메라 요를 따라 회전한다.
/// **방향 기반, 벽 차폐 없음**(확정 해석) · 방문 기억 없음 — 상태 저장 0.
/// 마커 레이어(markersRoot)는 이 마스크 위에 있어 어두운 영역에서도 보인다.
/// </summary>
public sealed class UIMapViewCone : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private RectTransform coneRect; // 부채꼴 이미지(자식) — 피벗 = 플레이어 위치, 회전 = 시선 방향
    [SerializeField] private Image darknessOverlay; // 전면 어둠 오버레이(자식) — 부채꼴 밖 가림

    [Header("Tuning ⚪")]
    [SerializeField] private float coneAngleDegrees = 70f; // 부채꼴 각도(도) — 기본 카메라 수평 FOV 근사, Figma/플레이테스트 튜닝
    [SerializeField] private float outsideVisibility = 0.1f; // 부채꼴 밖 잔여 가시성(0 = 완전 암전) — "보는 곳만 밝힘" 강도

    /// <summary>부채꼴 피벗·회전을 갱신한다 — 매 프레임(패널 열림 중) UIMapOverview 가 호출한다.</summary>
    /// <param name="panelPosition">플레이어 마커의 패널 로컬 좌표.</param>
    /// <param name="cellAngleDegrees">맵 공간 시선 각도(도) — MapOverviewModel.WorldYawToCellAngle 출력.</param>
    public void SetPose(Vector2 panelPosition, float cellAngleDegrees)
    {
        // TODO(impl):
        Log.D("[UIMapViewCone] SetPose");
    }
}
