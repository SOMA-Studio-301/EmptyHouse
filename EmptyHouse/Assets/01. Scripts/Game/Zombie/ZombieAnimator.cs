using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 좀비 애니메이터를 구동한다. 포효·공격은 복제된 상태로 고르고, 대기·보행·질주는 실제 이동 속력을
/// 그대로 Speed 파라미터로 넘겨 Locomotion 블렌드 트리가 섞는다.
///
/// 블렌드 트리의 문턱값은 각 클립의 실측 고유 이동 속도(Idle 0 / Walk 0.957 / Run 3.406 m/s)다.
/// 그래서 Speed 에 실속력을 그대로 넣으면 보폭이 이동 속도에 맞아 발이 미끄러지지 않는다.
///
/// 클립을 상태만으로 고르면 안 된다 — Wander·Investigate·Chase·Subside 는 모두 목적지 도착 후
/// StopAgent() 로 제자리에 서는 구간을 갖는다(조사는 InvestigateToWanderSeconds 동안, 배회 OFF 인
/// 군중 좀비는 영구). 그 구간에서도 상태는 그대로라, 가만히 선 좀비가 걷기 클립을 제자리에서 돌린다.
/// 반대로 포효·공격은 이동이 없는 연출이라 이동량으로 고를 수 없다 — 그래서 둘을 나눠서 판정한다.
///
/// 속력의 출처는 서버와 클라가 다르다. 서버는 위치를 루트모션이 만들어(ZombieRootMotion) 위치 델타로
/// 재면 되먹임이 돌므로, 에이전트가 내려는 속도(desiredVelocity)를 쓴다. 클라는 NavMeshAgent 가 꺼져 있고
/// NetworkTransform 이 보간해 준 복제 좌표뿐이라 위치 델타로 잰다(PlayerFootstepSfx 와 같은 방식).
///
/// 상태는 서버가 쓰고 전 클라이언트에 복제되므로, 이 컴포넌트는 모든 인스턴스에서 동작한다(NetworkAnimator 불필요).
/// </summary>
public class ZombieAnimator : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private ZombieController controller;
    [SerializeField] private Animator animator;

    [Header("Locomotion")]
    [Tooltip("속력 평활 시간 상수(초). 네트워크 보간 지터와 제자리 회전 중의 미세 이동을 흡수한다. 클수록 속도 변화에 애니가 늦게 반응한다.")]
    [SerializeField] private float speedSmoothingSeconds = 0.1f;
    [Tooltip("이 속력(m/s)을 넘는 표본은 순간이동으로 보고 버린다(스폰·워프·NavMesh 재배치).")]
    [SerializeField] private float teleportSpeed = 20f;
    [Tooltip("Speed 값별로 블렌드 트리가 실제로 만들어내는 이동 속력(m/s) 실측값. 이 곡선으로 재생 배속을 역보정해 발 미끄러짐을 없앤다. 클립이나 문턱값을 바꾸면 다시 재서 갱신한다.")]
    [SerializeField] private AnimationCurve blendedSpeedCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.5f, 0.352f),
        new Keyframe(1.2f, 1.011f),
        new Keyframe(1.6f, 1.217f),
        new Keyframe(2f, 1.506f),
        new Keyframe(2.5f, 1.995f),
        new Keyframe(3f, 2.672f),
        new Keyframe(3.406f, 3.429f));

    [Header("Turn (Legs)")]
    [Tooltip("좀비 회전 각속도(도/초, 절대값)를 다리 회전 세기(0~1)로 바꾸는 곡선. 위로 올릴수록 같은 속도에서 발이 더 많이 돌아간다.")]
    [SerializeField] private AnimationCurve turnBlendCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(30f, 0.4f),
        new Keyframe(90f, 0.85f),
        new Keyframe(180f, 1f));
    [Tooltip("Turn 클립이 본래 도는 속도(도/초). 실제 회전 속도를 이 값으로 나눠 재생 배속을 만든다 — 빨리 돌수록 발도 빨리 끈다.")]
    [SerializeField] private float turnClipDegreesPerSecond = 90f;
    [Tooltip("이 속력(m/s)에 도달하면 다리 회전 연출을 완전히 끈다. 걷는 중에는 로코모션 클립이 이미 다리를 쓴다.")]
    [SerializeField] private float turnFadeOutSpeed = 0.4f;
    [Tooltip("회전 각속도 평활 시간 상수(초). 클라이언트의 복제 회전 보간 지터를 흡수한다.")]
    [SerializeField] private float turnSmoothingSeconds = 0.12f;

    private const string turnLayerName = "Turn (Legs)"; // AnimationSetupTool 이 만드는 다리 전용 마스크 레이어 이름

    private const int animLocomotion = 0; // 대기·보행·질주 — Speed 로 블렌드
    private const int animRoar = 2;       // 포효
    private const int animAttack = 3;     // 공격

    private const int animNone = -1;  // 아직 한 번도 쓰지 않았다 — 첫 판정은 무조건 밀어 넣는다

    private const float minNaturalSpeed = 0.05f;   // 이 아래에서는 배속 보정을 하지 않는다(나눗셈 폭주)
    private const float speedMultiplierMin = 0.5f; // 배속 하한
    private const float speedMultiplierMax = 2f;   // 배속 상한

    private static readonly int stateHash = Animator.StringToHash("State");
    private static readonly int speedHash = Animator.StringToHash("Speed");
    private static readonly int speedMulHash = Animator.StringToHash("SpeedMul");
    private static readonly int turnBlendHash = Animator.StringToHash("TurnBlend");
    private static readonly int turnMulHash = Animator.StringToHash("TurnMul");

    private Vector3 previousPosition; // 직전 프레임의 위치. 이동량은 이 차이로 구한다
    private float smoothedSpeed;      // 평활된 평면 속력(m/s)
    private int appliedAnimState = animNone;

    private float previousYaw;     // 직전 프레임의 좀비 루트 Y 회전(도). 회전 각속도는 이 차이로 구한다
    private float smoothedYawRate; // 평활된 회전 각속도(도/초, 부호 있음)
    private int turnLayerIndex;    // 다리 전용 Turn 레이어 인덱스. 스폰 시 이름으로 찾는다

    /// <summary>상태 변화를 구독하고 현재 상태를 즉시 반영한다(늦게 접속한 클라이언트 포함).</summary>
    public override void OnNetworkSpawn()
    {
        previousPosition = transform.position;
        smoothedSpeed = 0f;
        appliedAnimState = animNone;

        previousYaw = controller.transform.eulerAngles.y;
        smoothedYawRate = 0f;
        turnLayerIndex = animator.GetLayerIndex(turnLayerName);

        controller.StateChanged += HandleStateChanged;
        animator.SetFloat(speedHash, 0f);
        animator.SetFloat(speedMulHash, 1f);
        animator.SetFloat(turnBlendHash, 0f);
        animator.SetFloat(turnMulHash, 1f);
        animator.SetLayerWeight(turnLayerIndex, 0f);
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
            // 서버는 에이전트가 내려는 속도를 그대로 쓴다. 루트모션이 위치를 만드는 쪽이라, 서버에서
            // 위치 델타로 속력을 재면 애니 → 위치 → 속력 → 애니 로 되먹임이 돌아 속도가 발산한다.
            // 클라는 NetworkTransform 이 밀어 준 복제 좌표뿐이므로 지금처럼 위치 델타로 잰다.
            float sampleSpeed = IsServer
                ? controller.Agent.desiredVelocity.magnitude
                : delta.magnitude / deltaTime;

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

        // 문턱 판정 없이 실속력을 그대로 넘긴다 — 대기·보행·질주 사이는 블렌드 트리가 이어 준다.
        animator.SetFloat(speedHash, smoothedSpeed);
        animator.SetFloat(speedMulHash, ResolveSpeedMultiplier());

        ApplyAnimState();
        UpdateTurn(deltaTime); // 레이어 가중치가 appliedAnimState 를 읽으므로 상태 확정 뒤에 돈다
    }

    /// <summary>
    /// 제자리 회전을 다리 전용 레이어에 반영한다. 회전은 코드가 만들고(ZombieStateMachine.UpdateFacing)
    /// 클립은 포즈가 제자리인 [RM] 판이라, 이 레이어는 발을 끄는 연출만 얹는다.
    ///
    /// 최종 세기는 트리 블렌드(TurnBlend)와 레이어 가중치의 곱이다 — 둘 다 회전량을 타므로 완만하게 붙는다.
    /// 가중치를 회전량과 무관하게 두면 안 된다. 트리 중앙의 대기 클립이 베이스 레이어와 재생 위상이
    /// 어긋나 있어서, 가만히 선 좀비의 다리만 따로 떠는 그림이 나온다.
    /// </summary>
    /// <param name="deltaTime">이번 프레임 경과 시간(초).</param>
    private void UpdateTurn(float deltaTime)
    {
        float currentYaw = controller.transform.eulerAngles.y;
        if (deltaTime > 0f)
        {
            float sample = Mathf.DeltaAngle(previousYaw, currentYaw) / deltaTime;
            float weight = turnSmoothingSeconds > 0f
                ? 1f - Mathf.Exp(-deltaTime / turnSmoothingSeconds)
                : 1f;
            smoothedYawRate = Mathf.Lerp(smoothedYawRate, sample, weight);
        }
        previousYaw = currentYaw;

        float turnBlend = turnBlendCurve.Evaluate(Mathf.Abs(smoothedYawRate)) * Mathf.Sign(smoothedYawRate);
        animator.SetFloat(turnBlendHash, turnBlend);
        animator.SetFloat(turnMulHash, ResolveTurnMultiplier());

        // 이동 중이면 로코모션 클립이 이미 다리를 쓰고, 포효·공격은 다리까지 짜인 연출이다. 둘 다 비켜 준다.
        float stationary = turnFadeOutSpeed > 0f
            ? 1f - Mathf.Clamp01(smoothedSpeed / turnFadeOutSpeed)
            : 1f;
        float locomotion = appliedAnimState == animLocomotion ? 1f : 0f;

        animator.SetLayerWeight(turnLayerIndex, Mathf.Abs(turnBlend) * stationary * locomotion);
    }

    /// <summary>실제 회전 속도와 클립 고유 회전 속도의 비로 다리 회전 재생 배속을 구한다.</summary>
    /// <returns>Turn 상태에 넘길 재생 배속.</returns>
    private float ResolveTurnMultiplier()
    {
        if (turnClipDegreesPerSecond <= 0f) return 1f;

        return Mathf.Clamp(
            Mathf.Abs(smoothedYawRate) / turnClipDegreesPerSecond,
            speedMultiplierMin,
            speedMultiplierMax);
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
    /// 블렌드 트리가 실제로 만들어내는 속력과 실이동 속력의 비로 재생 배속을 구한다.
    /// 두 클립을 섞으면 보폭이 짧아져 실이동보다 느리게 걷는 탓에(중간 구간 최대 25%),
    /// 그만큼 빠르게 돌려야 발이 지면에 붙는다.
    /// </summary>
    /// <returns>Locomotion 상태에 넘길 재생 배속.</returns>
    private float ResolveSpeedMultiplier()
    {
        float natural = blendedSpeedCurve.Evaluate(smoothedSpeed);
        if (natural <= minNaturalSpeed) return 1f;

        return Mathf.Clamp(smoothedSpeed / natural, speedMultiplierMin, speedMultiplierMax);
    }

    /// <summary>
    /// 애니메이터 상태 인덱스를 결정한다. 포효·공격은 제자리 연출이라 상태로 고르고,
    /// 나머지는 전부 Locomotion 으로 보낸다 — 그 안의 대기·보행·질주는 Speed 가 가른다.
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
                return animLocomotion;
        }
    }
}
