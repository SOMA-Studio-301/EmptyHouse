using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 좀비 애니메이터를 구동한다. 포효·공격은 복제된 상태로 고르고, 보행·대기는 실제 이동량으로 가른다.
///
/// 클립을 상태만으로 고르면 안 된다 — Wander·Investigate·Chase·Subside 는 모두 목적지 도착 후
/// StopAgent() 로 제자리에 서는 구간을 갖는다(조사는 InvestigateToWanderSeconds 동안, 배회 OFF 인
/// 군중 좀비는 영구). 그 구간에서도 상태는 그대로라, 가만히 선 좀비가 걷기 클립을 제자리에서 돌린다.
/// 반대로 포효·공격은 이동이 없는 연출이라 이동량으로 고를 수 없다 — 그래서 둘을 나눠서 판정한다.
///
/// 이동량은 transform 위치 델타로 잰다. NavMeshAgent 는 서버에서만 경로를 가져 클라의
/// agent.velocity 는 0 이지만, 위치는 NetworkTransform 이 보간해 전 클라에 복제하므로
/// 각 인스턴스가 자기 위치 변화만 보면 서버·클라가 같은 판정을 낸다(PlayerFootstepSfx 와 같은 방식).
///
/// 상태는 서버가 쓰고 전 클라이언트에 복제되므로, 이 컴포넌트는 모든 인스턴스에서 동작한다(NetworkAnimator 불필요).
/// </summary>
public class ZombieAnimator : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private ZombieController controller;
    [SerializeField] private Animator animator;

    [Header("Locomotion")]
    [Tooltip("이 속력(m/s) 이상이면 보행. 가장 느린 이동 속도(WanderSpeed 1.2) 한참 아래로 둔다.")]
    [SerializeField] private float walkSpeedThreshold = 0.15f;
    [Tooltip("이 속력(m/s) 이하면 대기. 두 문턱 사이는 직전 판정을 유지해 경계에서 깜빡이지 않는다.")]
    [SerializeField] private float idleSpeedThreshold = 0.05f;
    [Tooltip("속력 평활 시간 상수(초). 네트워크 보간 지터와 제자리 회전 중의 미세 이동을 흡수한다. 클수록 멈춘 뒤 보행 클립이 오래 남는다.")]
    [SerializeField] private float speedSmoothingSeconds = 0.1f;
    [Tooltip("이 속력(m/s)을 넘는 표본은 순간이동으로 보고 버린다(스폰·워프·NavMesh 재배치).")]
    [SerializeField] private float teleportSpeed = 20f;

    private const int animIdle = 0;   // 대기
    private const int animWalk = 1;   // 보행
    private const int animRoar = 2;   // 포효
    private const int animAttack = 3; // 공격

    private const int animNone = -1;  // 아직 한 번도 쓰지 않았다 — 첫 판정은 무조건 밀어 넣는다

    private static readonly int stateHash = Animator.StringToHash("State");

    private Vector3 previousPosition; // 직전 프레임의 위치. 이동량은 이 차이로 구한다
    private float smoothedSpeed;      // 평활된 평면 속력(m/s)
    private bool moving;              // 히스테리시스가 적용된 보행 여부
    private int appliedAnimState = animNone;

    /// <summary>상태 변화를 구독하고 현재 상태를 즉시 반영한다(늦게 접속한 클라이언트 포함).</summary>
    public override void OnNetworkSpawn()
    {
        previousPosition = transform.position;
        smoothedSpeed = 0f;
        moving = false;
        appliedAnimState = animNone;

        controller.StateChanged += HandleStateChanged;
        ApplyAnimState();
    }

    /// <summary>상태 변화 구독을 해제한다.</summary>
    public override void OnNetworkDespawn()
    {
        controller.StateChanged -= HandleStateChanged;
    }

    /// <summary>이동 속력을 갱신하고 보행·대기 판정을 반영한다.</summary>
    private void Update()
    {
        // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
        if (!IsSpawned) return;

        Vector3 current = transform.position;
        Vector3 delta = current - previousPosition;
        previousPosition = current;
        delta.y = 0f;

        float deltaTime = Time.deltaTime;
        if (deltaTime > 0f)
        {
            float sampleSpeed = delta.magnitude / deltaTime;

            // 순간이동 표본은 평활에 먹이지 않는다. 기준점은 위에서 이미 옮겼으므로 다음 프레임부터 정상 측정된다.
            if (sampleSpeed <= teleportSpeed)
            {
                // 지수 평활이라 프레임률이 흔들려도 반응 속도가 일정하다.
                float weight = speedSmoothingSeconds > 0f
                    ? 1f - Mathf.Exp(-deltaTime / speedSmoothingSeconds)
                    : 1f;
                smoothedSpeed = Mathf.Lerp(smoothedSpeed, sampleSpeed, weight);
            }
        }

        // 두 문턱 사이(불감대)에서는 직전 판정을 유지한다 — 문턱 하나면 걷기 시작·정지 직전에 클립이 떤다.
        if (smoothedSpeed >= walkSpeedThreshold) moving = true;
        else if (smoothedSpeed <= idleSpeedThreshold) moving = false;

        ApplyAnimState();
    }

    /// <summary>복제된 상태가 바뀐 즉시 반영한다. 포효·공격은 한 프레임도 늦으면 안 되는 연출이다.</summary>
    /// <param name="kind">복제된 현재 좀비 상태.</param>
    private void HandleStateChanged(ZombieStateKind kind)
    {
        ApplyAnimState();
    }

    /// <summary>결정된 애니메이터 상태를 반영한다. 값이 그대로면 쓰지 않는다.</summary>
    private void ApplyAnimState()
    {
        int next = ResolveAnimState();
        if (next == appliedAnimState) return;

        appliedAnimState = next;
        animator.SetInteger(stateHash, next);
    }

    /// <summary>
    /// 애니메이터 상태 인덱스를 결정한다. 포효·공격은 제자리 연출이라 상태로 고르고,
    /// 나머지는 전부 실제 이동 여부로 보행·대기를 가른다.
    /// </summary>
    /// <returns>ZombieLocomotion 컨트롤러의 State 파라미터 값.</returns>
    private int ResolveAnimState()
    {
        switch (controller.CurrentState)
        {
            case ZombieStateKind.RoarTransition:
                return animRoar;
            case ZombieStateKind.Attack:
                return animAttack;
            default:
                return moving ? animWalk : animIdle;
        }
    }
}
