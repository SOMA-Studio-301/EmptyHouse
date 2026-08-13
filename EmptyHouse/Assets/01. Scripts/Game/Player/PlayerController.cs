using Border.Core;
using Border.Events;
using Unity.Cinemachine;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// 소유 클라이언트의 입력을 받아 플레이어 캐릭터의 시선과 이동을 처리하는 네트워크 컨트롤러.
/// InputReader 는 ScriptableObject(프로세스 전역 단일 인스턴스)이므로 소유자만 구독한다.
/// </summary>
public class PlayerController : NetworkBehaviour
{
    [Header("Input")]
    [SerializeField] private InputReader inputReader; // 입력 이벤트 소스. OnNetworkSpawn 에서 소유자만 구독한다

    [Header("Look")]
    [SerializeField] private Transform cameraPivot; // pitch 회전 축이자 Main Camera 부착 지점(눈 높이)
    [SerializeField] private float lookSensitivity = 0.1f; // 기본 시선 감도(튜닝값). 설정창의 배율이 여기에 곱해진다
    [SerializeField] private float pitchClamp = 89f; // pitch 상하 클램프 각도(±)
    [SerializeField] private float wardrobeLookAngleDeg = 60f; // 은신 중 시야 콘 반각(3-10 wardrobe_look_angle_deg). 진입 시점 정면 기준 yaw·pitch 를 ±이 각도로 클램프
    [SerializeField] private float upperBodyYawLimitDeg = 60f; // 정지 중 상체만 시선을 따라가는 yaw 한계각(±). 초과분은 하체가 따라 돈다
    [SerializeField] private float bodyTurnSpeedDeg = 360f; // 하체(bodyYaw)가 시선을 따라 도는 속도(도/초)

    [Header("Settings")]
    [SerializeField] private SaveLoadSystem saveLoadSystem; // 스폰 시 저장된 감도 배율을 읽어오는 원본
    [SerializeField] private FloatEventChannelSO changeMouseSensitivityEvent; // 설정창에서 감도를 바꾸면 방송되는 라이브 채널

    [Header("Camera")]
    [SerializeField] private CameraEventChannelSO onCameraChanged; // Vcam 전환 요청 채널
    [SerializeField] private CinemachineCamera eyeVcam; // 1인칭 기본 Vcam. cameraPivot 자식, 로컬 identity — 기존 수동 부착을 대체
    [SerializeField] private CinemachineCamera disguiseVcam; // 위장 중 숄더뷰로 전환할 Vcam

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f; // 기본 이동 속도(m/s)
    [SerializeField] private float crouchSpeedMultiplier = 0.5f; // 웅크림 중 이동속도 배율. moveSpeed 에 곱해 적용한다
    [SerializeField] private float disguiseSpeedMultiplier = 0.6f; // 위장 중 자동 전진 속도 배율. moveSpeed 에 곱해 적용한다

    // [SerializeField] private float jumpSpeed = 5f; // TODO(점프 삭제 예정): 점프 시작 시 설정할 상승 속도. v²/2g ≈ 1.27m 상승한다

    [Header("Ground Check")]
    [SerializeField] private LayerMask groundMask; // 접지 판정 대상 레이어. 인스펙터에서 Ground 만 선택한다
    [SerializeField] private float groundCheckRadius = 0.4f; // 접지 판정 SphereCast 의 반지름. 캡슐 반지름(0.5)보다 작아야 벽 모서리를 바닥으로 오검출하지 않는다
    [SerializeField] private float groundCheckDistance = 0.7f; // 접지 판정 SphereCast 의 거리. 중심→바닥 1.0 - 반지름 0.4 + 여유 0.1

    [Header("Ownership-gated")]
    [SerializeField] private PlayerInteractor interactor; // 상호작용 판정. 비소유자 인스턴스에서는 OnNetworkSpawn 에서 끈다
    [SerializeField] private PlayerInventory inventory; // 인벤토리. 비소유자 인스턴스에서는 OnNetworkSpawn 에서 끈다
    [SerializeField] private PlayerItemUser itemUser; // 좌·우클릭 아이템 사용 라우팅. InputReader 가 전역 SO 라, 끄지 않으면 남의 캐릭터가 내 클릭에 반응한다
    [SerializeField] private ThrowAimIndicator aimIndicator; // 투척 조준 궤적. 로컬 표시물이므로 비소유자 인스턴스에서는 그릴 이유가 없다
    [SerializeField] private Canvas promptCanvas; // 상호작용 프롬프트 전용 Canvas. 다른 HUD 요소와 분리해 두어, 프롬프트가 매 프레임 갱신돼도 그쪽 Canvas 는 리빌드되지 않는다

    private Rigidbody body; // Awake 캐시
    private NetworkTransform networkTransform; // Owner 권한 NetworkTransform — 스폰 포즈 강제 적용(Teleport)에 쓴다. Awake 캐시
    private PlayerDeathHandler deathHandler; // 비활성 게이팅 소스 — 형제. 사망 시 이동·시선·상호작용·인벤을 차단한다(2-1·관전은 PlayerSpectatorController)
    private PlayerReturn playerReturn; // 비활성 게이팅 소스 — 형제. 귀환도 사망과 똑같이 조작을 차단한다(세션루프.md 3장)
    private PlayerHiding hiding; // 은신 게이팅 소스 — 형제. 은신 중 이동·점프를 차단하고 시선을 콘 안으로 제한한다(2-1, 조작상호작용UI.md 3-5-1)
    private PlayerDisguise disguise; // 위장 게이팅 소스 — 형제. 위장 중 WASD 를 무시하고 시선 정면으로 자동 전진시킨다

    private bool IsInactive => deathHandler.IsDead.Value || playerReturn.HasExtracted.Value; // 비활성(사망 OR 귀환) 여부. 어느 쪽이든 조작을 차단하고 관전으로 넘긴다 — PlayerSpectatorController 진입 조건과 같다

    private Vector2 moveInput; // 이동 입력 캐시 — OnMoveInput 이 쓰고 HandleMove 가 읽는다
    private Vector2 lookInput; // 시선 입력 캐시 — OnLookInput 이 쓰고 HandleLook 이 읽고 소비 후 zero 로 리셋한다

    // private bool jumpRequested; // TODO(점프 삭제 예정): 점프 요청 — 입력 콜백이 세우고 FixedUpdate 의 HandleJump 가 소비한다
    private bool isCrouching; // 웅크림 상태(홀드) — Crouch 입력 콜백이 켜고 끄며, HandleMove 가 이동속도 배율에 반영한다

    // 웅크림 상태의 네트워크 사본. 입력은 소유자에게만 오므로 비소유자 인스턴스의 isCrouching 은 항상 false 인데,
    // 발소리(PlayerFootstepSfx)는 모든 클라에서 각자 울리고 소음 발신은 서버에서 판정한다 — 둘 다 남의 웅크림을 알아야 한다.
    // 소유자 권한이라 소유자 인스턴스에는 즉시 반영된다(자기 발소리가 RTT 만큼 새지 않는다).
    private readonly NetworkVariable<bool> networkCrouching = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private float pitch; // 시선 상태 — cameraPivot 의 로컬 X 회전
    private float yaw; // 시선 상태 — 카메라가 향하는 월드 Y 회전. 본체 회전(bodyYaw)과 분리되어 있다
    private float bodyYaw; // 하체(본체)의 Y 회전 — 시선이 상체 한계각을 넘거나 이동 중일 때만 시선을 따라온다

    private float bodyYawRateDeg; // 이번 프레임 bodyYaw 의 변화량을 초당으로 환산한 값. 제자리 회전 애니메이션이 읽는다

    private float hiddenBaseYaw; // 은신 시야 콘의 기준 yaw. 은신 진입 순간의 시선(또는 스냅 앵커 정면)이다. 은신 중에만 유효하다
    private bool wasHidden; // 직전 프레임의 은신 여부 — 진입 순간(false→true)에 hiddenBaseYaw 를 잡기 위한 에지 검출용
    private float sensitivityMultiplier = 1f; // 시선 감도 배율 — 스폰 시 Profile 에서 읽고, 설정창 변경 시 채널로 갱신된다. lookSensitivity 에 곱해 실효 감도를 만든다

    // ── 애니메이션 노출 (읽기 전용) — PlayerAnimator 가 소유자에서 참조한다 ──

    public float PlanarSpeed => new Vector2(body.linearVelocity.x, body.linearVelocity.z).magnitude; // 수평(XZ) 이동 속력(m/s). 이동 블렌드 파라미터용
    public Vector2 LocalPlanarVelocity => ToLocalPlanar(body.linearVelocity); // 본체 로컬 기준 수평 속도(m/s). x=우, y=전. 2D 스트레이프 블렌드용
    public float BodyYawRateDeg => bodyYawRateDeg; // 하체 회전 각속도(도/초, 부호 있음). 제자리 회전 연출용
    public bool Grounded => IsGrounded(); // 접지 여부. 애니메이션 파라미터용
    public bool Crouching => networkCrouching.Value; // 웅크림 상태 여부. 애니메이션 파라미터·발소리·소음 발신 공통 창구다(비소유자 인스턴스에서도 유효)
    public float AimPitchDeg => pitch; // 시선 pitch(도). 상체 조준 표현(AimPitch 파라미터)용
    public float AimYawOffsetDeg => Mathf.DeltaAngle(bodyYaw, yaw); // 시선-하체 yaw 차(도). 상체 비틀림 표현(AimYawOffset 파라미터)용
    public bool IsDisguised => disguise.IsDisguised; // 위장 상태 여부. 애니메이션 IsDisguised 파라미터용
    // public event System.Action JumpPerformed; // TODO(점프 삭제 예정): 점프가 실제로 발동한 순간 발행된다. 애니메이션 트리거용

    /// <summary>Rigidbody 와 형제 PlayerDeathHandler·PlayerReturn·PlayerHiding·PlayerDisguise 참조를 캐시한다.</summary>
    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        networkTransform = GetComponent<NetworkTransform>();
        deathHandler = GetComponent<PlayerDeathHandler>();
        playerReturn = GetComponent<PlayerReturn>();
        hiding = GetComponent<PlayerHiding>();
        disguise = GetComponent<PlayerDisguise>();
    }

    /// <summary>
    /// 네트워크 스폰 시 소유자에 한해 입력 이벤트를 구독한다.
    /// 상호작용 판정과 프롬프트 UI 는 로컬 전용이므로 비소유자 인스턴스에서는 꺼 둔다
    /// </summary>
    public override void OnNetworkSpawn()
    {
        interactor.enabled = IsOwner;
        promptCanvas.enabled = IsOwner;
        inventory.enabled = IsOwner;
        itemUser.enabled = IsOwner;
        aimIndicator.enabled = IsOwner;

        // 비소유자 인스턴스의 Vcam 은 로컬에서 즉시 꺼둔다. Vcam 은 네트워크를 모르는 일반 컴포넌트라
        // CinemachineBrain 이 소유권 구분 없이 활성 Vcam 중 Priority 최고를 그냥 골라버리기 때문에,
        // 이벤트 채널(onCameraChanged)에만 맡기면 남의 Vcam 이 먼저 켜져 있을 때 화면을 뺏길 수 있다.
        if (!IsOwner)
        {
            eyeVcam.gameObject.SetActive(false);
            disguiseVcam.gameObject.SetActive(false);
            return;
        }

        inputReader.MoveEvent += OnMoveInput;
        inputReader.LookEvent += OnLookInput;
        // inputReader.JumpEvent += OnJumpInput; // TODO(점프 삭제 예정)
        inputReader.CrouchEvent += OnCrouchInput;
        inputReader.CrouchCanceledEvent += OnCrouchCanceledInput;
        inputReader.AttackEvent += OnAttackInput;
        inputReader.AttackCanceledEvent += OnAttackCanceledInput;
        deathHandler.IsDead.OnValueChanged += HandleInactiveChanged;
        playerReturn.HasExtracted.OnValueChanged += HandleInactiveChanged;
        disguise.DisguiseChanged += HandleDisguiseChanged;

        // 저장된 감도 배율을 스폰 시점에 읽어 1차 반영하고, 이후 변경은 채널로 받는다.
        sensitivityMultiplier = saveLoadSystem.Profile.MouseSensitivity;
        changeMouseSensitivityEvent.OnEventRaised += HandleMouseSensitivityChanged;

        // 1인칭 기본 Vcam 을 켠다. eyeVcam 은 cameraPivot 자식(로컬 identity)이라 pitch 를 그대로 따라간다.
        onCameraChanged.RaiseEvent(eyeVcam);
    }

    /// <summary>
    /// 서버가 지정한 스폰 포즈를 소유자에게 전달해 강제 적용한다.
    /// Owner 권한 NetworkTransform 은 동적 스폰 직후 포스트 스폰 동기화가 스폰 위치를
    /// 원점 계열로 덮어쓰는 NGO 이슈(#2531 계열)가 있어, 권한자인 소유자가 직접 텔레포트해 바로잡는다.
    /// </summary>
    /// <param name="position">서버 지정 스폰 위치.</param>
    /// <param name="rotation">서버 지정 스폰 회전.</param>
    [ClientRpc]
    public void SetSpawnPoseClientRpc(Vector3 position, Quaternion rotation)
    {
        if (!IsOwner) return;

        StartCoroutine(EnforceSpawnPose(position, rotation));
    }

    /// <summary>
    /// 스폰 포즈를 즉시 적용하고, 이후 3초간 조작으로는 불가능한 순간이동(프레임당 2m 초과)이 감지되면
    /// 한 번 더 원복한다 — NGO 포스트 스폰 덮어쓰기가 RPC 처리 이후에 올 수 있어서다.
    /// </summary>
    /// <param name="position">유지할 스폰 위치.</param>
    /// <param name="rotation">유지할 스폰 회전.</param>
    private System.Collections.IEnumerator EnforceSpawnPose(Vector3 position, Quaternion rotation)
    {
        ForcePose(position, rotation);

        Vector3 last = transform.position;
        float end = Time.time + 3f;
        while (Time.time < end)
        {
            yield return null;
            Vector3 now = transform.position;
            if ((now - last).sqrMagnitude > 4f)
            {
                ForcePose(position, rotation);
                yield break;
            }
            last = transform.position;
        }
    }

    /// <summary>권한자(소유자) 기준으로 포즈를 강제 적용한다. NetworkTransform 텔레포트 + Rigidbody 동기화.</summary>
    /// <param name="position">적용할 위치.</param>
    /// <param name="rotation">적용할 회전.</param>
    private void ForcePose(Vector3 position, Quaternion rotation)
    {
        networkTransform.Teleport(position, rotation, transform.localScale);
        body.position = position;
        body.rotation = rotation;
        body.linearVelocity = Vector3.zero;
    }

    /// <summary>
    /// 서버가 지정한 포즈를 소유자가 즉시 1회 적용한다(벽장 진입/탈출 앵커 스냅 등).
    /// 플레이어 NetworkTransform 이 소유자 권위라 서버가 직접 위치를 쓸 수 없어 소유자에게 위임한다.
    /// 스폰 경로(<see cref="SetSpawnPoseClientRpc"/>)와 달리 감시 코루틴이 없다 — 짧은 간격의 연속 스냅(진입↔탈출)을 서로 되돌리지 않기 위함.
    /// yaw/pitch 필드와 시야 콘 기준도 함께 동기화한다 — 빼먹으면 다음 프레임 HandleLook 이 옛 시선으로 회전을 되돌린다.
    /// </summary>
    /// <param name="position">적용할 위치.</param>
    /// <param name="rotation">적용할 회전(정면). yaw 로 흡수되고 pitch 는 수평으로 초기화한다.</param>
    [ClientRpc]
    public void SnapPoseClientRpc(Vector3 position, Quaternion rotation)
    {
        if (!IsOwner) return;

        ForcePose(position, rotation);
        yaw = rotation.eulerAngles.y;
        bodyYaw = yaw;
        pitch = 0f;
        cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        hiddenBaseYaw = yaw; // 은신 스냅이면 콘 기준을 앵커 정면으로 재설정. 비은신 스냅에서는 쓰이지 않는 값이라 무해하다.
    }

    /// <summary>네트워크 디스폰 시 소유자에 한해 구독을 해제한다. 액션맵 비활성화는 ClientGameManager 소관이다.</summary>
    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;

        inputReader.MoveEvent -= OnMoveInput;
        inputReader.LookEvent -= OnLookInput;
        // inputReader.JumpEvent -= OnJumpInput; // TODO(점프 삭제 예정)
        inputReader.CrouchEvent -= OnCrouchInput;
        inputReader.CrouchCanceledEvent -= OnCrouchCanceledInput;
        inputReader.AttackEvent -= OnAttackInput;
        inputReader.AttackCanceledEvent -= OnAttackCanceledInput;
        deathHandler.IsDead.OnValueChanged -= HandleInactiveChanged;
        playerReturn.HasExtracted.OnValueChanged -= HandleInactiveChanged;
        disguise.DisguiseChanged -= HandleDisguiseChanged;
        changeMouseSensitivityEvent.OnEventRaised -= HandleMouseSensitivityChanged;

        // 활성 Vcam 을 끈다. eyeVcam/disguiseVcam 은 이 오브젝트의 자식이라 디스폰과 함께 파괴되므로,
        // CameraManager 가 파괴된 참조를 들고 있지 않도록 명시적으로 null 을 발행해 비운다.
        onCameraChanged.RaiseEvent(null);
    }

    /// <summary>렌더 프레임마다 시선 회전을 처리한다. 회전은 물리와 무관하므로 Update 에서 처리한다. 비활성이면 관전(PlayerSpectatorController)이 카메라를 쥐므로 처리하지 않는다.</summary>
    private void Update()
    {
        if (!IsOwner || IsInactive) return;

        // 은신 진입 순간(false→true)의 시선을 시야 콘 기준으로 잡는다. 스냅 RPC 가 나중에 도착하면 그쪽이 앵커 정면으로 갱신한다.
        bool isHidden = hiding.IsHidden;
        if (isHidden && !wasHidden) hiddenBaseYaw = yaw;
        wasHidden = isHidden;

        HandleLook();
    }

    /// <summary>
    /// 물리 스텝마다 이동을 처리한다. Rigidbody 기반이므로 FixedUpdate 에서 처리한다.
    /// 비활성이면 이동을 멈춘다(2-1) — 시신은 그 자리에 고정되고, 귀환자는 버스에 남는다.
    /// </summary>
    private void FixedUpdate()
    {
        if (!IsOwner || IsInactive) return;

        // 은신 중에는 이동을 차단하고 자리에 고정한다(2-1: WASD 무반응). 시선은 Update 쪽 콘 클램프가 맡는다.
        if (hiding.IsHidden)
        {
            // jumpRequested = false; // TODO(점프 삭제 예정)
            body.linearVelocity = new Vector3(0f, body.linearVelocity.y, 0f);
            return;
        }

        HandleMove();
        // HandleJump(); // TODO(점프 삭제 예정)
    }

    /// <summary>
    /// 사망·귀환 어느 쪽의 변화든 받아 소유자 조작을 차단한다(2-1). 상호작용·인벤·프롬프트를 끄고 잔여 속도를 지워 캐릭터를 고정한다.
    /// 이동·시선은 Update/FixedUpdate 가 IsInactive 로 게이팅한다. 부활(D18)은 MVP 미발동이라 복구는 다루지 않는다.
    /// 두 채널이 같은 핸들러를 공유하므로 인자는 쓰지 않고 현재 상태를 다시 읽는다.
    /// </summary>
    /// <param name="previous">이전 상태(미사용).</param>
    /// <param name="current">새 상태(미사용).</param>
    private void HandleInactiveChanged(bool previous, bool current)
    {
        if (!IsInactive) return;

        interactor.enabled = false;
        inventory.enabled = false;
        promptCanvas.enabled = false;
        itemUser.enabled = false;
        aimIndicator.enabled = false; // OnDisable 에서 궤적선을 스스로 지운다 — 관전 화면에 남지 않는다
        body.linearVelocity = Vector3.zero;
        body.useGravity = false; // PlayerBodyVisibility 가 콜라이더를 끈 뒤에도 중력이 남으면 계속 낙하해 위치가 흘러간다

        // Vcam 을 꺼서 CinemachineBrain 이 Camera.main 을 계속 이쪽으로 붙잡지 않게 한다.
        // 그러지 않으면 PlayerSpectatorController 가 궤도로 옮긴 Camera.main 트랜스폼을 Brain 이 매 프레임 되돌린다.
        onCameraChanged.RaiseEvent(null);
    }

    /// <summary>
    /// 앱 포커스를 잃는 순간 입력 캐시를 전부 리셋한다.
    /// 포커스 이탈 시 키 release 이벤트가 게임에 도착하지 않아 마지막 입력이 눌린 채로 박제되고,
    /// 이후 새 입력과 합성돼 유령 이동·방향 오염이 생기는 문제 방지(멀티 창 테스트·알트탭 공통).
    /// </summary>
    /// <param name="hasFocus">앱 포커스 획득 여부.</param>
    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus) return;

        moveInput = Vector2.zero;
        lookInput = Vector2.zero;
        // jumpRequested = false; // TODO(점프 삭제 예정)
        SetCrouching(false);
    }

    // ── 입력 콜백 — 캐시 갱신만 한다 ────────────────────────────
    // Move 는 Value 액션이라 값이 바뀔 때만 콜백이 온다. 키를 누른 채 정지해 있으면
    // 콜백이 더는 오지 않으므로, 콜백에서 직접 이동시키면 한 프레임만 움직인다.

    /// <summary>이동 입력을 캐시한다.</summary>
    /// <param name="value">정규화되지 않은 2D 이동 입력값.</param>
    private void OnMoveInput(Vector2 value)
    {
        moveInput = value;
    }

    /// <summary>시선 입력을 캐시한다.</summary>
    /// <param name="value">포인터 delta 또는 스틱 입력값.</param>
    private void OnLookInput(Vector2 value)
    {
        lookInput = value;
    }

    // TODO(점프 삭제 예정):
    // /// <summary>
    // /// 점프 입력을 캐시한다. 실제 물리 처리는 FixedUpdate 의 HandleJump 가 맡는다.
    // /// 입력 콜백은 물리 스텝과 타이밍이 다르므로 여기서 직접 속도를 건드리지 않는다.
    // /// </summary>
    // private void OnJumpInput()
    // {
    //     jumpRequested = true;
    // }

    /// <summary>웅크리기 버튼을 누르기 시작했을 때 호출된다. 웅크림 상태로 진입한다.</summary>
    private void OnCrouchInput()
    {
        SetCrouching(true);
    }

    /// <summary>웅크리기 버튼에서 손을 뗐을 때 호출된다. 웅크림 상태를 해제한다.</summary>
    private void OnCrouchCanceledInput()
    {
        SetCrouching(false);
    }

    /// <summary>
    /// 웅크림 상태를 로컬 캐시와 네트워크 사본에 함께 반영한다.
    /// 네트워크 쓰기는 소유자 권한이므로, 비소유자·스폰 전에는 로컬 캐시만 갱신한다(예외 방지).
    /// </summary>
    /// <param name="value">적용할 웅크림 상태.</param>
    private void SetCrouching(bool value)
    {
        isCrouching = value;
        if (IsSpawned && IsOwner) networkCrouching.Value = value;
    }

    /// <summary>공격 입력을 받아 공격 처리를 호출한다.</summary>
    private void OnAttackInput()
    {
        HandleAttack();
    }

    /// <summary>공격 버튼에서 손을 뗐을 때 호출된다. 아직 처리할 동작이 없다.</summary>
    private void OnAttackCanceledInput()
    {
    }

    /// <summary>설정창에서 감도가 바뀌면 배율을 갱신한다. 다음 프레임 HandleLook 부터 즉시 반영된다.</summary>
    /// <param name="multiplier">새 감도 배율.</param>
    private void HandleMouseSensitivityChanged(float multiplier)
    {
        sensitivityMultiplier = multiplier;
    }

    /// <summary>
    /// 위장 상태 변화에 맞춰 1인칭 eyeVcam 과 숄더뷰 disguiseVcam 사이를 전환한다.
    /// 둘 다 정식 Vcam 이라 CinemachineBrain 의 기본 블렌드로 자연스럽게 전환된다 — 수동 트랜스폼 보정이 필요 없다.
    /// </summary>
    /// <param name="isDisguised">변경된 위장 상태.</param>
    private void HandleDisguiseChanged(bool isDisguised)
    {
        if (!IsOwner) return;

        onCameraChanged.RaiseEvent(isDisguised ? disguiseVcam : eyeVcam);
    }

    // ── 실제 행동 ───────────────────────────────────────────────

    /// <summary>
    /// 캐시된 시선 입력으로 yaw/pitch 를 누적하고, 시선(cameraPivot)과 하체(bodyYaw) 회전을 분리 적용한다.
    /// 본체는 bodyYaw 로 회전하고, cameraPivot 로컬 회전이 pitch 와 시선-하체 yaw 차를 흡수한다.
    /// Look 바인딩이 &lt;Pointer&gt;/delta 이므로, 소비 후 lookInput 을 반드시 zero 로 리셋해야 한다.
    /// 리셋하지 않으면 마우스를 멈춰도 마지막 delta 가 매 프레임 재적용되어 계속 회전한다.
    /// </summary>
    private void HandleLook()
    {
        float previousBodyYaw = bodyYaw;
        float sensitivity = lookSensitivity * sensitivityMultiplier;
        yaw += lookInput.x * sensitivity;
        pitch -= lookInput.y * sensitivity;
        pitch = Mathf.Clamp(pitch, -pitchClamp, pitchClamp);

        // 은신 중에는 시선을 기준 정면(hiddenBaseYaw)의 콘 안으로 제한한다(3-5-1 문틈 시야, 3-10 wardrobe_look_angle_deg).
        if (hiding.IsHidden)
        {
            float yawDelta = Mathf.DeltaAngle(hiddenBaseYaw, yaw);
            yaw = hiddenBaseYaw + Mathf.Clamp(yawDelta, -wardrobeLookAngleDeg, wardrobeLookAngleDeg);
            pitch = Mathf.Clamp(pitch, -wardrobeLookAngleDeg, wardrobeLookAngleDeg);
            bodyYaw = hiddenBaseYaw; // 문틈 시야는 상체·카메라만 움직인다 — 하체는 앵커 정면에 고정
        }
        else
        {
            HandleBodyTurn();
        }

        // 하체 회전 각속도. 은신 고정이든 시선 추종이든 bodyYaw 가 확정된 뒤 한 번만 재면 두 경로가 모두 잡힌다.
        bodyYawRateDeg = Time.deltaTime > 0f
            ? Mathf.DeltaAngle(previousBodyYaw, bodyYaw) / Time.deltaTime
            : 0f;

        transform.rotation = Quaternion.Euler(0f, bodyYaw, 0f);
        cameraPivot.localRotation = Quaternion.Euler(pitch, Mathf.DeltaAngle(bodyYaw, yaw), 0f);

        // <Pointer>/delta 는 이번 프레임에 소비하고 반드시 zero 로 리셋한다. 그러지 않으면
        // 마우스를 멈춰도 마지막 delta 가 매 프레임 재적용되어 계속 회전한다.
        lookInput = Vector2.zero;
    }

    /// <summary>
    /// 하체(bodyYaw)를 시선에 맞춰 따라 돌린다.
    /// 이동 중에는 시선으로 정렬하고, 정지 중에는 시선-하체 차가 상체 한계각을 넘었을 때만 한계 안으로 끌어온다.
    /// 위장 중에는 입력이 없어도 계속 전진하므로 이동 중과 같게 취급한다 — 그러지 않으면 상체 한계각에
    /// 걸려 몸은 옆을 본 채 게걸음으로 나아간다.
    /// </summary>
    private void HandleBodyTurn()
    {
        float offset = Mathf.DeltaAngle(bodyYaw, yaw);
        float step = bodyTurnSpeedDeg * Time.deltaTime;

        if (disguise.IsDisguised || moveInput.sqrMagnitude > 0.0001f)
        {
            bodyYaw = Mathf.MoveTowardsAngle(bodyYaw, yaw, step);
        }
        else if (Mathf.Abs(offset) > upperBodyYawLimitDeg)
        {
            bodyYaw = Mathf.MoveTowardsAngle(bodyYaw, yaw - Mathf.Sign(offset) * upperBodyYawLimitDeg, step);
        }
    }

    /// <summary>월드 속도를 본체 로컬 수평 성분으로 바꾼다. 이동 방향이 정면 기준 어느 쪽인지 재는 용도다.</summary>
    /// <param name="world">월드 좌표계 속도.</param>
    /// <returns>x=우, y=전 성분(m/s).</returns>
    private Vector2 ToLocalPlanar(Vector3 world)
    {
        Vector3 local = transform.InverseTransformDirection(world);
        return new Vector2(local.x, local.z);
    }

    /// <summary>
    /// 캐시된 이동 입력을 yaw 기준 월드 방향으로 변환해 Rigidbody 의 수평 속도를 설정한다.
    /// dynamic Rigidbody 에는 MovePosition 을 쓰지 않는다 — 솔버가 그 스텝의 속도를 목표 지점에
    /// 맞춰 덮어써 중력이 뭉개지므로(공중에서 스르륵 내려온다), Y 속도는 중력에 맡기고 X/Z 만 설정한다.
    /// </summary>
    private void HandleMove()
    {
        // 위장 중에는 WASD 를 아예 읽지 않고 시선 정면으로만 자동 전진한다 — 조향 수단은 마우스뿐이다.
        // 속도도 웅크림과 겹치지 않는 단일 배율로 덮어쓴다(웅크려도 느려지지 않는다):
        // 위장은 "좀비 걸음을 흉내 내는 고정 보행"이라 플레이어가 속도를 고를 여지를 두지 않는다.
        bool disguised = disguise.IsDisguised;

        // transform 의 회전은 Rigidbody 보간이 매 프레임 물리 몸체의 옛 회전으로 되돌려 쓸 수 있어
        // FixedUpdate 시점에 낡은 값이 걸린다(시선은 꺾였는데 이동은 이전 방향으로 가는 간헐 버그).
        // 카메라와 같은 근원인 yaw 필드로 직접 방향을 계산해 시선·이동을 항상 일치시킨다.
        // 전진 방향은 pitch 를 뺀 yaw 평면 정면이다 — 올려다본다고 떠오르거나 내려다본다고 느려지지 않는다.
        Vector3 local = disguised ? Vector3.forward : new Vector3(moveInput.x, 0f, moveInput.y);
        Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * local;

        float speed = disguised
            ? moveSpeed * disguiseSpeedMultiplier
            : isCrouching ? moveSpeed * crouchSpeedMultiplier : moveSpeed;

        Vector3 v = dir.normalized * speed;
        body.linearVelocity = new Vector3(v.x, body.linearVelocity.y, v.z);
    }

    // TODO(점프 삭제 예정):
    // /// <summary>
    // /// 점프 요청이 있고 접지 상태라면 Y 속도를 jumpSpeed 로 설정해 띄움
    // /// 요청은 접지 성공/실패와 무관하게 이번 스텝에 소비
    // /// 남겨 두면 공중에서 누른 입력이 착지하는 순간 발동해 의도치 않은 점프가 튀는걸 방지
    // /// </summary>
    // private void HandleJump()
    // {
    //     if (!jumpRequested) return;
    //     jumpRequested = false;
    //
    //     if (!IsGrounded()) return;
    //
    //     Vector3 v = body.linearVelocity;
    //     body.linearVelocity = new Vector3(v.x, jumpSpeed, v.z);
    //
    //     JumpPerformed?.Invoke();
    // }

    /// <summary>
    /// 캡슐 중심에서 아래로 SphereCast 해 접지 여부를 판정한다.
    /// Player 는 Default, 바닥은 Ground 레이어이므로 groundMask 로 자기 자신은 걸러진다.
    /// </summary>
    /// <returns>발밑 groundCheckDistance 안에 groundMask 콜라이더가 있으면 true.</returns>
    private bool IsGrounded()
    {
        return Physics.SphereCast(
            transform.position,
            groundCheckRadius,
            Vector3.down,
            out _,
            groundCheckDistance,
            groundMask,
            QueryTriggerInteraction.Ignore);
    }

    /// <summary>카메라 전방으로 Raycast 해 상호작용 대상을 찾는다.</summary>
    private void HandleAttack()
    {
        // TODO: cameraPivot 전방 Raycast → 상호작용 처리
        Log.D("[PlayerController] Attack");
    }
}
