using UnityEngine;
using UnityEngine.Events;

public readonly struct ZombiePlayerCaughtEvent
{
    public readonly ulong ZombieNetworkObjectId;
    public readonly ulong PlayerNetworkObjectId;

    public ZombiePlayerCaughtEvent(ulong zombieNetworkObjectId, ulong playerNetworkObjectId)
    {
        ZombieNetworkObjectId = zombieNetworkObjectId;
        PlayerNetworkObjectId = playerNetworkObjectId;
    }
}

[CreateAssetMenu(fileName = "SO_Event_PlayerCaught", menuName = "Events/Game/Player Caught")]
public class ZombiePlayerCaughtEventChannelSO : ScriptableObject
{
    public event UnityAction<ZombiePlayerCaughtEvent> OnEventRaised;
    public void RaiseEvent(ZombiePlayerCaughtEvent payload) => OnEventRaised?.Invoke(payload);
}
