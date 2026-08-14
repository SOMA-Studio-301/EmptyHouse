using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 체력을 화면 붉어짐으로 표현하는 HUD 오버레이 (EH-97 — 전용 게이지 UI 없이 화면 전체 틴트).
/// 씬 레벨 Canvas-HUD 의 게임플레이 패널 밑에서 화면을 꽉 채우는 붉은 Image 하나의 알파만 조절한다.
/// 판단을 하지 않는 순수 표시자다 — 채널이 밀어넣는 체력 비율을 알파로 번역할 뿐, 네트워크도 피격 규칙도 알지 못한다(<see cref="UIDisguiseGauge"/> 와 같은 형태).
/// 피격 순간에는 펀치 알파로 확 붉어졌다가 체력 비율에 대응하는 유지 알파로 가라앉는다 — 맞았다는 사실을 놓치지 않게 하기 위해서다.
/// Image 는 Raycast Target 을 꺼 둬야 한다 — 화면 전체를 덮으므로 켜면 모든 UI 클릭을 삼킨다.
/// </summary>
public class UIDamageOverlay : MonoBehaviour
{
    [Header("Channels")]
    [SerializeField] private PlayerHealthChangedEventChannelSO healthChanged; // 구독: 로컬 플레이어 체력 비율 방송. 늦게 켜졌을 때의 초기값은 CurrentHealth01 로 받는다

    [Header("Widgets")]
    [SerializeField] private Image overlayImage; // 화면 전체를 덮는 붉은 이미지. 알파만 이 스크립트가 조절한다

    [Header("Alpha")]
    [Range(0f, 1f)] [SerializeField] private float maxSustainAlpha = 0.45f; // 체력 최저(마지막 1대 남음)일 때의 유지 알파. 1 에 가까우면 화면이 안 보인다
    [Range(0f, 1f)] [SerializeField] private float punchAlphaBoost = 0.3f; // 피격 순간 유지 알파 위에 얹는 순간 알파
    [SerializeField, Min(0.01f)] private float punchDecaySeconds = 0.8f; // 펀치 알파가 유지 알파로 가라앉는 데 걸리는 시간

    private float sustainAlpha; // 현재 체력 비율이 요구하는 유지 알파(수렴 목표)
    private float currentAlpha; // 이번 프레임에 실제로 칠한 알파(펀치에서 유지로 감쇠 중일 수 있다)
    private float lastHealth01 = 1f; // 직전에 받은 체력 비율. 감소 판정(펀치 발동)에 쓴다

    /// <summary>채널을 구독하고, 이미 발행된 마지막 체력으로 초기 표시를 맞춘다(늦은 구독자 동기화). 재활성화 시 펀치는 재생하지 않는다.</summary>
    private void OnEnable()
    {
        lastHealth01 = healthChanged.CurrentHealth01;
        sustainAlpha = ResolveSustainAlpha(lastHealth01);
        currentAlpha = sustainAlpha;
        ApplyAlpha(currentAlpha);

        healthChanged.OnEventRaised += Render;
    }

    /// <summary>구독을 해제한다. 채널은 SO 라 씬 밖에서 살아남으므로 죽은 델리게이트를 남기지 않는다.</summary>
    private void OnDisable()
    {
        healthChanged.OnEventRaised -= Render;
    }

    /// <summary>펀치 알파를 유지 알파로 감쇠시킨다. 평상시(수렴 완료)에는 위젯을 건드리지 않는다.</summary>
    private void Update()
    {
        if (Mathf.Approximately(currentAlpha, sustainAlpha)) return;

        currentAlpha = Mathf.MoveTowards(currentAlpha, sustainAlpha, punchAlphaBoost / punchDecaySeconds * Time.deltaTime);
        ApplyAlpha(currentAlpha);
    }

    /// <summary>체력 비율을 오버레이 알파에 반영한다. 감소면 펀치 알파부터 시작해 유지 알파로 가라앉는다.</summary>
    /// <param name="health01">표시할 체력 비율(0~1).</param>
    private void Render(float health01)
    {
        bool damaged = health01 < lastHealth01;
        lastHealth01 = health01;

        sustainAlpha = ResolveSustainAlpha(health01);
        currentAlpha = damaged ? Mathf.Min(1f, sustainAlpha + punchAlphaBoost) : sustainAlpha;
        ApplyAlpha(currentAlpha);
    }

    /// <summary>체력 비율을 유지 알파로 번역한다. 만땅이면 0(완전 투명), 잃을수록 maxSustainAlpha 까지 선형으로 붉어진다.</summary>
    /// <param name="health01">현재 체력 비율(0~1).</param>
    /// <returns>수렴 목표 알파.</returns>
    private float ResolveSustainAlpha(float health01)
    {
        return Mathf.Lerp(maxSustainAlpha, 0f, health01);
    }

    /// <summary>알파를 이미지 색에 칠한다. 색상(붉은 톤)은 인스펙터의 Image 색을 그대로 쓴다.</summary>
    /// <param name="alpha">칠할 알파(0~1).</param>
    private void ApplyAlpha(float alpha)
    {
        Color color = overlayImage.color;
        color.a = alpha;
        overlayImage.color = color;
    }
}
