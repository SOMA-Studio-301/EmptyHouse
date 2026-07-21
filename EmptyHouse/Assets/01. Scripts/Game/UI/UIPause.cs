using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 퍼즈 패널 내부 로직(버튼 액션) 전담 컨트롤러.
/// 자기 패널을 켜고 끄지 않는다 — 화면 가시성과 GameState 발행은 전부 UIManager 소유다.
/// 이쪽은 버튼 클릭을 의도 이벤트로 올리기만 한다. 그래야 Esc 복귀와 버튼 복귀가 같은 한 경로로 수렴한다.
/// 각 핸들러는 인스펙터로 연결한 UIGenericButton 의 Clicked 에 패널 활성화 동안만 구독된다.
/// Esc(Cancel) 입력은 이 패널의 관심사가 아니다 — 입력 소유권은 UIManager 에 있고, 결과적으로 같은 ResumeRequested 경로로 합류한다.
/// </summary>
public class UIPause : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private UIGenericButton resumeButton;
    [SerializeField] private UIGenericButton backButton;     // 뒤로 가기 = 계속하기와 동일 액션(복귀)
    [SerializeField] private UIGenericButton settingsButton;
    [SerializeField] private UIGenericButton quitButton;

    /// <summary>계속하기 요청. UIManager 가 Game 상태 발행과 패널 닫기를 수행한다.</summary>
    public event UnityAction ResumeRequested;

    /// <summary>설정 창 열기 요청. UIManager 가 퍼즈를 감추고 설정 창을 연다.</summary>
    public event UnityAction SettingsRequested;

    /// <summary>나가기(개인 이탈·포기) 요청. UIManager 가 실제 이탈 흐름을 수행한다.</summary>
    public event UnityAction QuitRequested;

    /// <summary>패널이 켜질 때 각 버튼의 Clicked 에 핸들러를 구독한다. 계속하기·뒤로가기는 같은 복귀 핸들러를 공유한다.</summary>
    private void OnEnable()
    {
        resumeButton.Clicked += OnResumeClicked;
        backButton.Clicked += OnResumeClicked;
        settingsButton.Clicked += OnSettingsClicked;
        quitButton.Clicked += OnQuitClicked;
    }

    /// <summary>패널이 꺼질 때 모든 버튼 구독을 해제한다. 재활성화 시 중복 구독이 쌓이지 않게 한다.</summary>
    private void OnDisable()
    {
        resumeButton.Clicked -= OnResumeClicked;
        backButton.Clicked -= OnResumeClicked;
        settingsButton.Clicked -= OnSettingsClicked;
        quitButton.Clicked -= OnQuitClicked;
    }

    /// <summary>계속하기·뒤로가기 버튼이 공유하는 핸들러. 복귀 요청만 올린다.</summary>
    private void OnResumeClicked()
    {
        ResumeRequested?.Invoke();
    }

    /// <summary>설정 버튼. 설정 창 열기 요청만 올린다.</summary>
    private void OnSettingsClicked()
    {
        SettingsRequested?.Invoke();
    }

    /// <summary>나가기 버튼. 개인 이탈(포기) 요청만 올린다.</summary>
    private void OnQuitClicked()
    {
        QuitRequested?.Invoke();
    }
}
