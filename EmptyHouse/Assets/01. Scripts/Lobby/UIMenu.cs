using System;
using UnityEngine;

/// <summary>
/// 메뉴 패널 뷰. Start/Settings/Exit 버튼을 소유하고 클릭 의도만 이벤트로 올린다.
/// 각 의도가 무슨 일을 하는지는 부모(UIMenuManager)가 구독으로 정한다.
/// </summary>
public class UIMenu : MonoBehaviour
{
    /// <summary>발행: Start 버튼 클릭.</summary>
    public event Action StartClicked;

    /// <summary>발행: Settings 버튼 클릭.</summary>
    public event Action SettingsClicked;

    /// <summary>발행: Exit 버튼 클릭.</summary>
    public event Action ExitClicked;

    [Header("Menu Buttons")]
    [SerializeField] private UIGenericButton startButton;    // 게임 시작(로비로 전환) 버튼
    [SerializeField] private UIGenericButton settingsButton; // 설정 버튼
    [SerializeField] private UIGenericButton exitButton;      // 게임 종료 버튼

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
