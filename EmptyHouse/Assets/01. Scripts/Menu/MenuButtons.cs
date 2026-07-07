using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class MenuButtons : MonoBehaviour
{
    [Header("Menu Buttons")]
    [SerializeField] private Button gameStartButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button exitButton;

    [Header("Scene Settings")]
    [SerializeField] private string lobbySceneName = "LobbyScene"; // 이동할 로비 씬 이름

    private void Start()
    {
        // 버튼 리스너 초기화 및 등록
        if (gameStartButton != null)
        {
            gameStartButton.onClick.RemoveAllListeners();
            gameStartButton.onClick.AddListener(OnGameStartButtonClicked);
        }

        if (settingsButton != null)
        {
            settingsButton.onClick.RemoveAllListeners();
            settingsButton.onClick.AddListener(OnSettingsButtonClicked);
        }

        if (exitButton != null)
        {
            exitButton.onClick.RemoveAllListeners();
            exitButton.onClick.AddListener(OnExitButtonClicked);
        }
    }

    private void OnGameStartButtonClicked()
    {
        Debug.Log("[MENU] 게임 시작 버튼 클릭 -> 로비 씬으로 이동합니다.");
        SceneManager.LoadScene(lobbySceneName);
    }

    private void OnSettingsButtonClicked()
    {
        Debug.Log("[MENU] 설정 버튼 클릭 (추후 구현 예정)");
    }

    private void OnExitButtonClicked()
    {
        Debug.Log("[MENU] 게임 종료");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}