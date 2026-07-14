using UnityEngine;

public class ZombieWanderState : IZombieState
{
    private enum PatrolPhase
    {
        ReturningHome,
        MovingToPatrol
    }

    private PatrolPhase phase;
    private Vector3 patrolPoint;
    private bool hasPatrolPoint;
    private float retryTimer;

    public ZombieStateKind Kind => ZombieStateKind.Wander;

    public void Enter(ZombieStateMachine machine)
    {
        machine.SetSpeed(machine.Controller.Data.WanderSpeed);
        phase = PatrolPhase.ReturningHome;
        hasPatrolPoint = false;
        retryTimer = 0f;
        machine.ClearPatrolPoint();
        machine.MoveToHome();
    }

    public void Tick(ZombieStateMachine machine, float deltaTime)
    {
        if (phase == PatrolPhase.MovingToPatrol)
        {
            if (!machine.IsAtDestination(0.25f))
            {
                machine.SetDestination(patrolPoint);
                return;
            }

            machine.StopAgent();
            phase = PatrolPhase.ReturningHome;
            machine.MoveToHome();
            return;
        }

        if (!machine.IsAtDestination(0.25f))
        {
            machine.MoveToHome();
            return;
        }

        machine.StopAgent();

        // Keep the current point during the return trip. Replace it only once
        // the zombie has actually reached its home anchor.
        if (hasPatrolPoint)
        {
            hasPatrolPoint = false;
            machine.ClearPatrolPoint();
        }

        retryTimer -= deltaTime;
        if (retryTimer > 0f) return;

        if (!machine.TryCreateRandomPatrolPoint(out patrolPoint))
        {
            retryTimer = 0.5f;
            return;
        }

        hasPatrolPoint = true;
        machine.SetPatrolPoint(patrolPoint);
        phase = PatrolPhase.MovingToPatrol;
        machine.SetDestination(patrolPoint);
    }

    public void Exit(ZombieStateMachine machine) => machine.ClearPatrolPoint();
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

        if (!controller.HasTrackingStimulus)
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

        if (controller.CurrentTarget != null
            && Vector3.Distance(controller.AttackOrigin.position, controller.CurrentTarget.position) <= controller.Data.AttackRange)
        {
            machine.SwitchState(ZombieStateKind.Attack);
        }
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

        if (controller.CurrentTarget == null)
        {
            machine.SwitchState(ZombieStateKind.Investigate);
            return;
        }

        float distance = Vector3.Distance(controller.AttackOrigin.position, controller.CurrentTarget.position);
        if (!caught && distance > controller.Data.AttackRange)
        {
            machine.SwitchState(ZombieStateKind.Chase);
            return;
        }

        if (!caught && controller.AttackTimer >= controller.Data.AttackWindupSeconds)
        {
            caught = true;
            machine.NotifyPlayerCaught();
        }

        if (caught && controller.AttackTimer >= controller.Data.AttackLockSeconds)
        {
            machine.SwitchState(ZombieStateKind.Chase);
        }
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
