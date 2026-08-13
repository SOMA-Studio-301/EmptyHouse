using Border.Core;
using Border.Events;
using UnityEngine;

/// <summary>
/// 상태 구동 화면 루트를 토글하는 매니저 — 퍼즈/설정(입력 구동)과 결과창(결과 방송 구동)을 소유
/// 퍼즈/설정은 입력(PauseEvent)과 화면 의도 이벤트를 받아 GameState(Pause/Game) 를 발행하고 패널을 연다.
/// Cancel(Esc) 입력은 이 매니저만 소유한다 — 패널은 버튼만 담당하고, Esc 와 버튼은 같은 Resume/Close 경로로 합류한다.
/// Cancel 구독은 Pause 상태 동안에만 살아 있다(퍼즈 진입 시 구독, 게임 복귀 시 해제). 설정 창도 Pause 안이라 그대로 유지된다.
/// 결과창은 반대로 구독이다 — GameResultEventChannelSO 를 받아 결과 패널을 켜고 확정 사유를 UIResult 에 넘긴다.
/// 결과의 GameState(Result) 발행은 같은 채널을 듣는 ClientGameManager 가 하므로 여기서는 상태를 발행하지 않는다(이중 발행 방지)
/// 설정 창은 퍼즈 위에 겹치는 것이 아니라 퍼즈를 대체한다 — 두 화면은 동시에 켜지지 않으며 둘 다 Pause 상태에 속한다.
/// HUD류(게임플레이 이벤트 구동 UI)는 관리 대상이 아니다 — 이 매니저는 상태 구동 화면만 다룬다.
/// </summary>
public class UIManager : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputReader inputReader;

    [Header("Audio")]
    [SerializeField] private SFXEventChannelSO sfxEventChannel;
    [SerializeField] private AudioId cancelAudioId = AudioId.Sfx_Ui_TabClose; // Esc 로 한 겹 걷어낼 때의 입력 피드백

    [Header("State")]
    [SerializeField] private GameStateEventChannelSO gameStateChanged;   // 발행: Pause/Game
    [SerializeField] private GameResultEventChannelSO gameResultChanged; // 구독: 결과창 열기 트리거

    /// <summary>발행: 개인 이탈 요청. 네트워크 이탈 핸들러가 구독해 NGO 연결 종료 + Menu 씬 로드를 수행한다.</summary>
    [Header("Session Exit")]
    [SerializeField] private VoidEventChannelSO sessionExitRequested;
    [SerializeField] private PopupEventChannelSO popupRequested; // 나가기는 되돌릴 수 없으므로 확인 팝업을 한 단계 거친다

    [Header("Screens")]
    [SerializeField] private UIPause pausePanel;
    [SerializeField] private UISettings settingsPanel;
    [SerializeField] private UIResult resultPanel;
    [SerializeField] private UIPopup popupPanel; // 공용 확인 팝업. 수명은 스스로 관리하고 이 매니저는 Esc 우선순위만 준다

    /// <summary>입력·화면 의도 이벤트·결과 방송을 구독하고 모든 화면을 닫은 상태로 초기화한다. 씬 진입 상태는 결코 Pause 나 Result 가 아니다.</summary>
    private void OnEnable()
    {
        inputReader.PauseEvent += HandlePauseInput;

        pausePanel.ResumeRequested += ResumeGame;
        pausePanel.SettingsRequested += OpenSettings;
        pausePanel.QuitRequested += QuitGame;
        settingsPanel.CloseRequested += CloseSettings;

        gameResultChanged.OnEventRaised += HandleGameResult;

        pausePanel.gameObject.SetActive(false);
        settingsPanel.gameObject.SetActive(false);
        resultPanel.gameObject.SetActive(false);
    }

    /// <summary>
    /// 구독을 해제한다. InputReader·이벤트 채널은 SO 라 씬 밖에서 살아남으므로 죽은 델리게이트를 남기지 않는다.
    /// Pause 상태에서 씬이 내려갈 수도 있으므로 Cancel 도 함께 떼어낸다(구독 중이 아니면 -= 는 무해하다).
    /// </summary>
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

    /// <summary>
    /// Gameplay 맵의 Pause 입력을 받아 Pause 상태를 발행하고 퍼즈 패널을 연다.
    /// 이 시점부터 Esc 가 의미를 가지므로 Cancel 구독도 여기서 시작한다(해제는 <see cref="ResumeGame"/>).
    /// Pause 중에는 Gameplay 맵이 꺼져 Pause 입력이 다시 들어오지 않으므로 중복 구독은 발생하지 않는다.
    /// </summary>
    private void HandlePauseInput()
    {
        gameStateChanged.RaiseEvent(GameState.Pause);
        pausePanel.gameObject.SetActive(true);

        inputReader.CancelEvent += HandleCancelInput;
    }

    /// <summary>
    /// UI 맵의 Cancel 입력. 팝업이 최상위라 떠 있으면 그것부터 닫고, 설정 창이 열려 있으면 퍼즈로 한 단계만 되돌리고, 아니면 게임으로 복귀한다.
    /// Pause 상태에서만 구독돼 있으므로 게임 중 Esc 로는 도달하지 않는다 — 어느 분기를 타든 한 겹은 걷어내므로 사운드를 먼저 낸다.
    /// 분기 안에서 내면 같은 경로를 쓰는 버튼들과 UIGenericButton 클릭음이 이중으로 울린다.
    /// </summary>
    private void HandleCancelInput()
    {
        sfxEventChannel.RaisePlayEvent(cancelAudioId, transform.position);

        if (popupPanel.IsOpen)
        {
            popupPanel.Close();
            return;
        }

        if (settingsPanel.gameObject.activeSelf)
        {
            CloseSettings();
            return;
        }

        ResumeGame();
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
    /// Game 상태를 발행하고 퍼즈 패널을 닫는다. Esc 복귀와 계속하기·뒤로가기 버튼이 함께 쓰는 유일한 경로다.
    /// Pause 를 벗어나므로 <see cref="HandlePauseInput"/> 에서 건 Cancel 구독을 여기서 해제한다.
    /// </summary>
    private void ResumeGame()
    {
        gameStateChanged.RaiseEvent(GameState.Game);
        pausePanel.gameObject.SetActive(false);

        inputReader.CancelEvent -= HandleCancelInput;
    }

    /// <summary>
    /// 나가기 = 개인 이탈(포기). 되돌릴 수 없으므로 바로 나가지 않고 확인 팝업을 띄운다.
    /// 실제 이탈은 확인을 눌렀을 때 실행할 콜백(<see cref="ConfirmQuit"/>)으로 팝업에 맡긴다 — 취소하면 아무 일도 없다.
    /// 팝업이 떠 있는 동안 Esc 는 <see cref="HandleCancelInput"/> 이 팝업 닫기로 우선 처리하므로 취소 경로가 자동으로 맞물린다.
    /// </summary>
    private void QuitGame()
    {
        popupRequested.RaiseEvent(PopupType.ExitRoom, ConfirmQuit);
    }

    /// <summary>
    /// 나가기 확인. 개인 이탈(포기)을 실제로 수행한다 — 나 혼자 연결을 끊고 메인 메뉴로 나간다.
    /// 멀티플레이라 한 명이 전원 세션을 끝낼 수 없다(세션 종료는 서버 권위의 종료 조건으로만).
    /// 이탈 요청만 채널로 발행한다 — 실제 NGO 연결 종료·씬 로드는 네트워크 이탈 핸들러가 구독해 처리하므로
    /// 이 매니저는 netcode 를 만지지 않는다. 로스터 Left 는 그 연결 종료를 서버가 감지해 자동으로 반영한다.
    /// </summary>
    private void ConfirmQuit()
    {
        Log.D("[UIManager] ConfirmQuit — 개인 이탈 요청");
        sessionExitRequested.RaiseEvent();
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
