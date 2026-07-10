using UnityEngine;

/// <summary>
/// 입력(PauseEvent/CancelEvent)을 받아 GameState 를 발행하고 해당 화면 루트를 토글하는 매니저.
/// 채널의 발행자이지 구독자가 아니다 — 상태 방송을 듣고 입력 액션맵·커서를 바꾸는 것은 GameManager 몫이다.
/// 전이 규칙은 검증 코드가 아니라 액션맵 활성화로 강제된다: PauseEvent 는 Gameplay 맵에서만, CancelEvent 는 UI 맵에서만 발화한다.
/// 두 이벤트를 상시 구독해도 꺼진 맵의 입력은 도달하지 않으므로 구독을 갈아끼우지 않는다.
/// HUD류(게임플레이 이벤트 구동 UI)는 관리 대상이 아니다 — 이 매니저는 상태 구동 화면만 다룬다.
/// </summary>
public class UIManager : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputReader inputReader;

    [Header("State")]
    [SerializeField] private GameStateEventChannelSO gameStateChanged;

    [Header("Screens")]
    [SerializeField] private UIPause pausePanel;

    /// <summary>Pause·Cancel 입력을 구독하고 퍼즈 패널을 닫힌 상태로 초기화한다. 씬 진입 상태는 결코 Pause 가 아니다.</summary>
    private void OnEnable()
    {
        inputReader.PauseEvent += HandlePauseInput;
        inputReader.CancelEvent += HandleCancelInput;
        pausePanel.gameObject.SetActive(false);
    }

    /// <summary>입력 구독을 해제한다. InputReader 는 SO 라 씬 밖에서 살아남으므로 죽은 델리게이트를 남기지 않는다.</summary>
    private void OnDisable()
    {
        inputReader.PauseEvent -= HandlePauseInput;
        inputReader.CancelEvent -= HandleCancelInput;
    }

    /// <summary>Gameplay 맵의 Pause 입력을 받아 Pause 상태를 발행하고 퍼즈 패널을 연다.</summary>
    private void HandlePauseInput()
    {
        gameStateChanged.RaiseEvent(GameState.Pause);
        pausePanel.gameObject.SetActive(true);
    }

    /// <summary>UI 맵의 Cancel 입력을 받아 Game 상태를 발행하고 퍼즈 패널을 닫는다. Pause 상태에서만 도달한다.</summary>
    private void HandleCancelInput()
    {
        gameStateChanged.RaiseEvent(GameState.Game);
        pausePanel.gameObject.SetActive(false);
    }
}
