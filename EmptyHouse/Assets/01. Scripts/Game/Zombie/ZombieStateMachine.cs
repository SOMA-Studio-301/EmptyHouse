using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(ZombieController), typeof(NavMeshAgent))]
public class ZombieStateMachine : NetworkBehaviour
{
    // 추격 회피 우선순위(낮을수록 우선). 전원이 프리팹 기본값 50 동순위면 RVO 가 누가 비킬지
    // 정하지 못해 회피 부담을 반반 나누다 오실레이션이 남는다(EH-43). 타겟에 가까운 좀비에게
    // 우선권을 주면 뒤의 좀비가 앞을 비켜 옆으로 돌아 나간다. 범위는 기본값 50 을 가운데 둔 30~70.
    private const int chasePriorityClosest = 30;
    private const int chasePriorityFarthest = 70;
    private const float chasePriorityMaxDistance = 20f; // 이 거리(m) 이상은 전부 최하 우선권
    private const int defaultAvoidancePriority = 50;    // Zombie.prefab 의 NavMeshAgent 기본값

    [SerializeField] private ZombieController controller;
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private ZombiePlayerCaughtEventChannelSO playerCaughtChannel;

    private IZombieState currentState;
    private Vector3 lastRequestedDestination;
    private bool hasRequestedDestination;
    private Vector3 patrolPoint;
    private bool hasPatrolPoint;

    private Vector3 facingTarget;              // 회전 오버라이드가 바라볼 월드 지점
    private float facingDegreesPerSecond;      // 그 지점까지 도는 속도(도/초). 주어진 시간에 딱 맞게 역산된 값이다
    private bool hasFacingOverride;            // 오버라이드 사용 여부. 꺼져 있으면 기존 규칙(이동 방향/관심 지점)을 따른다

    public ZombieController Controller => controller;
    public ZombieStateKind CurrentStateKind => currentState != null ? currentState.Kind : controller.CurrentState;
    public Vector3 PatrolPoint => patrolPoint;
    public bool HasPatrolPoint => hasPatrolPoint;

    public void ServerInitialize()
    {
        if (!IsServer) return;
        agent.updateRotation = false;
        // 위치는 루트모션이 만든다(ZombieRootMotion). 에이전트가 직접 밀면 애니와 이중으로 이동한다.
        agent.updatePosition = false;
        SwitchState(ZombieStateKind.Wander, true);
    }

    public void ServerTick(float deltaTime)
    {
        if (!IsServer || controller == null || controller.Data == null || agent == null) return;

        if (currentState == null || currentState.Kind != controller.CurrentState)
        {
            SwitchState(controller.CurrentState, true);
        }

        EvaluatePerceptionTransitions();
        currentState?.Tick(this, deltaTime);
        UpdateFacing(deltaTime);
    }

    private void EvaluatePerceptionTransitions()
    {
        ZombieStateKind kind = currentState.Kind;
        if (kind == ZombieStateKind.RoarTransition || kind == ZombieStateKind.Chase || kind == ZombieStateKind.Attack)
        {
            return;
        }

        if (controller.IsLatched && controller.CurrentTarget != null)
        {
            SwitchState(ZombieStateKind.RoarTransition);
            return;
        }

        float value = controller.SuspicionValue;
        if (value >= controller.Data.ThInvestigate)
        {
            SwitchState(ZombieStateKind.Investigate);
            return;
        }

        // 하향 전이는 의심도 밴드로 하지 않는다. 각 상태가 스스로 내려간다(Investigate → Subside → Wander).
        // 밴드로 끌어내리면 조사 상태가 냉각 시작(SuspicionGraceSeconds) 직후 곧바로 Alert 에 뺏겨
        // 조사 타이머(InvestigateToWanderSeconds)도 Subside 도 영영 성립하지 않는다. 그 결과
        // 교전 직후 좀비는 아무 동작도 없는 Alert 에 굳고(정지·무회전), 스쿼드 해제 조건(Subside/Wander)도
        // 성립하지 않아 주변 무리가 leash 만료까지 자기 지각을 멈춘 채 리더만 복사한다.
        if (kind != ZombieStateKind.Wander && kind != ZombieStateKind.Alert) return;

        SwitchState(value >= controller.Data.ThAlert
            ? ZombieStateKind.Alert
            : ZombieStateKind.Wander);
    }

    public void SwitchState(ZombieStateKind nextState, bool force = false)
    {
        if (!IsServer || controller == null || controller.Data == null) return;
        if (!force && currentState != null && currentState.Kind == nextState) return;

        currentState?.Exit(this);
        currentState = CreateState(nextState);
        controller.ServerSetState(nextState);
        currentState.Enter(this);
    }

    private static IZombieState CreateState(ZombieStateKind kind)
    {
        switch (kind)
        {
            case ZombieStateKind.Alert: return new ZombieAlertState();
            case ZombieStateKind.Investigate: return new ZombieInvestigateState();
            case ZombieStateKind.RoarTransition: return new ZombieRoarTransitionState();
            case ZombieStateKind.Chase: return new ZombieChaseState();
            case ZombieStateKind.Attack: return new ZombieAttackState();
            case ZombieStateKind.Subside: return new ZombieSubsideState();
            default: return new ZombieWanderState();
        }
    }

    public void SetDestination(Vector3 destination)
    {
        if (!IsServer || agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 2f, agent.areaMask))
        {
            float refreshDistance = controller.Data.DestinationRefreshDistance;
            if (hasRequestedDestination
                && !agent.isStopped
                && agent.hasPath
                && (lastRequestedDestination - hit.position).sqrMagnitude <= refreshDistance * refreshDistance)
            {
                return;
            }

            lastRequestedDestination = hit.position;
            hasRequestedDestination = true;
            agent.isStopped = false;
            agent.SetDestination(hit.position);
        }
    }

    public void StopAgent()
    {
        if (!IsServer || agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        agent.ResetPath();
        agent.isStopped = true;
    }

    public bool IsAtDestination(float tolerance = 0.25f)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh || agent.pathPending) return false;
        if (!hasRequestedDestination) return false;
        if (agent.hasPath) return agent.remainingDistance <= tolerance;

        Vector3 current = transform.position;
        Vector3 destination = lastRequestedDestination;
        current.y = 0f;
        destination.y = 0f;
        return Vector3.Distance(current, destination) <= tolerance;
    }

    public void MoveToHome() => SetDestination(controller.HomePosition);
    public void MoveToInvestigationPoint() => SetDestination(controller.LastKnownPosition);

    /// <summary>
    /// 주어진 시간에 딱 맞게 목표 지점을 바라보도록 회전을 넘겨받는다.
    /// 선회 속도를 <c>남은 각도 ÷ 시간</c> 으로 역산하므로, 90도를 돌든 180도를 돌든 같은 순간에 끝난다.
    /// 기본 선회 속도(TurnSpeedDegreesPerSecond)는 추격·조사용이라 배회 대기에 쓰면 한참 남기고 멈춰 선다.
    /// </summary>
    /// <param name="worldPoint">바라볼 월드 지점.</param>
    /// <param name="seconds">이 시간 안에 회전을 끝낸다.</param>
    public void BeginTimedFacing(Vector3 worldPoint, float seconds)
    {
        facingTarget = worldPoint;
        hasFacingOverride = true;

        Vector3 direction = worldPoint - transform.position;
        direction.y = 0f;

        // 목표가 발밑이거나 시간이 없으면 역산이 성립하지 않는다. 기본 선회 속도로 되돌린다.
        if (direction.sqrMagnitude < 0.01f || seconds <= 0f)
        {
            facingDegreesPerSecond = controller.Data.TurnSpeedDegreesPerSecond;
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        facingDegreesPerSecond = Quaternion.Angle(transform.rotation, targetRotation) / seconds;
    }

    /// <summary>회전 오버라이드를 해제하고 기본 규칙(이동 방향 / 관심 지점)으로 되돌린다.</summary>
    public void EndTimedFacing() => hasFacingOverride = false;

    public bool TryCreateRandomPatrolPoint(out Vector3 point)
    {
        point = controller.HomePosition;
        if (!IsServer || agent == null || !agent.enabled || !agent.isOnNavMesh) return false;

        float radius = controller.PatrolRadius;
        if (radius <= 0f) return false;

        // 스냅 거리는 배회 반경을 넘지 않는다. 2m 로 고정하면 반경이 그보다 좁은 무리 좀비(ZombieCrowd)의
        // 후보가 반경 밖으로 스냅돼 아래 반경 검사에서 계속 버려지고, 순찰점이 영영 안 잡혀 재시도만 돈다.
        float snapDistance = Mathf.Min(2f, radius);

        int attempts = Mathf.Max(1, controller.Data.PatrolSampleAttempts);
        for (int i = 0; i < attempts; i++)
        {
            Vector2 offset = Random.insideUnitCircle * radius;
            Vector3 candidate = controller.HomePosition + new Vector3(offset.x, 0f, offset.y);
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, snapDistance, agent.areaMask)) continue;

            Vector3 homeToPoint = hit.position - controller.HomePosition;
            homeToPoint.y = 0f;
            if (homeToPoint.sqrMagnitude > radius * radius) continue;

            NavMeshPath path = new NavMeshPath();
            if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) continue;

            point = hit.position;
            return true;
        }

        return false;
    }

    public void SetPatrolPoint(Vector3 point)
    {
        patrolPoint = point;
        hasPatrolPoint = true;
    }

    public void ClearPatrolPoint()
    {
        patrolPoint = default;
        hasPatrolPoint = false;
    }

    public void MoveToTarget()
    {
        if (controller.CurrentTarget != null) SetDestination(controller.CurrentTarget.position);
    }

    public void SetSpeed(float speed)
    {
        if (!IsServer || agent == null) return;
        agent.speed = speed;
    }

    /// <summary>
    /// 타겟까지 남은 거리로 회피 우선순위를 매 프레임 갱신한다(가까울수록 우선).
    /// 타겟이 없으면(추격 상실 구간) 마지막 값을 유지한다 — 어차피 상태를 벗어날 때 리셋된다.
    /// </summary>
    public void UpdateChaseAvoidancePriority()
    {
        if (!IsServer || agent == null || controller.CurrentTarget == null) return;

        float distance = Vector3.Distance(transform.position, controller.CurrentTarget.position);
        float t = Mathf.Clamp01(distance / chasePriorityMaxDistance);
        agent.avoidancePriority = Mathf.RoundToInt(Mathf.Lerp(chasePriorityClosest, chasePriorityFarthest, t));
    }

    /// <summary>회피 우선순위를 프리팹 기본값으로 되돌린다. 추격을 벗어날 때 반드시 호출한다.</summary>
    public void ResetAvoidancePriority()
    {
        if (!IsServer || agent == null) return;
        agent.avoidancePriority = defaultAvoidancePriority;
    }

    private void UpdateFacing(float deltaTime)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        Vector3 direction;
        float degreesPerSecond = controller.Data.TurnSpeedDegreesPerSecond;

        if (hasFacingOverride)
        {
            // 배회 대기처럼 "정해진 시간에 걸쳐 이쪽을 보게 하라"는 요구는 기본 선회 속도로는 표현할 수 없다.
            direction = facingTarget - transform.position;
            degreesPerSecond = facingDegreesPerSecond;
        }
        else if (!agent.isStopped)
        {
            direction = agent.desiredVelocity;
        }
        else
        {
            // 정지 중에도 관심 지점을 향해 돈다. 멈췄다고 회전을 막으면 시야 원뿔이 마지막 이동 방향에
            // 그대로 굳어, 경계·조사·타격 락처럼 제자리에 서는 구간에서는 바로 옆을 지나가도 영영 보지 못한다.
            Vector3 focus = controller.CurrentTarget != null
                ? controller.CurrentTarget.position
                : controller.LastKnownPosition;
            direction = focus - transform.position;
        }

        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        float angle = Quaternion.Angle(transform.rotation, targetRotation);
        if (angle <= controller.Data.TurnDeadZoneDegrees) return;

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            degreesPerSecond * deltaTime);
    }

    public float GetInvestigationSpeed()
    {
        float effectiveDb = controller.InvestigationStimulusDb;
        if (effectiveDb <= 0f) return controller.Data.InvestigateBaseSpeed;

        float speed = controller.Data.InvestigateBaseSpeed
            + Mathf.Max(0f, effectiveDb - controller.Data.HearMinDb) * controller.Data.InvestigateDbToSpeed;
        return Mathf.Min(speed, controller.Data.InvestigateCapSpeed);
    }

    public void NotifyPlayerCaught()
    {
        if (!IsServer || playerCaughtChannel == null || controller.CurrentTarget == null) return;

        playerCaughtChannel.RaiseEvent(new ZombiePlayerCaughtEvent(
            controller.NetworkObjectId,
            controller.CurrentTargetNetworkObjectId));
    }
}
