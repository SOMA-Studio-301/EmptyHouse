using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Netcode가 각 클라이언트의 게임 씬 로드를 완료한 뒤 PlayerObject를 생성한다.
/// </summary>
public class GameScenePlayerSpawner : MonoBehaviour
{
    [SerializeField] private NetworkObject playerPrefab;

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

    private void SpawnPlayer(ulong clientId)
    {
        if (spawnedClients.Contains(clientId)) return;
        if (!networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client)) return;

        if (client.PlayerObject != null)
        {
            spawnedClients.Add(clientId);
            return;
        }

        NetworkObject player = Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);
        player.SpawnAsPlayerObject(clientId);
        spawnedClients.Add(clientId);
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        spawnedClients.Remove(clientId);
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
