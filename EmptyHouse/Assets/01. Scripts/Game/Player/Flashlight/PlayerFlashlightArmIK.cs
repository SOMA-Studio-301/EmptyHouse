using UnityEngine;

/// <summary>
/// 조준(pitch·시선-하체 yaw 차)을 상체 표현으로 반영하는 컴포넌트.
/// 휴머노이드 LookAt 으로 척추·머리를 항상 시선 쪽으로 비틀고,
/// 손전등이 켜져 있는 동안은 오른손 IK 타깃을 뷰 공간(뷰 원점 + 뷰 회전)에 붙여
/// 카메라가 타는 머리 밥과 손이 함께 움직이게 한다 — 화면상 손 위치가 고정된다.
/// 조준값은 Animator 파라미터(AimPitch/AimYawOffset)로 읽는다 — 소유자는 PlayerAnimator 가 쓰고
/// 원격 인스턴스는 OwnerNetworkAnimator 복제로 같은 값을 받으므로 전 클라이언트에서 동일하게 동작한다.
/// Animator 가 있는 오브젝트(UnityChanModel)에 부착해야 OnAnimatorIK 콜백을 받는다(레이어 IK Pass 필요).
/// </summary>
[RequireComponent(typeof(Animator))]
public sealed class PlayerFlashlightArmIK : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private PlayerFlashlight flashlight; // 켜짐 상태를 읽을 네트워크 권위자(프리팹 루트)
    [SerializeField] private PlayerHeadCameraFollow cameraFollow; // 뷰 원점(눈 앵커 + 오프셋) 제공자(프리팹 루트)
    [SerializeField] private PlayerController controller; // 소유자 전용 원본 조준값 소스(프리팹 루트). 감쇠 없는 즉시값으로 시선 추종 지연을 없앤다

    [Header("Hand Pose (뷰 공간: x=우, y=상, z=시선 전방. 실시간 튜닝 가능)")]
    [SerializeField] private Vector3 handOffsetFromEye = new Vector3(0.20f, -0.08f, 0.20f); // 뷰 원점 기준 손(weight 1) 위치. 화면 프레이밍 기준으로 잡는다
    [SerializeField] private Vector3 handEulerFromView = Vector3.zero; // 뷰 회전 기준 손 IK 회전 목표(오일러). 빔 방향 자체는 FlashlightSocket 로컬 회전이 담당한다
    [SerializeField] private Vector3 elbowHintOffsetFromEye = new Vector3(0.45f, -0.45f, 0f); // 뷰 원점 기준 오른팔꿈치 유도 위치

    [Header("Aim")]
    [SerializeField, Min(0f)] private float maxAimPitchDeg = 60f; // 손 회전(=빔)·상체에 반영할 pitch 한계(±). 카메라 클램프(89°)보다 좁혀 극단 각을 막는다
    [SerializeField, Min(0f)] private float maxAimYawOffsetDeg = 70f; // 상체 표현에 반영할 yaw 오프셋 한계(±)
    [SerializeField, Min(0f)] private float handPositionPitchClampDeg = 35f; // 손 "위치"가 pitch 를 따라 공전하는 한계(±). 넘으면 손은 멈추고 손목·빔만 더 꺾인다 — 하향 시 손이 몸통으로 파고들며 비틀리는 것을 막는다
    [SerializeField] private float handPositionYawMinDeg = -20f; // 손 "위치" yaw 공전의 왼쪽(교차 방향) 한계. 오른손이 가슴을 가로질러 꺾이는 것을 막기 위해 좁게 잡는다
    [SerializeField] private float handPositionYawMaxDeg = 45f; // 손 "위치" yaw 공전의 오른쪽 한계. 어깨가 열리는 방향이라 왼쪽보다 넓다

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

    /// <summary>
    /// IK 패스마다 조준값으로 뷰(원점+회전)를 재구성해 상체 LookAt 과 오른손 IK 를 적용한다.
    /// 소유자는 PlayerController 원본값(지연 0)을, 원격은 복제된 애니메이터 파라미터(감쇠)를 읽는다.
    /// </summary>
    /// <param name="layerIndex">IK 패스가 실행된 Animator 레이어 인덱스.</param>
    private void OnAnimatorIK(int layerIndex)
    {
        float rawPitch;
        float rawYaw;
        if (flashlight.IsOwner)
        {
            rawPitch = controller.AimPitchDeg;
            rawYaw = controller.AimYawOffsetDeg;
        }
        else
        {
            rawPitch = animator.GetFloat(aimPitchHash);
            rawYaw = animator.GetFloat(aimYawOffsetHash);
        }

        float aimPitch = Mathf.Clamp(rawPitch, -maxAimPitchDeg, maxAimPitchDeg);
        float aimYaw = Mathf.Clamp(rawYaw, -maxAimYawOffsetDeg, maxAimYawOffsetDeg);
        Quaternion viewRotation = root.rotation * Quaternion.Euler(aimPitch, aimYaw, 0f);
        Vector3 viewOrigin = cameraFollow.GetViewOrigin(viewRotation);

        // 손 "위치"용 회전은 pitch·yaw 를 더 좁게 클램프 — 하향 시 몸통 파고듦, 좌회전(교차) 시 팔 꺾임을 막는다.
        // yaw 는 오른손 기하학상 비대칭: 왼쪽(음수)은 가슴을 가로지르는 방향이라 좁고, 오른쪽은 어깨가 열려 넓다.
        float positionPitch = Mathf.Clamp(aimPitch, -handPositionPitchClampDeg, handPositionPitchClampDeg);
        float positionYaw = Mathf.Clamp(aimYaw, handPositionYawMinDeg, handPositionYawMaxDeg);
        Quaternion positionRotation = root.rotation * Quaternion.Euler(positionPitch, positionYaw, 0f);

        ApplyLookAt(viewOrigin, viewRotation);
        ApplyHandIK(viewOrigin, viewRotation, positionRotation);
    }

    /// <summary>척추·머리가 조준 방향을 바라보도록 휴머노이드 LookAt 을 적용한다.</summary>
    /// <param name="viewOrigin">뷰 원점 월드 좌표.</param>
    /// <param name="viewRotation">뷰 월드 회전.</param>
    private void ApplyLookAt(Vector3 viewOrigin, Quaternion viewRotation)
    {
        animator.SetLookAtWeight(1f, lookBodyWeight, lookHeadWeight, 0f, 0.5f);
        animator.SetLookAtPosition(viewOrigin + viewRotation * Vector3.forward * 10f);
    }

    /// <summary>
    /// 손전등 켜짐 상태로 보간한 가중치로 오른손 IK 를 적용한다.
    /// 손 위치·팔꿈치는 pitch 가 좁게 클램프된 회전을, 손 회전(=빔)은 뷰 회전을 그대로 따른다.
    /// </summary>
    /// <param name="viewOrigin">뷰 원점 월드 좌표.</param>
    /// <param name="viewRotation">뷰 월드 회전. 손 회전(빔 방향)에 쓴다.</param>
    /// <param name="positionRotation">손 위치용 회전(pitch 클램프 적용). 손·팔꿈치 위치 공전에 쓴다.</param>
    private void ApplyHandIK(Vector3 viewOrigin, Quaternion viewRotation, Quaternion positionRotation)
    {
        float target = flashlight.IsOn ? 1f : 0f;
        weight = Mathf.MoveTowards(weight, target, blendSpeed * Time.deltaTime);

        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, weight);
        animator.SetIKRotationWeight(AvatarIKGoal.RightHand, weight);
        if (weight <= 0f) return;

        // 손·팔꿈치를 뷰에 강체로 붙인다 — 머리 밥이 카메라와 손을 같은 궤적으로 움직여 화면상 위치가 고정된다.
        animator.SetIKPosition(AvatarIKGoal.RightHand, viewOrigin + positionRotation * handOffsetFromEye);
        animator.SetIKRotation(AvatarIKGoal.RightHand, viewRotation * Quaternion.Euler(handEulerFromView));

        animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, weight);
        animator.SetIKHintPosition(AvatarIKHint.RightElbow, viewOrigin + positionRotation * elbowHintOffsetFromEye);
    }
}
