using Border.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// GameInput 의 Gameplay 액션맵 콜백을 받아 게임 로직용 이벤트로 중계하는 ScriptableObject.
/// ScriptableObject 라 프로세스 전역 단일 인스턴스로 동작하므로, 구독자는 자신이 입력 주체일 때만 구독해야 한다.
/// </summary>
[CreateAssetMenu(fileName = "InputReader", menuName = "Game/Input Reader")]
public class InputReader : ScriptableObject, GameInput.IGameplayActions
{
    /// <summary>이동 입력값이 갱신될 때 발행된다. 입력이 없어지면 Vector2.zero 로 한 번 발행된다.</summary>
    public event UnityAction<Vector2> MoveEvent = delegate { };

    /// <summary>시선 입력값(포인터 delta)이 갱신될 때 발행된다.</summary>
    public event UnityAction<Vector2> LookEvent = delegate { };

    /// <summary>공격 버튼이 눌렸을 때 발행된다.</summary>
    public event UnityAction AttackEvent = delegate { };

    /// <summary>공격 버튼에서 손을 뗐을 때 발행된다.</summary>
    public event UnityAction AttackCanceledEvent = delegate { };

    /// <summary>점프 버튼이 눌렸을 때 발행된다.</summary>
    public event UnityAction JumpEvent = delegate { };

    /// <summary>일시정지 버튼이 눌렸을 때 발행된다.</summary>
    public event UnityAction PauseEvent = delegate { };

    private GameInput _gameInput;

    /// <summary>
    /// SO 활성화 시 GameInput 을 1회 생성하고 Gameplay 액션맵의 콜백 수신자로 자신을 등록한다.
    /// 액션맵 Enable 은 하지 않는다 — 활성화 시점은 소유자(EnableGameplayInput)가 결정한다.
    /// </summary>
    private void OnEnable()
    {
        if (_gameInput == null)
        {
            _gameInput = new GameInput();
            _gameInput.Gameplay.SetCallbacks(this);
        }
    }

    /// <summary>SO 비활성화 시 모든 액션맵을 끈다.</summary>
    private void OnDisable()
    {
        DisableAllInput();
    }

    /// <summary>Gameplay 액션맵을 활성화해 입력 수신을 시작한다.</summary>
    public void EnableGameplayInput()
    {
        _gameInput.Gameplay.Enable();
    }

    /// <summary>모든 액션맵을 비활성화해 입력 수신을 중단한다.</summary>
    public void DisableAllInput()
    {
        _gameInput.Gameplay.Disable();
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

    // ── 미사용 액션 ─────────────────────────────────────────────
    // Gameplay 맵에 남아 있는 템플릿 잔재. 인터페이스 구현을 위해 스텁만 둔다.

    /// <summary>Interact 액션 콜백. 아직 사용하지 않는다.</summary>
    /// <param name="context">Input System 이 전달하는 콜백 컨텍스트.</param>
    public void OnInteract(InputAction.CallbackContext context)
    {
        Log.D($"[InputReader] Interact {context.phase}");
    }

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
}
