using System.Collections.Generic;
using Border.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 모든 클라이언트의 게임 씬 로드가 끝난 시점(LoadEventCompleted)에 PlayerObject 를 일괄 생성한다 —
/// 먼저 로드된 쪽(주로 호스트)만 혼자 게임을 시작하는 문제를 막는 진입 동시화.
/// Load 이벤트를 놓치고 전체 동기화로 합류한 클라(SynchronizeComplete)는 그 시점에 개별 스폰한다.
/// 스폰/연결끊김을 PlayerLifecycleEventChannelSO 로 발화해 ServerGameManager 로스터에 반영시킨다 —
/// 서버 전용 컴포넌트라 이 발화는 항상 서버에서 일어난다(채널 계약). 매니저를 직접 참조하지 않는다.
/// </summary>
public class GameScenePlayerSpawner : MonoBehaviour
{
    [SerializeField] private NetworkObject playerPrefab;

    /// <summary>합류/이탈 신호 발화 채널. 로스터 등록·이탈이 이 채널로만 흐른다.</summary>
    [SerializeField] private PlayerLifecycleEventChannelSO playerLifecycle;

    private const float GroundClearance = 0.05f; // 스폰 시 바닥 겹침 방지 여유 높이

    private readonly HashSet<ulong> spawnedClients = new HashSet<ulong>();

    private NetworkManager networkManager;
    private string gameSceneName;
    private int nextSpawnIndex; // 순차 스폰 포인트 인덱스 — 랜덤 중복 배정으로 인한 스폰 겹침 방지
    private float spawnHeightOffset; // 스폰 포인트(바닥) 기준 캡슐 피벗 높이 — Awake 에서 프리팹 캡슐로 계산
    private bool initialSpawnDone; // 전원 로드 완료 일괄 스폰을 마쳤는지 — 이후 LoadComplete 는 낙오자 개별 스폰으로 처리

    private void Awake()
    {
        networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            enabled = false;
            return;
        }

        // Awake에서 생성하면 씬 로딩 중인 동적 Player가 ScenePlacedObject로 수집될 수 있다.
        gameSceneName = gameObject.scene.name;

        // 캡슐 피벗이 중심이라, 바닥에 붙은 스폰 포인트 기준 스폰 높이 = 반높이 − center.y + 여유.
        CapsuleCollider capsule = playerPrefab.GetComponent<CapsuleCollider>();
        spawnHeightOffset = capsule.height * 0.5f - capsule.center.y + GroundClearance;

        if (networkManager.IsServer)
        {
            SubscribeServerEvents();
            return;
        }

        // 개발용 직접 플레이(DevBootstrap): 씬이 먼저 뜨고 Start 에서 StartHost 하므로 Awake 시점엔 아직 서버가 아니다.
        // 서버가 되는 순간 초기화한다 — 클라 인스턴스는 이 콜백이 오지 않아 그대로 잠든다.
        networkManager.OnServerStarted += HandleServerStarted;
    }

    /// <summary>
    /// 직접 플레이에서 StartHost 완료를 받아 서버 구독을 걸고, 이미 접속된 클라(호스트 자신 포함)를 스폰한다.
    /// 이 경로는 씬 로드 이벤트가 발생하지 않아 접속 콜백이 스폰 트리거를 대신한다.
    /// </summary>
    private void HandleServerStarted()
    {
        networkManager.OnServerStarted -= HandleServerStarted;
        SubscribeServerEvents();

        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            SpawnPlayer(clientId);
        }
    }

    /// <summary>서버 전용 이벤트 구독. 로비 흐름은 Awake, 직접 플레이는 OnServerStarted 시점에 걸린다.</summary>
    private void SubscribeServerEvents()
    {
        networkManager.SceneManager.OnLoadComplete += HandleLoadComplete;
        networkManager.SceneManager.OnLoadEventCompleted += HandleLoadEventCompleted;
        networkManager.SceneManager.OnSynchronizeComplete += HandleSynchronizeComplete;
        networkManager.OnClientConnectedCallback += HandleClientConnected;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    /// <summary>
    /// 접속 즉시 스폰 — 씬 로드 이벤트가 없는 직접 플레이의 기본 스폰 트리거.
    /// 로비 흐름에서는 씬 이벤트가 먼저 처리하므로 중복은 spawnedClients 가 거른다.
    /// </summary>
    /// <param name="clientId">접속한 클라이언트 ID.</param>
    private void HandleClientConnected(ulong clientId)
    {
        SpawnPlayer(clientId);
    }

    /// <summary>
    /// 개별 클라의 게임 씬 로드 완료. 일괄 스폰 전이면 대기만 하고(전원 완료 시 HandleLoadEventCompleted 가 스폰),
    /// 일괄 스폰 후라면 뒤늦게 로드를 마친 낙오자이므로 즉시 스폰한다.
    /// </summary>
    /// <param name="clientId">로드를 마친 클라이언트 ID.</param>
    /// <param name="sceneName">로드된 씬 이름.</param>
    /// <param name="loadSceneMode">씬 로드 모드(미사용).</param>
    private void HandleLoadComplete(ulong clientId, string sceneName, LoadSceneMode loadSceneMode)
    {
        if (sceneName != gameSceneName) return;

        if (!initialSpawnDone)
        {
            Log.D($"[GameScenePlayerSpawner] client {clientId}: 씬 로드 완료 — 전원 로드 완료 대기");
            return;
        }

        SpawnPlayer(clientId);
    }

    /// <summary>
    /// 모든 클라이언트의 씬 로드가 끝난 시점에 완료자 전원을 일괄 스폰한다(진입 동시화).
    /// 타임아웃된 클라는 이후 자기 LoadComplete(HandleLoadComplete) 또는 동기화 합류(HandleSynchronizeComplete)로 스폰된다.
    /// </summary>
    /// <param name="sceneName">로드된 씬 이름.</param>
    /// <param name="loadSceneMode">씬 로드 모드(미사용).</param>
    /// <param name="clientsCompleted">로드를 완료한 클라이언트 목록.</param>
    /// <param name="clientsTimedOut">로드 제한시간을 넘긴 클라이언트 목록.</param>
    private void HandleLoadEventCompleted(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (sceneName != gameSceneName) return;

        initialSpawnDone = true;
        Log.D($"[GameScenePlayerSpawner] 전원 로드 완료 → 일괄 스폰. 완료 {clientsCompleted.Count}명, 타임아웃 {clientsTimedOut.Count}명");
        foreach (ulong clientId in clientsCompleted)
        {
            SpawnPlayer(clientId);
        }
    }

    /// <summary>
    /// 씬 전환에 끼지 못하고 전체 동기화로 합류한 클라(늦은 합류)를 스폰한다.
    /// 이 경로는 LoadComplete 가 발생하지 않아 HandleLoadComplete 만으로는 영구 누락된다.
    /// </summary>
    /// <param name="clientId">동기화를 마친 클라이언트 ID.</param>
    private void HandleSynchronizeComplete(ulong clientId)
    {
        Log.D($"[GameScenePlayerSpawner] client {clientId}: 전체 동기화 완료(늦은 합류) → 스폰");
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
            // 자동 스폰(NetworkManager.PlayerPrefab) 경로 — 스포너가 위치를 잡아주지 않았다는 진단 근거로 남긴다.
            Log.D($"[GameScenePlayerSpawner] client {clientId}: PlayerObject 이미 존재(자동 스폰 경로) → 위치 재배치 없이 등록만. 현재 위치 {client.PlayerObject.transform.position}");
            spawnedClients.Add(clientId);
            playerLifecycle.RaiseJoined(clientId);
            return;
        }

        // 스포너의 자식 Transform들이 스폰 포인트다. 씬에서 바닥에 딱 붙여 두면 캡슐 높이만큼 띄워 스폰한다.
        // 순차 배정이라 같은 포인트 중복 선택으로 두 캡슐이 겹쳐 튕겨나가는 일이 없다.
        int spawnIndex = nextSpawnIndex % transform.childCount;
        Transform spawnPoint = transform.GetChild(spawnIndex);
        nextSpawnIndex++;
        Vector3 spawnPosition = spawnPoint.position + Vector3.up * spawnHeightOffset;
        Log.D($"[GameScenePlayerSpawner] client {clientId}: 스폰 포인트 {spawnIndex}번({spawnPoint.name}) → {spawnPosition}");
        NetworkObject player = Instantiate(playerPrefab, spawnPosition, spawnPoint.rotation);
        // PlayerObject는 한 판에만 속한다. Lobby 복귀 시 함께 Despawn하고 다음 판에 새로 만든다.
        player.SpawnAsPlayerObject(clientId, true);
        // Owner 권한 NetworkTransform 의 포스트 스폰 덮어쓰기(NGO #2531 계열) 대비 — 소유자에게 스폰 포즈를 재적용시킨다.
        player.GetComponent<PlayerController>().SetSpawnPoseClientRpc(spawnPosition, spawnPoint.rotation);
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

        networkManager.OnServerStarted -= HandleServerStarted;

        if (networkManager.SceneManager != null)
        {
            networkManager.SceneManager.OnLoadComplete -= HandleLoadComplete;
            networkManager.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
            networkManager.SceneManager.OnSynchronizeComplete -= HandleSynchronizeComplete;
        }

        networkManager.OnClientConnectedCallback -= HandleClientConnected;
        networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
    }
}
