using System;
using UnityEngine;

/// <summary>서버가 검증한 사체 상호작용의 위장 재료 충전 요청.</summary>
public readonly struct DisguiseRefillRequestedEvent
{
    public ulong PlayerNetworkObjectId { get; }

    public DisguiseRefillRequestedEvent(ulong playerNetworkObjectId)
    {
        PlayerNetworkObjectId = playerNetworkObjectId;
    }
}

/// <summary>좀비 사체와 플레이어 위장 컴포넌트를 직접 결합하지 않는 서버 로컬 SO 채널.</summary>
[CreateAssetMenu(fileName = "SO_Event_DisguiseRefillRequested", menuName = "Events/Player/Disguise Refill Requested")]
public sealed class DisguiseRefillRequestedEventChannelSO : ScriptableObject
{
    public event Action<DisguiseRefillRequestedEvent> OnEventRaised;

    public void RaiseEvent(DisguiseRefillRequestedEvent payload) => OnEventRaised?.Invoke(payload);
}
