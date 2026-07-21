using System;
using UnityEngine;

/// <summary>서버가 확정해 복제한 플레이어 위장 상태의 로컬 프로세스 알림.</summary>
public readonly struct DisguiseStateChangedEvent
{
    public ulong PlayerNetworkObjectId { get; }
    public bool IsDisguised { get; }

    public DisguiseStateChangedEvent(ulong playerNetworkObjectId, bool isDisguised)
    {
        PlayerNetworkObjectId = playerNetworkObjectId;
        IsDisguised = isDisguised;
    }
}

/// <summary>
/// NetworkVariable의 변경을 플레이어 로컬 컴포넌트에 전달하는 위장 전용 SO 채널.
/// 네트워크 전송은 하지 않으며, 같은 프로세스 안에서만 발행·구독한다.
/// </summary>
[CreateAssetMenu(fileName = "SO_Event_DisguiseStateChanged", menuName = "Events/Player/Disguise State Changed")]
public sealed class DisguiseStateChangedEventChannelSO : ScriptableObject
{
    public event Action<DisguiseStateChangedEvent> OnEventRaised;

    public void RaiseEvent(DisguiseStateChangedEvent payload) => OnEventRaised?.Invoke(payload);
}
