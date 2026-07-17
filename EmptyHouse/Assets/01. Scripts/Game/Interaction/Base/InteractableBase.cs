using Border.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 상호작용 가능한 모든 오브젝트의 공통 부모 클래스
/// 이 클래스는 프롬프트 정보 제공과 실행 시점의 소음 발행 계약만 책임진다.
///
/// 상호작용 결과(픽업 소멸 등)는 전 클라에 복제돼야 하므로 NetworkBehaviour 를 상속한다 —
/// 따라서 모든 Interactable 프리팹에는 NetworkObject 가 필요하고, 런타임 스폰되는 픽업은
/// NetworkManager 의 NetworkPrefabs 에도 등록돼 있어야 한다.
/// </summary>
public abstract class InteractableBase : NetworkBehaviour
{
    [Header("Event Channels")]
    [SerializeField] private NoiseEventChannelSO noiseEmittedChannel;

    private const float MaxAllowedNoiseDb = 100f;

    /// <summary>입력 방식. Tap/Hold 여부에 따라 PlayerInteractor가 입력을 라우팅한다.</summary>
    public abstract InteractInputMethod InputMethod { get; }

    /// <summary>
    /// 다른 플레이어가 이미 점유 중인지 여부
    /// 기본값은 false — 점유 락이 필요한 대상만 override
    /// </summary>
    public virtual bool IsOccupied => false;

    /// <summary>
    /// 현재 프레임의 프롬프트 정보를 반환. UI는 이 값을 그대로 그림
    /// 프롬프트는 "조준 대상 × 손에 든 것"의 2차원 판정이므로 상호작용 주체를 받아
    /// 그의 인벤토리(<see cref="PlayerInteractor.Inventory"/>)를 조회
    /// </summary>
    /// <param name="interactor">이 대상을 조준 중인 상호작용 주체.</param>
    /// <returns>표시 상태·행위명·입력 방식·비활성 사유를 담은 정보.</returns>
    public abstract InteractPromptInfo GetPromptInfo(PlayerInteractor interactor);

    /// <summary>
    /// 이 Interactable의 실제 효과를 실행. Tap형은 입력 즉시, Hold형은 진행률 완료 시점에 호출된다.
    /// 호출 시점 판단은 하위 입력방식 클래스의 책임이며, 이 메서드는 결과 효과만 구현.
    /// </summary>
    /// <param name="interactor">행위를 실행한 상호작용 주체. 인벤 편입·소모는 이 주체의 인벤토리에 적용한다.</param>
    protected abstract void OnActivate(PlayerInteractor interactor);

    /// <summary>
    /// 실행 시점에 서버로 소음 발행을 1회 요청한다. (홀드는 완료 시점 1회).
    /// 호출부는 소음 크기만 전달하고, 위치와 발행 주체는 서버가 신뢰 가능한 값으로 결정한다.
    /// </summary>
    /// <param name="noiseDb">발행할 소음 강도(dB).</param>
    protected void RaiseNoise(float noiseDb)
    {
        if (!IsSpawned)
        {
            Debug.LogWarning($"[{nameof(InteractableBase)}] Cannot raise noise from an unspawned object: {name}.", this);
            return;
        }

        RequestNoiseServerRpc(noiseDb);
    }

    /// <summary>
    /// 소유권이 없는 상호작용 오브젝트에서도 호출할 수 있는 서버 소음 요청.
    /// 발행 위치는 서버 오브젝트의 위치를, SourceId 는 RPC 송신자의 PlayerObject 를 사용한다.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestNoiseServerRpc(float noiseDb, RpcParams rpcParams = default)
    {
        if (noiseEmittedChannel == null)
        {
            Debug.LogError($"[{nameof(InteractableBase)}] Noise channel is not assigned on {name}.", this);
            return;
        }

        if (!float.IsFinite(noiseDb) || noiseDb <= 0f)
        {
            Debug.LogWarning($"[{nameof(InteractableBase)}] Rejected invalid noise value {noiseDb} from {name}.", this);
            return;
        }

        float validatedDb = Mathf.Min(noiseDb, MaxAllowedNoiseDb);
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (NetworkManager == null
            || !NetworkManager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient client)
            || client.PlayerObject == null)
        {
            Debug.LogWarning($"[{nameof(InteractableBase)}] Rejected noise from client {senderClientId}: PlayerObject is unavailable.", this);
            return;
        }

        ulong sourceId = client.PlayerObject.NetworkObjectId;
        noiseEmittedChannel.RaiseEvent(new NoiseEvent(transform.position, validatedDb, gameObject, sourceId));
        Log.D($"[InteractableBase] RaiseNoise {validatedDb}dB from {name}, source={sourceId}");
    }

    /// <summary>
    /// 이 오브젝트를 전 클라에서 소멸시킨다. 회수된 픽업처럼 "사라짐"이 결과인 상호작용이 호출한다.
    /// 스폰된 NetworkObject 는 클라이언트가 Destroy 할 수 없으므로 서버에 요청만 보낸다.
    /// </summary>
    protected void DespawnSelf()
    {
        RequestDespawnServerRpc();
    }

    /// <summary>
    /// 소멸 요청을 서버에서 수신해 전 클라에서 despawn 한다.
    /// 픽업은 서버 소유라 상호작용 주체가 소유자가 아니므로 RequireOwnership 을 끈다.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestDespawnServerRpc()
    {
        Log.D($"[InteractableBase] RequestDespawnServerRpc {name}");

        // 두 클라가 같은 프레임에 회수하면 요청이 두 번 온다. 이미 despawn 됐으면 두 번째는 무시한다.
        if (!IsSpawned) return;

        NetworkObject.Despawn();
    }
}
