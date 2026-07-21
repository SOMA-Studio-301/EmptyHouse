using System.Collections.Generic;
using Border.Core;
using Border.Localization;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 맵에 배치하는 재사용 가능한 좀비 사체. E를 설정 시간 동안 홀드하면
/// 서버 검증 후 상호작용한 플레이어의 위장 게이지를 최대치로 회복한다.
/// </summary>
public sealed class ZombieCorpseInteractable : HoldInteractableBase
{
    [Header("Hold")]
    [SerializeField, Min(0.1f)] private float holdSeconds = 3f;

    [Header("Result")]
    [SerializeField, Range(0f, 100f)] private float completionNoiseDb = 10f;
    [SerializeField, Min(0f)] private float serverMaxInteractDistance = 3.25f;

    [Header("Event Channels")]
    [SerializeField] private DisguiseRefillRequestedEventChannelSO disguiseRefillRequested;

    [Header("Localization")]
    [LocalizeKey][SerializeField] private string applyBloodActionKey = "INT_PROMPT_APPLY_BLOOD";
    [LocalizeKey][SerializeField] private string disguiseFullReasonKey = "INT_MSG_DISGUISE_FULL";

    private readonly Dictionary<ulong, double> serverHoldStartedAt = new Dictionary<ulong, double>();
    private const double HoldValidationToleranceSeconds = 0.15d;

    public override float HoldDurationSeconds => holdSeconds;

    public override InteractPromptInfo GetPromptInfo(PlayerInteractor interactor)
    {
        if (interactor.Disguise != null && interactor.Disguise.IsGaugeFull)
        {
            return new InteractPromptInfo(
                InteractPromptState.Inactive,
                null,
                InteractInputMethod.Hold,
                holdSeconds,
                disguiseFullReasonKey);
        }

        return new InteractPromptInfo(
            InteractPromptState.Active,
            applyBloodActionKey,
            InteractInputMethod.Hold,
            holdSeconds,
            null);
    }

    protected override void OnActivate(PlayerInteractor interactor)
    {
        Log.D($"[ZombieCorpseInteractable] Hold completed on {name}");
        RequestRefillRpc();
        RaiseNoise(completionNoiseDb);
    }

    protected override void OnHoldStarted(PlayerInteractor interactor)
    {
        BeginRefillHoldRpc();
    }

    protected override void OnHoldCanceled(PlayerInteractor interactor)
    {
        CancelRefillHoldRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void BeginRefillHoldRpc(RpcParams rpcParams = default)
    {
        if (!IsServer || !TryGetRequestingPlayer(rpcParams, out NetworkObject player)) return;
        if (!IsPlayerInRange(player)) return;

        serverHoldStartedAt[rpcParams.Receive.SenderClientId] = Time.timeAsDouble;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void CancelRefillHoldRpc(RpcParams rpcParams = default)
    {
        if (!IsServer) return;
        serverHoldStartedAt.Remove(rpcParams.Receive.SenderClientId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestRefillRpc(RpcParams rpcParams = default)
    {
        if (!IsServer || disguiseRefillRequested == null) return;

        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (!serverHoldStartedAt.TryGetValue(senderClientId, out double startedAt))
            return;

        serverHoldStartedAt.Remove(senderClientId);

        double heldSeconds = Time.timeAsDouble - startedAt;
        if (heldSeconds + HoldValidationToleranceSeconds < holdSeconds)
        {
            Debug.LogWarning($"[{nameof(ZombieCorpseInteractable)}] Rejected refill from client {senderClientId}: hold was too short ({heldSeconds:F2}s).", this);
            return;
        }

        if (!TryGetRequestingPlayer(rpcParams, out NetworkObject player) || !IsPlayerInRange(player)) return;

        disguiseRefillRequested.RaiseEvent(
            new DisguiseRefillRequestedEvent(player.NetworkObjectId));
    }

    private bool TryGetRequestingPlayer(RpcParams rpcParams, out NetworkObject player)
    {
        player = null;
        if (NetworkManager == null) return false;

        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (!NetworkManager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient client)
            || client.PlayerObject == null)
            return false;

        player = client.PlayerObject;
        return true;
    }

    private bool IsPlayerInRange(NetworkObject player)
    {
        float maxDistanceSqr = serverMaxInteractDistance * serverMaxInteractDistance;
        bool isInRange = (player.transform.position - transform.position).sqrMagnitude <= maxDistanceSqr;
        if (!isInRange)
        {
            Debug.LogWarning($"[{nameof(ZombieCorpseInteractable)}] Rejected refill from player {player.NetworkObjectId}: out of range.", this);
        }

        return isInRange;
    }
}
