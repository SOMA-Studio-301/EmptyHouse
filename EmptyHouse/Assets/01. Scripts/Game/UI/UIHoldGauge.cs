using Border.Core;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 입력키 프레임을 감싸는 사각 홀드 진행률 게이지 (조작상호작용UI.md 3-7 홀드 다이어그램).
/// 네 변(위·오른쪽·아래·왼쪽)을 각각 Filled Image 로 두고 시계방향으로 순서대로 채운다.
/// 홀드가 진행 중일 때만 나타나며, 취소·완료 시 사라진다.
/// PlayerInteractor 가 매 프레임 <see cref="Render"/> 로 진행률을 밀어넣는 단방향이며,
/// 이 클래스는 대상 타입을 알지 못한다 — 기름·사체 등 어떤 홀드든 같은 게이지를 쓴다.
/// </summary>
public class UIHoldGauge : MonoBehaviour
{
    // 게이지를 구성하는 변의 수. 진행률을 이 수만큼 등분해 한 변씩 채운다.
    private const int SegmentCount = 4;

    [Header("Widgets")]
    [SerializeField] private GameObject gaugeRoot; // 사각 테두리 전체(배경 + 채움). 진행 중이 아닐 때 통째로 끈다

    [Header("Segments (시계방향)")]
    [SerializeField] private Image topFill; // 0.00~0.25 구간. Filled / Horizontal / Origin Left
    [SerializeField] private Image rightFill; // 0.25~0.50 구간. Filled / Vertical / Origin Top
    [SerializeField] private Image bottomFill; // 0.50~0.75 구간. Filled / Horizontal / Origin Right
    [SerializeField] private Image leftFill; // 0.75~1.00 구간. Filled / Vertical / Origin Bottom

    // 직전에 그린 진행률. Render 는 매 프레임 호출되므로 실제로 바뀐 프레임에만 위젯을 건드린다.
    private float renderedProgress01;

    /// <summary>게이지를 숨긴 상태로 시작해 캐시 초기값(0)과 실제 위젯 상태를 일치시킨다.</summary>
    private void Awake()
    {
        gaugeRoot.SetActive(false);
        ApplyFill(0f);
    }

    /// <summary>
    /// 진행률을 화면에 반영한다. 0 이하면 게이지를 숨기고, 그 외에는 네 변을 시계방향으로 채운다.
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
            gaugeRoot.SetActive(true);
        }

        ApplyFill(progress01);
    }

    /// <summary>진행률을 네 변에 시계방향 순서로 나눠 넣는다. 앞 변이 다 차야 다음 변이 차기 시작한다.</summary>
    /// <param name="progress01">현재 홀드 진행률(0~1).</param>
    private void ApplyFill(float progress01)
    {
        topFill.fillAmount = SegmentFill(progress01, 0);
        rightFill.fillAmount = SegmentFill(progress01, 1);
        bottomFill.fillAmount = SegmentFill(progress01, 2);
        leftFill.fillAmount = SegmentFill(progress01, 3);
    }

    /// <summary>전체 진행률에서 해당 변이 차지할 몫을 잘라낸다. 이전 변 구간은 1, 이후 변 구간은 0 이 된다.</summary>
    /// <param name="progress01">현재 홀드 진행률(0~1).</param>
    /// <param name="segmentIndex">시계방향 변 순번(0=위, 1=오른쪽, 2=아래, 3=왼쪽).</param>
    /// <returns>해당 변의 fillAmount(0~1).</returns>
    private static float SegmentFill(float progress01, int segmentIndex)
    {
        return Mathf.Clamp01(progress01 * SegmentCount - segmentIndex);
    }
}
