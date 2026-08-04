using Border.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 벽장 루트 — 점유 상태의 단일 소스이자 진입/탈출의 서버 권위자 (조작상호작용UI.md 3-5-1).
/// InteractableBase 가 아니다: 직접 조준되는 것은 두 상호작용부(<see cref="WardrobeEntrance"/>·<see cref="WardrobeExit"/>)이고,
/// 이 루트는 점유·은신 플래그·문 연출만 관리한다(3-5-1: 두 면은 판정·프롬프트만, 점유는 루트가 단일 소스).
/// 1인 점유이며 동시 완료 시에도 서버가 한 명만 승인한다(3-9 M1).
/// </summary>
public sealed class WardrobeInteractable : NetworkBehaviour
{
    [Header("Anchors")]
    [SerializeField] private Transform hideAnchor; // 진입 시 플레이어가 스냅될 벽장 안 은신 지점(자식 Transform). 위치만 쓴다 — 시선은 루트 forward
    [SerializeField] private Transform exitAnchor; // 탈출 시 플레이어가 복귀할 문 앞 위치(자식 Transform, 3-5-1)

    [Header("Audio")]
    [SerializeField] private SFXEventChannelSO sfxEventChannel; // 원샷 SFX 발행 채널(§4.5 오브젝트 사운드)
    [SerializeField] private AudioId doorCloseAudioId = AudioId.Sfx_Wardrobe_Close; // 진입 확정(문 닫힘) 시 재생
    [SerializeField] private AudioId doorOpenAudioId = AudioId.Sfx_Wardrobe_Open; // 탈출 확정(문 열림) 시 재생

    // 점유자의 PlayerObject NetworkObjectId. 0 = 비어 있음. 점유 여부의 단일 소스이며 서버만 쓴다.
    private readonly NetworkVariable<ulong> occupantId = new NetworkVariable<ulong>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public bool IsOccupied => occupantId.Value != 0; // 바깥 문 프롬프트가 '사용 중' 판정에 조회한다(M1)

    private const float GroundClearance = 0.05f; // 스냅 시 바닥 겹침 방지 여유 높이(GameScenePlayerSpawner 와 동일)

    /// <summary>문 개폐 연출·SFX 를 점유 변화에 태우기 위해 구독한다(§4.5 오브젝트 사운드: 상태 복제에 편승).</summary>
    public override void OnNetworkSpawn()
    {
        occupantId.OnValueChanged += HandleOccupantChanged;
        // 늦은 합류자도 현재 점유값으로 문 상태를 맞춘다. previous == current 이므로 SFX 는 나지 않는다.
        HandleOccupantChanged(occupantId.Value, occupantId.Value);
    }

    /// <summary>점유 변화 구독을 해제한다.</summary>
    public override void OnNetworkDespawn()
    {
        occupantId.OnValueChanged -= HandleOccupantChanged;
    }

    /// <summary>
    /// 바깥 문 홀드 완료 시 <see cref="WardrobeEntrance"/> 가 호출한다. 진입을 서버에 요청한다.
    /// 서버가 점유·은신 확정과 위치 스냅을 모두 처리하므로 클라는 요청만 보낸다(interactor 는 wire 로 넘기지 않는다 — 신원은 서버가 sender 로 판정).
    /// </summary>
    /// <param name="interactor">진입을 시도한 상호작용 주체. 로컬 참조이며 서버 판정에는 쓰이지 않는다.</param>
    public void RequestEnter(PlayerInteractor interactor)
    {
        RequestEnterServerRpc();
    }

    /// <summary>
    /// 안쪽 홀드 완료 시 <see cref="WardrobeExit"/> 가 호출한다. 탈출을 서버에 요청한다.
    /// </summary>
    /// <param name="interactor">탈출을 시도한 상호작용 주체. 로컬 참조이며 서버 판정에는 쓰이지 않는다.</param>
    public void RequestExit(PlayerInteractor interactor)
    {
        RequestExitServerRpc();
    }

    /// <summary>
    /// 진입 요청을 서버에서 수신해 점유를 확정한다. 비어 있을 때만 승인하며, 동시 완료 시 먼저 도달한 한 명만 성공한다(3-9 M1).
    /// 승인 시 sender 의 PlayerObject 를 점유자로 기록하고, 그 <see cref="PlayerHiding"/> 를 은신 ON, 위치를 hideAnchor 로 스냅한다.
    /// 픽업처럼 서버 소유 오브젝트라 sender 가 소유자가 아니므로 소유권 검사를 끈다(InteractableBase 소음 RPC 와 동일 패턴).
    /// </summary>
    /// <param name="rpcParams">송신자 식별용 RPC 파라미터.</param>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestEnterServerRpc(RpcParams rpcParams = default)
    {
        if (!IsServer || IsOccupied) return; // 이미 점유 = 동시 완료 경쟁 패배(3-9 M1)
        if (!TryGetSenderPlayer(rpcParams, out NetworkObject playerObject)) return;

        occupantId.Value = playerObject.NetworkObjectId;
        playerObject.GetComponent<PlayerHiding>().ServerSetHidden(true);
        // 플레이어 NetworkTransform 이 소유자 권위라 서버가 직접 위치를 못 쓰니 소유자에게 스냅을 위임.
        // 시선은 앵커 회전이 아니라 벽장 루트의 forward(문 방향)로 통일한다 — 앵커 회전과 무관하게 항상 들어온 문을 본다.
        playerObject.GetComponent<PlayerController>().SnapPoseClientRpc(GetSnapPosition(hideAnchor, playerObject), transform.rotation);
    }

    /// <summary>
    /// 탈출 요청을 서버에서 수신한다. 요청자가 현재 점유자일 때만 승인한다.
    /// 승인 시 점유를 비우고, 점유자의 은신을 OFF, 위치를 exitAnchor 로 스냅한다.
    /// </summary>
    /// <param name="rpcParams">송신자 식별용 RPC 파라미터.</param>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestExitServerRpc(RpcParams rpcParams = default)
    {
        if (!IsServer) return;
        if (!TryGetSenderPlayer(rpcParams, out NetworkObject playerObject)) return;
        if (playerObject.NetworkObjectId != occupantId.Value) return; // 점유자만 탈출

        playerObject.GetComponent<PlayerHiding>().ServerSetHidden(false);
        playerObject.GetComponent<PlayerController>().SnapPoseClientRpc(GetSnapPosition(exitAnchor, playerObject), exitAnchor.rotation);
        occupantId.Value = 0;
    }

    /// <summary>
    /// 바닥에 밀착 배치된 앵커 기준, 캡슐 피벗이 와야 할 스냅 위치를 계산한다.
    /// 스냅 대상 플레이어의 캡슐로 직접 계산하므로 프리팹 치수가 바뀌어도 따라간다(GameScenePlayerSpawner 와 동일 공식).
    /// </summary>
    /// <param name="anchor">바닥에 밀착 배치된 스냅 앵커.</param>
    /// <param name="playerObject">스냅할 플레이어.</param>
    /// <returns>캡슐 반높이 + 여유만큼 띄운 스냅 위치.</returns>
    private static Vector3 GetSnapPosition(Transform anchor, NetworkObject playerObject)
    {
        // 캡슐 피벗이 중심이라, 바닥에 붙은 앵커 기준 스냅 높이 = 반높이 − center.y + 여유.
        CapsuleCollider capsule = playerObject.GetComponent<CapsuleCollider>();
        float heightOffset = capsule.height * 0.5f - capsule.center.y + GroundClearance;
        return anchor.position + Vector3.up * heightOffset;
    }

    /// <summary>
    /// RPC 송신자의 PlayerObject 를 조회한다. 클라가 보낸 참조를 신뢰하지 않고 sender 로만 신원을 판정한다
    /// (InteractableBase 소음 RPC 와 동일 패턴).
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
            Log.W($"[{nameof(WardrobeInteractable)}] Rejected request from client {senderClientId}: PlayerObject is unavailable.", this);
            return false;
        }

        playerObject = client.PlayerObject;
        return true;
    }

    /// <summary>점유 변화에 맞춰 문 개폐 연출·SFX 를 구동한다. 모든 클라에서 각자 자기 인스턴스가 실행한다(§4.5).</summary>
    /// <param name="previous">이전 점유자 Id(0=빔).</param>
    /// <param name="current">현재 점유자 Id(0=빔).</param>
    private void HandleOccupantChanged(ulong previous, ulong current)
    {
        // 문 개폐 시각 연출은 아트 리소스 미확정이라 아직 SFX 만 태운다. 스폰 동기화 호출(previous == current)은 상태 변화가 아니므로 소리를 내지 않는다.
        if (previous == current) return;

        sfxEventChannel.RaisePlayEvent(current != 0 ? doorCloseAudioId : doorOpenAudioId, transform.position);
    }
}
