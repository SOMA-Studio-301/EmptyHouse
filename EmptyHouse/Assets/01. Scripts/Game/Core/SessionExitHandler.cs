using Border.Core;
using Border.Events;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 개인 이탈(퍼즈 「게임 나가기」)의 클라 측 실행자. UIManager 가 발행한 sessionExitRequested 를 구독해
/// 내 NGO 연결만 끊고 메인 메뉴 씬을 로컬 로드한다 — 세션을 끝내는 게 아니라 나 혼자 빠지는 것이다(세션루프.md 원칙 2).
/// 서버에는 이 채널이 아니라 「연결 끊김」으로 전달된다: 서버가 OnClientDisconnectCallback 을 감지하면
/// GameScenePlayerSpawner 가 Left 를 발화해 로스터에 반영한다(경로 단일화 — RPC 후 즉시 Shutdown 시의 유실 레이스 회피).
/// UIManager 를 netcode-free 로 두기 위한 분리라, netcode 접촉은 전부 이 클래스가 진다.
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
    /// 내 연결만 끊고 메인 메뉴로 나간다. 로스터 Left 는 서버가 이 연결 끊김을 감지해 자동으로 반영하므로
    /// 여기서 별도로 알리지 않는다.
    /// 호스트는 막는다 — 호스트=서버라 Shutdown 하면 세션 자체가 죽는데, 미드게임 호스트 이탈 정책
    /// (호스트 승계 vs 세션 중단)이 네트워크.md §4 Q1 미결이라 여기서 임의로 정하지 않는다.
    /// </summary>
    private void HandleSessionExit()
    {
        Log.D("[SessionExitHandler] HandleSessionExit");

        if (NetworkManager.Singleton.IsHost)
        {
            // TODO(impl): 호스트 이탈 정책 확정(네트워크.md §4 Q1) 후 구현. 지금 Shutdown 하면 전원 세션이 죽는다.
            Log.W("[SessionExitHandler] 호스트 이탈은 정책 미결(네트워크.md §4 Q1) — 무시한다.", this);
            return;
        }

        NetworkManager.Singleton.Shutdown();
        SceneManager.LoadScene(menuSceneName);
    }
}
