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
    [Tooltip("끄면 홈 앵커에 제자리 대기한다. 지각·조사·추격·무리 동조는 정상 동작.")]
    [SerializeField] private bool wanderEnabled = true; // 배회 여부 (군중 좀비는 OFF)

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
    private IZombiePerceptionSource currentTargetSource;
    private Transform currentTarget;
    private ulong currentTargetNetworkObjectId;
    private bool hasTrackingStimulus;
    private bool hasTargetStimulus;
    private bool hadStimulusThisFrame;

    public ZombieDataSO Data => zombieData;
    public NavMeshAgent Agent => agent;
    public Animator Animator => animator;
    public Transform VisionOrigin => visionOrigin != null ? visionOrigin : transform;
    public Transform AttackOrigin => attackOrigin != null ? attackOrigin : transform;
    public Vector3 HomePosition => homePosition;
    public Vector3 LastKnownPosition => lastKnownPosition;
    public Transform CurrentTarget => currentTarget;
    public IZombiePerceptionSource CurrentTargetSource => currentTargetSource;
    public ulong CurrentTargetNetworkObjectId => currentTargetNetworkObjectId;
    public float SuspicionValue => suspicion.Value;
    public bool IsLatched => suspicionLatched.Value;
    public ZombieStateKind CurrentState => stateKind.Value;
    public bool IsFollower => squadSync != null && squadSync.IsFollower;
    public bool HasTrackingStimulus => hasTrackingStimulus;

    /// <summary>
    /// 이번 프레임에 <see cref="CurrentTarget"/> 본인을 지각했는가. 추격 유지 판정은 이 값으로 한다 —
    /// "아무 자극"(<see cref="HasTrackingStimulus"/>)으로 판정하면 다른 플레이어의 소음이
    /// 추격 상실 타이머를 영구히 리셋해 좀비가 Chase 에서 영영 못 내려온다.
    /// </summary>
    public bool HasTargetStimulus => hasTargetStimulus;
    public bool HadStimulusThisFrame => hadStimulusThisFrame;
    public float InvestigationStimulusDb => investigationStimulusDb;
    public ZombieController SquadLeader => squadSync != null ? squadSync.Leader : null;
    public float PatrolRadius => patrolRadiusOverride >= 0f
        ? patrolRadiusOverride
        : zombieData != null ? zombieData.PatrolRadius : 0f;
    public bool WanderEnabled => wanderEnabled; // 배회 여부. OFF면 배회 상태에서 홈 대기

    /// <summary>복제된 상태가 바뀔 때 발행된다. 서버·클라이언트 모두에서 발행되므로 애니메이션 구동에 쓴다.</summary>
    public event System.Action<ZombieStateKind> StateChanged;

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

        // 상태는 서버가 쓰고 전 클라이언트에 복제된다. 애니메이션은 이 복제 상태로 구동하므로 서버 게이트 앞에서 구독한다.
        stateKind.OnValueChanged += HandleStateKindChanged;

        if (!IsServer) return;

        stateMachine.ServerInitialize();
    }

    /// <summary>복제 상태 구독을 해제한다.</summary>
    public override void OnNetworkDespawn()
    {
        stateKind.OnValueChanged -= HandleStateKindChanged;
    }

    /// <summary>복제된 상태 변화를 받아 StateChanged 를 발행한다.</summary>
    /// <param name="previous">이전 상태.</param>
    /// <param name="current">새 상태.</param>
    private void HandleStateKindChanged(ZombieStateKind previous, ZombieStateKind current)
    {
        StateChanged?.Invoke(current);
    }

    private void Update()
    {
        if (!IsServer || zombieData == null || stateMachine == null) return;

        float deltaTime = Time.deltaTime;

        // 지각·상태 갱신보다 타겟 유효성이 먼저다. 사망(관전)·위장·파괴된 대상을 계속 물고 있으면
        // Attack 의 탈출 조건(CurrentTarget == null)이 성립하지 않고 추격 상실 타이머도 리셋돼
        // 좀비가 시신에 영구 고착된다.
        ServerValidateTarget();

        if (stateMachine.CurrentStateKind == ZombieStateKind.Attack)
        {
            // 타격 락 동안은 지각을 멈춘다(연출 고정). 자극 플래그도 함께 내려 잔상이 남지 않게 한다.
            hadStimulusThisFrame = false;
            hasTrackingStimulus = false;
            hasTargetStimulus = false;
            stateMachine.ServerTick(deltaTime);
            squadSync?.ServerReleaseFollowersIfSettled(deltaTime);
            return;
        }

        if (squadSync != null && squadSync.IsFollower)
        {
            squadSync.ServerSynchronizeFollower();
        }
        else
        {
            ZombiePerceptionFrame perception = sensorySystem.ServerCollectPerception();
            ServerApplyPerception(perception, deltaTime);
        }

        stateMachine.ServerTick(deltaTime);
        squadSync?.ServerReleaseFollowersIfSettled(deltaTime);
    }

    private void ServerApplyPerception(ZombiePerceptionFrame frame, float deltaTime)
    {
        if (!IsServer) return;

        hadStimulusThisFrame = frame.HasVisualStimulus || frame.HasAuditoryStimulus;
        hasTrackingStimulus = frame.HasTrackingStimulus;
        float nextSuspicion = suspicion.Value;

        // 추격 중에는 소리에 타겟을 뺏기지 않는다. 단 물고 있던 타겟이 이미 해제된 뒤라면
        // (사망·위장으로 ServerReleaseTarget 이 돈 경우) 청각으로 새 대상을 잡을 수 있어야 한다 —
        // 이 예외가 없으면 Chase 상태에 갇힌 채 아무도 새로 타겟팅하지 못한다.
        bool chaseHoldsValidTarget = CurrentState == ZombieStateKind.Chase && currentTargetSource != null;

        // 명세 3-5 고정 순서: 즉시 발각 → 시각 가산 → 청각 pull-up → 냉각 → clamp → latch.
        if (frame.HasInstantVisualDetection)
        {
            nextSuspicion = 100f;
            AcceptTarget(frame.PreferredTarget, frame.VisualPosition);
        }
        else
        {
            if (frame.VisualGainPerSecond > 0f)
            {
                nextSuspicion += frame.VisualGainPerSecond * deltaTime;
                AcceptTarget(frame.PreferredTarget, frame.VisualPosition);
                investigationStimulusDb = 0f;
            }

            if (frame.HasAuditoryStimulus && !chaseHoldsValidTarget)
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

                if (frame.AuditoryEffectiveDb >= zombieData.HearDetectDb
                    && frame.PreferredTarget != null
                    && frame.PreferredTarget.Root != null)
                {
                    AcceptTarget(frame.PreferredTarget, frame.PreferredTarget.Root.position);
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

        // 발각(latch)은 대상을 "지각"한 게 아니라 "확보"했을 때만 성립한다 —
        // 불변식: IsLatched ⇒ CurrentTarget != null.
        // 확보는 즉시 발각·시야 가산·HearDetectDb 이상 청각에서만 일어나는데(AcceptTarget),
        // 지각만으로 latch 를 세우면 IsLatched 는 true 인데 CurrentTarget 은 null 인 상태가 된다.
        // 그러면 RoarTransition 조건(IsLatched && CurrentTarget != null)이 영원히 성립하지 않고,
        // latch 가 냉각까지 막아 의심도가 100 에 고정돼 좀비가 조사 상태로 대상에게 붙어 서성이기만 한다.
        bool holdsTarget = currentTargetSource != null;
        if (nextSuspicion >= 100f && holdsTarget)
        {
            suspicionLatched.Value = true;
            if (squadSync != null && !squadSync.IsFollower)
            {
                squadSync.ServerFormSquadSnapshot();
            }
        }
        else if (nextSuspicion >= 100f)
        {
            // 타겟 없는 100 은 유지하지 않는다. 조사 단계로 눌러 두고 재확보를 기다린다(좀비AI E8).
            suspicionLatched.Value = false;
            nextSuspicion = zombieData.ThInvestigate;
        }

        suspicion.Value = nextSuspicion;

        // 추격 유지 판정. 이 프레임에 지각한 대상이 지금 물고 있는 타겟 본인일 때만 참이다.
        hasTargetStimulus = currentTargetSource != null
            && ReferenceEquals(frame.PreferredTarget, currentTargetSource);
    }

    private void AcceptTarget(IZombiePerceptionSource target, Vector3 position)
    {
        lastKnownPosition = position;
        if (target == null || target.Root == null) return;

        currentTargetSource = target;
        currentTarget = target.Root;
        currentTargetNetworkObjectId = target.NetworkObjectId;
    }

    /// <summary>
    /// 물고 있는 타겟이 아직 유효한지 검사한다. Transform 은 Unity 의 == 오버로드로 파괴까지 걸러지므로
    /// 인터페이스 참조보다 먼저 확인한다 — 파괴된 컴포넌트의 프로퍼티를 읽으면 예외가 난다.
    /// </summary>
    private static bool IsTargetable(IZombiePerceptionSource source, Transform root)
    {
        if (source == null || root == null) return false;
        return !source.IsSpectator && !source.IsDisguised;
    }

    /// <summary>매 서버 프레임 타겟 유효성을 검증하고, 사망(관전)·위장·파괴된 대상이면 즉시 놓는다.</summary>
    private void ServerValidateTarget()
    {
        if (currentTargetSource == null && currentTarget == null) return;
        if (IsTargetable(currentTargetSource, currentTarget)) return;

        ServerReleaseTarget();
    }

    /// <summary>
    /// 타겟을 놓는다. latch 도 함께 푼다 — latch 가 선 채로 남으면 의심도 냉각이 막혀
    /// (<see cref="ServerApplyPerception"/> 의 냉각 분기) 좀비가 영영 평상시로 돌아오지 못한다.
    /// </summary>
    public void ServerReleaseTarget()
    {
        if (!IsServer) return;

        currentTargetSource = null;
        currentTarget = null;
        currentTargetNetworkObjectId = 0UL;
        hasTargetStimulus = false;
        suspicionLatched.Value = false;
    }

    public void ServerSetState(ZombieStateKind nextState)
    {
        if (!IsServer) return;
        stateKind.Value = nextState;
    }

    /// <summary>배회 여부를 설정한다. 서버 AI만 읽는 설정 값이라 씬 로드 시(군중 구성)에도 쓸 수 있다.</summary>
    /// <param name="enabled">배회 허용 여부.</param>
    public void SetWanderEnabled(bool enabled)
    {
        wanderEnabled = enabled;
    }

    /// <summary>
    /// 절차 스폰 직후 서버가 배회 원점·반경을 주입한다(MapStateObjectSpawner 전용 경로 — Spawn() 이후 호출).
    /// 수동 배치 좀비는 homeAnchor/인스펙터 값 그대로 동작한다. AI 가 서버 전용이라 복제는 불필요.
    /// </summary>
    /// <param name="home">배회 기준 원점(스폰 마커 위치).</param>
    /// <param name="patrolRadiusMeters">배회 반경(m). 음수면 기존 값(ZombieDataSO.PatrolRadius) 유지.</param>
    public void ServerConfigureSpawn(Vector3 home, float patrolRadiusMeters)
    {
        homePosition = home;
        lastKnownPosition = home;
        if (patrolRadiusMeters >= 0f)
        {
            patrolRadiusOverride = patrolRadiusMeters;
        }
    }

    public void ServerDowngradeToInvestigation()
    {
        if (!IsServer) return;
        ServerReleaseTarget();
        suspicion.Value = Mathf.Clamp(zombieData.ThInvestigate, 0f, 100f);
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
        currentTargetSource = leader.CurrentTargetSource;
        currentTarget = leader.CurrentTarget;
        currentTargetNetworkObjectId = leader.CurrentTargetNetworkObjectId;
        lastKnownPosition = leader.LastKnownPosition;
        investigationStimulusDb = leader.InvestigationStimulusDb;

        // 공유 타겟이 이미 죽었으면 자극이 있다고 속이지 않는다 — 그래야 팔로워도
        // 추격 상실 타이머를 굴려 조사 단계로 내려올 수 있다.
        bool sharedTargetAlive = IsTargetable(currentTargetSource, currentTarget);
        hasTrackingStimulus = leaderIsAttacking ? sharedTargetAlive : leader.HasTrackingStimulus;
        hadStimulusThisFrame = leaderIsAttacking ? sharedTargetAlive : leader.HadStimulusThisFrame;
        hasTargetStimulus = leaderIsAttacking ? sharedTargetAlive : leader.HasTargetStimulus;
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
                $"Latched {suspicionLatched.Value} | Signal {hasTrackingStimulus} | OnTarget {hasTargetStimulus}\n" +
                $"Target {(currentTarget != null ? currentTarget.name : "None")} | Lost {chaseLostTimer:0.0}s | Leader {leaderName}");
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
