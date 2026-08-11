using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 루트모션이 만든 이동을 좀비 루트로 옮긴다.
///
/// Animator 는 자식 ZombieModel 에 붙어 있어 OnAnimatorMove 콜백이 루트가 아니라 이 오브젝트로 온다.
/// 가로채지 않으면 Animator 가 자기 트랜스폼만 밀어 모델이 루트에서 떨어져 나간다.
///
/// 이동 권한은 서버뿐이다. 클라이언트는 applyRootMotion 이 꺼져 있어 델타가 0 이고,
/// 위치는 NetworkTransform 이 밀어 준다.
///
/// 회전은 건드리지 않는다 — 클립을 Rotation Bake Into Pose 로 넣어 루트 회전 델타가 나오지 않고,
/// 방향은 ZombieStateMachine.UpdateFacing 이 TurnSpeedDegreesPerSecond 로 직접 돌린다.
/// </summary>
[RequireComponent(typeof(Animator))]
public class ZombieRootMotion : MonoBehaviour
{
    private Animator animator;           // 이 오브젝트의 Animator. 루트모션 델타의 출처
    private ZombieController controller; // 좀비 루트. 서버 판정과 에이전트 접근에 쓴다

    /// <summary>참조를 캐시한다.</summary>
    private void Awake()
    {
        animator = GetComponent<Animator>();
        controller = GetComponentInParent<ZombieController>();
    }

    /// <summary>
    /// 루트모션 델타를 좀비 루트에 적용하고 NavMeshAgent 내부 위치를 맞춘다.
    /// nextPosition 동기를 빠뜨리면 에이전트 내부 위치가 제자리에 남아 경로가 계속 튄다.
    /// </summary>
    private void OnAnimatorMove()
    {
        if (!controller.IsSpawned || !controller.IsServer) return;

        Transform root = controller.transform;
        root.position += animator.deltaPosition;

        NavMeshAgent agent = controller.Agent;
        if (agent.isOnNavMesh) agent.nextPosition = root.position;
    }
}
