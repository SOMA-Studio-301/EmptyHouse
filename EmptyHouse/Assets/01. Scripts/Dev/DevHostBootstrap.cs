using Border.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 격리된 개발용 테스트 씬에서 Host 를 자동으로 시작하는 부트스트랩.
/// Build Settings 에 등록되지 않은 씬 전용이며, 실제 빌드 흐름에는 포함되지 않는다.
/// </summary>
public class DevHostBootstrap : MonoBehaviour
{
    /// <summary>
    /// 씬 시작과 동시에 NetworkManager.Singleton 으로 Host 를 시작한다.
    /// </summary>
    private void Start()
    {
        Log.D("[DevHostBootstrap] Host 자동 시작 시도");
        bool started = NetworkManager.Singleton.StartHost();
        Log.D($"[DevHostBootstrap] StartHost 결과: {started}");
    }
}
