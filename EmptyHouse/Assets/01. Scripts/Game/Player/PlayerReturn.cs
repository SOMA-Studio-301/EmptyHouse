using Border.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 플레이어의 클라→서버 생명주기 요청 창구(NetworkBehaviour, 플레이어 프리팹). 소유자 클라에서 생긴
/// "내 플레이어가 귀환했다"를 ServerRpc 로 서버에 올려, 서버가 PlayerLifecycleEventChannelSO 로 발행하게 한다
/// (채널 계약: 발행은 반드시 서버). 발행분은 기존 배선으로 ServerGameManager 로스터에 반영된다.
/// 귀환(Extracted, D36) 전용이다. 개인 포기(Left)는 여기 얹지 않는다 — 나가기는 곧 연결 종료라 서버가
/// OnClientDisconnectCallback 으로 감지해 스포너가 Left 를 발화한다(경로 단일화). RPC 로 알리고 곧바로
/// Shutdown 하면 플러시 전에 연결이 닫혀 유실될 수 있어 레이스가 생긴다. 사망(Dead)도 서버가 감지·발행하는
/// 권위 이벤트라 이 client 요청 경로에 넣지 않는다.
/// </summary>
public class PlayerReturn : NetworkBehaviour
{
    [Header("Broadcast (server-raised)")]
    [SerializeField] private PlayerLifecycleEventChannelSO playerLifecycle;

    // 서버 권위 귀환 상태. 한 번 서면 래치되고 전 클라에 복제된다 — PlayerDeathHandler.isDead 와 대칭이며 관전 진입·생존자 판별이 구독한다.
    private readonly NetworkVariable<bool> hasExtracted = new NetworkVariable<bool>(false);

    public NetworkVariable<bool> HasExtracted => hasExtracted; // 서버가 쓰고 전 클라가 읽는 복제 귀환 상태 — 관전·생존자 판별이 구독

    /// <summary>
    /// 소유자 클라에서 홀드 귀환 완료 시 호출한다(ReturnInteractable.OnActivate). 서버에 귀환을 요청한다.
    /// 관전 전환은 복제된 hasExtracted 를 PlayerSpectatorController 가 구독해 처리하므로 여기서 건드리지 않는다.
    /// 인터랙션은 소유자만 굴리므로(PlayerController) 비소유자에서 불리지 않는다.
    /// </summary>
    public void RequestReturn()
    {
        Log.D("[PlayerReturn] RequestReturn");

        RequestReturnServerRpc();
    }

    /// <summary>
    /// 서버에서 이 플레이어의 귀환을 확정하고 PlayerLifecycleEventChannelSO 로 발행한다. 이미 귀환이면 무시(래치).
    /// 소유자만 보낼 수 있고(기본 RequireOwnership) 서버에서 실행되므로 OwnerClientId 가 곧 귀환한 clientId 다.
    /// </summary>
    [ServerRpc]
    private void RequestReturnServerRpc()
    {
        Log.D($"[PlayerReturn] RequestReturnServerRpc owner={OwnerClientId}");

        if (hasExtracted.Value) return; // 이미 귀환이면 무시 — 이진 상태의 래치

        // 서버에서만 발행돼야 로스터·NetworkVariable 판정이 성립한다(채널 계약). OwnerClientId 가 곧 귀환한 clientId 다.
        hasExtracted.Value = true;
        playerLifecycle.RaiseExtracted(OwnerClientId);
    }
}
