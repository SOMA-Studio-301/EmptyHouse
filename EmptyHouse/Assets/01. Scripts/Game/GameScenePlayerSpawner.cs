using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class GameScenePlayerSpawner : MonoBehaviour
{
    [SerializeField] private GameObject playerPrefab;

    private readonly HashSet<ulong> spawnedClients = new HashSet<ulong>();

    private void Awake()
    {
        if (!NetworkManager.Singleton.IsServer) return; // ★ 서버(호스트)만 스폰 로직 수행

        // 이미 씬 로드 이전에 연결되어 있던 클라이언트(호스트 포함) 전부 스폰
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            SpawnPlayer(client.ClientId);
        }

        // 이후에 늦게 들어오는(late-join) 클라이언트도 대응
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    private void OnClientConnected(ulong clientId)
    {
        SpawnPlayer(clientId);
    }

    private void SpawnPlayer(ulong clientId)
    {
        if (spawnedClients.Contains(clientId)) return; // ★ 중복 스폰 방지
        if (NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject != null) return; // 이미 있으면 스킵

        GameObject playerObj = Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);
        playerObj.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
        spawnedClients.Add(clientId);
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }
}