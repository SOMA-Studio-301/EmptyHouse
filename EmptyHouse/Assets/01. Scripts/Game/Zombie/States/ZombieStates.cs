using UnityEngine;

/// <summary>
/// 홈과 순찰점 사이를 오가며, 도착할 때마다 잠시 서 있는다.
/// 홈 ─이동→ 순찰점 ─대기→ 홈 ─대기→ 순찰점 … 순으로 돈다.
///
/// 대기 시간은 도착마다 <see cref="ZombieDataSO.PatrolPauseMinSeconds"/>~Max 에서 새로 뽑는다.
/// 고정값을 쓰면 같은 순간에 스폰된 무리가 한 박자로 서고 걸어 기계처럼 보인다.
///
/// 다음 목적지는 대기가 끝날 때가 아니라 <b>시작할 때</b> 정한다. 그래야 서 있는 동안 그쪽으로
/// 몸을 돌릴 수 있고, 회전 속도를 대기 시간에 맞춰 역산해 출발 직전에 정확히 정면을 잡는다
/// (<see cref="ZombieStateMachine.BeginTimedFacing"/>).
/// </summary>
public class ZombieWanderState : IZombieState
{
    private enum PatrolPhase
    {
        MovingHome,     // 홈으로 이동 중
        MovingToPatrol, // 순찰점으로 이동 중
        Pausing         // 도착해서 서 있는 중
    }

    private const float patrolRetrySeconds = 0.5f; // 순찰점 생성 실패 시 재시도 간격(초)

    private PatrolPhase phase;
    private PatrolPhase phaseAfterPause; // 대기가 끝나면 넘어갈 단계
    private Vector3 patrolPoint;
    private bool hasPatrolPoint;
    private float pauseTimer;

    public ZombieStateKind Kind => ZombieStateKind.Wander;

    /// <summary>배회 속도로 맞추고 홈 복귀부터 시작한다.</summary>
    /// <param name="machine">소속 상태 머신.</param>
    public void Enter(ZombieStateMachine machine)
    {
        machine.SetSpeed(machine.Controller.Data.WanderSpeed);
        phase = PatrolPhase.MovingHome;
        hasPatrolPoint = false;
        pauseTimer = 0f;
        machine.ClearPatrolPoint();
        machine.EndTimedFacing();
        machine.MoveToHome();
    }

    /// <summary>이동·대기 단계를 진행한다.</summary>
    /// <param name="machine">소속 상태 머신.</param>
    /// <param name="deltaTime">경과 시간(초).</param>
    public void Tick(ZombieStateMachine machine, float deltaTime)
    {
        // 배회 OFF: 순찰을 버리고 홈에 제자리 대기한다. 플레이 중 토글 변경도 즉시 반영된다.
        if (!machine.Controller.WanderEnabled)
        {
            if (hasPatrolPoint)
            {
                hasPatrolPoint = false;
                machine.ClearPatrolPoint();
            }
            phase = PatrolPhase.MovingHome;
            machine.EndTimedFacing();

            if (!machine.IsAtDestination(0.25f))
            {
                machine.MoveToHome();
                return;
            }

            machine.StopAgent();
            return;
        }

        switch (phase)
        {
            case PatrolPhase.Pausing:
                TickPause(machine, deltaTime);
                return;

            case PatrolPhase.MovingToPatrol:
                if (!machine.IsAtDestination(0.25f))
                {
                    machine.SetDestination(patrolPoint);
                    return;
                }

                machine.StopAgent();
                BeginPause(machine, PatrolPhase.MovingHome, machine.Controller.HomePosition);
                return;

            default:
                if (!machine.IsAtDestination(0.25f))
                {
                    machine.MoveToHome();
                    return;
                }

                machine.StopAgent();

                // 순찰점은 귀가 중에도 들고 있다가 홈에 실제로 닿았을 때만 버린다.
                if (hasPatrolPoint)
                {
                    hasPatrolPoint = false;
                    machine.ClearPatrolPoint();
                }

                // 다음 순찰점을 지금 잡는다. 대기가 끝난 뒤에 잡으면 서 있는 동안 어디를 볼지 알 수 없다.
                if (!machine.TryCreateRandomPatrolPoint(out patrolPoint))
                {
                    // 잡히지 않으면 방향도 정할 수 없다. 짧게 쉬고 이 분기로 돌아와 다시 시도한다.
                    machine.EndTimedFacing();
                    phase = PatrolPhase.Pausing;
                    phaseAfterPause = PatrolPhase.MovingHome;
                    pauseTimer = patrolRetrySeconds;
                    return;
                }

                hasPatrolPoint = true;
                machine.SetPatrolPoint(patrolPoint);
                BeginPause(machine, PatrolPhase.MovingToPatrol, patrolPoint);
                return;
        }
    }

    /// <summary>배회를 벗어날 때 순찰점 표시와 회전 오버라이드를 함께 거둔다.</summary>
    /// <param name="machine">소속 상태 머신.</param>
    public void Exit(ZombieStateMachine machine)
    {
        machine.ClearPatrolPoint();

        // 이걸 빠뜨리면 추격·조사로 넘어간 좀비가 배회 때 잡아둔 지점을 계속 바라본다.
        machine.EndTimedFacing();
    }

    /// <summary>대기 타이머를 깎고, 만료되면 미리 정해 둔 다음 목적지로 출발한다.</summary>
    /// <param name="machine">소속 상태 머신.</param>
    /// <param name="deltaTime">경과 시간(초).</param>
    private void TickPause(ZombieStateMachine machine, float deltaTime)
    {
        // 대기 중 밀려나면 에이전트가 다시 움직이려 든다. 매 프레임 붙잡아 둔다.
        machine.StopAgent();

        pauseTimer -= deltaTime;
        if (pauseTimer > 0f) return;

        machine.EndTimedFacing();

        if (phaseAfterPause == PatrolPhase.MovingToPatrol)
        {
            phase = PatrolPhase.MovingToPatrol;
            machine.SetDestination(patrolPoint);
            return;
        }

        phase = PatrolPhase.MovingHome;
        machine.MoveToHome();
    }

    /// <summary>
    /// 도착 지점에서 대기를 시작한다. 대기 시간은 매번 새로 뽑고, 그 시간에 딱 맞춰
    /// 다음 목적지 쪽으로 도는 회전을 건다 — 출발할 때는 이미 정면을 보고 있게 된다.
    /// </summary>
    /// <param name="machine">소속 상태 머신.</param>
    /// <param name="next">대기가 끝나면 넘어갈 단계.</param>
    /// <param name="facingTarget">대기 중 바라볼 다음 목적지.</param>
    private void BeginPause(ZombieStateMachine machine, PatrolPhase next, Vector3 facingTarget)
    {
        ZombieDataSO data = machine.Controller.Data;

        phase = PatrolPhase.Pausing;
        phaseAfterPause = next;
        pauseTimer = Random.Range(data.PatrolPauseMinSeconds, data.PatrolPauseMaxSeconds);
        machine.BeginTimedFacing(facingTarget, pauseTimer);
    }
}

public class ZombieAlertState : IZombieState
{
    public ZombieStateKind Kind => ZombieStateKind.Alert;
    public void Enter(ZombieStateMachine machine) => machine.StopAgent();
    public void Tick(ZombieStateMachine machine, float deltaTime) { }
    public void Exit(ZombieStateMachine machine) { }
}

public class ZombieInvestigateState : IZombieState
{
    public ZombieStateKind Kind => ZombieStateKind.Investigate;

    public void Enter(ZombieStateMachine machine)
    {
        machine.SetSpeed(machine.GetInvestigationSpeed());
        machine.MoveToInvestigationPoint();
        machine.Controller.BeginInvestigateTimer();
    }

    public void Tick(ZombieStateMachine machine, float deltaTime)
    {
        ZombieController controller = machine.Controller;
        machine.SetSpeed(machine.GetInvestigationSpeed());

        if (controller.HadStimulusThisFrame)
        {
            controller.BeginInvestigateTimer();
            machine.MoveToInvestigationPoint();
            return;
        }

        if (!machine.IsAtDestination(1.5f))
        {
            controller.BeginInvestigateTimer();
            machine.MoveToInvestigationPoint();
            return;
        }

        machine.StopAgent();
        controller.AdvanceInvestigateTimer(deltaTime);
        if (controller.InvestigateTimer >= controller.Data.InvestigateToWanderSeconds)
        {
            machine.SwitchState(ZombieStateKind.Subside);
        }
    }

    public void Exit(ZombieStateMachine machine) { }
}

public class ZombieRoarTransitionState : IZombieState
{
    public ZombieStateKind Kind => ZombieStateKind.RoarTransition;

    public void Enter(ZombieStateMachine machine)
    {
        machine.StopAgent();
        machine.Controller.BeginAlertTimer();
    }

    public void Tick(ZombieStateMachine machine, float deltaTime)
    {
        ZombieController controller = machine.Controller;
        controller.AdvanceAlertTimer(deltaTime);
        if (controller.AlertTimer >= controller.Data.AlertMotionSeconds)
        {
            machine.SwitchState(ZombieStateKind.Chase);
        }
    }

    public void Exit(ZombieStateMachine machine) { }
}

public class ZombieChaseState : IZombieState
{
    public ZombieStateKind Kind => ZombieStateKind.Chase;

    public void Enter(ZombieStateMachine machine)
    {
        machine.SetSpeed(machine.Controller.Data.ChaseSpeed);
        machine.Controller.ResetChaseLostTimer();
    }

    public void Tick(ZombieStateMachine machine, float deltaTime)
    {
        ZombieController controller = machine.Controller;

        // 사거리 판정이 지각 판정보다 먼저다. 상실 분기 안에 두면 대상이 시야 원뿔 밖으로 돌아
        // 들어왔을 때(뒤·옆에 붙었을 때) 몸이 닿아 있는데도 ChaseToInvestigateSeconds 동안 때리지 않는다.
        // 유효성은 ServerValidateTarget 이 매 프레임 보장하므로, 여기서 잡히는 대상은 살아 있고 위장도 아니다.
        if (controller.CurrentTarget != null
            && Vector3.Distance(controller.AttackOrigin.position, controller.CurrentTarget.position) <= controller.Data.AttackRange)
        {
            machine.SwitchState(ZombieStateKind.Attack);
            return;
        }

        // 추격 상실 판정은 "타겟 본인을 지각했는가"(HasTargetStimulus)로 한다.
        // "아무 자극"(HasTrackingStimulus)으로 판정하면 다른 플레이어의 소음이 타이머를
        // 매 프레임 리셋해 좀비가 Chase 에서 영영 내려오지 못한다.
        if (!controller.HasTargetStimulus)
        {
            machine.MoveToInvestigationPoint();
            controller.AdvanceChaseLostTimer(deltaTime);
            if (controller.ChaseLostTimer >= controller.Data.ChaseToInvestigateSeconds)
            {
                controller.ServerDowngradeToInvestigation();
                machine.SwitchState(ZombieStateKind.Investigate);
            }
            return;
        }

        controller.ResetChaseLostTimer();
        machine.SetSpeed(controller.Data.ChaseSpeed);
        machine.MoveToTarget();
    }

    public void Exit(ZombieStateMachine machine) { }
}

public class ZombieAttackState : IZombieState
{
    public ZombieStateKind Kind => ZombieStateKind.Attack;
    private bool caught;

    public void Enter(ZombieStateMachine machine)
    {
        machine.StopAgent();
        machine.Controller.BeginAttackTimer();
        caught = false;
    }

    public void Tick(ZombieStateMachine machine, float deltaTime)
    {
        ZombieController controller = machine.Controller;
        controller.AdvanceAttackTimer(deltaTime);

        if (!caught)
        {
            // 타격 전에 대상을 잃으면(사망·위장·디스폰) 조사로 내려간다.
            if (controller.CurrentTarget == null)
            {
                machine.SwitchState(ZombieStateKind.Investigate);
                return;
            }

            if (Vector3.Distance(controller.AttackOrigin.position, controller.CurrentTarget.position) > controller.Data.AttackRange)
            {
                machine.SwitchState(ZombieStateKind.Chase);
                return;
            }

            if (controller.AttackTimer >= controller.Data.AttackWindupSeconds)
            {
                caught = true;
                machine.NotifyPlayerCaught();

                // 타격이 확정되면 대상은 사망한다. 시신을 계속 물고 있으면 추격 상실 타이머가
                // 리셋되고 다음 목표로 전이하지 못하므로, 여기서 즉시 타겟을 놓는다.
                controller.ServerReleaseTarget();
            }
            return;
        }

        // 타격 락은 연출 고정 구간이라 타겟이 사라져도 끝까지 채운다.
        if (controller.AttackTimer < controller.Data.AttackLockSeconds) return;

        machine.SwitchState(controller.CurrentTarget != null
            ? ZombieStateKind.Chase
            : ZombieStateKind.Investigate);
    }

    public void Exit(ZombieStateMachine machine) { }
}

public class ZombieSubsideState : IZombieState
{
    public ZombieStateKind Kind => ZombieStateKind.Subside;

    public void Enter(ZombieStateMachine machine)
    {
        machine.SetSpeed(machine.Controller.Data.SubsideSpeed);
        machine.MoveToHome();
    }

    public void Tick(ZombieStateMachine machine, float deltaTime)
    {
        ZombieController controller = machine.Controller;
        machine.SetSpeed(controller.Data.SubsideSpeed);
        machine.MoveToHome();

        if (machine.IsAtDestination(1f) && controller.SuspicionValue < controller.Data.ThAlert)
        {
            machine.SwitchState(ZombieStateKind.Wander);
        }
    }

    public void Exit(ZombieStateMachine machine) { }
}
