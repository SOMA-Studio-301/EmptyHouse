using System;
using UnityEngine;

/// <summary>서버가 확정해 복제한 플레이어 은신(벽장) 상태의 로컬 프로세스 알림.</summary>
public readonly struct HidingStateChangedEvent
{
    public ulong PlayerNetworkObjectId { get; }
    public bool IsHidden { get; }

    public HidingStateChangedEvent(ulong playerNetworkObjectId, bool isHidden)
    {
        PlayerNetworkObjectId = playerNetworkObjectId;
        IsHidden = isHidden;
    }
}

/// <summary>
/// NetworkVariable의 변경을 플레이어 로컬 컴포넌트·좀비 AI에 전달하는 은신 전용 SO 채널.
/// 네트워크 전송은 하지 않으며, 같은 프로세스 안에서만 발행·구독한다.
/// 위장(<see cref="DisguiseStateChangedEventChannelSO"/>)과 의미가 다르므로 별도 채널로 둔다(조작상호작용UI.md 3-5-1).
/// </summary>
[CreateAssetMenu(fileName = "SO_Event_HidingStateChanged", menuName = "Events/Player/Hiding State Changed")]
public sealed class HidingStateChangedEventChannelSO : ScriptableObject
{
    public event Action<HidingStateChangedEvent> OnEventRaised;

    public void RaiseEvent(HidingStateChangedEvent payload) => OnEventRaised?.Invoke(payload);
}
