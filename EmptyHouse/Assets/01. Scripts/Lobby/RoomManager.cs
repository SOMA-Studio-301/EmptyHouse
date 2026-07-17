using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Border.Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay; 
using Unity.Services.Relay.Models; 
using Unity.Netcode; 
using Unity.Netcode.Transports.UTP; 
using Unity.Networking.Transport.Relay; 
using Steamworks; 

public class RoomManager : MonoBehaviour
{
    private const string RelayJoinCodeDataKey = "RelayJoinCode";
    private const string PlayerNameDataKey = "PlayerName";
    private const string SteamIdDataKey = "SteamID";
    private const string ReadyDataKey = "IsReady";
    private const string RelayConnectionType = "dtls";
    private const int MaxRoomSlots = 4;

    [Header("Canvas References")]
    [SerializeField] private GameObject roomCanvas;
    [SerializeField] private GameObject joinCreateCanvas;
    [SerializeField] private GameObject renameCanvas;

    [Header("Interaction Panel")]
    [SerializeField] private Button readyButton;
    [SerializeField] private Button startButton;
    [SerializeField] private Button exitButton;

    [Header("Lobby Panel")]
    [SerializeField] private Text lobbyNameText;

    [Header("User List")]
    [SerializeField] private Transform userListContainer;
    [SerializeField] private UserPanel userPanelPrefab;

    [Header("Audio")]
    [SerializeField] private SFXEventChannelSO sfxEventChannel;
    [SerializeField] private AudioId gameStartAudioId = AudioId.Sfx_Ui_GameStart; // 결과 피드백 — 클릭음(버튼 컴포넌트 담당)이 아니라 시작이 확정된 뒤 울린다

    [Header("Settings")]
    [SerializeField] private float lobbyPollInterval = 1.5f;
    [SerializeField] private LobbyManager lobbyManager;
    
    [Header("Scene Settings")]
    [SerializeField] private string gameSceneName = "GameScene";

    private bool isStartingGame;
    private Lobby currentLobby;
    private bool isReady;
    private float nextPollTime;
    private bool isInRoom;
    private bool isPolling;
    private bool userSlotsInitialized;
    private readonly List<UserPanel> userPanels = new List<UserPanel>(MaxRoomSlots);

    // 씬 전환 시작 시점의 기대 인원 수를 저장해두고, 전원 로드 완료 시 로비 삭제
    private int expectedPlayerCount;
    private bool isSceneLoadEventSubscribed;

    private Callback<AvatarImageLoaded_t> avatarLoadedCallback;
    private Callback<PersonaStateChange_t> personaStateCallback;
    public bool IsStartingGame => isStartingGame;

    private void Start()
    {
        if (SteamManager.Initialized)
        {
            avatarLoadedCallback = Callback<AvatarImageLoaded_t>.Create(OnAvatarImageLoaded);
            personaStateCallback = Callback<PersonaStateChange_t>.Create(OnPersonaStateChange);
            Log.D("[STEAM] RoomManager 스팀 콜백 등록 완료");
        }
    }

    #region Room Entry

    public void EnterRoom(Lobby lobby)
    {
        currentLobby = lobby;
        isReady = false;
        isInRoom = true;
        isStartingGame = false; // ★ 재입장 시 초기화

        if (roomCanvas != null) roomCanvas.SetActive(true);
        if (joinCreateCanvas != null) joinCreateCanvas.SetActive(false);
        if (renameCanvas != null) renameCanvas.SetActive(false);

        SetupRoomUI();
        UpdateRoomUI();

        if (readyButton != null)
        {
            readyButton.onClick.RemoveAllListeners();
            readyButton.onClick.AddListener(OnReadyButtonClicked);
        }
        if (startButton != null)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(OnStartButtonClicked);
            startButton.interactable = true; // ★ 재입장 시 초기화
        }
        if (exitButton != null)
        {
            exitButton.onClick.RemoveAllListeners();
            exitButton.onClick.AddListener(OnExitButtonClicked);
            exitButton.interactable = true; // ★ 재입장 시 초기화
        }
        
        _ = PollLobbyData();
        
        Log.D($"[ROOM] Entered room: {lobby.Name}");
        
        //스팀 리치 프레즌스 설정
        if (SteamManager.Initialized)
        {
            SteamFriends.SetRichPresence("status", "버스 대기 중");
            SteamFriends.SetRichPresence("connect", lobby.Id);
            Log.D($"[STEAM] 리치 프레즌스 등록 완료 (LobbyID: {lobby.Id})");
        }
    }

    private void SetupRoomUI()
    {
        if (lobbyNameText != null && currentLobby != null)
            lobbyNameText.text = currentLobby.Name;
    }

    #endregion

    #region Lobby Polling

    private async Task PollLobbyData()
    {
        if (isPolling) return;
        isPolling = true;

        try
        {
            while (isInRoom && currentLobby != null)
            {
                try
                {
                    currentLobby = await LobbyService.Instance.GetLobbyAsync(currentLobby.Id);
                    if (currentLobby == null) continue;
                    UpdateRoomUI();

                    if (!IsHost()
                        && currentLobby.Data != null
                        && currentLobby.Data.TryGetValue(RelayJoinCodeDataKey, out DataObject relayData))
                    {
                        string joinCode = relayData.Value;
                        if (!string.IsNullOrEmpty(joinCode) && !isStartingGame)
                        {
                            isStartingGame = true;
                            if (exitButton != null) exitButton.interactable = false; // ★ 게임 조인 시작 시 Exit 잠금
                            _ = JoinGameAsync(joinCode);
                            break;
                        }
                    }
                }
                catch (LobbyServiceException e)
                {
                    if (e.ErrorCode == 404 || e.Message.ToLower().Contains("not found"))
                    {
                        // ★ 호스트가 게임 시작 후 정상적으로 로비를 삭제한 경우도 여기로 들어옴.
                        // isStartingGame이 true라면 정상 흐름이므로 경고 로그를 띄우지 않음.
                        if (!isStartingGame)
                        {
                            Log.W("[ROOM] 로비가 이미 서버에서 삭제되었습니다.", this);
                        }
                        currentLobby = null;
                        isInRoom = false;
                        break;
                    }

                    if (e.ErrorCode == 429)
                    {
                        await Task.Delay(3000); // 잠깐 쉬고 루프는 계속 진행
                    }
                    else
                    {
                        Log.E($"[ROOM] 로비 데이터 갱신 실패: {e.Message}", this);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(Mathf.Max(0.25f, lobbyPollInterval)));
            }
        }
        catch (Exception e)
        {
            Log.E($"[ROOM] 일반 에러: {e.Message}", this);
        }
        finally
        {
            isPolling = false;
        }
    }

    #endregion

    #region UI Update

    private void UpdateRoomUI()
    {
        if (currentLobby == null) return;

        UpdatePlayerList();
        UpdateInteractionButtons();
    }

    private void UpdatePlayerList()
    {
        if (userListContainer == null || userPanelPrefab == null) return;

        EnsureUserSlots();

        for (int i = 0; i < MaxRoomSlots; i++)
        {
            UserPanel userPanel = userPanels[i];
            Button slotButton = userPanel.SlotButton;

            if (i < currentLobby.Players.Count)
            {
                Player player = currentLobby.Players[i];
                if (userPanel != null)
                {
                    string playerName = GetPlayerName(player);
                    bool isHost = player.Id == currentLobby.HostId;
                    bool isPlayerReady = GetPlayerReadyStatus(player);
                
                    string steamId = TryGetPlayerData(player, SteamIdDataKey, out string value) ? value : string.Empty;

                    userPanel.SetPlayerInfo(playerName, isHost, isPlayerReady, steamId);
                }

                if (slotButton != null)
                {
                    slotButton.onClick.RemoveAllListeners();
                    slotButton.enabled = false;
                }
            }
            else
            {
                if (userPanel != null)
                {
                    userPanel.SetEmptySlot();
                }

                if (slotButton != null)
                {
                    slotButton.enabled = true;
                    slotButton.onClick.RemoveAllListeners();
                    slotButton.onClick.AddListener(() =>
                    {
                        if (SteamManager.Initialized)
                        {
                            SteamFriends.ActivateGameOverlay("Invite");
                            Log.D("[STEAM] 친구 초대 오버레이 창 활성화");
                        }
                    });
                }
            }
        }
    }

    private void EnsureUserSlots()
    {
        if (!userSlotsInitialized)
        {
            foreach (Transform child in userListContainer)
            {
                Destroy(child.gameObject);
            }

            userSlotsInitialized = true;
        }

        while (userPanels.Count < MaxRoomSlots)
        {
            userPanels.Add(Instantiate(userPanelPrefab, userListContainer));
        }
    }

    private void UpdateInteractionButtons()
    {
        bool isHost = IsHost();

        if (isHost)
        {
            if (readyButton != null) readyButton.gameObject.SetActive(false);
            if (startButton != null) startButton.gameObject.SetActive(true);
            
            if (lobbyManager != null && !lobbyManager.IsInvoking("SendLobbyHeartbeat"))
            {
                lobbyManager.StartHeartbeatInstance();
            }

            bool allReady = true;
            foreach (Player player in currentLobby.Players)
            {
                if (player.Id == currentLobby.HostId)
                    continue;

                if (!GetPlayerReadyStatus(player))
                {
                    allReady = false;
                    break;
                }
            }

            // ★ 게임 시작 중일 때는 allReady 여부와 상관없이 버튼 비활성 유지
            if (startButton != null && !isStartingGame)
            {
                startButton.interactable = allReady && currentLobby.Players.Count > 1;
            }
        }
        else
        {
            if (startButton != null) startButton.gameObject.SetActive(false);
            if (readyButton != null)
            {
                readyButton.gameObject.SetActive(true);
                readyButton.interactable = !isStartingGame; // ★ 게임 시작 중이면 Ready도 잠금
            }
        }
    }

    #endregion

    #region Button Handlers

    private async void OnReadyButtonClicked()
    {
        isReady = !isReady;

        try
        {
            string playerId = AuthenticationService.Instance.PlayerId;
            UpdatePlayerOptions options = new UpdatePlayerOptions
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    { ReadyDataKey, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, isReady.ToString()) }
                }
            };

            currentLobby = await LobbyService.Instance.UpdatePlayerAsync(currentLobby.Id, playerId, options);
            UpdateRoomUI();
        }
        catch (Exception e)
        {
            Log.E($"[ROOM] Failed to update ready status: {e.Message}", this);
            isReady = !isReady; 
        }
    }

    private async void OnStartButtonClicked()
    {
        if (!IsHost()) return;
        if (isStartingGame) return;

        isStartingGame = true;
        if (startButton != null) startButton.interactable = false;
        if (exitButton != null) exitButton.interactable = false; // ★ Start 누르는 순간 Exit 잠금

        sfxEventChannel.RaisePlayEvent(gameStartAudioId, transform.position);

        Log.D("[ROOM] Host가 Relay 서버 생성을 시작합니다...");

        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(currentLobby.MaxPlayers - 1);
            string relayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Log.D($"[ROOM] Relay 생성 완료! JoinCode: {relayJoinCode}");

            RelayServerData serverData = BuildRelayServerData(allocation, RelayConnectionType);
            GetUnityTransport().SetRelayServerData(serverData);

            UpdateLobbyOptions options = new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    { RelayJoinCodeDataKey, new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode) }
                }
            };
            currentLobby = await LobbyService.Instance.UpdateLobbyAsync(currentLobby.Id, options);

            expectedPlayerCount = currentLobby.Players.Count;

            NetworkManager.Singleton.StartHost();

            // ★ 방법 2: 바로 LoadScene 하지 않고 전원 Netcode 연결될 때까지 로비 씬에서 대기
            //   -> RoomManager/LobbyManager가 안 죽으니 하트비트도 안 끊김
            await WaitForClientsToConnect(expectedPlayerCount, 15f);

            if (!isSceneLoadEventSubscribed)
            {
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnAllClientsLoaded;
                isSceneLoadEventSubscribed = true;
            }

            NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        }
        catch (Exception e)
        {
            Log.E($"[ROOM] 게임 시작 실패 (Relay 에러): {e.Message}", this);
            isStartingGame = false;
            if (startButton != null) startButton.interactable = true;
            if (exitButton != null) exitButton.interactable = true;
        }
    }

    // ★ ConnectedClientsList엔 호스트 자신도 포함되므로 expectedClients는
    //   currentLobby.Players.Count(호스트 포함 전체 인원)를 그대로 넘기면 됨
    private async Task WaitForClientsToConnect(int expectedClients, float timeoutSec = 15f)
    {
        float elapsed = 0f;
        while (NetworkManager.Singleton.ConnectedClientsList.Count < expectedClients && elapsed < timeoutSec)
        {
            await Task.Delay(200);
            elapsed += 0.2f;
        }

        if (NetworkManager.Singleton.ConnectedClientsList.Count < expectedClients)
            Log.W($"[ROOM] 타임아웃: {NetworkManager.Singleton.ConnectedClientsList.Count}/{expectedClients}명만 연결됨. 그래도 진행합니다.", this);
        else
            Log.D($"[ROOM] 전원({expectedClients}명) 연결 완료. 씬 전환 시작.");
    }

    private void OnExitButtonClicked()
    {
        if (isStartingGame) return; // ★ 게임 시작 중이면 버튼 disable을 우회해도 방어
        _ = ExitRoom();
    }

    #endregion

    #region Game Scene Transition (Host)

    // 씬 로드가 완료된(혹은 타임아웃된) 클라이언트 목록을 확인하고,
    // 전원(또는 타임아웃 없이 예상 인원만큼) 로드가 끝났을 때 로비를 명시적으로 삭제한다.
    // ★ 캐릭터 스폰은 GameScene 전용 스포너가 담당하므로 여기서는 로비 정리만 수행한다.
    private async void OnAllClientsLoaded(string sceneName, LoadSceneMode loadSceneMode,
        List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        if (clientsTimedOut != null && clientsTimedOut.Count > 0)
        {
            Log.W($"[ROOM] 씬 로드에 실패(타임아웃)한 클라이언트가 있습니다: {clientsTimedOut.Count}명. " +
                             $"그래도 로비를 정리하고 게임을 진행합니다.");
        }

        Log.D($"[ROOM] 씬 로드 완료 클라이언트 수: {clientsCompleted.Count} / 예상 인원: {expectedPlayerCount}");

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnAllClientsLoaded;
        isSceneLoadEventSubscribed = false;

        isInRoom = false;

        if (currentLobby != null)
        {
            try
            {
                await LobbyService.Instance.DeleteLobbyAsync(currentLobby.Id);
                Log.D("[ROOM] 게임 시작 완료 - 로비를 정상적으로 삭제했습니다.");
            }
            catch (LobbyServiceException e)
            {
                Log.W($"[ROOM] 로비 삭제 중 예외 (무시 가능): {e.Message}", this);
            }
            finally
            {
                currentLobby = null;
            }
        }
    }

    #endregion

    #region Game Start (Client)

    private async Task JoinGameAsync(string joinCode)
    {
        Log.D($"[ROOM] Client가 Relay 연결을 시도합니다. 코드: {joinCode}");

        try
        {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            
            RelayServerData clientServerData = BuildRelayServerData(joinAllocation, RelayConnectionType);
            GetUnityTransport().SetRelayServerData(clientServerData);

            NetworkManager.Singleton.OnClientConnectedCallback += OnNetcodeConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnNetcodeDisconnected;

            NetworkManager.Singleton.StartClient();
            isInRoom = false; 
        }
        catch (Exception e)
        {
            Log.E($"[ROOM] Relay 연결 실패: {e.Message}", this);
            isStartingGame = false;
            if (exitButton != null) exitButton.interactable = true; // ★ 실패 시 Exit 복구
        }
    }
    
    private void OnNetcodeConnected(ulong clientId)
    {
        Log.D($"[NETCODE] 호스트와 연결 성공! 내 ID: {clientId}");
        UnsubscribeNetcodeEvents();
    }
    
    private void OnNetcodeDisconnected(ulong clientId)
    {
        Log.E("[NETCODE] 호스트와의 연결에 실패했거나 끊어졌습니다!", this);
        isStartingGame = false;
        if (exitButton != null) exitButton.interactable = true; // ★ 연결 끊김 시 Exit 복구
        UnsubscribeNetcodeEvents();
    }
    
    private void UnsubscribeNetcodeEvents()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnNetcodeConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnNetcodeDisconnected;
        }
    }

    #endregion

    #region Exit Room

    public async Task ExitRoom()
    {
        if (currentLobby == null) return;

        try
        {
            string playerId = AuthenticationService.Instance.PlayerId;
            await LobbyService.Instance.RemovePlayerAsync(currentLobby.Id, playerId);

            currentLobby = null;
            isReady = false;
            isInRoom = false;

            if (roomCanvas != null) roomCanvas.SetActive(false);
            if (joinCreateCanvas != null) joinCreateCanvas.SetActive(true);

            if (lobbyManager != null)
                await lobbyManager.RefreshLobbyList();
        }
        catch (Exception e)
        {
            Log.E($"[ROOM] Failed to exit room: {e.Message}", this);
        }
    }

    #endregion

    #region Relay Helpers

    // ServerEndpoints에서 원하는 connectionType(dtls)에 맞는 엔드포인트를 찾아서 RelayServerData를 정확하게 빌드
    private RelayServerData BuildRelayServerData(Allocation allocation, string connectionType = "dtls")
    {
        var endpoint = allocation.ServerEndpoints.First(ep => ep.ConnectionType == connectionType);

        return new RelayServerData(
            endpoint.Host,
            (ushort)endpoint.Port,
            allocation.AllocationIdBytes,
            allocation.ConnectionData,
            allocation.ConnectionData,
            allocation.Key,
            endpoint.Secure // ★ isSecure를 엔드포인트 정보에서 그대로 가져와서 절대 불일치가 안 나게 함
        );
    }

    private RelayServerData BuildRelayServerData(JoinAllocation joinAllocation, string connectionType = "dtls")
    {
        var endpoint = joinAllocation.ServerEndpoints.First(ep => ep.ConnectionType == connectionType);

        return new RelayServerData(
            endpoint.Host,
            (ushort)endpoint.Port,
            joinAllocation.AllocationIdBytes,
            joinAllocation.ConnectionData,
            joinAllocation.HostConnectionData,
            joinAllocation.Key,
            endpoint.Secure
        );
    }

    #endregion
    
    #region Helper Methods

    private static string GetPlayerName(Player player)
    {
        return TryGetPlayerData(player, PlayerNameDataKey, out string playerName) ? playerName : "Unknown";
    }

    private static bool GetPlayerReadyStatus(Player player)
    {
        return TryGetPlayerData(player, ReadyDataKey, out string value)
            && bool.TryParse(value, out bool isReadyStatus)
            && isReadyStatus;
    }

    private static bool TryGetPlayerData(Player player, string key, out string value)
    {
        value = string.Empty;
        if (player?.Data == null || !player.Data.TryGetValue(key, out PlayerDataObject data)) return false;
        value = data.Value;
        return true;
    }

    private static UnityTransport GetUnityTransport()
    {
        UnityTransport transport = NetworkManager.Singleton?.NetworkConfig.NetworkTransport as UnityTransport;
        if (transport == null) throw new InvalidOperationException("UnityTransport is not configured on NetworkManager.");
        return transport;
    }

    private bool IsHost()
    {
        if (currentLobby == null) return false;
        return AuthenticationService.Instance.PlayerId == currentLobby.HostId;
    }

    #endregion

    #region 스팀 콜백 핸들러 리전

    private void OnAvatarImageLoaded(AvatarImageLoaded_t callback)
    {
        if (currentLobby != null)
        {
            UpdateRoomUI();
        }
    }

    private void OnPersonaStateChange(PersonaStateChange_t callback)
    {
        if (currentLobby != null)
        {
            UpdateRoomUI();
        }
    }

    #endregion

    private void OnDestroy()
    {
        if (isSceneLoadEventSubscribed && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnAllClientsLoaded;
        }

        if (currentLobby != null && !isStartingGame)
        {
            _ = ExitRoom();
        }
    }
}
