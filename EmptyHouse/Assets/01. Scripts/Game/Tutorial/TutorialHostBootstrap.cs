using System.Collections;
using Border.Core;
using Border.Events;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// 튜토리얼 씬 전용 솔로 호스트 부트스트랩 (튜토리얼.md 1장 · A1).
/// 게임플레이 스택 전체가 NetworkBehaviour 라 씬 진입 시 로컬 StartHost 로 서버 권위를 세운다 —
/// Relay·UGS 로비를 타지 않으며, 항상 1인 호스트다(클라이언트 폴백 없음 — DevHostBootstrap 과 다른 점).
/// </summary>
public sealed class TutorialHostBootstrap : MonoBehaviour
{
    [Header("Map")]
    [SerializeField] private VoidEventChannelSO onMapAssembledServer; // 맵 조립 완료(X7) 발화 채널 — 정적 씬이라 StartHost 성공 직후 즉시 발화해 스포너 게이트를 연다

    /// <summary>
    /// 씬 시작 한 프레임 뒤에 호스트를 시작한다.
    /// 한 프레임 미루는 이유: 씬 오브젝트들의 Start 초기화(스포너의 OnServerStarted 구독 등)가
    /// 전부 끝난 뒤에 StartHost 가 돌아야 서버 시작 콜백을 놓치지 않는다(DevHostBootstrap 과 동일한 전제).
    /// </summary>
    private IEnumerator Start()
    {
        Log.D("[TutorialHostBootstrap] Start");
        yield return null;

        // 이미 네트워크가 떠 있으면(다른 부트스트랩·에디터 잔존 세션) 이중 시작하지 않는다.
        if (NetworkManager.Singleton.IsListening)
        {
            Log.D("[TutorialHostBootstrap] 이미 IsListening — 호스트 시작 생략");
            yield break;
        }

        // 로비를 거쳐 온 NetworkManager 에는 Relay 프로토콜 연결 정보가 실려 있을 수 있다 —
        // 튜토리얼은 항상 1인 로컬 호스트이므로 직결(127.0.0.1:7777)로 되돌린다
        // (SetConnectionData 는 내부에서 프로토콜을 UnityTransport 직결로 전환한다).
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", 7777);

        bool started = NetworkManager.Singleton.StartHost();
        Log.D($"[TutorialHostBootstrap] StartHost 결과: {started}");

        if (!started) yield break;

        // 정적 씬 = 절차 맵 조립이 없다 — 즉시 조립 완료(X7)를 발화해 GameScenePlayerSpawner 의
        // 스폰 게이트를 연다(발화 전까지 스포너는 호스트 클라 스폰을 대기 큐에 보류한다).
        onMapAssembledServer.RaiseEvent();
    }
}
