using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 사망·귀환으로 비활성화된 플레이어의 몸체를 모든 클라이언트 화면에서 감춘다.
/// PlayerController.HandleInactiveChanged 는 소유자 전용 조작 차단이라 다른 클라이언트 화면엔 영향이 없다 —
/// 이 컴포넌트는 IsOwner 게이트 없이 전 클라이언트에서 독립 구독해 시각적·물리적으로 사라지게 한다.
/// NetworkObject 는 Despawn 하지 않는다 — 관전 대상 순환(PlayerSpectatorController)이 살아있는 참조를 필요로 한다.
/// </summary>
public class PlayerBodyVisibility : NetworkBehaviour
{
    private Renderer[] renderers; // 감출 대상 전부 — Awake 캐시(자기완결, 인스펙터 배선 불필요)
    private Collider[] colliders; // 감출 대상 전부 — Awake 캐시(자기완결, 인스펙터 배선 불필요)

    private PlayerDeathHandler deathHandler; // 사망 게이팅 소스 — 형제
    private PlayerReturn playerReturn; // 귀환 게이팅 소스 — 형제

    /// <summary>형제 컴포넌트와 자신·자식의 렌더러·콜라이더를 캐시한다.</summary>
    private void Awake()
    {
        deathHandler = GetComponent<PlayerDeathHandler>();
        playerReturn = GetComponent<PlayerReturn>();
        renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
        colliders = GetComponentsInChildren<Collider>(includeInactive: true);
    }

    /// <summary>
    /// 사망/귀환 상태 변화를 전 클라이언트에서 구독한다 — IsOwner 게이트가 없어야
    /// 본인 화면뿐 아니라 다른 플레이어·관전자 화면에서도 몸체가 사라진다.
    /// 스폰 시점 현재 상태도 1차 반영한다(늦게 스폰되는 관전자·재접속 클라이언트 대비).
    /// </summary>
    public override void OnNetworkSpawn()
    {
        deathHandler.IsDead.OnValueChanged += HandleInactiveChanged;
        playerReturn.HasExtracted.OnValueChanged += HandleInactiveChanged;

        ApplyVisibility(deathHandler.IsDead.Value || playerReturn.HasExtracted.Value);
    }

    /// <summary>구독을 해제한다.</summary>
    public override void OnNetworkDespawn()
    {
        deathHandler.IsDead.OnValueChanged -= HandleInactiveChanged;
        playerReturn.HasExtracted.OnValueChanged -= HandleInactiveChanged;
    }

    /// <summary>
    /// 사망·귀환 어느 쪽이든 몸체를 감춘다. 두 채널이 같은 핸들러를 공유하므로 인자는 쓰지 않고 현재 상태를 다시 읽는다.
    /// </summary>
    /// <param name="previous">이전 상태(미사용).</param>
    /// <param name="current">새 상태(미사용).</param>
    private void HandleInactiveChanged(bool previous, bool current)
    {
        ApplyVisibility(deathHandler.IsDead.Value || playerReturn.HasExtracted.Value);
    }

    /// <summary>렌더러·콜라이더를 일괄 토글한다.</summary>
    /// <param name="hidden">true 면 감춘다.</param>
    private void ApplyVisibility(bool hidden)
    {
        foreach (Renderer r in renderers) r.enabled = !hidden;
        foreach (Collider c in colliders) c.enabled = !hidden;
    }
}
