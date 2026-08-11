using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 소유자의 PlayerController 상태를 읽어 Animator 파라미터를 세팅한다.
/// 원격 복제는 NetworkAnimator 가 담당하므로 파라미터 갱신은 소유자에서만 수행한다.
/// </summary>
public class PlayerAnimator : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerController controller;

    [Header("Tuning")]
    [SerializeField] private float speedDampTime = 0.1f;
    [SerializeField] private float aimDampTime = 0.05f; // 조준 파라미터 감쇠 시간. 원격에도 감쇠된 값이 복제되어 상체 움직임이 부드러워진다
    [SerializeField] private float moveDirectionDeadZone = 0.15f; // 이 속력(m/s) 아래에서는 이동 방향을 갱신하지 않는다. 정지 직전 방향이 홱 도는 것을 막는다

    [Header("Turn")]
    [Tooltip("하체 회전 각속도(도/초, 절대값)를 제자리 회전 세기(0~1)로 바꾸는 진입 게이트. 데드존 밖에서는 반드시 1 이어야 한다 — 1 미만이면 그 부족분이 그대로 발 미끄러짐이 된다.")]
    [SerializeField] private AnimationCurve turnBlendCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(10f, 0f),
        new Keyframe(25f, 1f),
        new Keyframe(360f, 1f));
    [Tooltip("Turn 클립이 본래 도는 속도(도/초). 실제 회전 속도를 이 값으로 나눠 재생 배속을 만든다 — 빨리 돌수록 발도 빨리 끈다. 실측 90.")]
    [SerializeField] private float turnClipDegreesPerSecond = 90f;
    [Tooltip("회전 각속도 평활 시간 상수(초). 마우스 delta 의 프레임 지터를 흡수한다.")]
    [SerializeField] private float turnSmoothingSeconds = 0.08f;

    // 재생 배속 한계. 상한은 bodyTurnSpeedDeg(360) / 클립 고유 속도(90) = 4 를 담아야 한다 —
    // 여기서 잘리면 잘린 만큼 그대로 발이 미끄러진다.
    private const float turnMultiplierMin = 0.15f;
    private const float turnMultiplierMax = 4f;

    private static readonly int speedHash = Animator.StringToHash("Speed");
    private static readonly int moveXHash = Animator.StringToHash("MoveX");
    private static readonly int moveYHash = Animator.StringToHash("MoveY");
    private static readonly int turnBlendHash = Animator.StringToHash("TurnBlend");
    private static readonly int turnMulHash = Animator.StringToHash("TurnMul");
    private static readonly int groundedHash = Animator.StringToHash("IsGrounded");
    private static readonly int crouchingHash = Animator.StringToHash("IsCrouching");
    private static readonly int jumpHash = Animator.StringToHash("Jump");
    private static readonly int aimPitchHash = Animator.StringToHash("AimPitch");
    private static readonly int aimYawOffsetHash = Animator.StringToHash("AimYawOffset");

    private Vector2 moveDirection = Vector2.up; // 본체 로컬 기준 이동 방향 단위벡터. 정지 중에는 마지막 값을 유지한다
    private float smoothedYawRate;              // 평활된 하체 회전 각속도(도/초, 부호 있음). 블렌드 세기와 재생 배속이 같이 읽는다

    /// <summary>소유자에 한해 점프 트리거를 구독한다.</summary>
    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;
        controller.JumpPerformed += HandleJump;
    }

    /// <summary>소유자에 한해 점프 트리거 구독을 해제한다.</summary>
    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;
        controller.JumpPerformed -= HandleJump;
    }

    /// <summary>소유자에서 매 프레임 이동/접지/웅크림/조준 상태를 Animator 파라미터로 반영한다. 조준값은 OwnerNetworkAnimator 가 원격에 복제해 상체 표현(PlayerFlashlightArmIK)이 전 클라이언트에서 같은 값을 읽는다.</summary>
    private void Update()
    {
        if (!IsOwner) return;

        animator.SetFloat(speedHash, controller.PlanarSpeed, speedDampTime, Time.deltaTime);
        animator.SetBool(groundedHash, controller.Grounded);
        animator.SetBool(crouchingHash, controller.Crouching);
        animator.SetFloat(aimPitchHash, controller.AimPitchDeg, aimDampTime, Time.deltaTime);
        animator.SetFloat(aimYawOffsetHash, controller.AimYawOffsetDeg, aimDampTime, Time.deltaTime);

        // 세기는 바깥 Speed 트리가 정하므로 2D 트리에는 단위 방향만 넘긴다. 크기까지 같이 줄이면
        // 저속 구간에서 트리 중앙에 몰려 8방향 클립이 전부 섞인 걸음이 나온다.
        Vector2 localVelocity = controller.LocalPlanarVelocity;
        if (localVelocity.magnitude > moveDirectionDeadZone) moveDirection = localVelocity.normalized;

        animator.SetFloat(moveXHash, moveDirection.x, speedDampTime, Time.deltaTime);
        animator.SetFloat(moveYHash, moveDirection.y, speedDampTime, Time.deltaTime);

        UpdateTurn(Time.deltaTime);
    }

    /// <summary>
    /// 제자리 회전 파라미터를 갱신한다.
    ///
    /// 발이 미끄러지지 않을 조건은 <c>실제 회전 속도 = 클립 고유 속도 × 재생 배속 × 블렌드 세기</c> 다.
    /// [RM] 클립은 발이 루트 대비 -90° 되돌아가는 포즈를 갖고 있고, 그 되돌림이 코드 회전을 정확히
    /// 상쇄할 때만 발이 지면에 붙는다. 그래서 세기를 1 미만으로 낮추면 낮춘 만큼 그대로 미끄러진다 —
    /// "빨리 돌수록 발이 더 많이 돈다"는 세기가 아니라 재생 배속이 담당해야 한다.
    ///
    /// 세기와 배속을 같은 평활값에서 뽑는 것도 같은 이유다. 세기만 애니메이터 감쇠에 맡기면
    /// 회전 시작 구간에서 세기는 아직 덜 올라왔는데 배속은 이미 최대라 그 차이가 미끄러짐이 된다.
    /// </summary>
    /// <param name="deltaTime">이번 프레임 경과 시간(초).</param>
    private void UpdateTurn(float deltaTime)
    {
        if (deltaTime > 0f)
        {
            float weight = turnSmoothingSeconds > 0f
                ? 1f - Mathf.Exp(-deltaTime / turnSmoothingSeconds)
                : 1f;
            smoothedYawRate = Mathf.Lerp(smoothedYawRate, controller.BodyYawRateDeg, weight);
        }

        float turnBlend = turnBlendCurve.Evaluate(Mathf.Abs(smoothedYawRate)) * Mathf.Sign(smoothedYawRate);
        animator.SetFloat(turnBlendHash, turnBlend);
        animator.SetFloat(turnMulHash, ResolveTurnMultiplier());
    }

    /// <summary>실제 회전 속도와 클립 고유 회전 속도의 비로 제자리 회전 재생 배속을 구한다.</summary>
    /// <returns>Turn 상태에 넘길 재생 배속.</returns>
    private float ResolveTurnMultiplier()
    {
        if (turnClipDegreesPerSecond <= 0f) return 1f;

        return Mathf.Clamp(
            Mathf.Abs(smoothedYawRate) / turnClipDegreesPerSecond,
            turnMultiplierMin,
            turnMultiplierMax);
    }

    /// <summary>점프 발동 시 Jump 트리거를 세팅한다.</summary>
    private void HandleJump()
    {
        animator.SetTrigger(jumpHash);
    }
}
