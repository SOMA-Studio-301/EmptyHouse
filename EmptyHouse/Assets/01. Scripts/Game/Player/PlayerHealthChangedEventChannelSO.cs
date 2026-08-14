using System;
using UnityEngine;

/// <summary>
/// 로컬 플레이어의 체력 비율을 HUD 로 전달하는 SO 채널 (EH-97 — 화면 붉어짐 표현).
/// 체력 NetworkVariable 변경을 소유 클라이언트의 PlayerHealth 하나만 발행한다 — 프로세스당 발행자는 항상 1개다.
/// 씬 레벨 Canvas-HUD 와 플레이어 프리팹이 서로를 참조하지 않고 만나는 지점이며, 네트워크 전송은 하지 않는다
/// (<see cref="DisguiseGaugeChangedEventChannelSO"/> 와 같은 형태).
/// </summary>
[CreateAssetMenu(fileName = "SO_Event_PlayerHealthChanged", menuName = "Events/Player/Health Changed")]
public sealed class PlayerHealthChangedEventChannelSO : ScriptableObject
{
    public event Action<float> OnEventRaised;

    public float CurrentHealth01 { get; private set; } // 마지막으로 발행된 체력 비율(0~1). 늦게 켜진 HUD 가 초기 표시에 읽는다

    /// <summary>SO 활성화 시 캐시를 만땅으로 되돌린다. 에디터에서 SO 는 플레이 세션 사이에 살아남으므로 이전 세션의 빈사 상태가 남지 않게 한다.</summary>
    private void OnEnable()
    {
        CurrentHealth01 = 1f;
    }

    /// <summary>체력 비율을 캐시에 기록하고 구독자에게 방송한다.</summary>
    /// <param name="health01">현재 체력 비율(0~1).</param>
    public void RaiseEvent(float health01)
    {
        CurrentHealth01 = health01;
        OnEventRaised?.Invoke(health01);
    }
}
