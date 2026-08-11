using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(ZombieController), typeof(NavMeshAgent))]
public class ZombieStateMachine : NetworkBehaviour
{
    [SerializeField] private ZombieController controller;
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private ZombiePlayerCaughtEventChannelSO playerCaughtChannel;

    private IZombieState currentState;
    private Vector3 lastRequestedDestination;
    private bool hasRequestedDestination;
    private Vector3 patrolPoint;
    private bool hasPatrolPoint;

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

    private void UpdateFacing(float deltaTime)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        Vector3 direction;
        if (!agent.isStopped)
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
            controller.Data.TurnSpeedDegreesPerSecond * deltaTime);
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
