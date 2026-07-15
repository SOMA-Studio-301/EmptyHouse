using Border.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 플레이어의 클라→서버 생명주기 요청 창구(NetworkBehaviour, 플레이어 프리팹). 소유자 클라에서 생긴
/// "내 플레이어가 귀환했다"를 ServerRpc 로 서버에 올려, 서버가 PlayerLifecycleEventChannelSO 로 발행하게 한다
/// (채널 계약: 발행은 반드시 서버). 발행분은 기존 배선으로 ServerGameManager 로스터에 반영된다.
/// 지금은 귀환(Extracted, D36)만 담당한다. 개인 포기(Left)도 같은 client→server 요청이라 이후 여기에 얹는다
/// (트리거는 UIPause → 로컬 PlayerObject 경유). 사망(Dead)은 서버가 감지·발행하는 권위 이벤트라 이 client 요청
/// 경로에 넣지 않는다.
/// </summary>
public class PlayerReturn : NetworkBehaviour
{
    [Header("Broadcast (server-raised)")]
    [SerializeField] private PlayerLifecycleEventChannelSO playerLifecycle;

    /// <summary>
    /// 소유자 클라에서 홀드 귀환 완료 시 호출한다(ReturnInteractable.OnActivate). 서버에 귀환을 요청하고 로컬은 관전으로 넘어간다.
    /// 인터랙션은 소유자만 굴리므로(PlayerController) 비소유자에서 불리지 않는다.
    /// </summary>
    public void RequestReturn()
    {
        Log.D("[PlayerReturn] RequestReturn");

        // 서버 권위 발행은 ServerRpc 에 맡기고, 로컬은 곧바로 관전으로 넘어간다.
        RequestReturnServerRpc();
        EnterSpectator();
    }

    /// <summary>
    /// 서버에서 이 플레이어의 귀환을 PlayerLifecycleEventChannelSO 로 발행한다.
    /// 소유자만 보낼 수 있고(기본 RequireOwnership) 서버에서 실행되므로 OwnerClientId 가 곧 귀환한 clientId 다.
    /// </summary>
    [ServerRpc]
    private void RequestReturnServerRpc()
    {
        Log.D($"[PlayerReturn] RequestReturnServerRpc owner={OwnerClientId}");

        // 서버에서만 발행돼야 로스터·NetworkVariable 판정이 성립한다(채널 계약). OwnerClientId 가 곧 귀환한 clientId 다.
        playerLifecycle.RaiseExtracted(OwnerClientId);
    }

    /// <summary>
    /// 귀환한 본인 클라를 관전 상태로 전환한다(세션루프.md 4장 관전 대기). 지금은 스텁 — 비워 둔다.
    /// </summary>
    private void EnterSpectator()
    {
        // TODO(impl): 관전 진입(카메라·입력 전환). 별도 작업에서 채운다.
        Log.D("[PlayerReturn] EnterSpectator (stub)");
    }
}
