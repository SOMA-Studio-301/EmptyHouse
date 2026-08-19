using System;
using UnityEngine;
using UnityEngine.Events;
using Border.Events;

/// <summary>
/// 설정 창의 탭 컨테이너. 탭 버튼과 패널을 짝지어 하나만 켜고, 닫기 요청을 UIManager 로 올린다.
/// 각 패널은 자기 값을 스스로 읽고 쓰는 자족 컴포넌트다 — 이 클래스는 어떤 설정 항목도 알지 못한다.
/// 그래서 DATA·GAMEPLAY 처럼 아직 내용이 없는 탭은 빈 패널만 물려두면 코드 없이 성립한다.
/// 창이 닫힐 때 디스크에 저장한다 — 값 적용은 즉시, 디스크 쓰기는 닫을 때 한 번이다.
/// 저장을 이벤트 채널로 위임하지 않고 직접 호출하는 이유는, 리스너가 없는 씬에서 조용히 저장이 누락되기 때문이다.
/// RESET 은 프로필을 기본값으로 되돌려 그 자리에서 저장하고, 화면 재적용은 채널로 SettingsSystem 에 맡긴다.
/// </summary>
public class UISettings : MonoBehaviour
{
    /// <summary>탭 버튼과 그 버튼이 여는 패널의 짝.</summary>
    [Serializable]
    private class SettingsTab
    {
        public UISettingsTabButton TabButton;
        public GameObject Panel;
    }

    [Header("Tabs")]
    [SerializeField] private SettingsTab[] tabs;

    [Header("Buttons")]
    [SerializeField] private UIGenericButton backButton;
    [SerializeField] private UIGenericButton resetButton;

    [Header("Save")]
    [SerializeField] private SaveLoadSystem saveLoadSystem;

    [Header("Broadcasting on")]
    [SerializeField] private VoidEventChannelSO resetSettingsEvent;

    /// <summary>BACK 버튼으로 닫기를 요청했음을 알린다. 실제로 창을 끄는 것은 UIManager 다.</summary>
    public event UnityAction CloseRequested;

    private Action[] tabClickHandlers;
    private int selectedTabIndex;  // 현재 열려 있는 탭. 기본값 복원 후 그 패널만 다시 초기화하는 데 쓴다

    /// <summary>버튼 구독을 걸고 첫 번째 탭을 연 상태로 시작한다.</summary>
    private void OnEnable()
    {
        backButton.Clicked += RequestClose;
        resetButton.Clicked += ResetToDefault;

        tabClickHandlers = new Action[tabs.Length];
        for (int i = 0; i < tabs.Length; i++)
        {
            int index = i;
            tabClickHandlers[i] = () => SelectTab(index);
            tabs[i].TabButton.Button.Clicked += tabClickHandlers[i];
        }

        SelectTab(0);
    }

    /// <summary>버튼 구독을 해제하고 현재 설정을 디스크에 저장한다.</summary>
    private void OnDisable()
    {
        backButton.Clicked -= RequestClose;
        resetButton.Clicked -= ResetToDefault;

        for (int i = 0; i < tabs.Length; i++)
        {
            tabs[i].TabButton.Button.Clicked -= tabClickHandlers[i];
        }

        saveLoadSystem.SaveProfile();
    }

    /// <summary>지정한 탭만 켜고 나머지는 끈다.</summary>
    /// <param name="index">켤 탭의 인덱스.</param>
    private void SelectTab(int index)
    {
        selectedTabIndex = index;

        for (int i = 0; i < tabs.Length; i++)
        {
            bool selected = i == index;
            tabs[i].TabButton.SetSelected(selected);
            tabs[i].Panel.SetActive(selected);
        }
    }

    /// <summary>BACK 버튼 클릭을 닫기 요청으로 올린다.</summary>
    private void RequestClose()
    {
        CloseRequested?.Invoke();
    }

    /// <summary>RESET 버튼. 프로필을 기본값으로 되돌려 저장하고, 재적용을 방송한 뒤 열려 있는 패널을 다시 초기화한다.</summary>
    private void ResetToDefault()
    {
        saveLoadSystem.ResetProfile();
        resetSettingsEvent.RaiseEvent();
        RefreshSelectedPanel();
    }

    /// <summary>열려 있는 패널을 껐다 켠다. 각 패널은 OnEnable 에서만 프로필을 읽으므로, 되돌린 값을 위젯에 반영하는 경로가 이것뿐이다.</summary>
    private void RefreshSelectedPanel()
    {
        GameObject panel = tabs[selectedTabIndex].Panel;
        panel.SetActive(false);
        panel.SetActive(true);
    }
}
