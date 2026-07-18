using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Border.Core;
using UnityEngine;
using UnityEngine.UI;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Steamworks; 

public class RoomManager : MonoBehaviour
{
    private const string PlayerNameDataKey = "PlayerName";
    private const string SteamIdDataKey = "SteamID";
    private const string ReadyDataKey = "IsReady";
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

    private Callback<AvatarImageLoaded_t> avatarLoadedCallback;
    private Callback<PersonaStateChange_t> personaStateCallback;
    public bool IsStartingGame => isStartingGame;

    private void Start()
    {
        SessionCoordinator.Instance.RoomCleared += HandleRoomCleared;

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
        SessionCoordinator.Instance.SetCurrentLobby(lobby);
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
        _ = PrepareRoomEntryAsync();
        
        Log.D($"[ROOM] Entered room: {lobby.Name}");
        
        //스팀 리치 프레즌스 설정
        if (SteamManager.Initialized)
        {
            SteamFriends.SetRichPresence("status", "버스 대기 중");
            SteamFriends.SetRichPresence("connect", lobby.Id);
            Log.D($"[STEAM] 리치 프레즌스 등록 완료 (LobbyID: {lobby.Id})");
        }
    }

    private async Task PrepareRoomEntryAsync()
    {
        await SessionCoordinator.Instance.NotifyReturnedToRoomAsync();
        if (this == null || !isInRoom) return;

        try
        {
            // 한 판 이상 진행된 방에 새로 들어온 경우 Room에서 기존 Relay에 미리 연결한다.
            await SessionCoordinator.Instance.ConnectToRoomNetworkIfNeededAsync();
        }
        catch (Exception e)
        {
            Log.E($"[ROOM] 기존 Room 네트워크 연결 실패: {e.Message}", this);
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
                    Lobby polledLobby = await LobbyService.Instance.GetLobbyAsync(currentLobby.Id);
                    if (!isInRoom || SessionCoordinator.Instance.IsCleaningUp) break;
                    currentLobby = polledLobby;
                    if (currentLobby == null) continue;
                    SessionCoordinator.Instance.SetCurrentLobby(currentLobby);
                    UpdateRoomUI();

                    if (!IsHost())
                    {
                        try
                        {
                            await SessionCoordinator.Instance.ConnectToRoomNetworkIfNeededAsync();
                        }
                        catch (Exception e)
                        {
                            Log.E($"[ROOM] Relay 연결 실패: {e.Message}", this);
                        }
                    }
                }
                catch (LobbyServiceException e)
                {
                    if (e.ErrorCode == 404 || e.Message.ToLower().Contains("not found"))
                    {
                        // 호스트의 방 파괴와 비정상 종료 후 Lobby 소멸을 같은 정리 경로로 처리한다.
                        if (!isStartingGame)
                        {
                            Log.W("[ROOM] 로비가 이미 서버에서 삭제되었습니다.", this);
                        }
                        isInRoom = false;
                        await SessionCoordinator.Instance.HandleRoomDestroyedAsync();
                        currentLobby = null;
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
            SessionCoordinator.Instance.SetCurrentLobby(currentLobby);
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

        Log.D("[ROOM] Host가 게임 세션 시작을 요청합니다.");

        try
        {
            await SessionCoordinator.Instance.StartGameAsHostAsync(gameSceneName);
        }
        catch (Exception e)
        {
            Log.E($"[ROOM] 게임 시작 실패: {e.Message}", this);
            isStartingGame = false;
            if (startButton != null) startButton.interactable = true;
            if (exitButton != null) exitButton.interactable = true;
        }
    }

    private void OnExitButtonClicked()
    {
        if (isStartingGame) return; // ★ 게임 시작 중이면 버튼 disable을 우회해도 방어
        _ = ExitRoom();
    }

    #endregion

    #region Exit Room

    public async Task ExitRoom()
    {
        if (SessionCoordinator.Instance.IsCleaningUp) return;
        isInRoom = false;
        if (exitButton != null) exitButton.interactable = false;

        await SessionCoordinator.Instance.ExitCurrentRoomAsync();
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
        if (SessionCoordinator.Instance != null)
            SessionCoordinator.Instance.RoomCleared -= HandleRoomCleared;
    }

    private async void HandleRoomCleared()
    {
        currentLobby = null;
        isReady = false;
        isInRoom = false;
        isStartingGame = false;

        if (roomCanvas != null) roomCanvas.SetActive(false);
        if (joinCreateCanvas != null) joinCreateCanvas.SetActive(true);
        if (exitButton != null) exitButton.interactable = true;

        if (lobbyManager != null)
        {
            lobbyManager.HandleRoomCleared();
            await lobbyManager.RefreshLobbyList();
        }
    }
}
