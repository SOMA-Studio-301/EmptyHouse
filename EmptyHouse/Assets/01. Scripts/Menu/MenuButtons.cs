using Border.Core;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 메뉴 패널의 설정/종료 버튼 처리기. 게임 시작 버튼은 CanvasController.EnableLobby 를
/// 인스펙터 onClick 에 직접 물려두므로 여기서 다루지 않는다.
/// </summary>
public class MenuButtons : MonoBehaviour
{
    [Header("Menu Buttons")]
    [SerializeField] private Button settingsButton; // 설정 버튼
    [SerializeField] private Button exitButton;     // 게임 종료 버튼

    /// <summary>버튼 리스너를 등록한다.</summary>
    private void Start()
    {
        settingsButton.onClick.RemoveAllListeners();
        settingsButton.onClick.AddListener(OnSettingsButtonClicked);

        exitButton.onClick.RemoveAllListeners();
        exitButton.onClick.AddListener(OnExitButtonClicked);
    }

    /// <summary>설정 버튼 처리. 추후 구현 예정.</summary>
    private void OnSettingsButtonClicked()
    {
        Log.D("[MENU] 설정 버튼 클릭 (추후 구현 예정)");
    }

    /// <summary>게임을 종료한다. 에디터에서는 플레이 모드를 중지한다.</summary>
    private void OnExitButtonClicked()
    {
        Log.D("[MENU] 게임 종료");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
