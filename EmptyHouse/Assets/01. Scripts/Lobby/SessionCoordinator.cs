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

    public const string SessionStateRoom = "Room";               // 세션 상태 — 방 대기
    public const string SessionStateLoadingGame = "LoadingGame"; // 세션 상태 — 게임 시작 절차(Relay 준비~씬 로드)
    public const string SessionStateInGame = "InGame";           // 세션 상태 — 게임 중

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
    private bool isMigratingHost;

    /// <summary>
    /// 지금 붙어 있는(또는 직접 연) Relay 세션의 MatchEpoch. 방장이 바뀌면 이전 Relay 는 죽으므로
    /// 로비의 epoch 가 이 값보다 클 때만 재접속한다 — 끊긴 Relay 코드로 다시 붙는 것을 막는 표식이다.
    /// </summary>
    private int connectedMatchEpoch = -1;

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

    /// <summary>발행: 게임 씬 로드가 시작됨(전 클라이언트). 방→게임 전환 연출용. 준비 미완이면 애초에 시작되지 않아 발행되지 않는다.</summary>
    public event Action GameStarting;

    private bool sceneEventsSubscribed; // NGO SceneManager.OnSceneEvent 구독 여부. SceneManager 는 접속 이후 생기므로 별도 게이트

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
        if (current == networkManager && networkEventsSubscribed)
        {
            TryBindSceneManager(); // SceneManager 는 접속 이후 생기므로 이미 바인딩된 상태에서도 재시도한다
            return;
        }

        UnbindNetworkManager();
        networkManager = current;
        if (networkManager == null) return;

        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        networkManager.OnServerStarted += TryBindSceneManager;
        networkManager.OnClientStarted += TryBindSceneManager;
        networkEventsSubscribed = true;

        TryBindSceneManager(); // 이미 리스닝 중(재입장 등)이면 즉시 바인딩
    }

    private void UnbindNetworkManager()
    {
        if (networkManager != null && networkEventsSubscribed)
        {
            networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            networkManager.OnServerStarted -= TryBindSceneManager;
            networkManager.OnClientStarted -= TryBindSceneManager;
        }

        RemoveSceneManagerBinding();
        networkEventsSubscribed = false;
        networkManager = null;
    }

    /// <summary>NGO SceneManager 가 준비되면 씬 이벤트를 한 번 구독한다. 접속마다 SceneManager 가 새로 생겨 재구독이 필요하다.</summary>
    private void TryBindSceneManager()
    {
        if (networkManager == null || networkManager.SceneManager == null || sceneEventsSubscribed) return;

        networkManager.SceneManager.OnSceneEvent += HandleNgoSceneEvent;
        sceneEventsSubscribed = true;
    }

    /// <summary>씬 이벤트 구독을 해제한다.</summary>
    private void RemoveSceneManagerBinding()
    {
        if (networkManager != null && networkManager.SceneManager != null && sceneEventsSubscribed)
        {
            networkManager.SceneManager.OnSceneEvent -= HandleNgoSceneEvent;
        }

        sceneEventsSubscribed = false;
    }

    /// <summary>NGO 씬 로드 시작을 받아 게임 시작 연출 신호를 올린다. 방으로 돌아가는 로컬 로드는 NGO 이벤트가 아니라 타지 않는다.</summary>
    /// <param name="sceneEvent">NGO 씬 이벤트</param>
    private void HandleNgoSceneEvent(SceneEvent sceneEvent)
    {
        if (sceneEvent.SceneEventType != SceneEventType.Load) return;

        GameStarting?.Invoke();
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

    /// <summary>
    /// 내가 방장인데 방의 Relay/NGO 세션이 끊겨 있어 다시 열어야 하는 상태인지 본다.
    /// 한 판도 시작한 적 없는 방(epoch 0)은 아직 Relay 자체가 없으므로 대상이 아니다.
    /// </summary>
    private bool NeedsRelayRehost()
    {
        if (!IsCurrentLobbyHost) return false;
        if (GetMatchEpoch(CurrentLobby) <= 0) return false;

        BindNetworkManager();
        return networkManager != null && !networkManager.IsListening;
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
        connectedMatchEpoch = -1;
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
            // 게스트 폴링이 Relay 준비(수 초)를 기다리지 않고 '출발중...' 을 띄우도록 상태부터 게시한다.
            // 조인 코드는 아직 없으니 기존 값(첫 판이면 빈 값)을 유지한다 — 게스트 접속은 코드가 실린 다음 게시가 연다.
            await UpdateLobbySessionDataAsync(GetRelayJoinCode(CurrentLobby), SessionStateLoadingGame, nextEpoch, true);

            try
            {
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(CurrentLobby.MaxPlayers - 1);
                string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                GetUnityTransport().SetRelayServerData(BuildRelayServerData(allocation));

                await UpdateLobbySessionDataAsync(joinCode, SessionStateLoadingGame, nextEpoch, true);

                if (!networkManager.StartHost())
                    throw new InvalidOperationException("NetworkManager.StartHost returned false.");
            }
            catch
            {
                // 조기 게시 원복 — 방을 Room 으로 되돌려 게스트의 '출발중...' 잔상과 잠금을 푼다.
                try { await UpdateLobbySessionDataAsync(GetRelayJoinCode(CurrentLobby), SessionStateRoom, nextEpoch, false); }
                catch (Exception e) { Log.W($"[SESSION] 게임 시작 실패 원복 게시 실패: {e.Message}", this); }
                throw;
            }
        }
        else
        {
            if (!networkManager.IsHost)
                throw new InvalidOperationException("A client connection is still active while trying to start as host.");

            await UpdateLobbySessionDataAsync(GetRelayJoinCode(CurrentLobby), SessionStateLoadingGame, nextEpoch, true);
        }

        await WaitForClientsToConnectAsync(CurrentLobby.Players.Count, connectTimeoutSeconds);

        await UpdateLobbySessionDataAsync(GetRelayJoinCode(CurrentLobby), SessionStateInGame, nextEpoch, true);
        connectedMatchEpoch = nextEpoch;

        TryBindSceneManager(); // 호스트가 자기 Load 이벤트를 놓치지 않도록 로드 직전 구독을 보장한다

        SceneEventProgressStatus status = networkManager.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        if (status != SceneEventProgressStatus.Started)
            throw new InvalidOperationException($"NGO scene load did not start: {status}");
    }

    /// <summary>
    /// Room 에 있는 동안 방 네트워크를 현재 방장 기준으로 맞춘다.
    /// 방장은 끊긴 Relay 를 다시 열고, 게스트는 아직 붙지 않은 Relay 에 붙는다.
    /// 방장 이관 직후에도 같은 진입점을 타므로 호출자는 누가 방장인지 알 필요가 없다.
    /// </summary>
    public Task EnsureRoomNetworkAsync()
    {
        return IsCurrentLobbyHost
            ? RehostRoomNetworkIfNeededAsync()
            : ConnectToRoomNetworkIfNeededAsync();
    }

    /// <summary>
    /// 이관받은 방장이 끊긴 Relay/NGO 세션을 다시 연다. Relay 를 만든 적 없는 방(epoch 0)은 아직 필요 없으므로 건너뛴다.
    /// 새 코드와 올라간 epoch 를 로비에 실어, 게스트가 죽은 이전 코드로 재접속하지 않게 한다.
    /// 게임 중이던 방이었다면 잠금도 함께 풀어 Room 상태로 되돌린다.
    /// </summary>
    public async Task RehostRoomNetworkIfNeededAsync()
    {
        if (CurrentLobby == null || isCleaningUp || isMigratingHost || isConnecting) return;
        if (!NeedsRelayRehost()) return;

        int currentEpoch = GetMatchEpoch(CurrentLobby);
        string lobbyId = CurrentLobby.Id;
        int generation = SessionGeneration;

        isConnecting = true;
        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(CurrentLobby.MaxPlayers - 1);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            if (!IsCurrentSession(lobbyId, generation)) return;

            GetUnityTransport().SetRelayServerData(BuildRelayServerData(allocation));
            if (!networkManager.StartHost())
                throw new InvalidOperationException("NetworkManager.StartHost returned false.");

            int nextEpoch = currentEpoch + 1;
            await UpdateLobbySessionDataAsync(joinCode, SessionStateRoom, nextEpoch, false);
            connectedMatchEpoch = nextEpoch;

            Log.D($"[SESSION] 새 방장이 Relay 를 다시 열었다. (epoch {nextEpoch})");
        }
        finally
        {
            isConnecting = false;
        }
    }

    /// <summary>Lobby에 Relay가 있고 아직 NGO에 연결되지 않은 참가자를 현재 방의 NGO 세션에 연결한다.</summary>
    public async Task ConnectToRoomNetworkIfNeededAsync()
    {
        if (CurrentLobby == null || isCleaningUp || isConnecting || isMigratingHost) return;

        BindNetworkManager();
        if (networkManager == null || networkManager.IsListening) return;

        // 이미 소비한 epoch 의 코드는 방장이 바뀌며 죽은 Relay 다. 새 방장이 코드를 갱신할 때까지 기다린다
        int lobbyEpoch = GetMatchEpoch(CurrentLobby);
        if (lobbyEpoch <= connectedMatchEpoch) return;

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
            connectedMatchEpoch = lobbyEpoch;
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

        // 이관받아 Relay 를 다시 열어야 하는 상황이면 죽은 코드를 다시 게시하지 않는다.
        // 상태 전환과 잠금 해제는 RehostRoomNetworkIfNeededAsync 가 새 코드와 함께 처리한다
        if (NeedsRelayRehost()) return;

        try
        {
            await UpdateLobbySessionDataAsync(GetRelayJoinCode(CurrentLobby), SessionStateRoom, GetMatchEpoch(CurrentLobby), false);
        }
        catch (Exception e)
        {
            Log.W($"[SESSION] Room 상태 갱신 실패: {e.Message}", this);
        }
    }

    /// <summary>
    /// 방에서 나간다. 남은 사람이 있으면 방장이라도 방을 파괴하지 않고 자기만 빠져,
    /// UGS 가 다음 플레이어를 방장으로 올리게 둔다 — 방은 살아남고 남은 인원은 Room 에서 이어간다.
    /// 마지막 한 명(혼자인 방장)일 때만 방을 삭제한다.
    /// </summary>
    public async Task ExitCurrentRoomAsync()
    {
        if (isCleaningUp) return;
        isCleaningUp = true;
        suppressDisconnectHandling = true;

        Lobby leavingLobby = CurrentLobby;
        bool wasHost = IsCurrentLobbyHost;
        bool isLastMember = leavingLobby?.Players == null || leavingLobby.Players.Count <= 1;
        InvalidateCurrentRoom();

        try
        {
            if (leavingLobby != null && AuthenticationService.Instance.IsSignedIn)
            {
                try
                {
                    if (wasHost && isLastMember)
                    {
                        await LobbyService.Instance.DeleteLobbyAsync(leavingLobby.Id);
                        Log.D("[SESSION] 마지막 인원이 나가 방을 파괴했습니다.");
                    }
                    else
                    {
                        await LobbyService.Instance.RemovePlayerAsync(
                            leavingLobby.Id,
                            AuthenticationService.Instance.PlayerId);
                        Log.D(wasHost
                            ? "[SESSION] 호스트가 방에서 나갔습니다. 방장은 UGS 가 남은 인원에게 이관합니다."
                            : "[SESSION] 클라이언트가 방에서 나갔습니다.");
                    }
                }
                catch (LobbyServiceException e) when (
                    e.Reason == LobbyExceptionReason.LobbyNotFound
                    || e.Reason == LobbyExceptionReason.EntityNotFound)
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

    /// <summary>
    /// NGO 연결 종료를 받는다. 게스트 입장에서 서버(호스트)가 사라진 경우에만 방장 이관 경로로 넘긴다.
    /// 다른 게스트의 이탈 통지도 같은 콜백으로 오므로 대상 clientId 를 구분해야 한다.
    /// </summary>
    private void HandleClientDisconnected(ulong clientId)
    {
        if (suppressDisconnectHandling || isConnecting || isCleaningUp || isMigratingHost) return;
        if (CurrentLobby == null || networkManager == null) return;
        if (networkManager.IsServer || IsCurrentLobbyHost) return;

        // 내가 끊겼거나 서버가 내려간 경우만 호스트 상실이다. 옆 게스트의 이탈은 무시한다
        if (clientId != networkManager.LocalClientId && clientId != NetworkManager.ServerClientId) return;

        Log.W("[SESSION] 호스트 연결이 끊겼습니다. 방장 이관을 기다립니다.", this);
        _ = HandleHostLostAsync();
    }

    /// <summary>
    /// 호스트가 사라졌을 때 NGO 만 정리하고 방(Lobby)은 유지한 채 Room 으로 돌려보낸다.
    /// UGS 가 다음 플레이어를 방장으로 올리면 그 방장이 Relay 를 다시 열고, 나머지는 Room 폴링에서 새 코드에 붙는다.
    /// 호스트가 비정상 종료해 이관이 일어나지 않으면 하트비트가 끊겨 로비가 만료되고,
    /// Room 폴링의 404 경로(HandleRoomDestroyedAsync)가 기존처럼 방 소멸을 처리한다.
    /// </summary>
    private async Task HandleHostLostAsync()
    {
        if (isCleaningUp || isMigratingHost || CurrentLobby == null) return;

        isMigratingHost = true;
        suppressDisconnectHandling = true;

        string lobbyId = CurrentLobby.Id;
        int generation = SessionGeneration;
        bool roomGone = false;

        try
        {
            // 죽은 Relay 세션을 끊는다. 새 방장이 여는 Relay 에 다시 붙어야 하기 때문이다
            await ShutdownNetworkAsync();
            if (!IsCurrentSession(lobbyId, generation)) return;

            // 게임 중에는 폴링이 멈춰 있어 로컬 스냅샷의 epoch 가 뒤처져 있을 수 있다.
            // 서버 값을 받아 기준선을 잡아야 죽은 Relay 코드로 재접속을 시도하지 않는다
            try
            {
                Lobby refreshed = await LobbyService.Instance.GetLobbyAsync(lobbyId);
                if (!IsCurrentSession(lobbyId, generation)) return;
                TryUpdateLobby(refreshed, lobbyId, generation);
                connectedMatchEpoch = GetMatchEpoch(refreshed);
            }
            catch (LobbyServiceException e) when (
                e.Reason == LobbyExceptionReason.LobbyNotFound
                || e.Reason == LobbyExceptionReason.EntityNotFound)
            {
                roomGone = true;
            }
            catch (Exception e)
            {
                // 조회에 실패해도 로컬 스냅샷 기준으로는 막아 둔다. 부족하면 Room 폴링이 따라잡는다
                Log.W($"[SESSION] 방장 이관 확인용 로비 조회 실패: {e.Message}", this);
                if (CurrentLobby != null) connectedMatchEpoch = GetMatchEpoch(CurrentLobby);
            }
        }
        finally
        {
            isMigratingHost = false;
            suppressDisconnectHandling = false;
            ResetFrontendInput();
        }

        // 방까지 사라졌다면(호스트가 마지막 인원이었거나 로비 만료) 기존 소멸 경로로 넘긴다
        if (roomGone)
        {
            await HandleRoomDestroyedAsync();
            return;
        }

        // 게임 중이었다면 Room 으로 돌아간다. 방 세션은 살아 있어 LobbyManager 가 복귀시킨다
        if (SceneManager.GetActiveScene().name != "Lobby")
            SceneManager.LoadScene("Lobby");
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
        catch (LobbyServiceException e) when (
            e.Reason == LobbyExceptionReason.ValidationError
            || e.Reason == LobbyExceptionReason.BadRequest
            || e.Reason == LobbyExceptionReason.Forbidden
            || e.Reason == LobbyExceptionReason.LobbyNotFound
            || e.Reason == LobbyExceptionReason.EntityNotFound)
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
