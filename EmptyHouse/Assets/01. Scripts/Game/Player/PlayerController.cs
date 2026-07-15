using Border.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 소유 클라이언트의 입력을 받아 플레이어 캐릭터의 시선과 이동을 처리하는 네트워크 컨트롤러.
/// InputReader 는 ScriptableObject(프로세스 전역 단일 인스턴스)이므로 소유자만 구독한다.
/// </summary>
public class PlayerController : NetworkBehaviour
{
    [Header("Input")]
    [SerializeField] private InputReader inputReader;

    [Header("Look")]
    [SerializeField] private Transform cameraPivot;
    [SerializeField] private float lookSensitivity = 0.1f;
    [SerializeField] private float pitchClamp = 89f;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f;

    /// <summary>웅크림 중 이동속도 배율. moveSpeed 에 곱해 적용한다.</summary>
    [SerializeField] private float crouchSpeedMultiplier = 0.5f;

    /// <summary>점프 시작 시 설정할 상승 속도. v²/2g ≈ 1.27m 상승한다.</summary>
    [Header("Jump")]
    [SerializeField] private float jumpSpeed = 5f;

    /// <summary>접지 판정 대상 레이어. 인스펙터에서 Ground 만 선택한다.</summary>
    [SerializeField] private LayerMask groundMask;

    /// <summary>접지 판정 SphereCast 의 반지름. 캡슐 반지름(0.5)보다 작아야 벽 모서리를 바닥으로 오검출하지 않는다.</summary>
    [SerializeField] private float groundCheckRadius = 0.4f;

    /// <summary>접지 판정 SphereCast 의 거리. 중심→바닥 1.0 - 반지름 0.4 + 여유 0.1.</summary>
    [SerializeField] private float groundCheckDistance = 0.7f;

    [Header("Ownership-gated")]
    [SerializeField] private PlayerInteractor interactor;
    [SerializeField] private PlayerInventory inventory;

    /// <summary>상호작용 프롬프트 전용 Canvas. 다른 HUD 요소와 분리해 두어, 프롬프트가 매 프레임 갱신돼도 그쪽 Canvas 는 리빌드되지 않는다.</summary>
    [SerializeField] private Canvas promptCanvas;

    private Rigidbody body;

    // 사망 게이팅 소스 — 형제 PlayerDeathHandler. 사망 시 이동·시선·상호작용·인벤을 차단한다(2-1·관전은 PlayerSpectatorController).
    private PlayerDeathHandler deathHandler;

    // 소유자 카메라 — OnNetworkSpawn 에서 Main Camera 를 cameraPivot 아래로 붙이고 캐시한다.
    private Transform cameraTransform;

    // 입력 캐시 — 콜백이 쓰고 Update/FixedUpdate 가 읽는다.
    private Vector2 moveInput;
    private Vector2 lookInput;

    // 점프 요청 — 입력 콜백이 세우고 FixedUpdate 의 HandleJump 가 소비한다.
    private bool jumpRequested;

    // 웅크림 상태(홀드) — Crouch 입력 콜백이 켜고 끄며, HandleMove 가 이동속도 배율에 반영한다.
    private bool isCrouching;

    // 시선 상태 — pitch 는 cameraPivot 의 로컬 X, yaw 는 본체의 Y 회전.
    private float pitch;
    private float yaw;

    /// <summary>Rigidbody 와 형제 PlayerDeathHandler 참조를 캐시한다.</summary>
    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        deathHandler = GetComponent<PlayerDeathHandler>();
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

        if (!IsOwner) return;

        inputReader.MoveEvent += OnMoveInput;
        inputReader.LookEvent += OnLookInput;
        inputReader.JumpEvent += OnJumpInput;
        inputReader.CrouchEvent += OnCrouchInput;
        inputReader.CrouchCanceledEvent += OnCrouchCanceledInput;
        inputReader.AttackEvent += OnAttackInput;
        inputReader.AttackCanceledEvent += OnAttackCanceledInput;
        deathHandler.IsDead.OnValueChanged += HandleDeadChanged;

        // 씬의 Main Camera 를 cameraPivot 아래로 붙여 pitch 회전을 따라가게 한다.
        cameraTransform = Camera.main.transform;
        cameraTransform.SetParent(cameraPivot, false);
        cameraTransform.localPosition = Vector3.zero;
        cameraTransform.localRotation = Quaternion.identity;
    }

    /// <summary>네트워크 디스폰 시 소유자에 한해 구독을 해제한다. 액션맵 비활성화는 ClientGameManager 소관이다.</summary>
    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;

        inputReader.MoveEvent -= OnMoveInput;
        inputReader.LookEvent -= OnLookInput;
        inputReader.JumpEvent -= OnJumpInput;
        inputReader.CrouchEvent -= OnCrouchInput;
        inputReader.CrouchCanceledEvent -= OnCrouchCanceledInput;
        inputReader.AttackEvent -= OnAttackInput;
        inputReader.AttackCanceledEvent -= OnAttackCanceledInput;
        deathHandler.IsDead.OnValueChanged -= HandleDeadChanged;

        // 카메라를 다시 분리한다. 이걸 생략하면 플레이어 NetworkObject 파괴 시
        // 자식으로 붙어 있는 씬의 Main Camera 가 함께 파괴된다(리스폰·로비 복귀·호스트 이주는 씬을 유지한 채 despawn 한다).
        cameraTransform.SetParent(null);
    }

    /// <summary>렌더 프레임마다 시선 회전을 처리한다. 회전은 물리와 무관하므로 Update 에서 처리한다. 사망 중이면 관전(PlayerSpectatorController)이 카메라를 쥐므로 처리하지 않는다.</summary>
    private void Update()
    {
        if (!IsOwner || deathHandler.IsDead.Value) return;

        HandleLook();
    }

    /// <summary>
    /// 물리 스텝마다 이동과 점프를 처리한다. Rigidbody 기반이므로 FixedUpdate 에서 처리한다.
    /// HandleMove 가 Y 속도를 보존하므로, 점프를 그 뒤에 두어야 상승 속도가 덮어써지지 않는다.
    /// 사망 중이면 이동을 멈춘다(2-1) — 시신은 그 자리에 고정된다.
    /// </summary>
    private void FixedUpdate()
    {
        if (!IsOwner || deathHandler.IsDead.Value) return;

        HandleMove();
        HandleJump();
    }

    /// <summary>
    /// 사망 상태 변화를 받아 소유자 조작을 차단한다(2-1). 상호작용·인벤·프롬프트를 끄고 잔여 속도를 지워 시신을 고정한다.
    /// 이동·시선은 Update/FixedUpdate 가 사망 상태로 게이팅한다. 부활(D18)은 MVP 미발동이라 복구는 다루지 않는다.
    /// </summary>
    /// <param name="previous">이전 사망 상태.</param>
    /// <param name="current">새 사망 상태.</param>
    private void HandleDeadChanged(bool previous, bool current)
    {
        if (!current) return;

        interactor.enabled = false;
        inventory.enabled = false;
        promptCanvas.enabled = false;
        body.linearVelocity = Vector3.zero;
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

    /// <summary>
    /// 점프 입력을 캐시한다. 실제 물리 처리는 FixedUpdate 의 HandleJump 가 맡는다.
    /// 입력 콜백은 물리 스텝과 타이밍이 다르므로 여기서 직접 속도를 건드리지 않는다.
    /// </summary>
    private void OnJumpInput()
    {
        jumpRequested = true;
    }

    /// <summary>웅크리기 버튼을 누르기 시작했을 때 호출된다. 웅크림 상태로 진입한다.</summary>
    private void OnCrouchInput()
    {
        Log.D("[PlayerController] Crouch");
        isCrouching = true;
    }

    /// <summary>웅크리기 버튼에서 손을 뗐을 때 호출된다. 웅크림 상태를 해제한다.</summary>
    private void OnCrouchCanceledInput()
    {
        Log.D("[PlayerController] Crouch canceled");
        isCrouching = false;
    }

    /// <summary>공격 입력을 받아 공격 처리를 호출한다.</summary>
    private void OnAttackInput()
    {
        HandleAttack();
    }

    /// <summary>공격 버튼에서 손을 뗐을 때 호출된다. 아직 처리할 동작이 없다.</summary>
    private void OnAttackCanceledInput()
    {
        Log.D("[PlayerController] Attack canceled");
    }

    // ── 실제 행동 ───────────────────────────────────────────────

    /// <summary>
    /// 캐시된 시선 입력으로 yaw/pitch 를 누적해 본체와 cameraPivot 을 회전시킨다.
    /// Look 바인딩이 &lt;Pointer&gt;/delta 이므로, 소비 후 lookInput 을 반드시 zero 로 리셋해야 한다.
    /// 리셋하지 않으면 마우스를 멈춰도 마지막 delta 가 매 프레임 재적용되어 계속 회전한다.
    /// </summary>
    private void HandleLook()
    {
        yaw += lookInput.x * lookSensitivity;
        pitch -= lookInput.y * lookSensitivity;
        pitch = Mathf.Clamp(pitch, -pitchClamp, pitchClamp);

        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        // <Pointer>/delta 는 이번 프레임에 소비하고 반드시 zero 로 리셋한다. 그러지 않으면
        // 마우스를 멈춰도 마지막 delta 가 매 프레임 재적용되어 계속 회전한다.
        lookInput = Vector2.zero;
    }

    /// <summary>
    /// 캐시된 이동 입력을 yaw 기준 월드 방향으로 변환해 Rigidbody 의 수평 속도를 설정한다.
    /// dynamic Rigidbody 에는 MovePosition 을 쓰지 않는다 — 솔버가 그 스텝의 속도를 목표 지점에
    /// 맞춰 덮어써 중력이 뭉개지므로(공중에서 스르륵 내려온다), Y 속도는 중력에 맡기고 X/Z 만 설정한다.
    /// </summary>
    private void HandleMove()
    {
        Vector3 dir = transform.right * moveInput.x + transform.forward * moveInput.y;
        float speed = isCrouching ? moveSpeed * crouchSpeedMultiplier : moveSpeed;
        Vector3 v = dir.normalized * speed;
        body.linearVelocity = new Vector3(v.x, body.linearVelocity.y, v.z);
    }

    /// <summary>
    /// 점프 요청이 있고 접지 상태라면 Y 속도를 jumpSpeed 로 설정해 띄움
    /// 요청은 접지 성공/실패와 무관하게 이번 스텝에 소비
    /// 남겨 두면 공중에서 누른 입력이 착지하는 순간 발동해 의도치 않은 점프가 튀는걸 방지
    /// </summary>
    private void HandleJump()
    {
        if (!jumpRequested) return;
        jumpRequested = false;

        if (!IsGrounded()) return;

        Vector3 v = body.linearVelocity;
        body.linearVelocity = new Vector3(v.x, jumpSpeed, v.z);

        // TODO: 점프·착지 소음 이벤트 발행 (소음 시스템 미구현 — 기획서 '행위 기반 소음')
    }

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
