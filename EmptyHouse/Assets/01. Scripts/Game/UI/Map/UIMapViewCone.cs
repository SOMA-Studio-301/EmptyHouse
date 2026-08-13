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
    [SerializeField] private float coneRadiusPixels = 160f; // 부채꼴 사거리(패널 픽셀) — 밝히는 범위 크기. 0 이하면 Cone RectTransform 크기를 그대로 쓴다
    [SerializeField] private float outsideVisibility = 0.1f; // 부채꼴 밖 잔여 가시성(0 = 완전 암전) — "보는 곳만 밝힘" 강도

    private Image coneImage; // 부채꼴 이미지 — Filled/Radial360(Origin Top)이면 각도를 fillAmount 로 구동. 통짜 스프라이트면 회전만(선택 의존)

    /// <summary>부채꼴 각도·사거리·어둠 농도를 인스펙터 값대로 1회 적용한다.</summary>
    private void Awake()
    {
        Log.D("[UIMapViewCone] Awake");

        coneImage = coneRect.GetComponent<Image>();
        ApplyTuning();
    }

    /// <summary>인스펙터 튜닝(각도·사거리·어둠)을 에디터에서 즉시 반영한다.</summary>
    private void OnValidate()
    {
        if (coneRect == null || darknessOverlay == null)
        {
            return; // 프리팹 조립 중 — 참조가 채워지기 전
        }

        coneImage = coneRect.GetComponent<Image>();
        ApplyTuning();
    }

    /// <summary>부채꼴 각도(fillAmount)·사거리(사각형 크기)와 어둠 오버레이 알파를 갱신한다.</summary>
    private void ApplyTuning()
    {
        if (coneImage != null && coneImage.type == Image.Type.Filled)
        {
            coneImage.fillAmount = Mathf.Clamp01(coneAngleDegrees / 360f);
        }

        if (coneRadiusPixels > 0f)
        {
            coneRect.sizeDelta = new Vector2(coneRadiusPixels * 2f, coneRadiusPixels * 2f); // 중심 피벗 기준 반지름
        }

        Color darkness = darknessOverlay.color;
        darkness.a = Mathf.Clamp01(1f - outsideVisibility);
        darknessOverlay.color = darkness;
    }

    /// <summary>부채꼴 피벗·회전을 갱신한다 — 매 프레임(패널 열림 중) UIMapOverview 가 호출한다.</summary>
    /// <param name="panelPosition">플레이어 마커의 패널 로컬 좌표.</param>
    /// <param name="cellAngleDegrees">맵 공간 시선 각도(도) — MapOverviewModel.WorldYawToCellAngle 출력.</param>
    public void SetPose(Vector2 panelPosition, float cellAngleDegrees)
    {
        // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
        coneRect.anchoredPosition = panelPosition;

        // Radial360(Origin Top)은 위에서 시계방향으로 채워진다 — 시선을 부채꼴 한가운데에 두려면 절반만큼 되돌린다
        float centerOffset = coneImage != null && coneImage.type == Image.Type.Filled ? coneAngleDegrees * 0.5f : 0f;
        coneRect.localRotation = Quaternion.Euler(0f, 0f, cellAngleDegrees + centerOffset);
    }
}
