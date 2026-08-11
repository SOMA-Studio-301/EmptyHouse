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
    [Tooltip("하체 회전 각속도(도/초, 절대값)를 제자리 회전 세기(0~1)로 바꾸는 곡선. 위로 올릴수록 같은 속도에서 발이 더 많이 돌아간다.")]
    [SerializeField] private AnimationCurve turnBlendCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(60f, 0.35f),
        new Keyframe(180f, 0.8f),
        new Keyframe(360f, 1f));
    [Tooltip("Turn 클립이 본래 도는 속도(도/초). 실제 회전 속도를 이 값으로 나눠 재생 배속을 만든다 — 빨리 돌수록 발도 빨리 끈다.")]
    [SerializeField] private float turnClipDegreesPerSecond = 90f;
    [SerializeField] private float turnDampTime = 0.08f; // 회전 파라미터 감쇠 시간. 마우스 delta 의 프레임 지터를 흡수한다

    private const float turnMultiplierMin = 0.5f; // 재생 배속 하한
    private const float turnMultiplierMax = 2f;   // 재생 배속 상한

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

        float yawRate = controller.BodyYawRateDeg;
        float turnBlend = turnBlendCurve.Evaluate(Mathf.Abs(yawRate)) * Mathf.Sign(yawRate);
        animator.SetFloat(turnBlendHash, turnBlend, turnDampTime, Time.deltaTime);
        animator.SetFloat(turnMulHash, ResolveTurnMultiplier(yawRate));
    }

    /// <summary>실제 회전 속도와 클립 고유 회전 속도의 비로 제자리 회전 재생 배속을 구한다.</summary>
    /// <param name="yawRateDeg">하체 회전 각속도(도/초, 부호 있음).</param>
    /// <returns>Turn 상태에 넘길 재생 배속.</returns>
    private float ResolveTurnMultiplier(float yawRateDeg)
    {
        if (turnClipDegreesPerSecond <= 0f) return 1f;

        return Mathf.Clamp(Mathf.Abs(yawRateDeg) / turnClipDegreesPerSecond, turnMultiplierMin, turnMultiplierMax);
    }

    /// <summary>점프 발동 시 Jump 트리거를 세팅한다.</summary>
    private void HandleJump()
    {
        animator.SetTrigger(jumpHash);
    }
}
