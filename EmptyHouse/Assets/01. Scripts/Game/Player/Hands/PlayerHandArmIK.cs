using System;
using UnityEngine;

/// <summary>
/// 조준(pitch·시선-하체 yaw 차)을 상체 표현으로 반영하고, 손에 든 것을 화면에 들어 보이는 컴포넌트.
/// 휴머노이드 LookAt 으로 척추·머리를 항상 시선 쪽으로 비틀고, 무언가를 들고 있는 동안은 그 손의 IK 타깃을
/// 뷰 공간(뷰 원점 + 뷰 회전)에 붙여 카메라가 타는 머리 밥과 손이 함께 움직이게 한다 — 화면상 손 위치가 고정된다.
///
/// 손 배분(손 충돌 규칙): 오른손은 손전등, 왼손은 한 손 아이템이 맡아 서로 간섭하지 않는다.
/// 양손 아이템은 두 손을 모두 가져가며, 이때 손전등은 <see cref="PlayerFlashlight"/> 단계에서
/// 이미 꺼진 것으로 계산되므로 여기서는 포즈만 고르면 된다.
///
/// 조준값은 Animator 파라미터(AimPitch/AimYawOffset)로 읽는다 — 소유자는 PlayerAnimator 가 쓰고
/// 원격 인스턴스는 OwnerNetworkAnimator 복제로 같은 값을 받으므로 전 클라이언트에서 동일하게 동작한다.
/// Animator 가 있는 오브젝트(UnityChanModel)에 부착해야 OnAnimatorIK 콜백을 받는다(레이어 IK Pass 필요).
/// </summary>
[RequireComponent(typeof(Animator))]
public sealed class PlayerHandArmIK : MonoBehaviour
{
    /// <summary>
    /// 한쪽 손을 뷰 공간 어디에 어떻게 놓을지의 한 벌.
    /// 손·용도마다 값이 다르다 — 특히 yaw 한계는 어깨 기하가 좌우 비대칭이라 손마다 뒤집어 잡아야 한다.
    /// </summary>
    [Serializable]
    private sealed class HandPose
    {
        [Tooltip("뷰 원점 기준 손(weight 1) 위치. 화면 프레이밍 기준으로 잡는다")]
        public Vector3 HandOffsetFromEye;

        [Tooltip("뷰 회전 기준 손 IK 회전 목표(오일러). 손전등 빔 방향 자체는 FlashlightSocket 로컬 회전이 담당한다")]
        public Vector3 HandEulerFromView;

        [Tooltip("뷰 원점 기준 팔꿈치 유도 위치")]
        public Vector3 ElbowHintOffsetFromEye;

        [Tooltip("손 위치 yaw 공전의 왼쪽 한계(음수). 팔이 가슴을 가로질러 꺾이는 쪽이라 좁게 잡는다")]
        public float PositionYawMinDeg = -20f;

        [Tooltip("손 위치 yaw 공전의 오른쪽 한계(양수)")]
        public float PositionYawMaxDeg = 45f;
    }

    /// <summary>이번 IK 패스의 뷰 기준값 묶음. 두 팔이 같은 값을 공유한다.</summary>
    private readonly struct ViewFrame
    {
        public readonly Vector3 Origin; // 뷰 원점 월드 좌표
        public readonly Quaternion Rotation; // 손 "회전" 목표의 기준. pitch 클램프가 넓어 손목·빔은 끝까지 꺾인다
        public readonly Quaternion RootRotation; // 하체 회전. 손 "위치" 공전 계산의 기준
        public readonly float PositionPitchDeg; // 손 위치 공전용으로 이미 클램프된 pitch
        public readonly float AimYawDeg; // yaw 는 포즈마다 한계가 달라 클램프 전 값을 넘긴다

        public ViewFrame(Vector3 origin, Quaternion rotation, Quaternion rootRotation, float positionPitchDeg, float aimYawDeg)
        {
            Origin = origin;
            Rotation = rotation;
            RootRotation = rootRotation;
            PositionPitchDeg = positionPitchDeg;
            AimYawDeg = aimYawDeg;
        }
    }

    /// <summary>
    /// 한쪽 팔의 IK 적용 상태. 포즈가 바뀌면 팔을 완전히 내렸다가 새 포즈로 올린다 —
    /// 손전등 포즈와 양손 포즈처럼 목표가 멀리 떨어진 경우 직선 보간하면 팔이 몸통을 관통하며 지나가기 때문이다.
    /// </summary>
    private sealed class ArmIKDriver
    {
        private readonly AvatarIKGoal goal;
        private readonly AvatarIKHint hint;

        private HandPose pose; // 지금 적용 중인 포즈. null 이면 팔을 내리는 중이거나 내려가 있다
        private float weight;

        public ArmIKDriver(AvatarIKGoal goal, AvatarIKHint hint)
        {
            this.goal = goal;
            this.hint = hint;
        }

        /// <summary>이번 프레임 목표 포즈로 가중치를 보간하고 IK 목표를 애니메이터에 밀어 넣는다.</summary>
        /// <param name="animator">IK 를 적용할 애니메이터.</param>
        /// <param name="desired">이번 프레임 이 팔이 잡아야 할 포즈. null 이면 팔을 내린다.</param>
        /// <param name="blendSpeed">초당 가중치 변화량.</param>
        /// <param name="frame">이번 IK 패스의 뷰 기준값.</param>
        public void Apply(Animator animator, HandPose desired, float blendSpeed, in ViewFrame frame)
        {
            // 팔이 완전히 내려간 순간에만 포즈를 갈아탄다.
            if (weight <= 0f)
            {
                pose = desired;
            }

            float target = pose != null && pose == desired ? 1f : 0f;
            weight = Mathf.MoveTowards(weight, target, blendSpeed * Time.deltaTime);

            animator.SetIKPositionWeight(goal, weight);
            animator.SetIKRotationWeight(goal, weight);
            animator.SetIKHintPositionWeight(hint, weight);
            if (weight <= 0f) return;

            // 손 "위치"용 회전은 pitch·yaw 를 더 좁게 클램프 — 하향 시 몸통 파고듦, 교차 방향 회전 시 팔 꺾임을 막는다.
            float positionYaw = Mathf.Clamp(frame.AimYawDeg, pose.PositionYawMinDeg, pose.PositionYawMaxDeg);
            Quaternion positionRotation = frame.RootRotation * Quaternion.Euler(frame.PositionPitchDeg, positionYaw, 0f);

            // 손·팔꿈치를 뷰에 강체로 붙인다 — 머리 밥이 카메라와 손을 같은 궤적으로 움직여 화면상 위치가 고정된다.
            animator.SetIKPosition(goal, frame.Origin + positionRotation * pose.HandOffsetFromEye);
            animator.SetIKRotation(goal, frame.Rotation * Quaternion.Euler(pose.HandEulerFromView));
            animator.SetIKHintPosition(hint, frame.Origin + positionRotation * pose.ElbowHintOffsetFromEye);
        }
    }

    [Header("Refs")]
    [SerializeField] private PlayerFlashlight flashlight; // 켜짐 상태를 읽을 네트워크 권위자(프리팹 루트)
    [SerializeField] private PlayerHandItem handItem; // 손에 든 아이템의 네트워크 권위자(프리팹 루트)
    [SerializeField] private PlayerHeadCameraFollow cameraFollow; // 뷰 원점(눈 앵커 + 오프셋) 제공자(프리팹 루트)
    [SerializeField] private PlayerController controller; // 소유자 전용 원본 조준값 소스(프리팹 루트). 감쇠 없는 즉시값으로 시선 추종 지연을 없앤다

    [Header("Hand Poses (뷰 공간: x=우, y=상, z=시선 전방. 실시간 튜닝 가능)")]
    [SerializeField] private HandPose flashlightPose = new HandPose
    {
        HandEulerFromView = Vector3.zero,
        HandOffsetFromEye = new Vector3(0.20f, -0.08f, 0.20f),
        ElbowHintOffsetFromEye = new Vector3(0.45f, -0.45f, 0f),
        PositionYawMinDeg = -20f,
        PositionYawMaxDeg = 45f,
    }; // 오른손 손전등

    [SerializeField] private HandPose oneHandItemPose = new HandPose
    {
        HandEulerFromView = Vector3.zero,
        HandOffsetFromEye = new Vector3(-0.22f, -0.12f, 0.26f),
        ElbowHintOffsetFromEye = new Vector3(-0.45f, -0.45f, 0f),
        PositionYawMinDeg = -45f,
        PositionYawMaxDeg = 20f,
    }; // 왼손 한 손 아이템. yaw 한계는 손전등 포즈의 좌우 반전이다

    [SerializeField] private HandPose twoHandLeftPose = new HandPose
    {
        HandEulerFromView = Vector3.zero,
        HandOffsetFromEye = new Vector3(-0.16f, -0.26f, 0.34f),
        ElbowHintOffsetFromEye = new Vector3(-0.42f, -0.50f, 0f),
        PositionYawMinDeg = -35f,
        PositionYawMaxDeg = 25f,
    }; // 양손 아이템의 왼손. 모델이 붙는 쪽이라 이 값이 화면상 아이템 위치를 정한다

    [SerializeField] private HandPose twoHandRightPose = new HandPose
    {
        HandEulerFromView = Vector3.zero,
        HandOffsetFromEye = new Vector3(0.16f, -0.26f, 0.34f),
        ElbowHintOffsetFromEye = new Vector3(0.42f, -0.50f, 0f),
        PositionYawMinDeg = -25f,
        PositionYawMaxDeg = 35f,
    }; // 양손 아이템의 오른손. 왼손 옆에 나란히 두어 함께 받쳐 든 모습을 만든다

    [Header("Aim")]
    [SerializeField, Min(0f)] private float maxAimPitchDeg = 60f; // 손 회전(=빔)·상체에 반영할 pitch 한계(±). 카메라 클램프(89°)보다 좁혀 극단 각을 막는다
    [SerializeField, Min(0f)] private float maxAimYawOffsetDeg = 70f; // 상체 표현에 반영할 yaw 오프셋 한계(±)
    [SerializeField, Min(0f)] private float handPositionPitchClampDeg = 35f; // 손 "위치"가 pitch 를 따라 공전하는 한계(±). 넘으면 손은 멈추고 손목·빔만 더 꺾인다 — 하향 시 손이 몸통으로 파고들며 비틀리는 것을 막는다

    [Header("LookAt")]
    [SerializeField, Range(0f, 1f)] private float lookBodyWeight = 0.4f; // 척추가 시선을 따라 비틀리는 가중치
    [SerializeField, Range(0f, 1f)] private float lookHeadWeight = 0.7f; // 머리가 시선을 따라 도는 가중치

    [Header("Tuning")]
    [SerializeField, Min(0.1f)] private float blendSpeed = 4f; // 초당 손 IK 가중치 변화량. 팔이 오르내리는 속도

    private static readonly int aimPitchHash = Animator.StringToHash("AimPitch");
    private static readonly int aimYawOffsetHash = Animator.StringToHash("AimYawOffset");

    private Animator animator;
    private Transform root; // 프리팹 루트(하체 회전 기준). 조준 회전은 이 로컬 공간에서 계산한다
    private ArmIKDriver rightArm;
    private ArmIKDriver leftArm;

    /// <summary>Animator·루트 참조와 두 팔 드라이버를 준비한다.</summary>
    private void Awake()
    {
        animator = GetComponent<Animator>();
        root = flashlight.transform;
        rightArm = new ArmIKDriver(AvatarIKGoal.RightHand, AvatarIKHint.RightElbow);
        leftArm = new ArmIKDriver(AvatarIKGoal.LeftHand, AvatarIKHint.LeftElbow);
    }

    /// <summary>
    /// IK 패스마다 조준값으로 뷰(원점+회전)를 재구성해 상체 LookAt 과 양손 IK 를 적용한다.
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

        ApplyLookAt(viewOrigin, viewRotation);

        ViewFrame frame = new ViewFrame(
            viewOrigin,
            viewRotation,
            root.rotation,
            Mathf.Clamp(aimPitch, -handPositionPitchClampDeg, handPositionPitchClampDeg),
            aimYaw);

        rightArm.Apply(animator, ResolveRightPose(), blendSpeed, in frame);
        leftArm.Apply(animator, ResolveLeftPose(), blendSpeed, in frame);
    }

    /// <summary>이번 프레임 오른팔이 잡아야 할 포즈. 양손 아이템이 최우선이고, 그 밖에는 실제로 켜진 손전등이다.</summary>
    /// <returns>적용할 포즈. 오른손이 놀고 있으면 null.</returns>
    private HandPose ResolveRightPose()
    {
        if (handItem.IsTwoHanded) return twoHandRightPose;

        return flashlight.IsOn ? flashlightPose : null;
    }

    /// <summary>이번 프레임 왼팔이 잡아야 할 포즈. 아이템은 손 개수와 무관하게 항상 왼손이 맡는다.</summary>
    /// <returns>적용할 포즈. 맨손이면 null.</returns>
    private HandPose ResolveLeftPose()
    {
        if (handItem.IsTwoHanded) return twoHandLeftPose;

        return handItem.IsHolding ? oneHandItemPose : null;
    }

    /// <summary>척추·머리가 조준 방향을 바라보도록 휴머노이드 LookAt 을 적용한다.</summary>
    /// <param name="viewOrigin">뷰 원점 월드 좌표.</param>
    /// <param name="viewRotation">뷰 월드 회전.</param>
    private void ApplyLookAt(Vector3 viewOrigin, Quaternion viewRotation)
    {
        animator.SetLookAtWeight(1f, lookBodyWeight, lookHeadWeight, 0f, 0.5f);
        animator.SetLookAtPosition(viewOrigin + viewRotation * Vector3.forward * 10f);
    }
}
