using Border.Core;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 위장 재료 게이지 HUD (조작상호작용UI.md 5장 — 상시 표시). 씬 레벨 Canvas-HUD 의 게임플레이 패널 밑에 붙는다.
/// 판단을 하지 않는 순수 표시자다 — 채널이 밀어넣는 잔량을 호 각도에 반영할 뿐, 네트워크도 위장 규칙도 알지 못한다(<see cref="UIHoldGauge"/> 와 같은 형태).
/// 배경·잔량 모두 링 스프라이트를 Filled / Radial 360 으로 깐 Image 이며, fillAmount 가 곧 호 각도다.
/// 호가 놓이는 방향은 이 스크립트가 아니라 Image 의 Fill Origin 과 RectTransform 의 Rotation Z 가 정한다 — 두 Image 의 Fill Origin·Clockwise 가 같아야 같은 끝에서 자란다.
/// 배경 호까지 여기서 채우는 이유는 각도가 인스펙터에 두 벌 존재해 어긋나는 상황을 막기 위해서다.
/// ⚠ 화면 중앙은 위장 균형 바(2-2, 미구현)의 자리이기도 하다 — 이 게이지를 크로스헤어 정중앙에 겹치지 말고 Y 오프셋으로 레인을 갈라둘 것.
/// </summary>
public class UIDisguiseGauge : MonoBehaviour
{
    [Header("Channels")]
    [SerializeField] private DisguiseGaugeChangedEventChannelSO disguiseGaugeChanged; // 구독: 로컬 플레이어 잔량 방송. 늦게 켜졌을 때의 초기값은 CurrentGauge01 로 받는다

    [Header("Widgets")]
    [SerializeField] private Image backgroundImage; // 호 배경(= 잔량 만땅 자리). fullAngle 로 이 스크립트가 채운다
    [SerializeField] private Image fillImage; // 잔량 호. 각도를 입히는 대상이다(경고색은 아래 주석 참고)

    [Header("Arc")]
    [Range(0f, 360f)] [SerializeField] private float emptyAngle = 0f; // 잔량 0 일 때 호 각도(도). 0 보다 크면 바닥나도 얇게 남는다
    [Range(0f, 360f)] [SerializeField] private float fullAngle = 120f; // 잔량 1 일 때 호 각도(도). 배경 호도 이 값으로 고정된다

    [Header("Editor Preview")]
    [Range(0f, 1f)] [SerializeField] private float previewGauge01 = 1f; // 에디터 전용 미리보기 잔량. 슬라이더를 굴리면 OnValidate 가 호를 다시 그린다. 플레이 중에는 채널 값이 우선한다

    // 잔량 경고색은 보류 — 색 규칙 확정 시 아래 필드와 ResolveFillColor 를 함께 되살릴 것
    // [Header("Colors")]
    // [SerializeField] private Color fullColor = new Color(0.35f, 0.82f, 0.36f, 1f); // 잔량 충분 구간 색
    // [SerializeField] private Color lowColor = new Color(0.90f, 0.25f, 0.22f, 1f); // 잔량 고갈 시점 색
    // [Range(0f, 1f)] [SerializeField] private float lowThreshold01 = 0.3f; // 이 잔량 아래부터 lowColor 로 물들기 시작한다. 그 위는 fullColor 고정

    // 직전에 그린 잔량. 값이 실제로 바뀐 호출에만 위젯을 건드린다. -1 은 "아직 한 번도 안 그림"
    private float renderedGauge01 = -1f;

    /// <summary>채널을 구독하고, 이미 발행된 마지막 잔량으로 초기 표시를 맞춘다(늦은 구독자 동기화).</summary>
    private void OnEnable()
    {
        ApplyBackgroundArc();

        disguiseGaugeChanged.OnEventRaised += Render;
        Render(disguiseGaugeChanged.CurrentGauge01);
    }

    /// <summary>구독을 해제한다. 채널은 SO 라 씬 밖에서 살아남으므로 죽은 델리게이트를 남기지 않는다.</summary>
    private void OnDisable()
    {
        disguiseGaugeChanged.OnEventRaised -= Render;
    }

    /// <summary>인스펙터에서 각도를 굴리는 동안 배경·잔량 호를 즉시 다시 그린다(에디터 튜닝용).</summary>
    private void OnValidate()
    {
        // Add Component 직후에는 위젯 참조가 아직 비어 있다. 편집 중에만 도는 경로라 여기서만 가드한다.
        if (backgroundImage == null || fillImage == null) return;

        ApplyBackgroundArc();

        // 편집 중이면 미리보기 슬라이더 값을 보여주고, 플레이 중이면 마지막으로 그린 잔량을 그대로 다시 그린다.
        float replay = Application.isPlaying && renderedGauge01 >= 0f ? renderedGauge01 : previewGauge01;

        renderedGauge01 = -1f; // 각도만 바뀐 호출에서 Render 가 조기 반환하지 않도록 캐시를 무효화한다
        Render(replay);
    }

    /// <summary>잔량을 호 각도와 색에 반영한다.</summary>
    /// <param name="gauge01">표시할 잔량(0~1).</param>
    private void Render(float gauge01)
    {
        // 잔량이 바뀔 때마다 호출되므로 진입 트레이스를 두지 않는다.
        if (Mathf.Approximately(gauge01, renderedGauge01)) return;

        renderedGauge01 = gauge01;

        fillImage.fillAmount = Mathf.Lerp(emptyAngle, fullAngle, gauge01) / 360f;
        // fillImage.color = ResolveFillColor(gauge01); // 경고색 보류 — 색은 Inspector 에 설정된 값을 그대로 둔다
    }

    // /// <summary>잔량을 경고색으로 번역한다. 임계 위는 fullColor 고정이고, 그 아래에서 lowColor 로 보간된다.</summary>
    // /// <param name="gauge01">현재 잔량(0~1).</param>
    // /// <returns>Fill 에 칠할 색.</returns>
    // private Color ResolveFillColor(float gauge01)
    // {
    //     // Render 와 같은 빈도로 호출되므로 진입 트레이스를 두지 않는다.
    //     // 임계가 0 이면 경고 구간 자체가 없다 — 나눗셈에 들어가기 전에 걸러낸다.
    //     if (lowThreshold01 <= 0f || gauge01 >= lowThreshold01) return fullColor;
    //
    //     return Color.Lerp(lowColor, fullColor, gauge01 / lowThreshold01);
    // }

    /// <summary>배경 호를 잔량 만땅 각도(fullAngle)에 맞춘다.</summary>
    private void ApplyBackgroundArc()
    {
        backgroundImage.fillAmount = fullAngle / 360f;
    }
}
