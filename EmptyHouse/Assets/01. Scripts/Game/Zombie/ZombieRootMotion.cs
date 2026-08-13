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
///
/// 이동 방향은 정면이 아니라 에이전트의 회피 결과(desiredVelocity)를 따른다(EH-43).
/// 루트모션 그대로 정면 직진만 하면 RVO 가 "옆으로 비켜라"를 계산해도 실행할 몸이 없어,
/// 뭉친 좀비끼리 서로의 회피 반경에 갇혀 집단 교착에 빠진다. 보폭(크기)은 애니메이션이,
/// 방향은 회피가 정하게 나누면 발 미끄러짐 없이 서로 비껴간다.
/// </summary>
[RequireComponent(typeof(Animator))]
public class ZombieRootMotion : MonoBehaviour
{
    // 이 속력(m/s) 미만의 desiredVelocity 는 방향 기준으로 삼지 않는다 — 경로 재계산 순간이나
    // 정지 프레임에 0 벡터로 정규화하면 이동이 한 프레임씩 끊겨 덜컥거린다. 그때는 정면 델타로 폴백.
    private const float minDesiredSpeed = 0.05f;

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
    /// 수평 델타는 크기만 취하고 방향은 desiredVelocity(경로+회피)를 따른다 — EH-43 주석 참조.
    /// </summary>
    private void OnAnimatorMove()
    {
        if (!controller.IsSpawned || !controller.IsServer) return;

        Transform root = controller.transform;
        Vector3 delta = animator.deltaPosition;

        NavMeshAgent agent = controller.Agent;
        Vector3 desired = agent.desiredVelocity;
        desired.y = 0f;
        if (desired.sqrMagnitude >= minDesiredSpeed * minDesiredSpeed)
        {
            Vector3 planar = delta;
            planar.y = 0f;
            delta = desired.normalized * planar.magnitude + Vector3.up * delta.y;
        }

        root.position += delta;
        if (agent.isOnNavMesh) agent.nextPosition = root.position;
    }
}
