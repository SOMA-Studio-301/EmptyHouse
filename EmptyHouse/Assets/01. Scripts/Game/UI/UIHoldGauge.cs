using Border.Core;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 조준선을 감싸는 도넛형 홀드 진행률 게이지 (조작상호작용UI.md 3-7 홀드 다이어그램).
/// 홀드가 진행 중일 때만 나타나 링을 시계방향으로 채우고, 취소·완료 시 사라진다.
/// PlayerInteractor 가 매 프레임 <see cref="Render"/> 로 진행률을 밀어넣는 단방향이며,
/// 이 클래스는 대상 타입을 알지 못한다 — 기름·사체 등 어떤 홀드든 같은 게이지를 쓴다.
/// </summary>
public class UIHoldGauge : MonoBehaviour
{
    [Header("Widgets")]
    [SerializeField] private GameObject gaugeRoot; // 링 전체(배경 + 채움). 진행 중이 아닐 때 통째로 끈다
    [SerializeField] private Image fillImage; // Filled / Radial360 로 설정된 채움 링

    // 직전에 그린 진행률. Render 는 매 프레임 호출되므로 실제로 바뀐 프레임에만 위젯을 건드린다.
    private float renderedProgress01;

    /// <summary>게이지를 숨긴 상태로 시작해 캐시 초기값(0)과 실제 위젯 상태를 일치시킨다.</summary>
    private void Awake()
    {
        gaugeRoot.SetActive(false);
        fillImage.fillAmount = 0f;
    }

    /// <summary>
    /// 진행률을 화면에 반영한다. 0 이하면 게이지를 숨기고, 그 외에는 링을 그만큼 채운다.
    /// </summary>
    /// <param name="progress01">현재 홀드 진행률(0~1). 진행 중이 아니면 0.</param>
    public void Render(float progress01)
    {
        // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
        if (Mathf.Approximately(progress01, renderedProgress01)) return;

        bool wasHidden = renderedProgress01 <= 0f;
        renderedProgress01 = progress01;

        if (progress01 <= 0f)
        {
            gaugeRoot.SetActive(false);
            return;
        }

        if (wasHidden)
        {
            Log.D("[UIHoldGauge] Show");
            gaugeRoot.SetActive(true);
        }

        fillImage.fillAmount = progress01;
    }
}
