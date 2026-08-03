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
    [SerializeField] private Transform hideAnchor; // 진입 시 플레이어가 스냅될 벽장 안 은신 지점(자식 Transform)
    [SerializeField] private Transform exitAnchor; // 탈출 시 플레이어가 복귀할 문 앞 위치(자식 Transform, 3-5-1)

    // 점유자의 PlayerObject NetworkObjectId. 0 = 비어 있음. 점유 여부의 단일 소스이며 서버만 쓴다.
    private readonly NetworkVariable<ulong> occupantId = new NetworkVariable<ulong>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public bool IsOccupied => occupantId.Value != 0; // 바깥 문 프롬프트가 '사용 중' 판정에 조회한다(M1)

    /// <summary>문 개폐 연출·SFX 를 점유 변화에 태우기 위해 구독한다(§4.5 오브젝트 사운드: 상태 복제에 편승).</summary>
    public override void OnNetworkSpawn()
    {
        // TODO(impl): occupantId.OnValueChanged += HandleOccupantChanged; 후 현재값으로 문 상태 1회 동기화(늦은 합류 대비).
        Log.D($"[WardrobeInteractable] OnNetworkSpawn {NetworkObjectId}");
    }

    /// <summary>점유 변화 구독을 해제한다.</summary>
    public override void OnNetworkDespawn()
    {
        // TODO(impl): occupantId.OnValueChanged -= HandleOccupantChanged;
        Log.D($"[WardrobeInteractable] OnNetworkDespawn {NetworkObjectId}");
    }

    /// <summary>
    /// 바깥 문 홀드 완료 시 <see cref="WardrobeEntrance"/> 가 호출한다. 진입을 서버에 요청한다.
    /// 서버가 점유·은신 확정과 위치 스냅을 모두 처리하므로 클라는 요청만 보낸다(interactor 는 wire 로 넘기지 않는다 — 신원은 서버가 sender 로 판정).
    /// </summary>
    /// <param name="interactor">진입을 시도한 상호작용 주체. 로컬 참조이며 서버 판정에는 쓰이지 않는다.</param>
    public void RequestEnter(PlayerInteractor interactor)
    {
        // TODO(impl): RequestEnterServerRpc() 호출.
        Log.D($"[WardrobeInteractable] RequestEnter on {NetworkObjectId}");
    }

    /// <summary>
    /// 안쪽 홀드 완료 시 <see cref="WardrobeExit"/> 가 호출한다. 탈출을 서버에 요청한다.
    /// </summary>
    /// <param name="interactor">탈출을 시도한 상호작용 주체. 로컬 참조이며 서버 판정에는 쓰이지 않는다.</param>
    public void RequestExit(PlayerInteractor interactor)
    {
        // TODO(impl): RequestExitServerRpc() 호출.
        Log.D($"[WardrobeInteractable] RequestExit on {NetworkObjectId}");
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
        // TODO(impl): !IsServer || IsOccupied 면 무시(이미 점유 = 경쟁 패배).
        // TODO(impl): sender ClientId → NetworkManager.ConnectedClients[id].PlayerObject 로 신원 확인(없으면 무시).
        // TODO(impl): occupantId.Value = playerObject.NetworkObjectId.
        // TODO(impl): playerObject.GetComponent<PlayerHiding>().ServerSetHidden(true).
        // TODO(impl): 서버에서 플레이어 위치를 hideAnchor 로 스냅(서버 권위 NetworkTransform 필요 — 프리팹 설정 사항).
        Log.D($"[WardrobeInteractable] RequestEnterServerRpc on {NetworkObjectId}");
    }

    /// <summary>
    /// 탈출 요청을 서버에서 수신한다. 요청자가 현재 점유자일 때만 승인한다.
    /// 승인 시 점유를 비우고, 점유자의 은신을 OFF, 위치를 exitAnchor 로 스냅한다.
    /// </summary>
    /// <param name="rpcParams">송신자 식별용 RPC 파라미터.</param>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestExitServerRpc(RpcParams rpcParams = default)
    {
        // TODO(impl): !IsServer 면 무시. sender 의 PlayerObject.NetworkObjectId 가 occupantId 와 다르면 무시(점유자 아님).
        // TODO(impl): playerObject.GetComponent<PlayerHiding>().ServerSetHidden(false).
        // TODO(impl): 서버에서 플레이어 위치를 exitAnchor 로 스냅.
        // TODO(impl): occupantId.Value = 0.
        Log.D($"[WardrobeInteractable] RequestExitServerRpc on {NetworkObjectId}");
    }

    /// <summary>점유 변화에 맞춰 문 개폐 연출·SFX 를 구동한다. 모든 클라에서 각자 자기 인스턴스가 실행한다(§4.5).</summary>
    /// <param name="previous">이전 점유자 Id(0=빔).</param>
    /// <param name="current">현재 점유자 Id(0=빔).</param>
    private void HandleOccupantChanged(ulong previous, ulong current)
    {
        // TODO(impl): current != 0 이면 문 닫힘 연출/SFX, 0 이면 열림 연출/SFX. SFX 는 sfxEventChannel 로 발행(§4.5 오브젝트 사운드).
        Log.D($"[WardrobeInteractable] HandleOccupantChanged {previous}->{current}");
    }
}
