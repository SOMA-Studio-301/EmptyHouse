using Border.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 격리된 개발용 테스트 씬에서 네트워크를 자동으로 시작하는 부트스트랩.
/// Build Settings 에 등록되지 않은 씬 전용이며, 실제 빌드 흐름에는 포함되지 않는다.
/// 먼저 실행된 인스턴스는 Host 로 포트를 점유하고, 이후 인스턴스(MPPM 가상 플레이어·추가 빌드)는
/// 포트 점유 실패를 감지해 자동으로 Client 로 접속한다.
/// </summary>
public class DevHostBootstrap : MonoBehaviour
{
    /// <summary>
    /// 씬 시작과 동시에 Host 를 시도하고, 이미 다른 인스턴스가 호스팅 중이면 Client 로 접속한다.
    /// </summary>
    private void Start()
    {
        Log.D("[DevHostBootstrap] Host 자동 시작 시도");
        bool started = NetworkManager.Singleton.StartHost();
        Log.D($"[DevHostBootstrap] StartHost 결과: {started}");

        if (started) return;

        // StartHost 실패 = 이미 다른 인스턴스가 이 포트로 호스팅 중이라는 뜻.
        // 두 인스턴스가 각각 Host 로 남으면 서로의 세션에 플레이어가 스폰되지 않으므로 Client 로 전환한다.
        Log.D("[DevHostBootstrap] Host 실패 → Client 자동 접속 시도");
        bool connected = NetworkManager.Singleton.StartClient();
        Log.D($"[DevHostBootstrap] StartClient 결과: {connected}");
    }
}
