using System;
using UnityEngine;

/// <summary>좀비 상태 전이 알림 페이로드. 서버의 ZombieStateMachine.SwitchState 가 전이 확정 시점에 발행한다.</summary>
public readonly struct ZombieStateChangedEvent
{
    public ulong ZombieNetworkObjectId { get; } // 전이한 좀비의 NetworkObjectId
    public ZombieStateKind Previous { get; }    // 직전 상태
    public ZombieStateKind Current { get; }     // 전이된 현재 상태

    /// <summary>상태 전이 페이로드를 만든다.</summary>
    /// <param name="zombieNetworkObjectId">전이한 좀비의 NetworkObjectId.</param>
    /// <param name="previous">직전 상태.</param>
    /// <param name="current">전이된 현재 상태.</param>
    public ZombieStateChangedEvent(ulong zombieNetworkObjectId, ZombieStateKind previous, ZombieStateKind current)
    {
        ZombieNetworkObjectId = zombieNetworkObjectId;
        Previous = previous;
        Current = current;
    }
}

/// <summary>
/// 좀비 상태 전이를 상태머신 밖(튜토리얼 판정 등)으로 중계하는 서버 로컬 SO 채널.
/// 네트워크 전송은 하지 않는다 — 상태 전이는 서버에서만 확정되므로 발행도 서버에서만 일어난다.
/// 튜토리얼.md 6장 "단계 판정 = 기존 시스템 이벤트 구독"의 좀비 측 노출 지점이다 (폴링 금지 — 9장).
/// </summary>
[CreateAssetMenu(fileName = "SO_Event_ZombieStateChanged", menuName = "Events/Zombie/State Changed")]
public sealed class ZombieStateChangedEventChannelSO : ScriptableObject
{
    public event Action<ZombieStateChangedEvent> OnEventRaised;

    /// <summary>상태 전이를 구독자에게 방송한다.</summary>
    /// <param name="payload">전이 페이로드.</param>
    public void RaiseEvent(ZombieStateChangedEvent payload) => OnEventRaised?.Invoke(payload);
}
