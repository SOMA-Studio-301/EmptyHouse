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
    [Header("NAVIGATION UI")]
    [SerializeField] private GameObject createPanel;          // 방 만들기 입력 팝업/패널
    [SerializeField] private Button openCreatePanelButton;    // Lobby화면에서 Create창을 여는 버튼
    [SerializeField] private Button backButton;               // Create창에서 Lobby화면으로 돌아가는 버튼
    
    [Header("Join Password Popup UI")]
    [SerializeField] private GameObject privateJoinContent;      // 팝업 창 전체 패널 (이미지의 회색 창)
    [SerializeField] private InputField popupPasswordInput;       // 팝업 내부의 비밀번호 입력창
    [SerializeField] private Button popupConfirmJoinButton;      // 팝업 내부의 Join 버튼
    [SerializeField] private Button popupCancelJoinButton;       // 팝업 내부의 Back 버튼
    [SerializeField] private GameObject passwordWarningLabel;    // 비밀번호가 틀렸을 때 켜질 경고 메시지 오브젝트

    [Header("JOIN Tab UI")]
    [SerializeField] private Transform lobbyListContainer;
    [SerializeField] private GameObject lobbyListCellPrefab;
    [SerializeField] private InputField joinLobbyPasswordInput;

    [Header("CREATE Tab UI")]
    [SerializeField] private InputField createLobbyNameInput;
    [SerializeField] private InputField createLobbyPasswordInput;
    [SerializeField] private Toggle passwordToggle;
    [SerializeField] private Button createButton;             // 최종 방 생성 확정 버튼

    [Header("Settings")]
    [SerializeField] private int maxPlayersPerLobby = 4;
    [SerializeField] private float lobbyRefreshInterval = 10f;

    [Header("Room Manager")]
    [SerializeField] private RoomManager roomManager;

    private string playerName = "Player";
    private Lobby currentLobby;
    private List<Lobby> availableLobbies = new List<Lobby>();
    private float nextRefreshTime;
    private bool isRefreshing = false;
    private string selectedLobbyIdForPopup; // 팝업창 제어 시 선택된 로비 ID 임시 저장용
    
    private async void Start()
    {
        await InitializeUnityServices();
        SetupUI();
        
        if (passwordToggle != null && createLobbyPasswordInput != null)
        {
            passwordToggle.onValueChanged.RemoveAllListeners();
            passwordToggle.onValueChanged.AddListener(OnPasswordToggleChanged);
            createLobbyPasswordInput.gameObject.SetActive(passwordToggle.isOn);
        }
    }
    
    private void OnPasswordToggleChanged(bool isOn)
    {
        if (createLobbyPasswordInput != null)
        {
            createLobbyPasswordInput.gameObject.SetActive(isOn);
            Debug.Log($"[LOBBY] 비밀번호 방 설정 토글 변경: {isOn}");
        }
    }

    private void Update()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized || !AuthenticationService.Instance.IsSignedIn) 
            return;

        if (Time.time >= nextRefreshTime && currentLobby == null && !isRefreshing)
        {
            nextRefreshTime = Time.time + lobbyRefreshInterval;
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
            
            if (SteamManager.Initialized)
            {
                playerName = SteamFriends.GetPersonaName();
                Debug.Log($"[STEAM] 스팀 닉네임 적용 완료: {playerName}");
            }
            
            Debug.Log("[INIT] Loading initial lobby list...");
            await RefreshLobbyList();
            
            nextRefreshTime = Time.time + lobbyRefreshInterval;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ERROR] Failed to initialize Unity Services: {e.Message}");
        }
    }

    #endregion

    #region UI Setup & Navigation

    private void SetupUI()
    {
        // 1. 패널 내비게이션 리스너 등록
        if (openCreatePanelButton != null)
        {
            openCreatePanelButton.onClick.RemoveAllListeners();
            openCreatePanelButton.onClick.AddListener(ShowCreatePanel);
        }

        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(HideCreatePanel);
        }

        // 2. 최종 방 생성 버튼 리스너 등록
        if (createButton != null)
        {
            createButton.onClick.RemoveAllListeners();
            createButton.onClick.AddListener(() => _ = CreateLobby());
        }

        // 3. 비밀번호 입력 팝업 (Join / Back) 버튼 리스너 등록 (★추가)
        if (popupConfirmJoinButton != null)
        {
            popupConfirmJoinButton.onClick.RemoveAllListeners();
            popupConfirmJoinButton.onClick.AddListener(OnPopupConfirmJoinClicked);
        }

        if (popupCancelJoinButton != null)
        {
            popupCancelJoinButton.onClick.RemoveAllListeners();
            popupCancelJoinButton.onClick.AddListener(OnPopupCancelClicked);
        }

        // 초기 상태 패널 상태 비활성화 정돈
        HideCreatePanel();
        if (privateJoinContent != null) privateJoinContent.SetActive(false);
        if (passwordWarningLabel != null) passwordWarningLabel.SetActive(false);
    }

    public void ShowCreatePanel()
    {
        if (createPanel != null)
        {
            createPanel.SetActive(true);
        }
    }

    public void HideCreatePanel()
    {
        if (createPanel != null)
        {
            createPanel.SetActive(false);
        }
    }

    // ★ [수정] 팝업 내부의 Back(취소) 버튼을 눌렀을 때의 처리
    private void OnPopupCancelClicked()
    {
        selectedLobbyIdForPopup = "";
        if (popupPasswordInput != null) popupPasswordInput.text = "";
        if (passwordWarningLabel != null) passwordWarningLabel.SetActive(false);
        if (privateJoinContent != null) privateJoinContent.SetActive(false);
        Debug.Log("[JOIN] 비밀번호 입력창을 닫았습니다.");
    }

    // 방에서 나가거나 초기화될 때 UI 상태를 깔끔하게 청소하는 헬퍼 함수
    public void ResetLobbyUI()
    {
        if (createLobbyNameInput != null) createLobbyNameInput.text = "";
        if (createLobbyPasswordInput != null) createLobbyPasswordInput.text = "";
        if (passwordToggle != null) passwordToggle.isOn = false;
        
        HideCreatePanel();

        // ★ [추가] 패스워드 관련 UI 컴포넌트 데이터도 함께 청소
        selectedLobbyIdForPopup = "";
        if (popupPasswordInput != null) popupPasswordInput.text = "";
        if (passwordWarningLabel != null) passwordWarningLabel.SetActive(false);
        if (privateJoinContent != null) privateJoinContent.SetActive(false);
    }

    #endregion

    #region Lobby List Management

    public async Task RefreshLobbyList()
    {
        if (!AuthenticationService.Instance.IsSignedIn || isRefreshing)
        {
            return;
        }

        isRefreshing = true;

        try
        {
            Debug.Log("[REFRESH] Querying lobbies...");
            QueryResponse queryResponse = await LobbyService.Instance.QueryLobbiesAsync();
            availableLobbies = queryResponse.Results;
            Debug.Log($"[REFRESH] Found {availableLobbies.Count} lobbies");

            UpdateLobbyListUI();
        }
        catch (Exception e)
        {
            Debug.LogError($"[ERROR] Failed to refresh lobby list: {e.Message}");
        }
        finally
        {
            isRefreshing = false;
        }
    }

    private void UpdateLobbyListUI()
    {
        if (lobbyListContainer == null || lobbyListCellPrefab == null) return;

        foreach (Transform child in lobbyListContainer)
        {
            if (Application.isPlaying)
            {
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }

        foreach (Lobby lobby in availableLobbies)
        {
            GameObject cellObj = Instantiate(lobbyListCellPrefab, lobbyListContainer);
            LobbyListCell cell = cellObj.GetComponent<LobbyListCell>();

            if (cell != null)
            {
                cell.SetLobbyInfo(lobby, OnLobbyListJoinClicked);
            }
        }
    }

    // ★ [구조 변경] 리스트 내부 셀의 Join 버튼을 클릭했을 때의 분기 처리
    private void OnLobbyListJoinClicked(Lobby lobby)
    {
        // 로비에 비밀번호가 설정되어 있는 경우
        if (lobby.Data != null && lobby.Data.ContainsKey("Password"))
        {
            Debug.Log($"[JOIN] '{lobby.Name}' 방은 비밀번호 방입니다. 입력 팝업창을 엽니다.");
            selectedLobbyIdForPopup = lobby.Id; // 대상 방 ID 보관
            
            if (popupPasswordInput != null) popupPasswordInput.text = "";
            if (passwordWarningLabel != null) passwordWarningLabel.SetActive(false); // 경고 레이블은 일단 숨김
            if (privateJoinContent != null) privateJoinContent.SetActive(true);      // 팝업 패널 활성화
        }
        else
        {
            // 비밀번호가 없는 일반 공개 방인 경우 -> 패스워드 없이 다이렉트 조인 시도
            Debug.Log($"[JOIN] '{lobby.Name}' 방은 공개 방입니다. 즉시 입장을 시도합니다.");
            _ = JoinLobbyById(lobby.Id, "");
        }
    }

    // ★ [추가] 팝업 내부의 Join 버튼을 눌렀을 때의 리스너 이벤트
    private async void OnPopupConfirmJoinClicked()
    {
        if (string.IsNullOrEmpty(selectedLobbyIdForPopup)) return;

        string enteredPassword = popupPasswordInput != null ? popupPasswordInput.text.Trim() : "";
        await JoinLobbyById(selectedLobbyIdForPopup, enteredPassword);
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
                IsPrivate = false, 
                Player = new Player
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        { "PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, playerName) },
                        { "SteamID", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, SteamUser.GetSteamID().ToString())}
                    }
                },
                Data = new Dictionary<string, DataObject>()
            };

            if (!string.IsNullOrEmpty(password))
            {
                options.Data.Add("Password", new DataObject(
                    DataObject.VisibilityOptions.Public,
                    password,
                    DataObject.IndexOptions.S1));
            }

            currentLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayersPerLobby, options);

            Debug.Log($"[CREATE] Created lobby: {currentLobby.Name} (ID: {currentLobby.Id})");

            ResetLobbyUI();

            InvokeRepeating(nameof(SendLobbyHeartbeat), 15f, 15f);

            if (roomManager != null)
            {
                roomManager.EnterRoom(currentLobby);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to create lobby: {e.Message}");
        }
    }

    private async void SendLobbyHeartbeat()
    {
        if (currentLobby == null || !AuthenticationService.Instance.IsSignedIn)
        {
            CancelInvoke(nameof(SendLobbyHeartbeat));
            return;
        }

        try
        {
            await LobbyService.Instance.SendHeartbeatPingAsync(currentLobby.Id);
        }
        catch (LobbyServiceException e)
        {
            if (e.ErrorCode == 404 || e.ErrorCode == 403 || e.ErrorCode == 400 || 
                e.Message.ToLower().Contains("not found") || e.Message.ToLower().Contains("host"))
            {
                CancelInvoke(nameof(SendLobbyHeartbeat));
                return;
            }
        }
        catch (Exception e)
        {
            if (e.Message.ToLower().Contains("host") || e.Message.ToLower().Contains("not found"))
            {
                CancelInvoke(nameof(SendLobbyHeartbeat));
                return;
            }
        }
    }

    #endregion

    #region Join Lobby

    private async Task JoinLobbyById(string lobbyId, string password = "")
    {
        try
        {
            Lobby targetLobby = await LobbyService.Instance.GetLobbyAsync(lobbyId);

            // 비밀번호가 존재하는 방인 경우 1차 문자열 일치성 검증 수행
            if (targetLobby.Data != null && targetLobby.Data.ContainsKey("Password"))
            {
                string lobbyPassword = targetLobby.Data["Password"].Value;
                if (string.IsNullOrEmpty(password) || lobbyPassword != password)
                {
                    Debug.LogWarning("[JOIN] 비밀번호가 일치하지 않습니다!");
                    
                    // ★ [핵심 구현] 비밀번호 오기입 경고 메시지 활성화 (창은 닫지 않음)
                    if (passwordWarningLabel != null) passwordWarningLabel.SetActive(true);
                    return;
                }
            }

            JoinLobbyByIdOptions options = new JoinLobbyByIdOptions
            {
                Player = new Player
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        { "PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, playerName) },
                        { "SteamID", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, SteamUser.GetSteamID().ToString())}
                    }
                }
            };

            currentLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId, options);
            
            if (joinLobbyPasswordInput != null) joinLobbyPasswordInput.text = "";

            // ★ [추가] 입장 완벽 성공 시 패스워드 입력창 및 경고 UI 완전 초기화 오프
            if (privateJoinContent != null) privateJoinContent.SetActive(false);
            if (passwordWarningLabel != null) passwordWarningLabel.SetActive(false);
            selectedLobbyIdForPopup = "";

            if (roomManager != null)
            {
                roomManager.EnterRoom(currentLobby);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to join lobby by ID: {e.Message}");
            
            // 네트워크 오류나 방 폭파 등 일반 API 예외 차원에서도 에러 텍스트 표기 방어
            if (passwordWarningLabel != null) passwordWarningLabel.SetActive(true);
        }
    }

    #endregion

    #region Leave Lobby

    public async Task LeaveLobby()
    {
        if (currentLobby == null) return;

        CancelInvoke(nameof(SendLobbyHeartbeat)); 

        try
        {
            string lobbyId = currentLobby.Id;
            string playerId = AuthenticationService.Instance.PlayerId;

            await LobbyService.Instance.RemovePlayerAsync(lobbyId, playerId);

            if (this == null) return; 
            Debug.Log($"Left lobby: {currentLobby.Name}");
        }
        catch (Exception e)
        {
            if (this == null) return; 
            Debug.LogError($"Failed to leave lobby: {e.Message}");
        }
    
        if (this != null)
        {
            currentLobby = null;
            ResetLobbyUI(); 
        }
    }

    #endregion

    private void OnDestroy()
    {
        // RoomManager를 찾아서 현재 게임 시작 중인지 확인합니다.
        RoomManager roomManager = FindFirstObjectByType<RoomManager>();
        bool isStarting = roomManager != null && roomManager.IsStartingGame;

        // 게임 시작 중이 아닐 때만 방에서 나갑니다.
        if (currentLobby != null && !isStarting)
        {
            _ = LeaveLobby();
        }
    }
    
    public void StartHeartbeatInstance()
    {
        CancelInvoke(nameof(SendLobbyHeartbeat));
        InvokeRepeating(nameof(SendLobbyHeartbeat), 15f, 15f);
        Debug.Log("[HEARTBEAT] 새 방장 권한을 위임받아 하트비트 타이머를 가동합니다.");
    }
}