using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Border.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 방에 들어온 시점부터 방을 나갈 때까지 Unity Lobby와 NGO/Relay의 수명을 보존한다.
/// 씬 UI를 참조하지 않으며 Lobby -> Game -> Lobby 전환 중에도 유지된다.
/// </summary>
public sealed class SessionCoordinator : MonoBehaviour
{
    public const string RelayJoinCodeDataKey = "RelayJoinCode";
    public const string SessionStateDataKey = "SessionState";
    public const string MatchEpochDataKey = "MatchEpoch";

    private const string ReadyDataKey = "IsReady";
    private const string RelayConnectionType = "dtls";
    private const float HeartbeatIntervalSeconds = 15f;
    private const float NetworkShutdownTimeoutSeconds = 5f;

    private static SessionCoordinator instance;

    private NetworkManager networkManager;
    private Coroutine heartbeatRoutine;
    private bool networkEventsSubscribed;
    private bool suppressDisconnectHandling;
    private bool isConnecting;
    private bool isCleaningUp;

    public static SessionCoordinator Instance
    {
        get
        {
            if (instance != null) return instance;

            GameObject coordinatorObject = new GameObject(nameof(SessionCoordinator));
            instance = coordinatorObject.AddComponent<SessionCoordinator>();
            DontDestroyOnLoad(coordinatorObject);
            return instance;
        }
    }

    public Lobby CurrentLobby { get; private set; }
    public int SessionGeneration { get; private set; }
    public bool HasRoom => CurrentLobby != null;
    public bool IsCleaningUp => isCleaningUp;
    public bool IsCurrentLobbyHost => CurrentLobby != null
        && AuthenticationService.Instance.IsSignedIn
        && CurrentLobby.HostId == AuthenticationService.Instance.PlayerId;

    public event Action RoomCleared;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateInstance() => _ = Instance;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += HandleUnitySceneLoaded;
        BindNetworkManager();
    }

    private void HandleUnitySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        BindNetworkManager();
        DestroyDuplicateNetworkManagers();

        if (scene.name == "Menu" || scene.name == "Lobby")
            ResetFrontendInput();
    }

    private static void DestroyDuplicateNetworkManagers()
    {
        NetworkManager singleton = NetworkManager.Singleton;
        if (singleton == null) return;

        NetworkManager[] managers = FindObjectsByType<NetworkManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (NetworkManager manager in managers)
        {
            if (manager != null && manager != singleton)
                Destroy(manager.gameObject);
        }
    }

    private static void ResetFrontendInput()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void BindNetworkManager()
    {
        NetworkManager current = NetworkManager.Singleton;
        if (current == networkManager && networkEventsSubscribed) return;

        UnbindNetworkManager();
        networkManager = current;
        if (networkManager == null) return;

        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        networkEventsSubscribed = true;
    }

    private void UnbindNetworkManager()
    {
        if (networkManager != null && networkEventsSubscribed)
        {
            networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        networkEventsSubscribed = false;
        networkManager = null;
    }

    public void SetCurrentLobby(Lobby lobby)
    {
        if (lobby == null) return;
        if (CurrentLobby == null || CurrentLobby.Id != lobby.Id)
            SessionGeneration++;

        CurrentLobby = lobby;
        RefreshHeartbeatOwnership();
    }

    public bool TryUpdateLobby(Lobby lobby, string expectedLobbyId, int expectedSessionGeneration)
    {
        if (lobby == null
            || !IsCurrentSession(expectedLobbyId, expectedSessionGeneration)
            || lobby.Id != expectedLobbyId)
            return false;

        CurrentLobby = lobby;
        RefreshHeartbeatOwnership();
        return true;
    }

    private bool IsCurrentSession(string lobbyId, int generation)
    {
        return !isCleaningUp
            && CurrentLobby != null
            && SessionGeneration == generation
            && CurrentLobby.Id == lobbyId;
    }

    private void InvalidateCurrentRoom()
    {
        SessionGeneration++;
        CurrentLobby = null;
        StopHeartbeat();
    }

    public async Task<Lobby> RefreshCurrentLobbyAsync()
    {
        if (CurrentLobby == null) return null;

        string lobbyId = CurrentLobby.Id;
        int generation = SessionGeneration;
        Lobby refreshed = await LobbyService.Instance.GetLobbyAsync(lobbyId);
        return TryUpdateLobby(refreshed, lobbyId, generation) ? refreshed : null;
    }

    public async Task ResetLocalReadyAsync()
    {
        if (CurrentLobby == null || !AuthenticationService.Instance.IsSignedIn) return;
        string lobbyId = CurrentLobby.Id;
        int generation = SessionGeneration;

        try
        {
            UpdatePlayerOptions options = new UpdatePlayerOptions
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    { ReadyDataKey, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, bool.FalseString) }
                }
            };

            Lobby updated = await LobbyService.Instance.UpdatePlayerAsync(
                lobbyId,
                AuthenticationService.Instance.PlayerId,
                options);
            TryUpdateLobby(updated, lobbyId, generation);
        }
        catch (Exception e)
        {
            Log.W($"[SESSION] Ready 초기화 실패: {e.Message}", this);
        }
    }

    /// <summary>호스트가 Room에서 게임 시작을 요청한다. 첫 판만 Relay/NGO를 만들고 이후에는 연결을 재사용한다.</summary>
    public async Task StartGameAsHostAsync(string gameSceneName, float connectTimeoutSeconds = 15f)
    {
        if (!IsCurrentLobbyHost) throw new InvalidOperationException("Only the lobby host can start the game.");
        if (isCleaningUp) throw new InvalidOperationException("The previous room is still being cleaned up.");

        BindNetworkManager();
        if (networkManager == null) throw new InvalidOperationException("NetworkManager is not available.");

        int nextEpoch = GetMatchEpoch(CurrentLobby) + 1;
        if (!networkManager.IsListening)
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(CurrentLobby.MaxPlayers - 1);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            GetUnityTransport().SetRelayServerData(BuildRelayServerData(allocation));

            await UpdateLobbySessionDataAsync(joinCode, "LoadingGame", nextEpoch, true);

            if (!networkManager.StartHost())
                throw new InvalidOperationException("NetworkManager.StartHost returned false.");
        }
        else
        {
            if (!networkManager.IsHost)
                throw new InvalidOperationException("A client connection is still active while trying to start as host.");

            await UpdateLobbySessionDataAsync(GetRelayJoinCode(CurrentLobby), "LoadingGame", nextEpoch, true);
        }

        await WaitForClientsToConnectAsync(CurrentLobby.Players.Count, connectTimeoutSeconds);

        await UpdateLobbySessionDataAsync(GetRelayJoinCode(CurrentLobby), "InGame", nextEpoch, true);

        SceneEventProgressStatus status = networkManager.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        if (status != SceneEventProgressStatus.Started)
            throw new InvalidOperationException($"NGO scene load did not start: {status}");
    }

    /// <summary>Lobby에 Relay가 있고 아직 NGO에 연결되지 않은 참가자를 현재 방의 NGO 세션에 연결한다.</summary>
    public async Task ConnectToRoomNetworkIfNeededAsync()
    {
        if (CurrentLobby == null || isCleaningUp || isConnecting) return;

        BindNetworkManager();
        if (networkManager == null || networkManager.IsListening) return;

        string joinCode = GetRelayJoinCode(CurrentLobby);
        if (string.IsNullOrEmpty(joinCode)) return;
        string lobbyId = CurrentLobby.Id;
        int generation = SessionGeneration;

        isConnecting = true;
        try
        {
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            if (!IsCurrentSession(lobbyId, generation)) return;
            GetUnityTransport().SetRelayServerData(BuildRelayServerData(allocation));
            await StartClientAndWaitAsync(15f);
        }
        finally
        {
            isConnecting = false;
        }
    }

    /// <summary>Lobby 씬으로 돌아온 참가자의 한 판 상태만 초기화한다. NGO와 Lobby는 유지한다.</summary>
    public async Task NotifyReturnedToRoomAsync()
    {
        if (CurrentLobby == null) return;

        await ResetLocalReadyAsync();
        if (!IsCurrentLobbyHost) return;

        try
        {
            await UpdateLobbySessionDataAsync(GetRelayJoinCode(CurrentLobby), "Room", GetMatchEpoch(CurrentLobby), false);
        }
        catch (Exception e)
        {
            Log.W($"[SESSION] Room 상태 갱신 실패: {e.Message}", this);
        }
    }

    /// <summary>클라이언트는 방에서 나가고, 호스트는 방 전체를 파괴한다.</summary>
    public async Task ExitCurrentRoomAsync()
    {
        if (isCleaningUp) return;
        isCleaningUp = true;
        suppressDisconnectHandling = true;

        Lobby leavingLobby = CurrentLobby;
        bool wasHost = IsCurrentLobbyHost;
        InvalidateCurrentRoom();

        try
        {
            if (leavingLobby != null && AuthenticationService.Instance.IsSignedIn)
            {
                try
                {
                    if (wasHost)
                    {
                        await LobbyService.Instance.DeleteLobbyAsync(leavingLobby.Id);
                        Log.D("[SESSION] 호스트가 방을 파괴했습니다.");
                    }
                    else
                    {
                        await LobbyService.Instance.RemovePlayerAsync(
                            leavingLobby.Id,
                            AuthenticationService.Instance.PlayerId);
                        Log.D("[SESSION] 클라이언트가 방에서 나갔습니다.");
                    }
                }
                catch (LobbyServiceException e) when (e.ErrorCode == 404)
                {
                    // 호스트가 먼저 방을 삭제한 정상 경쟁 조건이다.
                }
            }

            await ShutdownNetworkAsync();
        }
        finally
        {
            isCleaningUp = false;
            suppressDisconnectHandling = false;
            ResetFrontendInput();
            RoomCleared?.Invoke();
        }
    }

    /// <summary>Polling 404 또는 호스트 NGO 연결 종료 시 로컬 세션을 정리한다.</summary>
    public async Task HandleRoomDestroyedAsync()
    {
        if (isCleaningUp || CurrentLobby == null) return;

        isCleaningUp = true;
        suppressDisconnectHandling = true;
        Lobby destroyedLobby = CurrentLobby;
        InvalidateCurrentRoom();

        try
        {
            if (AuthenticationService.Instance.IsSignedIn)
            {
                try
                {
                    await LobbyService.Instance.RemovePlayerAsync(
                        destroyedLobby.Id,
                        AuthenticationService.Instance.PlayerId);
                }
                catch
                {
                    // 이미 삭제된 방이거나 연결이 끊긴 경우이므로 로컬 정리를 계속한다.
                }
            }

            await ShutdownNetworkAsync();
        }
        finally
        {
            isCleaningUp = false;
            suppressDisconnectHandling = false;
            ResetFrontendInput();
            RoomCleared?.Invoke();

            if (SceneManager.GetActiveScene().name != "Lobby")
                SceneManager.LoadScene("Lobby");
        }
    }

    public async Task DiscardStaleSessionAsync()
    {
        if (isCleaningUp) return;

        isCleaningUp = true;
        suppressDisconnectHandling = true;
        InvalidateCurrentRoom();

        try
        {
            await ShutdownNetworkAsync();
        }
        finally
        {
            isCleaningUp = false;
            suppressDisconnectHandling = false;
            ResetFrontendInput();
        }
    }

    private async Task UpdateLobbySessionDataAsync(string relayJoinCode, string state, int epoch, bool isLocked)
    {
        if (CurrentLobby == null) throw new InvalidOperationException("No active lobby session.");
        string lobbyId = CurrentLobby.Id;
        int generation = SessionGeneration;

        UpdateLobbyOptions options = new UpdateLobbyOptions
        {
            IsLocked = isLocked,
            Data = new Dictionary<string, DataObject>
            {
                { RelayJoinCodeDataKey, new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode ?? string.Empty) },
                { SessionStateDataKey, new DataObject(DataObject.VisibilityOptions.Member, state) },
                { MatchEpochDataKey, new DataObject(DataObject.VisibilityOptions.Member, epoch.ToString()) }
            }
        };

        Lobby updated = await LobbyService.Instance.UpdateLobbyAsync(lobbyId, options);
        if (!TryUpdateLobby(updated, lobbyId, generation))
            throw new InvalidOperationException("The lobby session changed while updating its state.");
    }

    private async Task StartClientAndWaitAsync(float timeoutSeconds)
    {
        TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();

        void Connected(ulong clientId)
        {
            if (networkManager != null && clientId == networkManager.LocalClientId)
                completion.TrySetResult(true);
        }

        void Disconnected(ulong clientId) => completion.TrySetException(
            new InvalidOperationException("Disconnected while connecting to the room host."));

        networkManager.OnClientConnectedCallback += Connected;
        networkManager.OnClientDisconnectCallback += Disconnected;

        try
        {
            if (!networkManager.StartClient())
                throw new InvalidOperationException("NetworkManager.StartClient returned false.");

            Task timeout = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
            Task completed = await Task.WhenAny(completion.Task, timeout);
            if (completed == timeout)
                throw new TimeoutException("Timed out while connecting to the room host.");

            await completion.Task;
        }
        catch
        {
            await ShutdownNetworkAsync();
            throw;
        }
        finally
        {
            if (networkManager != null)
            {
                networkManager.OnClientConnectedCallback -= Connected;
                networkManager.OnClientDisconnectCallback -= Disconnected;
            }
        }
    }

    private async Task WaitForClientsToConnectAsync(int expectedClients, float timeoutSeconds)
    {
        float elapsed = 0f;
        while (networkManager != null
            && networkManager.ConnectedClientsList.Count < expectedClients
            && elapsed < timeoutSeconds)
        {
            await Task.Delay(200);
            elapsed += 0.2f;
        }

        int connected = networkManager != null ? networkManager.ConnectedClientsList.Count : 0;
        if (connected < expectedClients)
            Log.W($"[SESSION] 연결 대기 시간 초과: {connected}/{expectedClients}명. 연결된 인원으로 진행합니다.", this);
    }

    private async Task ShutdownNetworkAsync()
    {
        BindNetworkManager();
        if (networkManager == null || (!networkManager.IsListening && !networkManager.ShutdownInProgress)) return;

        bool previousSuppress = suppressDisconnectHandling;
        suppressDisconnectHandling = true;
        networkManager.Shutdown();

        float elapsed = 0f;
        while (networkManager != null
            && (networkManager.IsListening || networkManager.ShutdownInProgress)
            && elapsed < NetworkShutdownTimeoutSeconds)
        {
            await Task.Delay(50);
            elapsed += 0.05f;
        }

        suppressDisconnectHandling = previousSuppress;
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (suppressDisconnectHandling || isConnecting || isCleaningUp || CurrentLobby == null) return;
        if (IsCurrentLobbyHost) return;

        Log.W("[SESSION] 호스트 연결이 종료되어 방을 나갑니다.", this);
        _ = HandleRoomDestroyedAsync();
    }

    private void RefreshHeartbeatOwnership()
    {
        if (IsCurrentLobbyHost)
        {
            if (heartbeatRoutine == null)
                heartbeatRoutine = StartCoroutine(HeartbeatLoop());
        }
        else
        {
            StopHeartbeat();
        }
    }

    private IEnumerator HeartbeatLoop()
    {
        WaitForSecondsRealtime wait = new WaitForSecondsRealtime(HeartbeatIntervalSeconds);
        while (CurrentLobby != null && IsCurrentLobbyHost)
        {
            _ = SendHeartbeatAsync(CurrentLobby.Id);
            yield return wait;
        }

        heartbeatRoutine = null;
    }

    private async Task SendHeartbeatAsync(string lobbyId)
    {
        try
        {
            await LobbyService.Instance.SendHeartbeatPingAsync(lobbyId);
        }
        catch (LobbyServiceException e) when (e.ErrorCode == 400 || e.ErrorCode == 403 || e.ErrorCode == 404)
        {
            StopHeartbeat();
        }
        catch (Exception e)
        {
            Log.W($"[SESSION] Lobby heartbeat 실패: {e.Message}", this);
        }
    }

    private void StopHeartbeat()
    {
        if (heartbeatRoutine == null) return;
        StopCoroutine(heartbeatRoutine);
        heartbeatRoutine = null;
    }

    private static int GetMatchEpoch(Lobby lobby)
    {
        if (lobby?.Data != null
            && lobby.Data.TryGetValue(MatchEpochDataKey, out DataObject data)
            && int.TryParse(data.Value, out int epoch))
            return epoch;
        return 0;
    }

    private static string GetRelayJoinCode(Lobby lobby)
    {
        if (lobby?.Data != null
            && lobby.Data.TryGetValue(RelayJoinCodeDataKey, out DataObject data))
            return data.Value;
        return string.Empty;
    }

    private static UnityTransport GetUnityTransport()
    {
        UnityTransport transport = NetworkManager.Singleton?.NetworkConfig.NetworkTransport as UnityTransport;
        if (transport == null) throw new InvalidOperationException("UnityTransport is not configured on NetworkManager.");
        return transport;
    }

    private static RelayServerData BuildRelayServerData(Allocation allocation)
    {
        foreach (RelayServerEndpoint endpoint in allocation.ServerEndpoints)
        {
            if (endpoint.ConnectionType != RelayConnectionType) continue;
            return new RelayServerData(
                endpoint.Host, (ushort)endpoint.Port, allocation.AllocationIdBytes,
                allocation.ConnectionData, allocation.ConnectionData, allocation.Key, endpoint.Secure);
        }

        throw new InvalidOperationException($"Relay endpoint '{RelayConnectionType}' was not found.");
    }

    private static RelayServerData BuildRelayServerData(JoinAllocation allocation)
    {
        foreach (RelayServerEndpoint endpoint in allocation.ServerEndpoints)
        {
            if (endpoint.ConnectionType != RelayConnectionType) continue;
            return new RelayServerData(
                endpoint.Host, (ushort)endpoint.Port, allocation.AllocationIdBytes,
                allocation.ConnectionData, allocation.HostConnectionData, allocation.Key, endpoint.Secure);
        }

        throw new InvalidOperationException($"Relay endpoint '{RelayConnectionType}' was not found.");
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        SceneManager.sceneLoaded -= HandleUnitySceneLoaded;
        StopHeartbeat();
        UnbindNetworkManager();
        instance = null;
    }
}
