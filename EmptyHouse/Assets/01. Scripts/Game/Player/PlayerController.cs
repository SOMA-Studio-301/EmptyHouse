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
    [SerializeField] private float moveSpeed = 5f;

    private Rigidbody body;

    // 입력 캐시 — 콜백이 쓰고 Update/FixedUpdate 가 읽는다.
    private Vector2 moveInput;
    private Vector2 lookInput;

    // 시선 상태 — pitch 는 cameraPivot 의 로컬 X, yaw 는 본체의 Y 회전.
    private float pitch;
    private float yaw;

    /// <summary>Rigidbody 참조를 캐시한다.</summary>
    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    /// <summary>
    /// 네트워크 스폰 시 소유자에 한해 입력 이벤트를 구독하고 Gameplay 입력을 활성화한다.
    /// 비소유자는 구독하지 않으므로 호스트 프로세스에서 남의 캐릭터가 내 입력을 받지 않는다.
    /// </summary>
    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        inputReader.MoveEvent += OnMoveInput;
        inputReader.LookEvent += OnLookInput;
        inputReader.AttackEvent += OnAttackInput;
        inputReader.AttackCanceledEvent += OnAttackCanceledInput;

        inputReader.EnableGameplayInput();
    }

    /// <summary>네트워크 디스폰 시 소유자에 한해 구독을 해제하고 입력을 비활성화한다.</summary>
    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;

        inputReader.MoveEvent -= OnMoveInput;
        inputReader.LookEvent -= OnLookInput;
        inputReader.AttackEvent -= OnAttackInput;
        inputReader.AttackCanceledEvent -= OnAttackCanceledInput;

        inputReader.DisableAllInput();
    }

    /// <summary>렌더 프레임마다 시선 회전을 처리한다. 회전은 물리와 무관하므로 Update 에서 처리한다.</summary>
    private void Update()
    {
        if (!IsOwner) return;

        HandleLook();
    }

    /// <summary>물리 스텝마다 이동을 처리한다. Rigidbody 기반이므로 FixedUpdate 에서 처리한다.</summary>
    private void FixedUpdate()
    {
        if (!IsOwner) return;

        HandleMove();
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
        // TODO: yaw += lookInput.x * lookSensitivity; pitch -= lookInput.y * lookSensitivity;
        // TODO: pitch = Mathf.Clamp(pitch, -pitchClamp, pitchClamp);
        // TODO: transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        // TODO: cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        // TODO: lookInput = Vector2.zero;
        Log.D("[PlayerController] Look: " + lookInput);
    }

    /// <summary>캐시된 이동 입력을 yaw 기준 월드 방향으로 변환해 Rigidbody 를 이동시킨다.</summary>
    private void HandleMove()
    {
        // TODO: 로컬 방향 = transform.right * moveInput.x + transform.forward * moveInput.y
        // TODO: body.MovePosition(body.position + direction.normalized * moveSpeed * Time.fixedDeltaTime);
        Log.D("[PlayerController] Move: " + moveInput);
    }

    /// <summary>카메라 전방으로 Raycast 해 상호작용 대상을 찾는다.</summary>
    private void HandleAttack()
    {
        // TODO: cameraPivot 전방 Raycast → 상호작용 처리
        Log.D("[PlayerController] Attack");
    }
}
