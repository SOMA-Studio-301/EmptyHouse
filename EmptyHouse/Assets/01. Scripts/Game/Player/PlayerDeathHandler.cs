using Border.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 플레이어의 사망 상태를 소유하는 서버 권위 컴포넌트. 사망은 원턴킬(D27)이라 체력 게이지가 아니라 이진 상태다.
/// 서버가 Die() 로 사망을 확정하면 isDead 를 세워 전 클라에 복제하고(관전·생존자 판별이 구독),
/// PlayerLifecycleEventChannelSO 로 ServerGameManager 에 사망을 발화한다(로스터·종료 판정).
/// 좀비 타격(좀비AI 7-4)은 ZombiePlayerCaughtEventChannelSO 로 서버에서 수신한다 — 같은 프로세스 안의 SO 이벤트라 RPC 왕복이 없다.
/// </summary>
public class PlayerDeathHandler : NetworkBehaviour
{
    [Header("Broadcasting on")]
    [SerializeField] private PlayerLifecycleEventChannelSO playerLifecycle; // 사망 신호 발화 채널

    [Header("Listening to")]
    [SerializeField] private ZombiePlayerCaughtEventChannelSO playerCaught; // 좀비 타격 수신 채널 — 서버에서만 구독

    [Header("Debug")]
    [SerializeField] private Key debugSuicideKey = Key.K; // 디버그 자살 키

    // 서버 권위 사망 상태. 한 번 서면 래치되고 전 클라에 복제된다 — 관전 진입·생존자 판별이 구독한다.
    private readonly NetworkVariable<bool> isDead = new NetworkVariable<bool>(false);

    public NetworkVariable<bool> IsDead => isDead; // 서버가 쓰고 전 클라가 읽는 복제 사망 상태 — 관전·생존자 판별이 구독

    /// <summary>좀비 타격 채널을 구독한다(서버 전용). 클라에선 좀비 로직이 돌지 않아 채널이 발화될 일이 없다.</summary>
    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        playerCaught.OnEventRaised += HandlePlayerCaught;
    }

    /// <summary>구독을 해제한다. OnNetworkSpawn 과 짝을 맞춘다.</summary>
    public override void OnNetworkDespawn()
    {
        if (!IsServer) return;
        playerCaught.OnEventRaised -= HandlePlayerCaught;
    }

    /// <summary>
    /// 좀비 타격을 수신해 사망을 확정한다(서버 전용). 채널은 전원의 핸들러에 발화되므로
    /// 자기 NetworkObjectId 와 대조해 대상이 자신일 때만 처리한다 — 없으면 한 명이 잡힐 때 전원이 죽는다.
    /// </summary>
    /// <param name="payload">타격한 좀비와 잡힌 플레이어의 NetworkObjectId 쌍.</param>
    private void HandlePlayerCaught(ZombiePlayerCaughtEvent payload)
    {
        if (payload.PlayerNetworkObjectId != NetworkObjectId) return;

        Log.D($"[PlayerDeathHandler] HandlePlayerCaught zombie={payload.ZombieNetworkObjectId}");
        Die();
    }

    /// <summary>소유 클라이언트에 한해 디버그 자살 키를 폴링한다. 서버 권위라 직접 죽지 않고 요청만 보낸다.</summary>
    private void Update()
    {
        if (!IsOwner) return;
        if (Keyboard.current == null) return;

        if (Keyboard.current[debugSuicideKey].wasPressedThisFrame)
        {
            RequestDieServerRpc();
        }
    }

    /// <summary>소유 클라의 자살 요청을 서버에서 수신해 사망을 확정한다. 디버그 경로 전용이다.</summary>
    [ServerRpc]
    private void RequestDieServerRpc()
    {
        Log.D("[PlayerDeathHandler] RequestDieServerRpc");
        Die();
    }

    /// <summary>사망을 확정한다(서버 전용). 좀비 공격·지형 조우(D27)가 서버에서 직접 호출한다. 이미 사망이면 무시(래치).</summary>
    public void Die()
    {
        Log.D("[PlayerDeathHandler] Die");
        if (!IsServer) return;
        if (isDead.Value) return; // 이미 사망이면 무시 — 원턴킬 이진 상태의 래치

        isDead.Value = true;
        playerLifecycle.RaiseDied(OwnerClientId);
    }
}
