using Border.Core;
using DG.Tweening;
using Unity.Services.Lobbies.Models;
using UnityEngine;

/// <summary>
/// 메뉴/로비/방 화면의 단일 배선 지점. 뷰가 올린 버튼 의도를 매니저 요청으로 라우팅하고 화면 전환을 전담한다.
/// 메뉴↔로비는 CanvasGroup 크로스페이드(트윈은 Awake 에서 1회 생성·재사용), 로비↔방은 세션 이벤트에 따른 패널 토글이다.
/// 세션 상태·네트워크 호출은 LobbySession/매니저 몫이라 여기서는 알지 못한다.
/// </summary>
public class UIMenuManager : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private CanvasGroup menuCanvasGroup;  // 자식 MenuPanel 의 CanvasGroup
    [SerializeField] private CanvasGroup lobbyCanvasGroup; // 자식 LobbyPanel 의 CanvasGroup

    [Header("Views")]
    [SerializeField] private UIMenu menuPanel;   // 메뉴 패널 뷰
    [SerializeField] private UILobby lobbyPanel; // 로비 패널 뷰. 방 화면은 이 뷰가 자식으로 품는다

    [Header("Input")]
    [SerializeField] private InputReader inputReader; // ESC(UI/Cancel) 수신. 입력 소스는 이 매니저만 안다

    [Header("Managers")]
    [SerializeField] private LobbyManager lobbyManager; // 로비 목록 프레젠터
    [SerializeField] private RoomManager roomManager;   // 방 프레젠터
    [SerializeField] private LobbySession session;      // 세션 단일 소유자

    [Header("Fade")]
    [SerializeField] private float fadeDuration = 0.3f; // 크로스페이드 시간(초)

    private Tween menuFade;  // 캐싱된 메뉴 페이드. PlayForward = 표시, PlayBackward = 숨김
    private Tween lobbyFade; // 캐싱된 로비 페이드. 위와 동일

    /// <summary>
    /// 재사용할 페이드 트윈을 생성하고, 뷰 의도와 세션 이벤트를 배선한 뒤 초기 화면을 메뉴로 맞춘다.
    /// 이 씬에는 게임플레이 입력 주체가 없으므로 InputReader 의 액션맵을 UI 로 돌려놓는다.
    /// </summary>
    private void OnEnable()
    {
        inputReader.EnableUIInput();
        inputReader.CancelEvent += HandleCancelInput;

        // 트윈 미리 제작해서 캐싱하기 - 메모리 절약/GC방지
        menuFade = CreateFade(menuCanvasGroup);
        lobbyFade = CreateFade(lobbyCanvasGroup);

        // 알파 0 인 상태로 만든 뒤 Complete 로 시작값(0)을 확정시킨다. 이 과정을 건너뛰면
        // 트윈이 첫 재생 시점의 알파를 시작값으로 잡아 PlayBackward 가 엉뚱한 값으로 되감긴다.
        menuFade.Complete();
        lobbyFade.Complete();
        lobbyFade.Rewind();

        SetInteractable(menuCanvasGroup, true);
        SetInteractable(lobbyCanvasGroup, false);

        // UI 활성화 설정
        menuPanel.gameObject.SetActive(true);
        lobbyPanel.gameObject.SetActive(false);

        // 메뉴 패널 액션 설정
        menuPanel.StartClicked += EnableLobby;
        menuPanel.SettingsClicked += ShowSettings;
        menuPanel.ExitClicked += ExitGame;
        // 로비 패널 액션 설정
        lobbyPanel.CreateRequested += lobbyManager.RequestCreate;
        lobbyPanel.RefreshRequested += lobbyManager.RequestRefresh;
        lobbyPanel.LobbyJoinRequested += lobbyManager.RequestJoin;
        lobbyPanel.BackButtonClicked += EnableMenu;
        // 룸 패널 액션 설정. 방 화면은 UILobby 가 품고 있어 의도도 UILobby 를 거쳐 올라온다
        lobbyPanel.RoomReadyRequested += roomManager.RequestToggleReady;
        lobbyPanel.RoomStartRequested += roomManager.RequestStartGame;
        lobbyPanel.RoomExitRequested += roomManager.RequestExitRoom;
        lobbyPanel.RoomInviteRequested += roomManager.OpenInviteOverlay;

        session.SessionStarted += HandleSessionStarted;
        session.SessionEnded += HandleSessionEnded;
    }

    /// <summary>배선을 해제한다.</summary>
    private void OnDisable()
    {
        inputReader.CancelEvent -= HandleCancelInput;

        session.SessionStarted -= HandleSessionStarted;
        session.SessionEnded -= HandleSessionEnded;

        lobbyPanel.RoomReadyRequested -= roomManager.RequestToggleReady;
        lobbyPanel.RoomStartRequested -= roomManager.RequestStartGame;
        lobbyPanel.RoomExitRequested -= roomManager.RequestExitRoom;
        lobbyPanel.RoomInviteRequested -= roomManager.OpenInviteOverlay;

        lobbyPanel.CreateRequested -= lobbyManager.RequestCreate;
        lobbyPanel.RefreshRequested -= lobbyManager.RequestRefresh;
        lobbyPanel.LobbyJoinRequested -= lobbyManager.RequestJoin;
        lobbyPanel.BackButtonClicked -= EnableMenu;

        menuPanel.StartClicked -= EnableLobby;
        menuPanel.SettingsClicked -= ShowSettings;
        menuPanel.ExitClicked -= ExitGame;
    }

    /// <summary>
    /// ESC 입력을 처리한다. 설정 창이 최상위라 열려 있으면 그것부터 닫고 끝낸다.
    /// 그 외에는 로비 뷰의 뒤로 가기로 넘긴다. 방 화면도 로비 뷰 자식이라 같은 경로를 탄다
    /// (팝업 스택 → 방 이탈 → 메뉴). 메뉴 화면에서는 로비 뷰가 꺼져 있어 무시된다.
    /// </summary>
    private void HandleCancelInput()
    {
        if (menuPanel.IsSettingsOpen)
        {
            menuPanel.HideSettings();
            return;
        }

        if (!lobbyPanel.gameObject.activeSelf) return;

        lobbyPanel.Back();
    }

    /// <summary>메뉴 화면으로 전환한다. 전환 중 재호출되면 진행 중인 트윈의 방향만 뒤집힌다.</summary>
    private void EnableMenu()
    {
        // UI 활성화 설정
        menuPanel.gameObject.SetActive(true);
        lobbyPanel.gameObject.SetActive(false);

        menuFade.PlayForward();
        lobbyFade.PlayBackwards();

        SetInteractable(menuCanvasGroup, true);
        SetInteractable(lobbyCanvasGroup, false);
    }

    /// <summary>로비 화면으로 전환한다. 전환 중 재호출되면 진행 중인 트윈의 방향만 뒤집힌다.</summary>
    private void EnableLobby()
    {
        // UI 활성화 설정
        menuPanel.gameObject.SetActive(false);
        lobbyPanel.gameObject.SetActive(true);

        lobbyFade.PlayForward();
        menuFade.PlayBackwards();

        SetInteractable(lobbyCanvasGroup, true);
        SetInteractable(menuCanvasGroup, false);
    }

    /// <summary>세션 시작을 받아 로비 화면을 접고 방 화면을 연다.</summary>
    /// <param name="lobby">입장한 로비</param>
    private void HandleSessionStarted(Lobby lobby)
    {
        // Game -> Lobby 복귀(부팅 직후 세션 복귀) 경로에서도 로비 캔버스를 표시 가능 상태로 맞춘다.
        // 일반 경로(메뉴->로비->방)에서는 이미 로비 상태라 사실상 no-op 이다.
        EnableLobby();

        lobbyPanel.ResetUI();
        lobbyPanel.ShowRoom();
    }

    /// <summary>세션 종료를 받아 방 화면을 접고 목록 화면을 되살린다.</summary>
    private void HandleSessionEnded()
    {
        lobbyPanel.ResetUI();
        lobbyPanel.HideRoom();
    }

    /// <summary>설정 화면을 연다. 창의 수명은 메뉴 뷰가 소유한다.</summary>
    private void ShowSettings()
    {
        Log.D("[UIMenuManager] ShowSettings");

        menuPanel.ShowSettings();
    }

    /// <summary>게임을 종료한다. 에디터에서는 플레이 모드를 멈춘다.</summary>
    private void ExitGame()
    {
        Log.D("[UIMenuManager] ExitGame");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>알파 0 → 1 페이드 트윈을 정지 상태로 만든다. 수명은 이 오브젝트에 묶는다.</summary>
    /// <param name="target">페이드 대상 CanvasGroup</param>
    /// <returns>재사용 가능한(AutoKill 꺼진) 정지 상태 트윈</returns>
    private Tween CreateFade(CanvasGroup target)
    {
        target.alpha = 0f;

        return target.DOFade(1f, fadeDuration)
            .SetAutoKill(false)
            .SetLink(gameObject)
            .SetUpdate(true)
            .Pause();
    }

    /// <summary>패널의 입력 수신 여부를 즉시 토글한다. 페이드를 기다리지 않아 전환 중 오클릭을 막는다.</summary>
    /// <param name="target">대상 CanvasGroup</param>
    /// <param name="value">입력 허용 여부</param>
    private void SetInteractable(CanvasGroup target, bool value)
    {
        target.interactable = value;
        target.blocksRaycasts = value;
    }
}
