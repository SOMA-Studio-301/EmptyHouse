using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Netcode가 각 클라이언트의 게임 씬 로드를 완료한 뒤 PlayerObject를 생성한다.
/// 스폰/연결끊김을 PlayerLifecycleEventChannelSO 로 발화해 ServerGameManager 로스터에 반영시킨다 —
/// 서버 전용 컴포넌트라 이 발화는 항상 서버에서 일어난다(채널 계약). 매니저를 직접 참조하지 않는다.
/// </summary>
public class GameScenePlayerSpawner : MonoBehaviour
{
    [SerializeField] private NetworkObject playerPrefab;

    /// <summary>합류/이탈 신호 발화 채널. 로스터 등록·이탈이 이 채널로만 흐른다.</summary>
    [SerializeField] private PlayerLifecycleEventChannelSO playerLifecycle;

    private readonly HashSet<ulong> spawnedClients = new HashSet<ulong>();

    private NetworkManager networkManager;
    private string gameSceneName;

    private void Awake()
    {
        networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsServer)
        {
            enabled = false;
            return;
        }

        // Awake에서 생성하면 씬 로딩 중인 동적 Player가 ScenePlacedObject로 수집될 수 있다.
        gameSceneName = gameObject.scene.name;
        networkManager.SceneManager.OnLoadComplete += HandleLoadComplete;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    private void HandleLoadComplete(ulong clientId, string sceneName, LoadSceneMode loadSceneMode)
    {
        if (sceneName != gameSceneName) return;

        SpawnPlayer(clientId);
    }

    /// <summary>
    /// PlayerObject 를 스폰하고 세션 합류를 발화한다. 이미 스폰된 클라는 무시한다.
    /// 합류 발화가 있어야 ServerGameManager 로스터에 Active 로 등록돼 종료 판정이 성립한다.
    /// </summary>
    /// <param name="clientId">스폰 대상 클라이언트 ID.</param>
    private void SpawnPlayer(ulong clientId)
    {
        if (spawnedClients.Contains(clientId)) return;
        if (!networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client)) return;

        if (client.PlayerObject != null)
        {
            spawnedClients.Add(clientId);
            playerLifecycle.RaiseJoined(clientId);
            return;
        }

        NetworkObject player = Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);
        // PlayerObject는 한 판에만 속한다. Lobby 복귀 시 함께 Despawn하고 다음 판에 새로 만든다.
        player.SpawnAsPlayerObject(clientId, true);
        spawnedClients.Add(clientId);
        playerLifecycle.RaiseJoined(clientId);
    }

    /// <summary>
    /// 연결이 끊긴 클라를 세션에서 이탈 처리한다(자발적 나가기·크래시 공통).
    /// 로스터상 Left = 비활성·미귀환이라, 이 발화가 없으면 끊긴 플레이어가 Active 로 영구 잔류해 세션이 끝나지 않는다.
    /// </summary>
    /// <param name="clientId">연결이 끊긴 클라이언트 ID.</param>
    private void HandleClientDisconnected(ulong clientId)
    {
        spawnedClients.Remove(clientId);
        playerLifecycle.RaiseLeft(clientId);
    }

    private void OnDestroy()
    {
        if (networkManager == null) return;

        if (networkManager.SceneManager != null)
        {
            networkManager.SceneManager.OnLoadComplete -= HandleLoadComplete;
        }

        networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
    }
}
