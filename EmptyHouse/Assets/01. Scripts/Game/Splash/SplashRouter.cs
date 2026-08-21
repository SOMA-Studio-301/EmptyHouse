using Border.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 스플래쉬 씬의 분기 라우터 — 프로필을 읽어 최초 실행이면 Tutorial, 아니면 Lobby 로 보낸다 (튜토리얼.md 1-1·1-3).
/// IsFirstPlay 는 게임을 켠 순간 소모된다 — 분기 직후 false 로 저장하므로 자동 진입은 정확히 1회다.
/// 중도 이탈해도 다시 자동 진입하지 않으며, 재진입 경로는 타이틀의 튜토리얼 버튼뿐이다.
/// </summary>
public sealed class SplashRouter : MonoBehaviour
{
    [Header("Save")]
    [SerializeField] private SaveLoadSystem saveLoadSystem; // 프로필(IsFirstPlay) 조회·저장. 첫 접근 시 자동 로드된다

    [Header("Scenes")]
    [SerializeField] private string tutorialSceneName = "Tutorial"; // 최초 실행 시 이동할 씬
    [SerializeField] private string lobbySceneName = "Lobby";       // 재실행 시 이동할 씬

    /// <summary>프로필의 IsFirstPlay 로 분기한다 — 최초 실행이면 플래그를 끄고 저장한 뒤 Tutorial, 아니면 Lobby 를 로드한다.</summary>
    private void Start()
    {
        Log.D("[SplashRouter] Start");

        bool isFirstPlay = saveLoadSystem.Profile.IsFirstPlay;
        if (isFirstPlay)
        {
            // 씬 로드 전에 플래그를 소모해 저장한다 — 튜토리얼을 중도 이탈해도 자동 진입은 정확히 1회다.
            saveLoadSystem.Profile.IsFirstPlay = false;
            saveLoadSystem.SaveProfile();
        }

        SceneManager.LoadScene(isFirstPlay ? tutorialSceneName : lobbySceneName);
    }
}
