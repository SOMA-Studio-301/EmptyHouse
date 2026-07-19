using Border.Core;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 메뉴/로비 화면 전환기. 씬 전환 대신 자식 패널 두 개의 CanvasGroup 알파를 크로스페이드한다.
/// 트윈은 Awake 에서 1회만 만들어 재사용하므로(SetAutoKill(false)) 전환당 할당이 없다.
/// 패널을 비활성화하지 않는 이유: 이 루트에 붙은 LobbyManager/RoomManager 의 하트비트·await 이 끊기면 안 된다.
/// </summary>
public class UIMenuManager : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private CanvasGroup menuCanvasGroup;  // 자식 MenuPanel 의 CanvasGroup
    [SerializeField] private CanvasGroup lobbyCanvasGroup; // 자식 LobbyPanel 의 CanvasGroup

    [Header("Menu View")]
    [SerializeField] private UIMenu uiMenu; // 메뉴 패널 뷰. 버튼 액션을 여기서 주입한다.

    [Header("Fade")]
    [SerializeField] private float fadeDuration = 0.3f; // 크로스페이드 시간(초)

    private Tween menuFade;  // 캐싱된 메뉴 페이드. PlayForward = 표시, PlayBackward = 숨김
    private Tween lobbyFade; // 캐싱된 로비 페이드. 위와 동일

    /// <summary>재사용할 페이드 트윈을 생성하고, 메뉴 의도를 구독한 뒤 초기 화면을 메뉴로 맞춘다.</summary>
    private void Awake()
    {
        menuFade = CreateFade(menuCanvasGroup);
        lobbyFade = CreateFade(lobbyCanvasGroup);

        // 알파 0 인 상태로 만든 뒤 Complete 로 시작값(0)을 확정시킨다. 이 과정을 건너뛰면
        // 트윈이 첫 재생 시점의 알파를 시작값으로 잡아 PlayBackward 가 엉뚱한 값으로 되감긴다.
        menuFade.Complete();
        lobbyFade.Complete();
        lobbyFade.Rewind();

        SetInteractable(menuCanvasGroup, true);
        SetInteractable(lobbyCanvasGroup, false);

        uiMenu.StartClicked += EnableLobby;
        uiMenu.SettingsClicked += ShowSettings;
        uiMenu.ExitClicked += ExitGame;
    }

    /// <summary>메뉴 의도 구독을 해제한다.</summary>
    private void OnDestroy()
    {
        uiMenu.StartClicked -= EnableLobby;
        uiMenu.SettingsClicked -= ShowSettings;
        uiMenu.ExitClicked -= ExitGame;
    }

    /// <summary>메뉴 화면으로 전환한다. 전환 중 재호출되면 진행 중인 트윈의 방향만 뒤집힌다.</summary>
    public void EnableMenu()
    {
        Log.D("[UIMenuManager] EnableMenu");

        menuFade.PlayForward();
        lobbyFade.PlayBackwards();

        SetInteractable(menuCanvasGroup, true);
        SetInteractable(lobbyCanvasGroup, false);
    }

    /// <summary>로비 화면으로 전환한다. 전환 중 재호출되면 진행 중인 트윈의 방향만 뒤집힌다.</summary>
    public void EnableLobby()
    {
        Log.D("[UIMenuManager] EnableLobby");

        lobbyFade.PlayForward();
        menuFade.PlayBackwards();

        SetInteractable(lobbyCanvasGroup, true);
        SetInteractable(menuCanvasGroup, false);
    }

    /// <summary>설정 화면을 연다. 추후 구현 예정.</summary>
    private void ShowSettings()
    {
        Log.D("[UIMenuManager] ShowSettings (미구현)");
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
