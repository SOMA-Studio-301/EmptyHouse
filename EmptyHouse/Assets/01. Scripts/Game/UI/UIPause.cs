using UnityEngine;
using UnityEngine.Events;
using Border.Core;
using Border.UI;

/// <summary>
/// 퍼즈 패널 내부 로직(버튼 액션) 전담 컨트롤러.
/// 자기 패널을 켜고 끄지 않는다 — 화면 가시성과 GameState 발행은 전부 UIManager 소유다.
/// 이쪽은 버튼 클릭을 의도 이벤트로 올리기만 한다. 그래야 Esc 복귀와 버튼 복귀가 같은 한 경로로 수렴한다.
/// 각 핸들러는 인스펙터로 연결한 UIGenericButton 의 Clicked 에 패널 활성화 동안만 구독된다.
/// </summary>
public class UIPause : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private UIGenericButton resumeButton;
    [SerializeField] private UIGenericButton settingsButton;
    [SerializeField] private UIGenericButton quitButton;

    /// <summary>계속하기 요청. UIManager 가 Game 상태 발행과 패널 닫기를 수행한다.</summary>
    public event UnityAction ResumeRequested;

    /// <summary>설정 창 열기 요청. UIManager 가 퍼즈를 감추고 설정 창을 연다.</summary>
    public event UnityAction SettingsRequested;

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

    /// <summary>계속하기 버튼. 복귀 요청만 올린다.</summary>
    private void OnResumeClicked()
    {
        ResumeRequested?.Invoke();
    }

    /// <summary>설정 버튼. 설정 창 열기 요청만 올린다.</summary>
    private void OnSettingsClicked()
    {
        SettingsRequested?.Invoke();
    }

    /// <summary>
    /// 나가기 버튼 = 개인 이탈(포기). 세션을 종료하는 게 아니라 나 혼자 연결을 끊고 메인 메뉴로 나감
    /// 멀티플레이라 한 명이 전원 세션을 끝낼 수 없다(세션 종료는 서버 권위의 종료 조건으로만).
    /// </summary>
    private void OnQuitClicked()
    {
        // TODO(impl): 개인 이탈 흐름(NGO 연결 종료 → Menu 씬) 확정 후 구현 — 그 전까지 이 트레이스 상태를 유지한다.
        Log.D("[UIPause] OnQuitClicked");
    }
}
