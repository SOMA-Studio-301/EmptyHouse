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

    private static readonly int speedHash = Animator.StringToHash("Speed");
    private static readonly int groundedHash = Animator.StringToHash("IsGrounded");
    private static readonly int crouchingHash = Animator.StringToHash("IsCrouching");
    private static readonly int jumpHash = Animator.StringToHash("Jump");

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

    /// <summary>소유자에서 매 프레임 이동/접지/웅크림 상태를 Animator 파라미터로 반영한다.</summary>
    private void Update()
    {
        if (!IsOwner) return;

        animator.SetFloat(speedHash, controller.PlanarSpeed, speedDampTime, Time.deltaTime);
        animator.SetBool(groundedHash, controller.Grounded);
        animator.SetBool(crouchingHash, controller.Crouching);
    }

    /// <summary>점프 발동 시 Jump 트리거를 세팅한다.</summary>
    private void HandleJump()
    {
        animator.SetTrigger(jumpHash);
    }
}
