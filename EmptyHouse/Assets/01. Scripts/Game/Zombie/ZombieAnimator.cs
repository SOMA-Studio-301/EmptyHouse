using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 복제된 좀비 상태를 받아 Animator 를 구동한다.
/// 상태는 서버가 쓰고 전 클라이언트에 복제되므로, 이 컴포넌트는 모든 인스턴스에서 동작한다(NetworkAnimator 불필요).
/// </summary>
public class ZombieAnimator : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private ZombieController controller;
    [SerializeField] private Animator animator;

    private const int animIdle = 0;   // 대기
    private const int animWalk = 1;   // 보행
    private const int animRoar = 2;   // 포효
    private const int animAttack = 3; // 공격

    private static readonly int stateHash = Animator.StringToHash("State");

    /// <summary>상태 변화를 구독하고 현재 상태를 즉시 반영한다(늦게 접속한 클라이언트 포함).</summary>
    public override void OnNetworkSpawn()
    {
        controller.StateChanged += HandleStateChanged;
        HandleStateChanged(controller.CurrentState);
    }

    /// <summary>상태 변화 구독을 해제한다.</summary>
    public override void OnNetworkDespawn()
    {
        controller.StateChanged -= HandleStateChanged;
    }

    /// <summary>좀비 상태를 애니메이터 State 파라미터로 반영한다.</summary>
    /// <param name="kind">복제된 현재 좀비 상태.</param>
    private void HandleStateChanged(ZombieStateKind kind)
    {
        animator.SetInteger(stateHash, ToAnimState(kind));
    }

    /// <summary>좀비 상태를 애니메이터 상태 인덱스로 매핑한다. 클립이 없는 상태는 대기로 수렴한다.</summary>
    /// <param name="kind">좀비 상태.</param>
    /// <returns>ZombieLocomotion 컨트롤러의 State 파라미터 값.</returns>
    private static int ToAnimState(ZombieStateKind kind)
    {
        switch (kind)
        {
            case ZombieStateKind.Wander:
            case ZombieStateKind.Investigate:
            case ZombieStateKind.Chase:
            case ZombieStateKind.Subside: // 홈으로 걸어서 복귀하는 구간이라 보행이다
                return animWalk;
            case ZombieStateKind.RoarTransition:
                return animRoar;
            case ZombieStateKind.Attack:
                return animAttack;
            default:
                return animIdle; // Alert
        }
    }
}
