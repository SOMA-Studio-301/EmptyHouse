using Border.Core;
using UnityEngine;

/// <summary>
/// 상태 구동 화면 루트를 토글하는 매니저 — 퍼즈/설정(입력 구동)과 결과창(결과 방송 구동)을 소유
/// 퍼즈/설정은 입력(PauseEvent/CancelEvent)과 화면 의도 이벤트를 받아 GameState(Pause/Game) 를 발행하고 패널을 연다.
/// 결과창은 반대로 구독이다 — GameResultEventChannelSO 를 받아 결과 패널을 켜고 확정 사유를 UIResult 에 넘긴다.
/// 결과의 GameState(Result) 발행은 같은 채널을 듣는 ClientGameManager 가 하므로 여기서는 상태를 발행하지 않는다(이중 발행 방지)
/// 설정 창은 퍼즈 위에 겹치는 것이 아니라 퍼즈를 대체한다 — 두 화면은 동시에 켜지지 않으며 둘 다 Pause 상태에 속한다.
/// HUD류(게임플레이 이벤트 구동 UI)는 관리 대상이 아니다 — 이 매니저는 상태 구동 화면만 다룬다.
/// </summary>
public class UIManager : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputReader inputReader;

    [Header("State")]
    [SerializeField] private GameStateEventChannelSO gameStateChanged;   // 발행: Pause/Game
    [SerializeField] private GameResultEventChannelSO gameResultChanged; // 구독: 결과창 열기 트리거

    [Header("Screens")]
    [SerializeField] private UIPause pausePanel;
    [SerializeField] private UISettings settingsPanel;
    [SerializeField] private UIResult resultPanel;

    /// <summary>입력·화면 의도 이벤트·결과 방송을 구독하고 모든 화면을 닫은 상태로 초기화한다. 씬 진입 상태는 결코 Pause 나 Result 가 아니다.</summary>
    private void OnEnable()
    {
        inputReader.PauseEvent += HandlePauseInput;
        inputReader.CancelEvent += HandleCancelInput;

        pausePanel.ResumeRequested += ResumeGame;
        pausePanel.SettingsRequested += OpenSettings;
        pausePanel.QuitRequested += QuitGame;
        settingsPanel.CloseRequested += CloseSettings;

        gameResultChanged.OnEventRaised += HandleGameResult;

        pausePanel.gameObject.SetActive(false);
        settingsPanel.gameObject.SetActive(false);
        resultPanel.gameObject.SetActive(false);
    }

    /// <summary>구독을 해제한다. InputReader·이벤트 채널은 SO 라 씬 밖에서 살아남으므로 죽은 델리게이트를 남기지 않는다.</summary>
    private void OnDisable()
    {
        inputReader.PauseEvent -= HandlePauseInput;
        inputReader.CancelEvent -= HandleCancelInput;

        pausePanel.ResumeRequested -= ResumeGame;
        pausePanel.SettingsRequested -= OpenSettings;
        pausePanel.QuitRequested -= QuitGame;
        settingsPanel.CloseRequested -= CloseSettings;

        gameResultChanged.OnEventRaised -= HandleGameResult;
    }

    /// <summary>Gameplay 맵의 Pause 입력을 받아 Pause 상태를 발행하고 퍼즈 패널을 연다.</summary>
    private void HandlePauseInput()
    {
        gameStateChanged.RaiseEvent(GameState.Pause);
        pausePanel.gameObject.SetActive(true);
    }

    /// <summary>
    /// 서버가 확정한 세션 결과를 받아 결과 패널을 켜고 확정 사유를 UIResult 에 넘긴다(코루틴 카운트다운을 위해 활성화가 선행돼야 한다).
    /// None 은 진행 중 기본값이라 무시한다. GameState(Result) 발행은 같은 채널을 듣는 ClientGameManager 몫이라 여기서 하지 않는다.
    /// </summary>
    /// <param name="reason">서버가 확정한 종료 결과.</param>
    private void HandleGameResult(GameResultReason reason)
    {
        if (reason == GameResultReason.None) return;

        resultPanel.gameObject.SetActive(true);
        resultPanel.Show(reason);
    }

    /// <summary>
    /// UI 맵의 Cancel 입력. 설정 창이 열려 있으면 퍼즈로 한 단계만 되돌리고, 아니면 게임으로 복귀한다.
    /// Pause 상태에서만 도달한다.
    /// </summary>
    private void HandleCancelInput()
    {
        if (settingsPanel.gameObject.activeSelf)
        {
            CloseSettings();
            return;
        }

        ResumeGame();
    }

    /// <summary>Game 상태를 발행하고 퍼즈 패널을 닫는다. Esc 복귀와 계속하기 버튼이 함께 쓰는 유일한 경로다.</summary>
    private void ResumeGame()
    {
        gameStateChanged.RaiseEvent(GameState.Game);
        pausePanel.gameObject.SetActive(false);
    }

    /// <summary>
    /// 나가기 = 개인 이탈(포기). 세션을 종료하는 게 아니라 나 혼자 연결을 끊고 메인 메뉴로 나간다.
    /// 멀티플레이라 한 명이 전원 세션을 끝낼 수 없다(세션 종료는 서버 권위의 종료 조건으로만).
    /// </summary>
    private void QuitGame()
    {
        // TODO(impl): 개인 이탈 흐름(NGO 연결 종료 → Menu 씬) 확정 후 구현 — 그 전까지 이 테스트 로그를 유지한다.
        Log.D("[UIManager] QuitGame — 개인 이탈 요청");
    }

    /// <summary>퍼즈를 감추고 설정 창을 연다. Pause 상태는 유지된다.</summary>
    private void OpenSettings()
    {
        pausePanel.gameObject.SetActive(false);
        settingsPanel.gameObject.SetActive(true);
    }

    /// <summary>설정 창을 닫고 퍼즈로 되돌린다. 설정 창의 OnDisable 이 저장 채널을 발행한다.</summary>
    private void CloseSettings()
    {
        settingsPanel.gameObject.SetActive(false);
        pausePanel.gameObject.SetActive(true);
    }
}
