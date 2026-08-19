using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 무전기 보유·송신 표시 아이콘. 씬 레벨 Canvas-HUD 의 소음 미터 위에 붙는다.
/// 판단을 하지 않는 순수 표시자다 — 채널이 밀어넣는 상태를 스프라이트로 바꿔 그릴 뿐,
/// 네트워크도 PTT 규칙도 알지 못한다(<see cref="UIDisguiseGauge"/> 와 같은 형태).
/// </summary>
public class UIRadioIndicator : MonoBehaviour
{
    [Header("Channels")]
    [SerializeField] private RadioHudStateEventChannelSO radioHudStateChanged; // 구독: 로컬 플레이어 무전기 표시 상태. 늦게 켜졌을 때의 초기값은 CurrentState 로 받는다

    [Header("Widgets")]
    [SerializeField] private Image iconImage; // 아이콘 이미지. 미보유 구간에는 이 컴포넌트를 꺼서 숨긴다

    [Header("Sprites")]
    [SerializeField] private Sprite idleSprite; // 대기 스프라이트(무전기 OFF) — Owned/HeldIdle 이 공유하고 톤으로 구분한다
    [SerializeField] private Sprite transmittingSprite; // 송신 중 스프라이트(무전기 ON)

    [Header("Colors")]
    [SerializeField] private Color ownedColor = new(1f, 1f, 1f, 0.4f); // 보유했지만 안 든 상태 — 흐리게. 별도 스프라이트 없이 송신 불가를 톤으로 알린다
    [SerializeField] private Color activeColor = Color.white; // 손에 든 상태(대기·송신)의 기본 톤

    // 직전에 그린 상태. 값이 실제로 바뀐 호출에만 위젯을 건드린다. null 은 "아직 한 번도 안 그림"
    private RadioHudState? renderedState;

    /// <summary>채널을 구독하고, 이미 발행된 마지막 상태로 초기 표시를 맞춘다(늦은 구독자 동기화).</summary>
    private void OnEnable()
    {
        radioHudStateChanged.OnEventRaised += Render;
        Render(radioHudStateChanged.CurrentState);
    }

    /// <summary>구독을 해제한다. 채널은 SO 라 씬 밖에서 살아남으므로 죽은 델리게이트를 남기지 않는다.</summary>
    private void OnDisable()
    {
        radioHudStateChanged.OnEventRaised -= Render;
    }

    /// <summary>
    /// 표시 상태를 아이콘에 반영한다. 미보유면 숨기고, 보유/듦/송신을 스프라이트·톤으로 구분한다.
    /// switch 명시 분기다 — 이항 삼항으로 접으면 새 상태가 조용히 기존 스프라이트로 그려진다.
    /// </summary>
    /// <param name="state">그릴 무전기 표시 상태.</param>
    private void Render(RadioHudState state)
    {
        if (renderedState == state) return;

        renderedState = state;

        iconImage.enabled = state != RadioHudState.None;

        switch (state)
        {
            case RadioHudState.Owned:
                iconImage.sprite = idleSprite;
                iconImage.color = ownedColor;
                break;
            case RadioHudState.HeldIdle:
                iconImage.sprite = idleSprite;
                iconImage.color = activeColor;
                break;
            case RadioHudState.Transmitting:
                iconImage.sprite = transmittingSprite;
                iconImage.color = activeColor;
                break;
        }
    }
}
