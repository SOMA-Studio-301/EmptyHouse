using Border.Core;
using Border.Events;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 퍼즈 「게임 나가기」 실행자. 클라이언트는 개인 이탈하고 호스트는 방 전체를 파괴한다.
/// Lobby/Relay/NGO 정리 순서는 영속 SessionCoordinator가 담당한다.
/// </summary>
public class SessionExitHandler : MonoBehaviour
{
    /// <summary>구독: 개인 이탈 요청. UIManager 가 퍼즈 「게임 나가기」에서 발행한다.</summary>
    [Header("Listening to")]
    [SerializeField] private VoidEventChannelSO sessionExitRequested;

    /// <summary>이탈 후 로드할 메인 메뉴 씬 이름. NGO 를 끊은 뒤라 네트워크 로드가 아닌 로컬 로드다. 빌드 세팅에 등록돼 있어야 한다.</summary>
    [Header("Scene")]
    [SerializeField] private string menuSceneName = "Menu";

    /// <summary>이탈 요청 구독을 시작한다.</summary>
    private void OnEnable()
    {
        sessionExitRequested.OnEventRaised += HandleSessionExit;
    }

    /// <summary>구독을 해제한다. SO 채널에 죽은 델리게이트가 남지 않게 OnEnable 과 짝을 맞춘다.</summary>
    private void OnDisable()
    {
        sessionExitRequested.OnEventRaised -= HandleSessionExit;
    }

    /// <summary>
    /// 클라이언트는 현재 방에서 나가고, 호스트는 Lobby 전체를 파괴한 뒤 메인 메뉴로 이동한다.
    /// NGO Shutdown 완료까지 기다려 다음 방의 StartHost/StartClient와 겹치지 않게 한다.
    /// </summary>
    private async void HandleSessionExit()
    {
        Log.D("[SessionExitHandler] HandleSessionExit");

        await SessionCoordinator.Instance.ExitCurrentRoomAsync();
        SceneManager.LoadScene(menuSceneName);
    }
}
