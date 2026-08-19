using Border.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 문 루트 — 잠금·개방 상태의 단일 소스이자 서버 권위자(WardrobeInteractable 동형, mapgen_roadmap M7).
/// 상호작용부(DoorLockFace·DoorHandleInteractable)는 판정·프롬프트만 갖고, 확정은 이 루트의 서버 RPC 로만 이뤄진다.
/// 개방은 영구(재잠금 없음 — 절차적맵생성 2절). 개방 변화에 애니메이터·SFX·NavMeshObstacle 해제가 편승한다(§4.5)
/// — 문이 열리면 좀비 길(Carve)도 같은 상태 변화 하나로 열린다.
/// </summary>
public sealed class DoorInteractable : NetworkBehaviour
{
    [Header("Visual / Nav")]
    [SerializeField] private Animator doorAnimator; // IsOpen 토글 표현 전용 — 권위는 NetworkVariable(스펙 2절)
    [SerializeField] private NavMeshObstacle navObstacle; // 닫힘 동안 길 차단(Carve) — 개방 시 비활성화로 길 개통

    [Header("Audio")]
    [SerializeField] private SFXEventChannelSO sfxEventChannel; // §4.5 오브젝트 사운드 채널
    [SerializeField] private AudioId openAudioId = AudioId.Sfx_Door_Open; // 개방 시 재생
    [SerializeField] private AudioId unlockAudioId = AudioId.None; // 해정 시 재생(전용 음원 등재 전까지 None — 채널이 무시)

    [Header("Lock")]
    [SerializeField] private Transform lockPosFront; // 자물쇠 스폰 앵커 앞면(로컬 −Z — 문이 닫히는 쪽) — 스포너가 접근측 면을 골라 스폰
    [SerializeField] private Transform lockPosBack; // 자물쇠 스폰 앵커 뒷면(로컬 +Z — 문이 열리는 쪽)

    private NetworkObject attachedLock; // 서버 전용 — 이 문에 걸린 자물쇠 오브젝트(해정 승인 시 소멸)

    /// <summary>
    /// 접근 지점을 향한 면의 자물쇠 앵커를 고른다 — 자물쇠는 입구에서 걸어오는 쪽(접근측)에 보여야 한다.
    /// 문짝은 로컬 +Z 로 열리는 규약이라, 접근점이 +Z 반공간이면 뒷면 앵커·아니면 앞면 앵커.
    /// </summary>
    /// <param name="approachWorldPos">접근측 기준 월드 좌표(접근 방 중심).</param>
    /// <returns>접근측 면의 자물쇠 앵커.</returns>
    public Transform LockPosToward(Vector3 approachWorldPos)
    {
        return Vector3.Dot(approachWorldPos - transform.position, transform.forward) > 0f ? lockPosBack : lockPosFront;
    }

    private static readonly int isOpenAnimHash = Animator.StringToHash("IsOpen"); // 애니메이터 개방 토글 파라미터(스펙 2절)

    private readonly NetworkVariable<bool> isLocked = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server); // 잠금 상태 단일 소스(서버 쓰기)

    private readonly NetworkVariable<bool> isOpen = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server); // 개방 상태 단일 소스 — 영구 개방(재잠금 없음)

    private readonly NetworkVariable<int> pairId = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server); // 자물쇠 번호(열쇠_XX 쌍, 0 = 비잠금 문) — 스폰 주입 복제(늦은 합류 대비)

    public bool IsLocked => isLocked.Value; // 면 컴포넌트의 프롬프트 게이팅 판정용
    public bool IsOpen => isOpen.Value; // 면 컴포넌트의 프롬프트 숨김 판정용
    public int PairId => pairId.Value; // 자물쇠 면의 쌍 열쇠 판정용

    /// <summary>
    /// 스폰 직후 서버가 잠금 여부·쌍 번호를 주입한다 — 절차 배치의 pairId 주입 지점(M7 요구 2,
    /// MapStateObjectSpawner 가 BlueprintEdge.LockNumber 로 호출). 수동 배치 맵은 씬 셋업에서 호출.
    /// </summary>
    /// <param name="lockPairId">자물쇠 번호(비잠금 문은 0).</param>
    /// <param name="locked">초기 잠금 여부.</param>
    public void ServerConfigure(int lockPairId, bool locked)
    {
        Log.D($"[DoorInteractable] ServerConfigure pairId={lockPairId} locked={locked}");
        pairId.Value = lockPairId; // 서버 외 호출은 NetworkVariable 쓰기 예외로 즉시 표면화(§4 — 가드 없음)
        isLocked.Value = locked;
    }

    /// <summary>스폰 직후 서버가 이 문에 걸린 자물쇠 오브젝트를 연결한다 — 해정 승인 시 문이 소멸시킨다(스포너 전용 경로).</summary>
    /// <param name="lockObject">자물쇠 NetworkObject.</param>
    public void ServerAttachLock(NetworkObject lockObject)
    {
        Log.D($"[DoorInteractable] ServerAttachLock {NetworkObjectId}");
        attachedLock = lockObject;
    }

    /// <summary>개방 변화 구독 + 늦은 합류 상태 동기화(Wardrobe 동형 — previous == current 호출은 연출 무음).</summary>
    public override void OnNetworkSpawn()
    {
        Log.D($"[DoorInteractable] OnNetworkSpawn {NetworkObjectId}");
        isOpen.OnValueChanged += HandleOpenChanged;
        isLocked.OnValueChanged += HandleLockChanged;
        HandleOpenChanged(isOpen.Value, isOpen.Value);
    }

    /// <summary>개방 변화 구독 해제.</summary>
    public override void OnNetworkDespawn()
    {
        Log.D($"[DoorInteractable] OnNetworkDespawn {NetworkObjectId}");
        isOpen.OnValueChanged -= HandleOpenChanged;
        isLocked.OnValueChanged -= HandleLockChanged;
    }

    /// <summary>자물쇠 면이 호출 — 해정+개방을 서버에 요청한다(M7 요구 1: 쌍 열쇠 상호작용 → 문 개방).</summary>
    /// <param name="interactor">해정을 시도한 상호작용 주체. 로컬 참조 — 서버 판정은 sender 로만.</param>
    public void RequestUnlockOpen(PlayerInteractor interactor)
    {
        Log.D($"[DoorInteractable] RequestUnlockOpen {NetworkObjectId}");
        RequestUnlockOpenServerRpc();
    }

    /// <summary>손잡이 면이 호출 — 개방을 서버에 요청한다. 잠금 상태면 서버가 거부한다(M7 요구 3).</summary>
    /// <param name="interactor">개방을 시도한 상호작용 주체. 로컬 참조 — 서버 판정은 sender 로만.</param>
    public void RequestOpen(PlayerInteractor interactor)
    {
        Log.D($"[DoorInteractable] RequestOpen {NetworkObjectId}");
        RequestOpenServerRpc();
    }

    /// <summary>
    /// 해정+개방 요청 서버 수신 — sender 신원 확인(Wardrobe TryGetSenderPlayer 패턴) 후 승인:
    /// isLocked=false·isOpen=true, 열쇠 소멸은 승인 후 sender 클라에 위임(인벤토리가 로컬 소유라 서버가 직접 소비 불가 —
    /// 쌍 열쇠 판정은 자물쇠 면의 클라 이중 가드 소관, 아키텍처 편차 보고됨).
    /// </summary>
    /// <param name="rpcParams">송신자 식별용 RPC 파라미터.</param>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestUnlockOpenServerRpc(RpcParams rpcParams = default)
    {
        Log.D($"[DoorInteractable] RequestUnlockOpenServerRpc {NetworkObjectId}");
        if (!IsServer || !isLocked.Value || isOpen.Value)
        {
            return; // 비잠금·기개방 문에 대한 해정 요청 = 경합 패배 or 조작 — 무시
        }

        if (!TryGetSenderPlayer(rpcParams, out NetworkObject playerObject))
        {
            return;
        }

        isLocked.Value = false;
        isOpen.Value = true;
        if (attachedLock != null && attachedLock.IsSpawned)
        {
            attachedLock.Despawn(); // 해정 = 자물쇠 소멸 — 자물쇠는 별도 오브젝트라 열린 문짝을 따라가지 않으므로 남겨두면 허공에 뜬다
        }

        ConsumeHeldKeyClientRpc(RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp)); // 승인 후 열쇠 소멸(3-6) — 해정한 클라만
    }

    /// <summary>개방 요청 서버 수신 — 잠금이면 거부(M7 요구 3), 아니면 isOpen=true(영구).</summary>
    /// <param name="rpcParams">송신자 식별용 RPC 파라미터.</param>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestOpenServerRpc(RpcParams rpcParams = default)
    {
        Log.D($"[DoorInteractable] RequestOpenServerRpc {NetworkObjectId}");
        if (!IsServer || isLocked.Value || isOpen.Value)
        {
            return; // 잠금 거부(M7 요구 3) · 기개방 중복 무시
        }

        isOpen.Value = true;
    }

    /// <summary>
    /// 개방 변화 편승 — 각 클라가 애니메이터 IsOpen·개방 SFX·NavMeshObstacle 비활성화를 구동한다(§4.5 오브젝트 사운드).
    /// 스폰 동기화 호출(previous == current)은 연출 없이 상태만 맞춘다.
    /// </summary>
    /// <param name="previous">이전 개방 상태.</param>
    /// <param name="current">현재 개방 상태.</param>
    private void HandleOpenChanged(bool previous, bool current)
    {
        Log.D($"[DoorInteractable] HandleOpenChanged {previous}->{current}");
        doorAnimator.SetBool(isOpenAnimHash, current);
        navObstacle.enabled = !current; // 개방 = Carve 해제 — 좀비 길이 실시간으로 열린다

        if (previous == current)
        {
            return; // 늦은 합류 동기화 — 연출 무음
        }

        sfxEventChannel.RaisePlayEvent(openAudioId, transform.position);
    }

    /// <summary>잠금 변화 편승 — 해정(true→false) 시 해정 SFX 를 재생한다(개방음과 별도 음원. 자물쇠 비주얼은 별도 스폰 오브젝트 소관).</summary>
    /// <param name="previous">이전 잠금 상태.</param>
    /// <param name="current">현재 잠금 상태.</param>
    private void HandleLockChanged(bool previous, bool current)
    {
        // 스폰 주입(ServerConfigure) 직후 false→true 전이는 연출 대상이 아니다 — 해정만 소리를 낸다
        if (!previous || current)
        {
            return;
        }

        sfxEventChannel.RaisePlayEvent(unlockAudioId, transform.position);
    }

    /// <summary>해정 승인 통지 — 해정한 클라만 수신해 자기 손의 열쇠를 소멸시킨다(ConsumeHeld, 3-6 · 서버 승인 후 실행).</summary>
    /// <param name="rpcParams">대상 지정 RPC 파라미터.</param>
    [Rpc(SendTo.SpecifiedInParams)]
    private void ConsumeHeldKeyClientRpc(RpcParams rpcParams = default)
    {
        Log.D($"[DoorInteractable] ConsumeHeldKeyClientRpc {NetworkObjectId}");
        NetworkManager.LocalClient.PlayerObject.GetComponentInChildren<PlayerInventory>().ConsumeHeld();
    }

    /// <summary>
    /// RPC 송신자의 PlayerObject 를 조회한다. 클라가 보낸 참조를 신뢰하지 않고 sender 로만 신원을 판정한다
    /// (WardrobeInteractable 과 동일 패턴).
    /// </summary>
    /// <param name="rpcParams">송신자 식별용 RPC 파라미터.</param>
    /// <param name="playerObject">확인된 송신자의 PlayerObject. 실패 시 null.</param>
    /// <returns>PlayerObject 확인 성공 여부.</returns>
    private bool TryGetSenderPlayer(RpcParams rpcParams, out NetworkObject playerObject)
    {
        playerObject = null;
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (NetworkManager == null
            || !NetworkManager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient client)
            || client.PlayerObject == null)
        {
            Log.W($"[{nameof(DoorInteractable)}] Rejected request from client {senderClientId}: PlayerObject is unavailable.", this);
            return false;
        }

        playerObject = client.PlayerObject;
        return true;
    }
}
