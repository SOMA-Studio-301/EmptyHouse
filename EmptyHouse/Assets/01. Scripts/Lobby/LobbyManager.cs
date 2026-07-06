using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Steamworks;
using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.UI;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

public class LobbyManager : MonoBehaviour
{
    [Header("JOIN Tab UI")]
    [SerializeField] private Transform lobbyListContainer;
    [SerializeField] private GameObject lobbyListCellPrefab;
    [SerializeField] private InputField joinLobbyNameInput;
    [SerializeField] private InputField joinLobbyPasswordInput;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button reconnectButton;
    [SerializeField] private Text playerNameText;
    [SerializeField] private Button renameButton;
    [SerializeField] private GameObject renamePanel;
    [SerializeField] private InputField renameInput;

    [Header("CREATE Tab UI")]
    [SerializeField] private InputField createLobbyNameInput;
    [SerializeField] private InputField createLobbyPasswordInput;
    [SerializeField] private Button createButton;

    [Header("Settings")]
    [SerializeField] private int maxPlayersPerLobby = 4;
    [SerializeField] private float lobbyRefreshInterval = 10f;

    [Header("Room Manager")]
    [SerializeField] private RoomManager roomManager;

    private string _playerName = "Player";
    private Lobby _currentLobby;
    private List<Lobby> _availableLobbies = new List<Lobby>();
    private float _nextRefreshTime;
    private bool _isRefreshing = false;
    
    private async void Start()
    {
        await InitializeUnityServices();
        SetupUI();
        UpdatePlayerNameDisplay();
    }

    private void Update()
    {
        // 유니티 서비스 인증(로그인)이 완료되지 않았다면 리턴
        if (UnityServices.State != ServicesInitializationState.Initialized || !AuthenticationService.Instance.IsSignedIn) 
            return;

        // 현재 로비 갱신 중(_isRefreshing)이 아닐 때만 타이머 체크
        if (Time.time >= _nextRefreshTime && _currentLobby == null && !_isRefreshing)
        {
            // 호출하기 직전에 타이머를 먼저 밀어주어 중복 호출 방지
            _nextRefreshTime = Time.time + lobbyRefreshInterval;
            _ = RefreshLobbyList();
        }
    }

    #region Unity Services Initialization

    private async Task InitializeUnityServices()
    {
        try
        {
            Debug.Log("[INIT] Initializing Unity Services...");
            await UnityServices.InitializeAsync();
            Debug.Log("[INIT] Unity Services initialized successfully");

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                Debug.Log("[INIT] Signing in anonymously...");
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log($"[INIT] Signed in as: {AuthenticationService.Instance.PlayerId}");
            }
            else
            {
                Debug.Log($"[INIT] Already signed in as: {AuthenticationService.Instance.PlayerId}");
            }
            
            // 유니티 로그인이 끝난 후 스팀이 켜져 있다면 스팀 닉네임으로 덮어쓰기
            if (SteamManager.Initialized)
            {
                _playerName = Steamworks.SteamFriends.GetPersonaName();
                UpdatePlayerNameDisplay(); // UI 텍스트 컴포넌트 갱신
                Debug.Log($"[STEAM] 스팀 닉네임 적용 완료: {_playerName}");
            }
            
            // 초기 로비 목록 로드
            Debug.Log("[INIT] Loading initial lobby list...");
            await RefreshLobbyList();
            
            _nextRefreshTime = Time.time + lobbyRefreshInterval;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ERROR] Failed to initialize Unity Services: {e.Message}");
            Debug.LogError($"[ERROR] Stack trace: {e.StackTrace}");
        }
    }

    #endregion

    #region UI Setup

    private void SetupUI()
    {
        // JOIN Tab
        joinButton.onClick.AddListener(() => _ = JoinLobbyByName());
        reconnectButton.onClick.AddListener(() => _ = RefreshLobbyList());

        // CREATE Tab
        createButton.onClick.AddListener(() => _ = CreateLobby());
    }

    #endregion

    #region Nickname Management

    private void UpdatePlayerNameDisplay()
    {
        if (playerNameText != null)
            playerNameText.text = _playerName;
    }

    #endregion

    #region Lobby List Management

    public async Task RefreshLobbyList()
    {
        // 방어 코드: 로그인 상태가 아니거나 "이미 로비를 불러오는 중"이면 취소
        if (!AuthenticationService.Instance.IsSignedIn || _isRefreshing)
        {
            return;
        }

        // 갱신 시작 플래그 ON
        _isRefreshing = true;

        try
        {
            Debug.Log("[REFRESH] Querying lobbies...");
            QueryResponse queryResponse = await LobbyService.Instance.QueryLobbiesAsync();
            _availableLobbies = queryResponse.Results;
            Debug.Log($"[REFRESH] Found {_availableLobbies.Count} lobbies");

            UpdateLobbyListUI();
        }
        catch (Exception e)
        {
            Debug.LogError($"[ERROR] Failed to refresh lobby list: {e.Message}");
        }
        finally
        {
            // [중요] 성공하든 실패하든 처리가 끝나면 플래그를 꺼서 다음 요청이 가능하게 함
            _isRefreshing = false;
        }
    }

    private void UpdateLobbyListUI()
    {
        Debug.Log($"[UI] Updating lobby list UI. Container: {(lobbyListContainer != null ? "OK" : "NULL")}, Prefab: {(lobbyListCellPrefab != null ? "OK" : "NULL")}");

        if (lobbyListContainer == null)
        {
            Debug.LogError("[UI] Lobby list container is NULL! Please assign it in the Inspector.");
            return;
        }

        if (lobbyListCellPrefab == null)
        {
            Debug.LogError("[UI] Lobby list cell prefab is NULL! Please assign it in the Inspector.");
            return;
        }

        // 기존 목록 제거
        int childCount = lobbyListContainer.childCount;
        Debug.Log($"[UI] Removing {childCount} existing lobby cells");
        foreach (Transform child in lobbyListContainer)
        {
            //에디터 오류 해결용 코드 (DestroyImmediate를 쓰면 에디터에서 Destroy를 써도 오류가 나지 않습니다.)
            if (Application.isPlaying)
            {
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }

        // 새로운 목록 생성
        Debug.Log($"[UI] Creating {_availableLobbies.Count} lobby cells");
        foreach (Lobby lobby in _availableLobbies)
        {
            GameObject cellObj = Instantiate(lobbyListCellPrefab, lobbyListContainer);
            LobbyListCell cell = cellObj.GetComponent<LobbyListCell>();

            if (cell != null)
            {
                cell.SetLobbyInfo(lobby, OnLobbyListJoinClicked);
                Debug.Log($"[UI] Created cell for lobby: {lobby.Name}");
            }
            else
            {
                Debug.LogError($"[UI] LobbyListCell component not found on prefab!");
            }
        }
        Debug.Log("[UI] Lobby list UI update complete");
    }

    private async void OnLobbyListJoinClicked(Lobby lobby)
    {
        string inputPassword = joinLobbyPasswordInput != null ? joinLobbyPasswordInput.text.Trim() : "";
        await JoinLobbyById(lobby.Id, inputPassword);
    }

    #endregion

    #region Create Lobby

    private async Task CreateLobby()
    {
        string lobbyName = createLobbyNameInput.text.Trim();
        string password = createLobbyPasswordInput.text.Trim();

        if (string.IsNullOrEmpty(lobbyName))
        {
            Debug.LogWarning("Lobby name cannot be empty!");
            return;
        }

        try
        {
            CreateLobbyOptions options = new CreateLobbyOptions
            {
                IsPrivate = false, // 모든 방을 Public으로 설정하여 Query에 표시
                Player = new Player
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        { "PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, _playerName) },
                        { "SteamID", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, Steamworks.SteamUser.GetSteamID().ToString())}
                    }
                },
                Data = new Dictionary<string, DataObject>()
            };

            // 비밀번호 확인
            if (!string.IsNullOrEmpty(password))
            {
                options.Data.Add("Password", new DataObject(
                    DataObject.VisibilityOptions.Public,
                    password,
                    DataObject.IndexOptions.S1));
            }

            _currentLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayersPerLobby, options);

            Debug.Log($"[CREATE] Created lobby: {_currentLobby.Name} (ID: {_currentLobby.Id})");
            Debug.Log($"[CREATE] Lobby Code: {_currentLobby.LobbyCode}");
            Debug.Log($"[CREATE] IsPrivate: {_currentLobby.IsPrivate}");

            // 생성 후 입력 필드 초기화
            createLobbyNameInput.text = "";
            createLobbyPasswordInput.text = "";

            // 로비 하트비트 시작 (로비가 자동으로 제거되지 않도록)
            InvokeRepeating(nameof(SendLobbyHeartbeat), 15f, 15f);

            // RoomManager로 방 입장
            if (roomManager != null)
            {
                roomManager.EnterRoom(_currentLobby);
            }
            else
            {
                Debug.LogError("[CREATE] RoomManager is not assigned!");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to create lobby: {e.Message}");
        }
    }

    private async void SendLobbyHeartbeat()
    {
        // 1. 로비가 없거나 로그아웃 상태면 타이머 정지
        if (_currentLobby == null || !AuthenticationService.Instance.IsSignedIn)
        {
            CancelInvoke(nameof(SendLobbyHeartbeat));
            return;
        }

        try
        {
            await LobbyService.Instance.SendHeartbeatPingAsync(_currentLobby.Id);
        }
        catch (LobbyServiceException e)
        {
            // UGS 서비스 예외 처리 (404: 방 삭제 / 403, 400: 권한 상실)
            if (e.ErrorCode == 404 || e.ErrorCode == 403 || e.ErrorCode == 400 || 
                e.Message.ToLower().Contains("not found") || e.Message.ToLower().Contains("host"))
            {
                Debug.LogWarning($"[HEARTBEAT] 방장 권한 위임 또는 방 삭제 감지. 하트비트를 정지합니다. ({e.Message})");
                CancelInvoke(nameof(SendLobbyHeartbeat));
                return;
            }
        
            Debug.LogError($"[HEARTBEAT] UGS 서비스 에러: {e.Message}");
        }
        catch (Exception e)
        {
            // ★ [핵심 수정] 일반 예외 블록 (330번째 줄 부근)
            // 위임 도중 타이밍 이슈로 서버 텍스트 에러가 이쪽으로 튕겨 들어올 때의 방어 코드
            if (e.Message.ToLower().Contains("host") || e.Message.ToLower().Contains("not found"))
            {
                Debug.LogWarning($"[HEARTBEAT] 레이스 컨디션으로 인한 권한 상실 감지. 타이머를 정지합니다.");
                CancelInvoke(nameof(SendLobbyHeartbeat));
                return;
            }
            
            Debug.LogError($"Failed to send heartbeat: {e.Message}");
        }
    }

    #endregion

    #region Join Lobby

    private async Task JoinLobbyByName()
    {
        string lobbyName = joinLobbyNameInput.text.Trim();
        string password = joinLobbyPasswordInput.text.Trim();

        if (string.IsNullOrEmpty(lobbyName))
        {
            Debug.LogWarning("Lobby name cannot be empty!");
            return;
        }

        try
        {
            // 이름으로 로비 찾기
            QueryResponse queryResponse = await LobbyService.Instance.QueryLobbiesAsync();
            Lobby targetLobby = queryResponse.Results.Find(l => l.Name == lobbyName);

            if (targetLobby == null)
            {
                Debug.LogWarning($"Lobby '{lobbyName}' not found!");
                return;
            }

            // 비밀번호 확인
            if (targetLobby.Data != null && targetLobby.Data.ContainsKey("Password"))
            {
                string lobbyPassword = targetLobby.Data["Password"].Value;
                if (lobbyPassword != password)
                {
                    Debug.LogWarning("Incorrect password!");
                    return;
                }
            }

            await JoinLobbyById(targetLobby.Id, password);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to join lobby: {e.Message}");
        }
    }

    private async Task JoinLobbyById(string lobbyId, string password = "")
    {
        try
        {
            // 먼저 로비 정보를 조회하여 비밀번호 확인
            Lobby targetLobby = await LobbyService.Instance.GetLobbyAsync(lobbyId);

            // 비밀번호 확인
            if (targetLobby.Data != null && targetLobby.Data.ContainsKey("Password"))
            {
                string lobbyPassword = targetLobby.Data["Password"].Value;
                if (string.IsNullOrEmpty(password) || lobbyPassword != password)
                {
                    Debug.LogWarning("[JOIN] 비밀번호가 필요한 방입니다!");
                    return;
                }
            }

            JoinLobbyByIdOptions options = new JoinLobbyByIdOptions
            {
                Player = new Player
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        { "PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, _playerName) },
                        {"SteamID", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, Steamworks.SteamUser.GetSteamID().ToString())}
                    }
                }
            };

            _currentLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId, options);

            Debug.Log($"[JOIN] Joined lobby: {_currentLobby.Name} (ID: {_currentLobby.Id})");
            Debug.Log($"[JOIN] Players in lobby: {_currentLobby.Players.Count}/{_currentLobby.MaxPlayers}");

            // 입력 필드 초기화
            joinLobbyNameInput.text = "";
            joinLobbyPasswordInput.text = "";

            // RoomManager로 방 입장
            if (roomManager != null)
            {
                roomManager.EnterRoom(_currentLobby);
            }
            else
            {
                Debug.LogError("[JOIN] RoomManager is not assigned!");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to join lobby by ID: {e.Message}");
        }
    }

    #endregion

    #region Leave Lobby

    public async Task LeaveLobby()
    {
        if (_currentLobby == null) return;

        CancelInvoke(nameof(SendLobbyHeartbeat)); 

        try
        {
            string lobbyId = _currentLobby.Id;
            string playerId = AuthenticationService.Instance.PlayerId;

            // 서버에 퇴장 요청 (여기서 잠깐 멈춤)
            await LobbyService.Instance.RemovePlayerAsync(lobbyId, playerId);

            // ★ [핵심 추가] await 통신이 끝난 직후, 이미 유니티 에디터가 꺼져서 이 오브젝트가 파괴되었는지 검사
            if (this == null) return; 

            Debug.Log($"Left lobby: {_currentLobby.Name}");
        }
        catch (Exception e)
        {
            // ★ 예외 처리 블록 내부에서도 로그를 찍기 전에 오브젝트 생존 여부 검사
            if (this == null) return; 
        
            Debug.LogError($"Failed to leave lobby: {e.Message}");
        }
    
        // ★ 최종적으로 오브젝트가 살아있을 때만 변수 초기화 수행
        if (this != null)
        {
            _currentLobby = null;
        }
    }

    #endregion

    private void OnDestroy()
    {
        if (_currentLobby != null)
        {
            _ = LeaveLobby();
        }
    }
    
    public void StartHeartbeatInstance()
    {
        // 이미 돌고 있다면 중복 방지를 위해 끄고 시작
        CancelInvoke(nameof(SendLobbyHeartbeat));
    
        // 15초 주기로 하트비트 가동
        InvokeRepeating(nameof(SendLobbyHeartbeat), 15f, 15f);
        Debug.Log("[HEARTBEAT] 새 방장 권한을 위임받아 하트비트 타이머를 가동합니다.");
    }
}
