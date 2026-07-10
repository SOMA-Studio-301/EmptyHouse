using Border.Core;
using Border.UI;
using UnityEngine;

/// <summary>
/// 퍼즈 패널 내부 로직(버튼 액션) 전담 컨트롤러.
/// 계속하기 버튼은 Esc 복귀(UIManager.HandleCancelInput)와 같은 일을 한다: GameState.Game 발행 + 패널 닫기.
/// 패널을 여는 것은 언제나 UIManager 다 — 이쪽은 자기를 닫기만 한다.
/// 각 핸들러는 인스펙터로 연결한 UIGenericButton 의 Clicked 에 패널 활성화 동안만 구독된다.
/// </summary>
public class UIPause : MonoBehaviour
{
    [Header("State")]
    [SerializeField] private GameStateEventChannelSO gameStateChanged;

    [Header("Buttons")]
    [SerializeField] private UIGenericButton resumeButton;
    [SerializeField] private UIGenericButton settingsButton;
    [SerializeField] private UIGenericButton quitButton;

    /// <summary>패널이 켜질 때 각 버튼의 Clicked 에 핸들러를 구독한다.</summary>
    private void OnEnable()
    {
        resumeButton.Clicked += OnResumeClicked;
        settingsButton.Clicked += OnSettingsClicked;
        quitButton.Clicked += OnQuitClicked;
    }

    /// <summary>패널이 꺼질 때 모든 버튼 구독을 해제한다. 재활성화 시 중복 구독이 쌓이지 않게 한다.</summary>
    private void OnDisable()
    {
        resumeButton.Clicked -= OnResumeClicked;
        settingsButton.Clicked -= OnSettingsClicked;
        quitButton.Clicked -= OnQuitClicked;
    }

    /// <summary>계속하기 버튼. Game 상태를 발행하고 자기 패널을 닫는다. SetActive(false) 는 OnDisable 을 태워 버튼 구독을 해제한다.</summary>
    private void OnResumeClicked()
    {
        gameStateChanged.RaiseEvent(GameState.Game);
        //gameObject.SetActive(false);
    }

    /// <summary>설정 버튼. 설정 화면 설계 확정 전까지 트레이스만 남긴다 — 구현 보류.</summary>
    private void OnSettingsClicked()
    {
        // TODO(impl): 설정 화면 설계 확정 후 구현 — 그 전까지 이 트레이스 상태를 유지한다.
        Log.D("[UIPause] OnSettingsClicked");
    }

    /// <summary>나가기 버튼. 멀티플레이 세션 이탈·Menu 씬 복귀 흐름 확정 전까지 트레이스만 남긴다 — 구현 보류.</summary>
    private void OnQuitClicked()
    {
        // TODO(impl): 세션 이탈 흐름(NGO 연동) 확정 후 구현 — 그 전까지 이 트레이스 상태를 유지한다.
        Log.D("[UIPause] OnQuitClicked");
    }
}
