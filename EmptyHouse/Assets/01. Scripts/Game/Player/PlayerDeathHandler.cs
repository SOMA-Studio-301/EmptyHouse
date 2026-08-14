using Border.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 플레이어의 사망 상태를 소유하는 서버 권위 컴포넌트. 사망 자체는 이진 상태(래치)다.
/// 서버가 Die() 로 사망을 확정하면 isDead 를 세워 전 클라에 복제하고(관전·생존자 판별이 구독),
/// PlayerLifecycleEventChannelSO 로 ServerGameManager 에 사망을 발화한다(로스터·종료 판정).
/// 좀비 타격은 원턴킬(D27)에서 체력제(EH-97)로 바뀌어 <see cref="PlayerHealth"/> 가 수신하며,
/// 체력이 소진되는 타격에서 이 컴포넌트의 Die() 를 호출한다.
/// </summary>
public class PlayerDeathHandler : NetworkBehaviour
{
    [Header("Broadcasting on")]
    [SerializeField] private PlayerLifecycleEventChannelSO playerLifecycle; // 사망 신호 발화 채널

    [Header("Debug")]
    [SerializeField] private Key debugSuicideKey = Key.K; // 디버그 자살 키

    // 서버 권위 사망 상태. 한 번 서면 래치되고 전 클라에 복제된다 — 관전 진입·생존자 판별이 구독한다.
    private readonly NetworkVariable<bool> isDead = new NetworkVariable<bool>(false);

    public NetworkVariable<bool> IsDead => isDead; // 서버가 쓰고 전 클라가 읽는 복제 사망 상태 — 관전·생존자 판별이 구독

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

    /// <summary>사망을 확정한다(서버 전용). 체력 소진(PlayerHealth)·지형 조우(D27)가 서버에서 직접 호출한다. 이미 사망이면 무시(래치).</summary>
    public void Die()
    {
        Log.D("[PlayerDeathHandler] Die");
        if (!IsServer) return;
        if (isDead.Value) return; // 이미 사망이면 무시 — 이진 상태의 래치

        isDead.Value = true;
        playerLifecycle.RaiseDied(OwnerClientId);
    }
}
