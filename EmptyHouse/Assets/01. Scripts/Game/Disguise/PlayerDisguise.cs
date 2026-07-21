using Border.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 플레이어 위장 상태의 네트워크 권위자.
/// 소유 클라이언트는 V 입력을 서버에 요청하고, 서버만 상태를 변경한다.
/// 게이지·사체 충전·미니게임·표현은 후속 기능이며 이 컴포넌트에는 포함하지 않는다.
/// </summary>
public sealed class PlayerDisguise : NetworkBehaviour
{
    [Header("Input")]
    [SerializeField] private InputReader inputReader;

    [Header("Event Channels")]
    [SerializeField] private DisguiseStateChangedEventChannelSO disguiseStateChanged;

    private readonly NetworkVariable<bool> isDisguised = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public bool IsDisguised => isDisguised.Value;

    public override void OnNetworkSpawn()
    {
        isDisguised.OnValueChanged += HandleDisguiseChanged;

        // 늦게 스폰된 구독자도 현재 복제값으로 초기화할 수 있도록 한 번 브릿지한다.
        PublishState(isDisguised.Value);

        if (IsOwner)
            inputReader.DisguiseToggleEvent += RequestToggle;
    }

    public override void OnNetworkDespawn()
    {
        isDisguised.OnValueChanged -= HandleDisguiseChanged;

        if (IsOwner)
            inputReader.DisguiseToggleEvent -= RequestToggle;
    }

    private void RequestToggle()
    {
        if (!IsOwner || !IsSpawned) return;
        RequestToggleRpc();
    }

    /// <summary>소유자의 요청을 받아 서버에서만 위장 상태를 토글한다.</summary>
    [Rpc(SendTo.Server)]
    private void RequestToggleRpc()
    {
        if (!IsServer || !IsSpawned) return;

        isDisguised.Value = !isDisguised.Value;
        Log.D($"[PlayerDisguise] Player {NetworkObjectId} disguise={isDisguised.Value}");
    }

    private void HandleDisguiseChanged(bool previous, bool current)
    {
        PublishState(current);
    }

    private void PublishState(bool value)
    {
        if (disguiseStateChanged == null)
        {
            Debug.LogError($"[{nameof(PlayerDisguise)}] Disguise state channel is not assigned on {name}.", this);
            return;
        }

        disguiseStateChanged.RaiseEvent(new DisguiseStateChangedEvent(NetworkObjectId, value));
    }
}
