using UnityEngine;

/// <summary>
/// 손전등이 켜져 있는 동안 오른손을 IK 로 전방 조준 타깃에 붙이는 표현 컴포넌트.
/// 재생 중인 애니메이션 위에 IK 가중치를 보간해 얹으므로 이동/웅크림 모션과 독립적으로 동작한다.
/// Animator 가 있는 오브젝트(UnityChanModel)에 부착해야 OnAnimatorIK 콜백을 받는다(레이어 IK Pass 필요).
/// </summary>
[RequireComponent(typeof(Animator))]
public sealed class PlayerFlashlightArmIK : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private PlayerFlashlight flashlight; // 켜짐 상태를 읽을 네트워크 권위자(프리팹 루트)
    [SerializeField] private Transform aimTarget; // 오른손이 붙을 전방 조준 타깃. 위치가 손 위치, 회전이 손 방향을 정한다
    [SerializeField] private Transform elbowHint; // 오른팔꿈치 유도 타깃(선택). 비우면 힌트 없이 IK 솔버에 맡긴다

    [Header("Tuning")]
    [SerializeField, Min(0.1f)] private float blendSpeed = 4f; // 초당 IK 가중치 변화량. 켜고 끌 때 팔이 오르내리는 속도

    private Animator animator;
    private float weight;

    /// <summary>Animator 참조를 캐싱한다.</summary>
    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    /// <summary>IK 패스마다 켜짐 상태로 목표 가중치를 정하고, 보간된 가중치로 오른손 IK 를 적용한다.</summary>
    /// <param name="layerIndex">IK 패스가 실행된 Animator 레이어 인덱스.</param>
    private void OnAnimatorIK(int layerIndex)
    {
        float target = flashlight.IsOn ? 1f : 0f;
        weight = Mathf.MoveTowards(weight, target, blendSpeed * Time.deltaTime);

        if (weight <= 0f)
        {
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
            animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
            return;
        }

        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, weight);
        animator.SetIKRotationWeight(AvatarIKGoal.RightHand, weight);
        animator.SetIKPosition(AvatarIKGoal.RightHand, aimTarget.position);
        animator.SetIKRotation(AvatarIKGoal.RightHand, aimTarget.rotation);

        if (elbowHint != null)
        {
            animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, weight);
            animator.SetIKHintPosition(AvatarIKHint.RightElbow, elbowHint.position);
        }
    }
}
