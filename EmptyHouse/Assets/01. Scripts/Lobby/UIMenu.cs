using System;
using UnityEngine;

/// <summary>
/// 메뉴 패널 뷰. Start/Tutorial/Settings/Exit 버튼을 소유하고 클릭 의도만 이벤트로 올린다.
/// 각 의도가 무슨 일을 하는지는 부모(UIMenuManager)가 구독으로 정한다.
/// </summary>
public class UIMenu : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private UISettings settingsPanel; // 설정 창. 닫힌 상태로 시작한다

    [Header("Menu Buttons")]
    [SerializeField] private UIGenericButton startButton;    // 게임 시작(로비로 전환) 버튼
    [SerializeField] private UIGenericButton tutorialButton; // 튜토리얼 재진입 버튼
    [SerializeField] private UIGenericButton settingsButton; // 설정 버튼
    [SerializeField] private UIGenericButton exitButton;      // 게임 종료 버튼

    public event Action StartClicked; // Start 버튼 클릭
    public event Action TutorialClicked; // Tutorial 버튼 클릭
    public event Action SettingsClicked; // Settings 버튼 클릭
    public event Action ExitClicked; // Exit 버튼 클릭

    /// <summary>설정 창이 열려 있는지 여부. ESC 처리 우선순위 판단에 쓰인다.</summary>
    public bool IsSettingsOpen => settingsPanel.gameObject.activeSelf;

    /// <summary>버튼 리스너를 등록하고 설정 창을 닫힌 상태로 맞춘다.</summary>
    private void OnEnable()
    {
        startButton.Clicked += RaiseStartClicked;
        tutorialButton.Clicked += RaiseTutorialClicked;
        settingsButton.Clicked += RaiseSettingsClicked;
        exitButton.Clicked += RaiseExitClicked;

        // 씬에 켜진 채로 저장돼 있어도 여기서 닫는다. 아직 구독 전이라 해제 없이 바로 끈다.
        settingsPanel.gameObject.SetActive(false);
    }

    /// <summary>리스너를 해제한다. 설정 창이 열린 채 메뉴가 꺼지면 구독이 남으므로 함께 닫는다.</summary>
    private void OnDisable()
    {
        startButton.Clicked -= RaiseStartClicked;
        tutorialButton.Clicked -= RaiseTutorialClicked;
        settingsButton.Clicked -= RaiseSettingsClicked;
        exitButton.Clicked -= RaiseExitClicked;

        if (IsSettingsOpen) HideSettings();
    }

    /// <summary>설정 창을 연다. 닫기 구독은 열려 있는 동안에만 유지한다.</summary>
    public void ShowSettings()
    {
        if (IsSettingsOpen) return;

        settingsPanel.CloseRequested += HideSettings;
        settingsPanel.gameObject.SetActive(true);
    }

    /// <summary>설정 창을 닫고 닫기 구독을 해제한다.</summary>
    public void HideSettings()
    {
        if (!IsSettingsOpen) return;

        settingsPanel.CloseRequested -= HideSettings;
        settingsPanel.gameObject.SetActive(false);
    }

    /// <summary>게임 시작 의도를 올린다.</summary>
    private void RaiseStartClicked()
    {
        StartClicked?.Invoke();
    }

    /// <summary>튜토리얼 재진입 의도를 올린다.</summary>
    private void RaiseTutorialClicked()
    {
        TutorialClicked?.Invoke();
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
