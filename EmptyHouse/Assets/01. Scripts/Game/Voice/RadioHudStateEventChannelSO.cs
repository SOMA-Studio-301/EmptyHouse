using System;
using UnityEngine;

/// <summary>무전기 HUD 아이콘이 그려야 할 상태. 보유·손에 듦·송신을 한 값으로 합친 것이다.</summary>
public enum RadioHudState
{
    None = 0,         // 미보유 또는 관전 — 아이콘을 숨긴다
    Owned = 1,        // 보유 + 손에 안 듦 — 흐린 아이콘. Space 를 눌러도 송신되지 않는 상태라 밝게 그리면 거짓 어포던스다
    HeldIdle = 2,     // 손에 듦 + 대기 — 송신 가능. Space 홀드를 기다린다
    Transmitting = 3, // 송신 중(손에 들고 Space 홀드)
}

/// <summary>
/// 로컬 플레이어의 무전기 표시 상태를 HUD 로 전달하는 SO 채널.
/// 보유 상태·PTT 입력을 모두 아는 소유 클라이언트의 PlayerRadioVoiceController 하나만 발행한다 — 프로세스당 발행자는 항상 1개다.
/// 씬 레벨 Canvas-HUD 와 플레이어 프리팹이 서로를 참조하지 않고 만나는 지점이며, 네트워크 전송은 하지 않는다
/// (<see cref="DisguiseGaugeChangedEventChannelSO"/> 와 같은 형태).
/// </summary>
[CreateAssetMenu(fileName = "SO_Event_RadioHudState", menuName = "Events/Player/Radio Hud State")]
public sealed class RadioHudStateEventChannelSO : ScriptableObject
{
    public event Action<RadioHudState> OnEventRaised;

    public RadioHudState CurrentState { get; private set; } // 마지막으로 발행된 상태. 늦게 켜진 HUD 가 초기 표시에 읽는다

    /// <summary>SO 활성화 시 캐시를 비운다. 에디터에서 SO 는 플레이 세션 사이에 살아남으므로 이전 세션 상태가 남지 않게 한다.</summary>
    private void OnEnable()
    {
        CurrentState = RadioHudState.None;
    }

    /// <summary>표시 상태를 캐시에 기록하고 구독자에게 방송한다.</summary>
    /// <param name="state">현재 무전기 표시 상태.</param>
    public void RaiseEvent(RadioHudState state)
    {
        CurrentState = state;
        OnEventRaised?.Invoke(state);
    }
}
