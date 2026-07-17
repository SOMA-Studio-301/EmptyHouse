using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 메뉴 패널 뷰. Start/Settings/Exit 버튼을 소유하고 클릭을 액션으로만 올린다.
/// 각 액션이 무슨 일을 하는지는 부모(UIMenuManager)가 정하고, 이 클래스는 버튼에 연결만 한다.
/// </summary>
public class UIMenu : MonoBehaviour
{
    [Header("Menu Buttons")]
    [SerializeField] private UIGenericButton startButton;    // 게임 시작(로비로 전환) 버튼
    [SerializeField] private UIGenericButton settingsButton; // 설정 버튼
    [SerializeField] private UIGenericButton exitButton;      // 게임 종료 버튼

    public UnityAction StartClicked;    // 부모가 주입: 게임 시작
    public UnityAction SettingsClicked; // 부모가 주입: 설정 열기
    public UnityAction ExitClicked;     // 부모가 주입: 게임 종료

    /// <summary>버튼 리스너를 등록한다.</summary>
    private void OnEnable()
    {
        startButton.Clicked += RaiseStartClicked;
        settingsButton.Clicked += RaiseSettingsClicked;
        exitButton.Clicked += RaiseExitClicked;
    }

    /// <summary>리스너를 해제한다.</summary>
    private void OnDisable()
    {
        startButton.Clicked -= RaiseStartClicked;
        settingsButton.Clicked -= RaiseSettingsClicked;
        exitButton.Clicked -= RaiseExitClicked;
    }

    /// <summary>게임 시작 의도를 올린다.</summary>
    private void RaiseStartClicked()
    {
        StartClicked?.Invoke();
    }

    /// <summary>설정 의도를 올린다.</summary>
    private void RaiseSettingsClicked()
    {
        SettingsClicked?.Invoke();
    }

    /// <summary>게임 종료 의도를 올린다.</summary>
    private void RaiseExitClicked()
    {
        ExitClicked?.Invoke();
    }
}
