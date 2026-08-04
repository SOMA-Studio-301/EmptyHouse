using UnityEngine;

/// <summary>
/// 조준(pitch·시선-하체 yaw 차)을 상체 표현으로 반영하는 컴포넌트.
/// 휴머노이드 LookAt 으로 척추·머리를 항상 시선 쪽으로 비틀고,
/// 손전등이 켜져 있는 동안은 오른손 IK 타깃을 조준 방향으로 회전시켜 손과 빔이 시선을 따라가게 한다.
/// 조준값은 Animator 파라미터(AimPitch/AimYawOffset)로 읽는다 — 소유자는 PlayerAnimator 가 쓰고
/// 원격 인스턴스는 OwnerNetworkAnimator 복제로 같은 값을 받으므로 전 클라이언트에서 동일하게 동작한다.
/// Animator 가 있는 오브젝트(UnityChanModel)에 부착해야 OnAnimatorIK 콜백을 받는다(레이어 IK Pass 필요).
/// </summary>
[RequireComponent(typeof(Animator))]
public sealed class PlayerFlashlightArmIK : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private PlayerFlashlight flashlight; // 켜짐 상태를 읽을 네트워크 권위자(프리팹 루트)
    [SerializeField] private Transform aimTarget; // 오른손 기본 조준 포즈(캘리브레이션 값, 루트 자식). 조준 회전이 0 일 때의 손 위치·방향
    [SerializeField] private Transform elbowHint; // 오른팔꿈치 유도 타깃(선택, 루트 자식). 비우면 힌트 없이 IK 솔버에 맡긴다

    [Header("Aim")]
    [SerializeField] private Vector3 aimPivotLocal = new Vector3(0f, 0.35f, 0f); // 조준 회전의 피벗(루트 로컬, 가슴 높이). 손 타깃·팔꿈치 힌트가 이 점을 중심으로 회전한다
    [SerializeField, Min(0f)] private float maxAimPitchDeg = 60f; // 상체 표현에 반영할 pitch 한계(±). 카메라 클램프(89°)보다 좁혀 팔이 꺾여 보이는 극단 각을 막는다
    [SerializeField, Min(0f)] private float maxAimYawOffsetDeg = 70f; // 상체 표현에 반영할 yaw 오프셋 한계(±)

    [Header("LookAt")]
    [SerializeField, Range(0f, 1f)] private float lookBodyWeight = 0.4f; // 척추가 시선을 따라 비틀리는 가중치
    [SerializeField, Range(0f, 1f)] private float lookHeadWeight = 0.7f; // 머리가 시선을 따라 도는 가중치

    [Header("Tuning")]
    [SerializeField, Min(0.1f)] private float blendSpeed = 4f; // 초당 손 IK 가중치 변화량. 켜고 끌 때 팔이 오르내리는 속도

    private static readonly int aimPitchHash = Animator.StringToHash("AimPitch");
    private static readonly int aimYawOffsetHash = Animator.StringToHash("AimYawOffset");

    private Animator animator;
    private Transform root; // 프리팹 루트(하체 회전 기준). 조준 회전은 이 로컬 공간에서 계산한다
    private float weight;

    /// <summary>Animator 와 루트 참조를 캐싱한다.</summary>
    private void Awake()
    {
        animator = GetComponent<Animator>();
        root = flashlight.transform;
    }

    /// <summary>IK 패스마다 복제된 조준값을 읽어 상체 LookAt 과 오른손 IK 를 적용한다.</summary>
    /// <param name="layerIndex">IK 패스가 실행된 Animator 레이어 인덱스.</param>
    private void OnAnimatorIK(int layerIndex)
    {
        float aimPitch = Mathf.Clamp(animator.GetFloat(aimPitchHash), -maxAimPitchDeg, maxAimPitchDeg);
        float aimYaw = Mathf.Clamp(animator.GetFloat(aimYawOffsetHash), -maxAimYawOffsetDeg, maxAimYawOffsetDeg);
        Quaternion aimRotation = Quaternion.Euler(aimPitch, aimYaw, 0f);

        ApplyLookAt(aimRotation);
        ApplyHandIK(aimRotation);
    }

    /// <summary>척추·머리가 조준 방향을 바라보도록 휴머노이드 LookAt 을 적용한다.</summary>
    /// <param name="aimRotation">루트 로컬 기준 조준 회전.</param>
    private void ApplyLookAt(Quaternion aimRotation)
    {
        Vector3 origin = root.TransformPoint(aimPivotLocal);
        Vector3 direction = root.rotation * aimRotation * Vector3.forward;
        animator.SetLookAtWeight(1f, lookBodyWeight, lookHeadWeight, 0f, 0.5f);
        animator.SetLookAtPosition(origin + direction * 10f);
    }

    /// <summary>손전등 켜짐 상태로 보간한 가중치로, 조준 회전을 반영한 타깃에 오른손 IK 를 적용한다.</summary>
    /// <param name="aimRotation">루트 로컬 기준 조준 회전.</param>
    private void ApplyHandIK(Quaternion aimRotation)
    {
        float target = flashlight.IsOn ? 1f : 0f;
        weight = Mathf.MoveTowards(weight, target, blendSpeed * Time.deltaTime);

        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, weight);
        animator.SetIKRotationWeight(AvatarIKGoal.RightHand, weight);
        if (weight <= 0f) return;

        // 캘리브레이션된 기본 포즈를 가슴 피벗 중심으로 조준 회전만큼 돌려 손 위치·방향을 만든다.
        Vector3 handLocal = aimPivotLocal + aimRotation * (aimTarget.localPosition - aimPivotLocal);
        animator.SetIKPosition(AvatarIKGoal.RightHand, root.TransformPoint(handLocal));
        animator.SetIKRotation(AvatarIKGoal.RightHand, root.rotation * aimRotation * aimTarget.localRotation);

        if (elbowHint != null)
        {
            Vector3 hintLocal = aimPivotLocal + aimRotation * (elbowHint.localPosition - aimPivotLocal);
            animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, weight);
            animator.SetIKHintPosition(AvatarIKHint.RightElbow, root.TransformPoint(hintLocal));
        }
    }
}
