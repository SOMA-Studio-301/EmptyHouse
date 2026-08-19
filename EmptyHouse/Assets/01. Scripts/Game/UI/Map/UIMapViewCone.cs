using Border.Core;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 안내도 방향 밝힘(EH-62, 요구 5) — 전면 어둠 오버레이 + 플레이어 위치 중심 부채꼴이 카메라 요를 따라 회전한다.
/// **방향 기반, 벽 차폐 없음**(확정 해석) · 방문 기억 없음 — 상태 저장 0.
/// 마커 레이어(markersRoot)는 이 마스크 위에 있어 어두운 영역에서도 보인다.
/// 각도·어둠 농도는 UIMapOverview 가 단일 소유해 <see cref="Configure"/> 로 주입하고,
/// 반경은 좀비 표시 사거리와 같은 길이라 <see cref="SetRadius"/> 로 도식 축척에 맞춰 받는다.
/// </summary>
public sealed class UIMapViewCone : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private RectTransform coneRect; // 부채꼴 이미지(자식) — 피벗 = 플레이어 위치, 회전 = 시선 방향
    [SerializeField] private Image darknessOverlay; // 전면 어둠 오버레이(자식) — 부채꼴 밖 가림

    private float coneAngleDegrees; // 부채꼴 각도(도) — UIMapOverview 주입
    private float outsideVisibility; // 부채꼴 밖 잔여 가시성 — UIMapOverview 주입
    private Image coneImage; // 부채꼴 이미지 — Filled/Radial360(Origin Top)이면 각도를 fillAmount 로 구동. 통짜 스프라이트면 회전만(선택 의존)

    /// <summary>부채꼴 이미지 참조를 캐시한다.</summary>
    private void Awake()
    {
        Log.D("[UIMapViewCone] Awake");

        coneImage = coneRect.GetComponent<Image>();
    }

    /// <summary>각도·어둠 농도를 주입받아 즉시 반영한다 — 값의 단일 소유자는 UIMapOverview 다.</summary>
    /// <param name="angleDegrees">부채꼴 각도(도).</param>
    /// <param name="outsideAlpha">부채꼴 밖 잔여 가시성(0 = 완전 암전).</param>
    public void Configure(float angleDegrees, float outsideAlpha)
    {
        coneAngleDegrees = angleDegrees;
        outsideVisibility = outsideAlpha;
        ApplyTuning();
    }

    /// <summary>부채꼴 반경(패널 픽셀)을 설정한다 — 좀비 표시 사거리를 도식 축척으로 환산한 값.</summary>
    /// <param name="radiusPixels">반경(패널 픽셀).</param>
    public void SetRadius(float radiusPixels)
    {
        coneRect.sizeDelta = new Vector2(radiusPixels * 2f, radiusPixels * 2f); // 중심 피벗 기준 반지름
    }

    /// <summary>부채꼴 각도(fillAmount)와 어둠 오버레이 알파를 갱신한다.</summary>
    private void ApplyTuning()
    {
        if (coneRect == null || darknessOverlay == null)
        {
            return; // 프리팹 조립 중 — 참조가 채워지기 전(런타임 누락은 SetPose 에서 NRE 로 드러난다)
        }

        coneImage = coneImage != null ? coneImage : coneRect.GetComponent<Image>();
        if (coneImage != null && coneImage.type == Image.Type.Filled)
        {
            coneImage.fillAmount = Mathf.Clamp01(coneAngleDegrees / 360f);
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
