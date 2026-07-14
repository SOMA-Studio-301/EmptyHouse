using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(NetworkObject), typeof(NavMeshAgent))]
public class ZombieController : NetworkBehaviour
{
    [Header("Data")]
    [SerializeField] private ZombieDataSO zombieData;

    [Header("Composition")]
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private ZombieSensorySystem sensorySystem;
    [SerializeField] private ZombieStateMachine stateMachine;
    [SerializeField] private ZombieSquadSync squadSync;

    [Header("Scene Anchors")]
    [SerializeField] private Transform homeAnchor;
    [SerializeField] private Transform visionOrigin;
    [SerializeField] private Transform attackOrigin;

    [Header("Optional")]
    [SerializeField] private Animator animator;

    [Header("Instance Overrides")]
    [Tooltip("Negative values use ZombieDataSO.PatrolRadius. Set this to customize only this zombie.")]
    [SerializeField] private float patrolRadiusOverride = -1f;

    [Header("Editor Debug")]
    [SerializeField] private bool drawDebugGizmos = true;

    private readonly NetworkVariable<float> suspicion = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<ZombieStateKind> stateKind = new NetworkVariable<ZombieStateKind>(ZombieStateKind.Wander, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> suspicionLatched = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private float silenceTimer;
    private float investigateTimer;
    private float alertTimer;
    private float attackTimer;
    private float chaseLostTimer;
    private float investigationStimulusDb;
    private Vector3 lastKnownPosition;
    private Vector3 homePosition;
    private Transform currentTarget;
    private ulong currentTargetNetworkObjectId;
    private bool hasTrackingStimulus;
    private bool hadStimulusThisFrame;

    public ZombieDataSO Data => zombieData;
    public NavMeshAgent Agent => agent;
    public Animator Animator => animator;
    public Transform VisionOrigin => visionOrigin != null ? visionOrigin : transform;
    public Transform AttackOrigin => attackOrigin != null ? attackOrigin : transform;
    public Vector3 HomePosition => homePosition;
    public Vector3 LastKnownPosition => lastKnownPosition;
    public Transform CurrentTarget => currentTarget;
    public ulong CurrentTargetNetworkObjectId => currentTargetNetworkObjectId;
    public float SuspicionValue => suspicion.Value;
    public bool IsLatched => suspicionLatched.Value;
    public ZombieStateKind CurrentState => stateKind.Value;
    public bool IsFollower => squadSync != null && squadSync.IsFollower;
    public bool HasTrackingStimulus => hasTrackingStimulus;
    public bool HadStimulusThisFrame => hadStimulusThisFrame;
    public float InvestigationStimulusDb => investigationStimulusDb;
    public ZombieController SquadLeader => squadSync != null ? squadSync.Leader : null;
    public float PatrolRadius => patrolRadiusOverride >= 0f
        ? patrolRadiusOverride
        : zombieData != null ? zombieData.PatrolRadius : 0f;

    private void Awake()
    {
        CacheHomePosition();

        if (agent == null || sensorySystem == null || stateMachine == null || squadSync == null)
        {
            Debug.LogError($"[{nameof(ZombieController)}] {name} prefab composition is incomplete.", this);
            enabled = false;
        }
    }

    public override void OnNetworkSpawn()
    {
        CacheHomePosition();
        if (!IsServer) return;

        stateMachine.ServerInitialize();
    }

    private void Update()
    {
        if (!IsServer || zombieData == null || stateMachine == null) return;

        if (stateMachine.CurrentStateKind == ZombieStateKind.Attack)
        {
            hadStimulusThisFrame = false;
            hasTrackingStimulus = false;
            stateMachine.ServerTick(Time.deltaTime);
            return;
        }

        if (squadSync != null && squadSync.IsFollower)
        {
            squadSync.ServerSynchronizeFollower();
        }
        else
        {
            ZombiePerceptionFrame perception = sensorySystem.ServerCollectPerception();
            ServerApplyPerception(perception, Time.deltaTime);
        }

        stateMachine.ServerTick(Time.deltaTime);
        squadSync?.ServerReleaseFollowersIfSettled();
    }

    private void ServerApplyPerception(ZombiePerceptionFrame frame, float deltaTime)
    {
        if (!IsServer) return;

        hadStimulusThisFrame = frame.HasVisualStimulus || frame.HasAuditoryStimulus;
        hasTrackingStimulus = frame.HasTrackingStimulus;
        float nextSuspicion = suspicion.Value;

        // 명세 3-5 고정 순서: 즉시 발각 → 시각 가산 → 청각 pull-up → 냉각 → clamp → latch.
        if (frame.HasInstantVisualDetection)
        {
            nextSuspicion = 100f;
            AcceptTarget(frame.PreferredTarget, frame.PreferredTargetNetworkObjectId, frame.VisualPosition);
        }
        else
        {
            if (frame.VisualGainPerSecond > 0f)
            {
                nextSuspicion += frame.VisualGainPerSecond * deltaTime;
                AcceptTarget(frame.PreferredTarget, frame.PreferredTargetNetworkObjectId, frame.VisualPosition);
                investigationStimulusDb = 0f;
            }

            if (frame.HasAuditoryStimulus && CurrentState != ZombieStateKind.Chase)
            {
                float previous = nextSuspicion;
                float pullUp = frame.AuditoryEffectiveDb >= zombieData.HearDetectDb
                    ? 100f
                    : Mathf.Lerp(zombieData.HearFloor, 100f,
                        Mathf.InverseLerp(zombieData.HearMinDb, zombieData.HearDetectDb, frame.AuditoryEffectiveDb));

                nextSuspicion = Mathf.Max(nextSuspicion, pullUp);
                if (nextSuspicion > previous)
                {
                    lastKnownPosition = frame.AuditoryPosition;
                    investigationStimulusDb = frame.AuditoryEffectiveDb;
                }

                if (frame.AuditoryEffectiveDb >= zombieData.HearDetectDb && frame.PreferredTarget != null)
                {
                    AcceptTarget(frame.PreferredTarget, frame.PreferredTargetNetworkObjectId, frame.PreferredTarget.position);
                }
            }

            if (!hadStimulusThisFrame)
            {
                silenceTimer += deltaTime;
                if (!suspicionLatched.Value && silenceTimer >= zombieData.SuspicionGraceSeconds)
                {
                    nextSuspicion = Mathf.MoveTowards(nextSuspicion, 0f, zombieData.CoolRate * deltaTime);
                }
            }
            else
            {
                silenceTimer = 0f;
            }
        }

        nextSuspicion = Mathf.Clamp(nextSuspicion, 0f, 100f);

        // 청각 발각이어도 비위장 타겟이 없으면 E8에 따라 조사만 한다.
        bool canLatch = frame.HasInstantVisualDetection || frame.PreferredTarget != null;
        if (nextSuspicion >= 100f && canLatch)
        {
            suspicionLatched.Value = true;
            if (squadSync != null && !squadSync.IsFollower)
            {
                squadSync.ServerFormSquadSnapshot();
            }
        }
        else if (nextSuspicion >= 100f && !suspicionLatched.Value)
        {
            nextSuspicion = zombieData.ThInvestigate;
        }

        suspicion.Value = nextSuspicion;
    }

    private void AcceptTarget(Transform target, ulong targetNetworkObjectId, Vector3 position)
    {
        lastKnownPosition = position;
        if (target != null)
        {
            currentTarget = target;
            currentTargetNetworkObjectId = targetNetworkObjectId;
        }
    }

    public void ServerSetState(ZombieStateKind nextState)
    {
        if (!IsServer) return;
        stateKind.Value = nextState;
    }

    public void ServerDowngradeToInvestigation()
    {
        if (!IsServer) return;
        suspicionLatched.Value = false;
        suspicion.Value = Mathf.Clamp(zombieData.ThInvestigate, 0f, 100f);
        currentTarget = null;
        currentTargetNetworkObjectId = 0UL;
        hasTrackingStimulus = false;
        investigationStimulusDb = zombieData.HearMinDb;
        silenceTimer = 0f;
    }

    public void ServerCopyLeaderSnapshot(ZombieController leader)
    {
        if (!IsServer || leader == null) return;

        suspicion.Value = leader.SuspicionValue;
        suspicionLatched.Value = leader.IsLatched;

        // Attack is an individual, distance-gated action. Followers keep
        // chasing the shared target and enter Attack only when they themselves
        // reach AttackRange.
        bool leaderIsAttacking = leader.CurrentState == ZombieStateKind.Attack;
        stateKind.Value = leaderIsAttacking ? ZombieStateKind.Chase : leader.CurrentState;
        currentTarget = leader.CurrentTarget;
        currentTargetNetworkObjectId = leader.CurrentTargetNetworkObjectId;
        lastKnownPosition = leader.LastKnownPosition;
        investigationStimulusDb = leader.InvestigationStimulusDb;
        hasTrackingStimulus = leaderIsAttacking
            ? leader.CurrentTarget != null
            : leader.HasTrackingStimulus;
        hadStimulusThisFrame = leaderIsAttacking
            ? leader.CurrentTarget != null
            : leader.HadStimulusThisFrame;
    }

    public void BeginInvestigateTimer() => investigateTimer = 0f;
    public void AdvanceInvestigateTimer(float deltaTime) => investigateTimer += deltaTime;
    public float InvestigateTimer => investigateTimer;
    public void BeginAlertTimer() => alertTimer = 0f;
    public void AdvanceAlertTimer(float deltaTime) => alertTimer += deltaTime;
    public float AlertTimer => alertTimer;
    public void BeginAttackTimer() => attackTimer = 0f;
    public void AdvanceAttackTimer(float deltaTime) => attackTimer += deltaTime;
    public float AttackTimer => attackTimer;
    public void BeginChaseLostTimer() => chaseLostTimer = 0f;
    public void AdvanceChaseLostTimer(float deltaTime) => chaseLostTimer += deltaTime;
    public float ChaseLostTimer => chaseLostTimer;
    public void ResetChaseLostTimer() => chaseLostTimer = 0f;

    private void CacheHomePosition()
    {
        homePosition = homeAnchor != null ? homeAnchor.position : transform.position;
        lastKnownPosition = homePosition;
    }

    private void OnDrawGizmos()
    {
        if (!drawDebugGizmos) return;

        Vector3 home = Application.isPlaying
            ? homePosition
            : homeAnchor != null ? homeAnchor.position : transform.position;
        Vector3 visionPos = visionOrigin != null ? visionOrigin.position : transform.position;
        Quaternion visionRot = visionOrigin != null ? visionOrigin.rotation : transform.rotation;
        Vector3 attackPos = attackOrigin != null ? attackOrigin.position : transform.position;

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
        Gizmos.DrawWireSphere(home, 0.2f);
        Gizmos.DrawLine(transform.position, home);

        if (zombieData == null) return;

        Gizmos.color = new Color(0.2f, 0.55f, 1f, 0.65f);
        Gizmos.DrawWireSphere(home, PatrolRadius);

        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(attackPos, Mathf.Max(0.1f, zombieData.AttackRange));
        DrawVisionGizmo(visionPos, visionRot, zombieData.VisionAngle, zombieData.VisionDistance);

        if (Application.isPlaying)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, lastKnownPosition);
            Gizmos.DrawWireSphere(lastKnownPosition, 0.25f);

            if (currentTarget != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(visionPos, currentTarget.position);
            }

            if (agent != null && agent.isOnNavMesh && agent.hasPath)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(transform.position, agent.destination);
                Gizmos.DrawWireSphere(agent.destination, 0.2f);
            }

            if (stateMachine != null && stateMachine.HasPatrolPoint)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(home, stateMachine.PatrolPoint);
                Gizmos.DrawWireSphere(stateMachine.PatrolPoint, 0.3f);
            }

#if UNITY_EDITOR
            string leaderName = SquadLeader != null ? SquadLeader.name : "None";
            Handles.Label(visionPos + Vector3.up * 0.5f,
                $"{stateKind.Value} | Suspicion {suspicion.Value:0.0}\n" +
                $"Latched {suspicionLatched.Value} | Signal {hasTrackingStimulus}\n" +
                $"Target {(currentTarget != null ? currentTarget.name : "None")} | Leader {leaderName}");
#endif
        }
    }

    private static void DrawVisionGizmo(Vector3 origin, Quaternion rotation, float angle, float distance)
    {
        Gizmos.color = new Color(0f, 1f, 0.35f, 0.85f);
        const int segments = 24;
        float halfAngle = angle * 0.5f;
        Vector3 previousPoint = origin + rotation * Quaternion.Euler(0f, -halfAngle, 0f) * Vector3.forward * distance;

        for (int i = 1; i <= segments; i++)
        {
            float currentAngle = Mathf.Lerp(-halfAngle, halfAngle, (float)i / segments);
            Vector3 currentPoint = origin + rotation * Quaternion.Euler(0f, currentAngle, 0f) * Vector3.forward * distance;
            Gizmos.DrawLine(previousPoint, currentPoint);
            Gizmos.DrawLine(origin, currentPoint);
            previousPoint = currentPoint;
        }
    }
}
