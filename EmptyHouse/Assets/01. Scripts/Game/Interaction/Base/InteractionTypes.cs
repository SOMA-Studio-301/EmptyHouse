/// <summary>
/// Interactable의 상호작용 프롬프트 표시 상태 (조작상호작용UI.md 3-3).
/// 사거리·활성 조건에 따라 UI가 어떻게 그릴지 결정한다.
/// </summary>
public enum InteractPromptState
{
    Hidden, // 프롬프트를 그리지 않는다. 사거리 밖이거나 상호작용 대상에서 완전히 제외된 상태(예: 개방 완료된 자물쇠)
    Inactive, // 비활성 프롬프트(회색)를 그린다. 조건 미충족 사유만 표시하고 입력키는 표기하지 않음
    Active // 활성 프롬프트를 그린다. E 입력 시 실행
}

/// <summary>
/// Interactable의 입력 방식. UI 프롬프트 문자열 구성과 PlayerInteractor의 입력 라우팅에 쓰인다.
/// </summary>
public enum InteractInputMethod
{
    Tap, // 탭 1회로 즉시 실행
    Hold // 지정된 시간만큼 눌러 유지해야 실행
}

/// <summary>
/// Interactable이 매 프레임 UI에 보고하는 프롬프트 정보
/// UI는 이 값을 그대로 그릴 뿐, 대상 타입으로 분기하지 않는다.
/// </summary>
public readonly struct InteractPromptInfo
{
    /// <summary>표시 상태.</summary>
    public readonly InteractPromptState State;

    /// <summary>행위명. 예: "회수", "열기", "분장 도포".</summary>
    public readonly string ActionName;

    /// <summary>입력 방식. UI가 "[E]" 또는 "[E 홀드]" 접두사를 구성하는 데 쓴다.</summary>
    public readonly InteractInputMethod InputMethod;

    /// <summary>홀드 입력일 때 필요한 유지 시간(초). Tap이면 의미 없음(0).</summary>
    public readonly float HoldDurationSeconds;

    /// <summary>비활성 사유 문구. State가 Inactive일 때만 사용한다. 입력키를 표기하지 않는다.</summary>
    public readonly string InactiveReason;

    /// <summary>모든 필드를 지정해 프롬프트 정보를 생성한다.</summary>
    /// <param name="state">표시 상태.</param>
    /// <param name="actionName">행위명.</param>
    /// <param name="inputMethod">입력 방식.</param>
    /// <param name="holdDurationSeconds">홀드 유지 시간(초).</param>
    /// <param name="inactiveReason">비활성 사유 문구.</param>
    public InteractPromptInfo(InteractPromptState state, string actionName, InteractInputMethod inputMethod, float holdDurationSeconds, string inactiveReason)
    {
        State = state;
        ActionName = actionName;
        InputMethod = inputMethod;
        HoldDurationSeconds = holdDurationSeconds;
        InactiveReason = inactiveReason;
    }
}
