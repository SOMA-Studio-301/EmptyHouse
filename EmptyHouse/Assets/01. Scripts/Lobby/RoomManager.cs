using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
    [SerializeField] private GameObject userPanelPrefab;

    [Header("Settings")]
    [SerializeField] private float lobbyPollInterval = 1.5f;
    
    [Header("Scene Settings")]
    [SerializeField] private string gameSceneName = "GameScene"; 

    private bool isStartingGame;
    private Lobby currentLobby;
    private bool isReady;
    private float nextPollTime;
    private bool isInRoom;
    private bool isPolling;

    private Callback<AvatarImageLoaded_t> avatarLoadedCallback;
    private Callback<PersonaStateChange_t> personaStateCallback;

    private void Start()
    {
        if (SteamManager.Initialized)
        {
            avatarLoadedCallback = Callback<AvatarImageLoaded_t>.Create(OnAvatarImageLoaded);
            personaStateCallback = Callback<PersonaStateChange_t>.Create(OnPersonaStateChange);
            Debug.Log("[STEAM] RoomManager 스팀 콜백 등록 완료");
        }
    }
    
    #region Room Entry

    public void EnterRoom(Lobby lobby)
    {
        currentLobby = lobby;
        isReady = false;
        isInRoom = true;

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
        }
        if (exitButton != null)
        {
            exitButton.onClick.RemoveAllListeners();
            exitButton.onClick.AddListener(OnExitButtonClicked);
        }
        
        _ = PollLobbyData();
        
        Debug.Log($"[ROOM] Entered room: {lobby.Name}");
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
                currentLobby = await LobbyService.Instance.GetLobbyAsync(currentLobby.Id);
                if (currentLobby == null) continue;
                UpdateRoomUI(); 
                
                if (!IsHost() && currentLobby.Data.ContainsKey("RelayJoinCode"))
                {
                    string joinCode = currentLobby.Data["RelayJoinCode"].Value;
                    if (!string.IsNullOrEmpty(joinCode) && !isStartingGame)
                    {
                        isStartingGame = true;
                        JoinGame(joinCode); 
                        break; 
                    }
                }
                
                await Task.Delay(2000); 
            }
        }
        catch (LobbyServiceException e)
        {
            if (e.ErrorCode == 404 || e.Message.ToLower().Contains("not found"))
            {
                Debug.LogWarning("[ROOM] 로비가 이미 서버에서 삭제되었습니다.");
                currentLobby = null; 
                isInRoom = false;
                return; 
            }
    
            if (e.ErrorCode == 429)
            {
                await Task.Delay(3000);
            }
            else
            {
                Debug.LogError($"[ROOM] 로비 데이터 갱신 실패: {e.Message}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[ROOM] 일반 에러: {e.Message}");
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
        UpdateStartButton();
    }

    private void UpdatePlayerList()
    {
        if (userListContainer == null || userPanelPrefab == null) return;

        foreach (Transform child in userListContainer)
        {
            Destroy(child.gameObject);
        }

        foreach (Player player in currentLobby.Players)
        {
            GameObject panelObj = Instantiate(userPanelPrefab, userListContainer);
            UserPanel userPanel = panelObj.GetComponent<UserPanel>();

            if (userPanel != null)
            {
                string playerName = GetPlayerName(player);
                bool isHost = player.Id == currentLobby.HostId;
                bool isReady = GetPlayerReadyStatus(player);
                string steamId = player.Data.ContainsKey("SteamID") ? player.Data["SteamID"].Value : "";

                userPanel.SetPlayerInfo(playerName, isHost, isReady, steamId);
            }
        }
    }

    private void UpdateStartButton()
    {
        if (startButton == null) return;

        bool isHost = AuthenticationService.Instance.PlayerId == currentLobby.HostId;

        if (!isHost)
        {
            startButton.interactable = false;
            return;
        }
        
        LobbyManager lobbyManager = FindFirstObjectByType<LobbyManager>();
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

        startButton.interactable = allReady && currentLobby.Players.Count > 1;
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
                    { "IsReady", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, isReady.ToString()) }
                }
            };

            currentLobby = await LobbyService.Instance.UpdatePlayerAsync(currentLobby.Id, playerId, options);
            UpdateRoomUI();
        }
        catch (Exception e)
        {
            Debug.LogError($"[ROOM] Failed to update ready status: {e.Message}");
            isReady = !isReady; 
        }
    }

    private async void OnStartButtonClicked()
    {
        if (!IsHost()) return;
        if (isStartingGame) return;

        isStartingGame = true;
        Debug.Log("[ROOM] Host가 Relay 서버 생성을 시작합니다...");

        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(currentLobby.MaxPlayers - 1);
            string relayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log($"[ROOM] Relay 생성 완료! JoinCode: {relayJoinCode}");

            RelayServerData serverData = new RelayServerData(
                allocation.RelayServer.IpV4,          
                (ushort)allocation.RelayServer.Port, 
                allocation.AllocationIdBytes,         
                allocation.ConnectionData,             
                allocation.ConnectionData,             
                allocation.Key,                        
                true                                   
            );
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(serverData);

            UpdateLobbyOptions options = new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    { "RelayJoinCode", new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode) }
                }
            };
            currentLobby = await LobbyService.Instance.UpdateLobbyAsync(currentLobby.Id, options);

            NetworkManager.Singleton.StartHost();
            NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        }
        catch (Exception e)
        {
            Debug.LogError($"[ROOM] 게임 시작 실패 (Relay 에러): {e.Message}");
            isStartingGame = false;
        }
    }

    private void OnExitButtonClicked()
    {
        _ = ExitRoom();
    }

    #endregion

    #region Game Start

    private async void JoinGame(string joinCode)
    {
        Debug.Log($"[ROOM] Client가 Relay 연결을 시도합니다. 코드: {joinCode}");

        try
        {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            RelayServerData clientServerData = new RelayServerData(
                joinAllocation.RelayServer.IpV4,         
                (ushort)joinAllocation.RelayServer.Port,
                joinAllocation.AllocationIdBytes,         
                joinAllocation.ConnectionData,             
                joinAllocation.HostConnectionData,         
                joinAllocation.Key,                        
                true                                       
            );
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(clientServerData);

            NetworkManager.Singleton.StartClient();
            isInRoom = false; 
        }
        catch (Exception e)
        {
            Debug.LogError($"[ROOM] Relay 연결 실패: {e.Message}");
            isStartingGame = false;
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

            LobbyManager lobbyManager = FindFirstObjectByType<LobbyManager>();
            if (lobbyManager != null)
                await lobbyManager.RefreshLobbyList();
        }
        catch (Exception e)
        {
            Debug.LogError($"[ROOM] Failed to exit room: {e.Message}");
        }
    }

    #endregion

    #region Helper Methods

    private string GetPlayerName(Player player)
    {
        if (player.Data != null && player.Data.ContainsKey("PlayerName"))
        {
            return player.Data["PlayerName"].Value;
        }
        return "Unknown";
    }

    private bool GetPlayerReadyStatus(Player player)
    {
        if (player.Data != null && player.Data.ContainsKey("IsReady"))
        {
            bool.TryParse(player.Data["IsReady"].Value, out bool isReadyStatus);
            return isReadyStatus;
        }
        return false;
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
        if (currentLobby != null)
        {
            _ = ExitRoom();
        }
    }
}