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
    public event UnityAction JumpEvent   = delegate { }; // 점프 버튼이 눌렸을 때 발행
    public event UnityAction PauseEvent  = delegate { }; // 일시정지 버튼이 눌렸을 때 발행
    public event UnityAction CancelEvent = delegate { }; // UI 맵의 Cancel(Esc) 이 눌렸을 때 발행. Pause 상태에서 게임으로 복귀하는 경로

    private GameInput gameInput;

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
        // TODO(impl): gameInput.Gameplay.Interact.GetBindingDisplayString() 로 현재 활성 컨트롤 스킴의 바인딩을 조회한다.
        Log.D("[InputReader] GetInteractBindingDisplayString");
        return default;
    }

    // ── Value 액션 ──────────────────────────────────────────────
    // Performed 는 값이 바뀔 때, Canceled 는 값이 기본값(zero)으로 돌아갈 때 온다.
    // Canceled 에서도 ReadValue 는 zero 를 돌려주므로 두 phase 를 함께 처리한다.

    /// <summary>Move 액션 콜백. 갱신된 이동 입력값을 <see cref="MoveEvent"/> 로 발행한다.</summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnMove(InputAction.CallbackContext context)
    {
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
        switch (context.phase)
        {
            case InputActionPhase.Performed:
            case InputActionPhase.Canceled:
                LookEvent.Invoke(context.ReadValue<Vector2>());
                break;
        }
    }

    // ── Button 액션 ─────────────────────────────────────────────
    // interaction 이 없는 버튼은 Started 와 Performed 가 같은 프레임에 연달아 온다.
    // Started 를 함께 처리하면 한 번의 입력이 두 번 발행되므로 Performed 만 잡는다.

    /// <summary>Attack 액션 콜백. 눌림은 <see cref="AttackEvent"/>, 뗌은 <see cref="AttackCanceledEvent"/> 로 발행한다.</summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnAttack(InputAction.CallbackContext context)
    {
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
        if (context.phase == InputActionPhase.Performed)
        {
            InteractPressedEvent.Invoke();
        }
        else if (context.phase == InputActionPhase.Canceled)
        {
            InteractCanceledEvent.Invoke();
        }
    }

    // ── 미사용 액션 ─────────────────────────────────────────────
    // Gameplay 맵에 남아 있는 템플릿 잔재. 인터페이스 구현을 위해 스텁만 둔다.

    /// <summary>Crouch 액션 콜백. 아직 사용하지 않는다.</summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnCrouch(InputAction.CallbackContext context)
    {
        Log.D($"[InputReader] Crouch {context.phase}");
    }

    /// <summary>Previous 액션 콜백. 아직 사용하지 않는다.</summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnPrevious(InputAction.CallbackContext context)
    {
        Log.D($"[InputReader] Previous {context.phase}");
    }

    /// <summary>Next 액션 콜백. 아직 사용하지 않는다.</summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnNext(InputAction.CallbackContext context)
    {
        Log.D($"[InputReader] Next {context.phase}");
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
