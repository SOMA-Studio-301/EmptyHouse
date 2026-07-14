using Border.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// GameInput 의 Gameplay 액션맵 콜백을 받아 게임 로직용 이벤트로 중계하는 ScriptableObject.
/// ScriptableObject 라 프로세스 전역 단일 인스턴스로 동작하므로, 구독자는 자신이 입력 주체일 때만 구독해야 한다.
/// </summary>
[CreateAssetMenu(fileName = "InputReader", menuName = "Game/Input Reader")]
public class InputReader : ScriptableObject, GameInput.IGameplayActions, GameInput.IUIActions
{
    public event UnityAction<Vector2> MoveEvent = delegate { }; // 이동 입력값이 갱신될 때 발행. 입력이 없어지면 Vector2.zero 한 번 발행
    public event UnityAction<Vector2> LookEvent = delegate { }; // 시선 입력값(포인터 delta)이 갱신될 때 발행
    public event UnityAction AttackEvent           = delegate { }; // 공격 버튼이 눌렸을 때 발행
    public event UnityAction AttackCanceledEvent   = delegate { }; // 공격 버튼에서 손을 뗐을 때 발행
    public event UnityAction InteractPressedEvent  = delegate { }; // 상호작용 버튼을 누르기 시작했을 때 발행. Tap형은 즉시 실행, Hold형은 홀드 진행 시작 신호
    public event UnityAction InteractCanceledEvent = delegate { }; // 상호작용 버튼에서 손을 뗐을 때 발행. 홀드 진행 중이었다면 취소 신호
    public event UnityAction<int> EquipSlotEvent = delegate { }; // 숫자 키로 슬롯 선택. payload = 슬롯 인덱스(0-based). ※ 임시: Tab 홀드 없이 단독 입력 — Tab 게이팅(2장 키맵)은 후속
    public event UnityAction<int> CycleHandEvent = delegate { }; // 마우스 휠로 손 순환. payload = 방향(+1 정방향 / -1 역방향)
    public event UnityAction JumpEvent   = delegate { }; // 점프 버튼이 눌렸을 때 발행
    public event UnityAction PauseEvent  = delegate { }; // 일시정지 버튼이 눌렸을 때 발행
    public event UnityAction CancelEvent = delegate { }; // UI 맵의 Cancel(Esc) 이 눌렸을 때 발행. Pause 상태에서 게임으로 복귀하는 경로

    private GameInput gameInput;

    // 마지막으로 입력이 들어온 디바이스. 프롬프트에 어느 스킴의 바인딩을 보여줄지 고르는 기준이다.
    // Interact 는 Keyboard&Mouse(E)·Gamepad 두 벌로 바인딩돼 있어, 스킴으로 거르지 않으면 "E | Y" 처럼 둘 다 나온다.
    private InputDevice lastUsedDevice;

    /// <summary>
    /// SO 활성화 시 GameInput 을 1회 생성하고 Gameplay/UI 두 액션맵의 콜백 수신자로 자신을 등록한다.
    /// UI 맵에서 실제로 중계하는 것은 Cancel 뿐이다 — 나머지 UI 액션은 UGUI 입력 모듈 몫이라 빈 스텁으로 둔다.
    /// 등록 직후 Gameplay 맵을 활성화해 기본 입력 상태로 진입한다.
    /// </summary>
    private void OnEnable()
    {
        if (gameInput == null)
        {
            gameInput = new GameInput();
            gameInput.Gameplay.SetCallbacks(this);
            gameInput.UI.SetCallbacks(this);
            EnableGameplayInput();
        }
    }

    /// <summary>SO 비활성화 시 모든 액션맵을 끈다.</summary>
    private void OnDisable()
    {
        DisableAllInput();
    }

    /// <summary>Gameplay 액션맵을 활성화하고 UI 액션맵을 비활성화한다. Game 상태에서 사용한다.</summary>
    public void EnableGameplayInput()
    {
        gameInput.UI.Disable();
        gameInput.Gameplay.Enable();
    }

    /// <summary>UI 액션맵을 활성화하고 Gameplay 액션맵을 비활성화한다. Menu/Pause 상태에서 사용한다.</summary>
    public void EnableUIInput()
    {
        gameInput.Gameplay.Disable();
        gameInput.UI.Enable();
    }

    /// <summary>모든 액션맵을 비활성화해 입력 수신을 중단한다.</summary>
    public void DisableAllInput()
    {
        gameInput.Disable();
    }

    /// <summary>
    /// 현재 Interact 액션에 바인딩된 키의 표시 문자열을 반환한다(예: "E").
    /// 프롬프트가 입력키를 하드코딩하지 않고 이 값을 조회하므로, 리바인드하면 UI 가 따라 바뀐다(조작상호작용UI.md 3-3).
    /// </summary>
    /// <returns>현재 바인딩된 키의 표시 문자열.</returns>
    public string GetInteractBindingDisplayString()
    {
        Log.D("[InputReader] GetInteractBindingDisplayString");

        // 그룹(=컨트롤 스킴)으로 거르지 않으면 Interact 에 걸린 모든 바인딩이 "E | Y" 처럼 이어붙어 나온다.
        return gameInput.Gameplay.Interact.GetBindingDisplayString(group: ResolveActiveBindingGroup());
    }

    /// <summary>
    /// 마지막으로 입력이 들어온 디바이스가 속한 컨트롤 스킴의 바인딩 그룹명을 반환한다.
    /// 아직 아무 입력도 없었거나(첫 프레임) 어느 스킴에도 속하지 않는 디바이스면 키보드, 마우스가 기본
    /// </summary>
    /// <returns>바인딩 그룹명(예: "Keyboard&amp;Mouse", "Gamepad").</returns>
    private string ResolveActiveBindingGroup()
    {
        if (lastUsedDevice != null)
        {
            InputControlScheme? scheme = InputControlScheme.FindControlSchemeForDevice(lastUsedDevice, gameInput.controlSchemes);
            if (scheme.HasValue) return scheme.Value.bindingGroup;
        }

        return gameInput.KeyboardMouseScheme.bindingGroup;
    }

    /// <summary>
    /// 이번 입력을 발생시킨 디바이스를 기억한다. 프롬프트가 보여줄 바인딩 스킴을 고르는 기준이 된다(<see cref="GetInteractBindingDisplayString"/>).
    /// 컨트롤이 없는 콜백도 있으므로(그 경우 직전 디바이스를 유지한다) 여기서는 의도적으로 null 을 허용한다.
    /// </summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    private void RememberDevice(InputAction.CallbackContext context)
    {
        InputDevice device = context.control?.device;
        if (device != null)
        {
            lastUsedDevice = device;
        }
    }

    /// <summary>Move 액션 콜백. 갱신된 이동 입력값을 <see cref="MoveEvent"/> 로 발행한다.</summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnMove(InputAction.CallbackContext context)
    {
        RememberDevice(context);

        switch (context.phase)
        {
            case InputActionPhase.Performed:
            case InputActionPhase.Canceled:
                MoveEvent.Invoke(context.ReadValue<Vector2>());
                break;
        }
    }

    /// <summary>Look 액션 콜백. 갱신된 시선 입력값을 <see cref="LookEvent"/> 로 발행한다.</summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnLook(InputAction.CallbackContext context)
    {
        RememberDevice(context);

        switch (context.phase)
        {
            case InputActionPhase.Performed:
            case InputActionPhase.Canceled:
                LookEvent.Invoke(context.ReadValue<Vector2>());
                break;
        }
    }

    /// <summary>Attack 액션 콜백. 눌림은 <see cref="AttackEvent"/>, 뗌은 <see cref="AttackCanceledEvent"/> 로 발행한다.</summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnAttack(InputAction.CallbackContext context)
    {
        RememberDevice(context);

        switch (context.phase)
        {
            case InputActionPhase.Performed:
                AttackEvent.Invoke();
                break;
            case InputActionPhase.Canceled:
                AttackCanceledEvent.Invoke();
                break;
        }
    }

    /// <summary>Jump 액션 콜백. 눌림을 <see cref="JumpEvent"/> 로 발행한다.</summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnJump(InputAction.CallbackContext context)
    {
        RememberDevice(context);

        if (context.phase == InputActionPhase.Performed)
        {
            JumpEvent.Invoke();
        }
    }

    /// <summary>Pause 액션 콜백. 눌림을 <see cref="PauseEvent"/> 로 발행한다.</summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnPause(InputAction.CallbackContext context)
    {
        if (context.phase == InputActionPhase.Performed)
        {
            PauseEvent.Invoke();
        }
    }

    /// <summary>
    /// Interact 액션 콜백. Performed phase에 <see cref="InteractPressedEvent"/>, Canceled phase에 <see cref="InteractCanceledEvent"/> 로 중계한다.
    /// 홀드 유지 시간은 대상마다 다르므로(<see cref="HoldInteractableBase.HoldDurationSeconds"/>) 액션에 Hold interaction 을 걸지 않는다.
    /// 누름(Performed)과 뗌(Canceled) 시점만 중계하고, 그 사이 시간 누적은 Interactable 이 판정한다.
    /// </summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnInteract(InputAction.CallbackContext context)
    {
        RememberDevice(context);

        if (context.phase == InputActionPhase.Performed)
        {
            InteractPressedEvent.Invoke();
        }
        else if (context.phase == InputActionPhase.Canceled)
        {
            InteractCanceledEvent.Invoke();
        }
    }

    /// <summary>
    /// EquipSlot 액션 콜백(숫자 키). 슬롯 하나당 액션을 만들지 않고 액션 1개에 키 1~5 를 바인딩
    /// 눌린 키을 0-based 인덱스로 바꿔 <see cref="EquipSlotEvent"/> 로 발행
    /// 슬롯이 늘어도 inputactions 에 바인딩만 추가하면 되고 이 코드는 바뀌지 않음
    /// 실제 슬롯 수를 넘는 번호인지는 여기서 거르지 않는다 — 슬롯 수를 아는 <see cref="PlayerInventory"/> 몫이다.
    /// </summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnEquipSlot(InputAction.CallbackContext context)
    {
        RememberDevice(context);

        if (context.phase != InputActionPhase.Performed) return;

        // 키보드 숫자 키의 컨트롤 이름은 "1".."5" 다. 숫자가 아닌 컨트롤이 바인딩되면 조용히 무시한다.
        if (!int.TryParse(context.control.name, out int slotNumber)) return;

        EquipSlotEvent.Invoke(slotNumber - 1);
    }

    /// <summary>
    /// CycleHand 액션 콜백(마우스 휠, Value/Axis). 스크롤값의 부호를 ±1 로 정규화해
    /// <see cref="CycleHandEvent"/> 로 발행한다. 0 이면 발행하지 않는다.
    /// </summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnCycleHand(InputAction.CallbackContext context)
    {
        RememberDevice(context);

        if (context.phase != InputActionPhase.Performed) return;

        float scroll = context.ReadValue<float>();
        if (Mathf.Approximately(scroll, 0f)) return;

        CycleHandEvent.Invoke(scroll > 0f ? 1 : -1);
    }

    // ── 미사용 액션 ─────────────────────────────────────────────
    // Gameplay 맵에 남아 있는 템플릿 잔재. 인터페이스 구현을 위해 스텁만 둔다.

    /// <summary>Crouch 액션 콜백. 아직 사용하지 않는다.</summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnCrouch(InputAction.CallbackContext context)
    {
        Log.D($"[InputReader] Crouch {context.phase}");
    }

    /// <summary>Sprint 액션 콜백. 아직 사용하지 않는다.</summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnSprint(InputAction.CallbackContext context)
    {
        Log.D($"[InputReader] Sprint {context.phase}");
    }

    // ── UI 액션 ─────────────────────────────────────────────────
    // Cancel 만 게임 로직으로 중계한다. 나머지는 UGUI 입력 모듈(InputSystemUIInputModule)이
    // 액션을 직접 읽어 처리하므로 여기서는 인터페이스 충족용 빈 스텁으로 둔다.
    // Navigate/Point/ScrollWheel 은 포인터가 움직이는 매 프레임 들어오므로 로그를 남기지 않는다.

    /// <summary>UI/Cancel 액션 콜백. 눌림을 <see cref="CancelEvent"/> 로 발행한다.</summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnCancel(InputAction.CallbackContext context)
    {
        Log.D($"[InputReader] Cancel {context.phase}");

        if (context.phase == InputActionPhase.Performed)
        {
            CancelEvent.Invoke();
        }
    }

    /// <summary>UI/Navigate 액션 콜백. UGUI 입력 모듈이 처리하므로 중계하지 않는다.</summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnNavigate(InputAction.CallbackContext context) { }

    /// <summary>UI/Submit 액션 콜백. UGUI 입력 모듈이 처리하므로 중계하지 않는다.</summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnSubmit(InputAction.CallbackContext context) { }

    /// <summary>UI/Point 액션 콜백. UGUI 입력 모듈이 처리하므로 중계하지 않는다.</summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnPoint(InputAction.CallbackContext context) { }

    /// <summary>UI/Click 액션 콜백. UGUI 입력 모듈이 처리하므로 중계하지 않는다.</summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnClick(InputAction.CallbackContext context) { }

    /// <summary>UI/RightClick 액션 콜백. UGUI 입력 모듈이 처리하므로 중계하지 않는다.</summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnRightClick(InputAction.CallbackContext context) { }

    /// <summary>UI/MiddleClick 액션 콜백. UGUI 입력 모듈이 처리하므로 중계하지 않는다.</summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnMiddleClick(InputAction.CallbackContext context) { }

    /// <summary>UI/ScrollWheel 액션 콜백. UGUI 입력 모듈이 처리하므로 중계하지 않는다.</summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnScrollWheel(InputAction.CallbackContext context) { }

    /// <summary>UI/TrackedDevicePosition 액션 콜백. XR 미사용이라 중계하지 않는다.</summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnTrackedDevicePosition(InputAction.CallbackContext context) { }

    /// <summary>UI/TrackedDeviceOrientation 액션 콜백. XR 미사용이라 중계하지 않는다.</summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnTrackedDeviceOrientation(InputAction.CallbackContext context) { }
}
