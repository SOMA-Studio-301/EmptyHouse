using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Border.Core;
using Steamworks;
using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.UI;
using TMPro; // ★ [마이그레이션] TextMeshPro InputField 사용을 위해 추가
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

public class LobbyManager : MonoBehaviour
{
    private const string PasswordDataKey = "Password";
    private const string PlayerNameDataKey = "PlayerName";
    private const string SteamIdDataKey = "SteamID";

    [Header("NAVIGATION UI")]
    [SerializeField] private GameObject createPanel;          // 방 만들기 입력 팝업/패널
    [SerializeField] private Button openCreatePanelButton;    // Lobby화면에서 Create창을 여는 버튼
    [SerializeField] private Button backButton;               // Create창에서 Lobby화면으로 돌아가는 버튼
    
    [Header("Join Password Popup UI")]
    [SerializeField] private GameObject privateJoinContent;      // 팝업 창 전체 패널 (이미지의 회색 창)
    [SerializeField] private TMP_InputField popupPasswordInput;   // ★ [마이그레이션] 팝업 내부의 비밀번호 입력창 (TMP)
    [SerializeField] private Button popupConfirmJoinButton;      // 팝업 내부의 Join 버튼
    [SerializeField] private Button popupCancelJoinButton;       // 팝업 내부의 Back 버튼
    [SerializeField] private GameObject passwordWarningLabel;    // 비밀번호가 틀렸을 때 켜질 경고 메시지 오브젝트

    [Header("JOIN Tab UI")]
    [SerializeField] private Transform lobbyListContainer;
    [SerializeField] private LobbyListCell lobbyListCellPrefab;
    [SerializeField] private TMP_InputField joinLobbyPasswordInput; // ★ [마이그레이션] TMP
    [SerializeField] private Button refreshButton;             // ★ [추가] 로비 리스트 새로고침 버튼

    [Header("CREATE Tab UI")]
    [SerializeField] private TMP_InputField createLobbyNameInput;     // ★ [마이그레이션] TMP
    [SerializeField] private TMP_InputField createLobbyPasswordInput; // ★ [마이그레이션] TMP
    [SerializeField] private Toggle passwordToggle;
    [SerializeField] private Button createButton;             // 최종 방 생성 확정 버튼
    [SerializeField] private GameObject forbiddenWordWarningLabel; // ★ [추가] 금칙어 포함 시 켜질 경고 메시지

    [Header("Forbidden Word Filter")]
    [SerializeField] private TextAsset forbiddenWordsCsv;     // ★ [추가] 금칙어 목록 CSV (콤마 또는 줄바꿈 구분)

    [Header("Settings")]
    [SerializeField] private int maxPlayersPerLobby = 4;
    [SerializeField] private float lobbyRefreshInterval = 10f;
    [SerializeField] private float refreshButtonCooldown = 5f; // ★ [추가] Refresh 버튼 재사용 대기시간(초)

    [Header("Room Manager")]
    [SerializeField] private RoomManager roomManager;

    private string playerName = "Player";
    private Lobby currentLobby;
    private List<Lobby> availableLobbies = new List<Lobby>();
    private float nextRefreshTime;
    private bool isRefreshing = false;
    private string selectedLobbyIdForPopup; // 팝업창 제어 시 선택된 로비 ID 임시 저장용

    private HashSet<string> forbiddenWords = new HashSet<string>(); // ★ [추가] 로드된 금칙어 집합
    private bool isRefreshButtonOnCooldown = false;                 // ★ [추가] Refresh 버튼 쿨다운 상태
    
    private async void Start()
    {
        LoadForbiddenWords();
        SetupUI();
        await InitializeUnityServices();
    }
    
    private void OnPasswordToggleChanged(bool isOn)
    {
        if (createLobbyPasswordInput != null)
        {
            createLobbyPasswordInput.gameObject.SetActive(isOn);
            Log.D($"[LOBBY] 비밀번호 방 설정 토글 변경: {isOn}");
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
            Log.D("[INIT] Initializing Unity Services...");
            await UnityServices.InitializeAsync();
            Log.D("[INIT] Unity Services initialized successfully");

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                Log.D("[INIT] Signing in anonymously...");
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Log.D($"[INIT] Signed in as: {AuthenticationService.Instance.PlayerId}");
            }
            else
            {
                Log.D($"[INIT] Already signed in as: {AuthenticationService.Instance.PlayerId}");
            }
            
            if (SteamManager.Initialized)
            {
                playerName = SteamFriends.GetPersonaName();
                Log.D($"[STEAM] 스팀 닉네임 적용 완료: {playerName}");
            }

            // Game -> Lobby 복귀 시 씬 UI는 새로 만들어지지만 방 세션은 Coordinator에 남아 있다.
            if (SessionCoordinator.Instance.HasRoom)
            {
                try
                {
                    currentLobby = await SessionCoordinator.Instance.RefreshCurrentLobbyAsync();
                    bool isStillMember = currentLobby?.Players != null
                        && currentLobby.Players.Any(
                            player => player.Id == AuthenticationService.Instance.PlayerId);

                    if (currentLobby != null && !isStillMember)
                    {
                        await SessionCoordinator.Instance.DiscardStaleSessionAsync();
                        currentLobby = null;
                    }

                    if (currentLobby != null && roomManager != null)
                    {
                        ResetLobbyUI();
                        roomManager.EnterRoom(currentLobby);
                        nextRefreshTime = Time.time + lobbyRefreshInterval;
                        return;
                    }
                }
                catch (LobbyServiceException e) when (e.ErrorCode == 404)
                {
                    await SessionCoordinator.Instance.HandleRoomDestroyedAsync();
                    currentLobby = null;
                }
            }
            
            Log.D("[INIT] Loading initial lobby list...");
            await RefreshLobbyList();
            
            nextRefreshTime = Time.time + lobbyRefreshInterval;
        }
        catch (Exception e)
        {
            Log.E($"[ERROR] Failed to initialize Unity Services: {e.Message}", this);
        }
    }

    #endregion

    #region Forbidden Word Filter ★ [추가]

    // CSV(TextAsset)에서 금칙어 목록을 읽어 소문자로 정규화하여 저장
    private void LoadForbiddenWords()
    {
        forbiddenWords.Clear();

        if (forbiddenWordsCsv == null)
        {
            Log.W("[FILTER] 금칙어 CSV 파일이 할당되지 않았습니다. 필터링이 동작하지 않습니다.", this);
            return;
        }

        // 콤마와 줄바꿈(개행) 둘 다 구분자로 처리 -> "욕설1,욕설2\n욕설3" 형태 모두 지원
        string[] tokens = forbiddenWordsCsv.text.Split(
            new[] { ',', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (string token in tokens)
        {
            string word = token.Trim();
            if (!string.IsNullOrEmpty(word))
            {
                forbiddenWords.Add(word.ToLowerInvariant());
            }
        }

        Log.D($"[FILTER] 금칙어 {forbiddenWords.Count}개 로드 완료");
    }

    // 대상 텍스트에 금칙어가 포함되어 있는지 검사 (부분 일치, 대소문자 무시)
    private bool ContainsForbiddenWord(string text, out string matchedWord)
    {
        matchedWord = null;

        if (string.IsNullOrEmpty(text) || forbiddenWords.Count == 0)
        {
            return false;
        }

        string lowerText = text.ToLowerInvariant();

        foreach (string word in forbiddenWords)
        {
            if (lowerText.Contains(word))
            {
                matchedWord = word;
                return true;
            }
        }

        return false;
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

        // 3. 비밀번호 입력 팝업 (Join / Back) 버튼 리스너 등록
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

        // 4. ★ [추가] Refresh 버튼 리스너 등록
        if (refreshButton != null)
        {
            refreshButton.onClick.RemoveAllListeners();
            refreshButton.onClick.AddListener(() => _ = OnRefreshButtonClicked());
        }

        if (passwordToggle != null)
        {
            passwordToggle.onValueChanged.RemoveAllListeners();
            passwordToggle.onValueChanged.AddListener(OnPasswordToggleChanged);
            OnPasswordToggleChanged(passwordToggle.isOn);
        }

        // 초기 상태 패널 상태 비활성화 정돈
        HideCreatePanel();
        if (privateJoinContent != null) privateJoinContent.SetActive(false);
        if (passwordWarningLabel != null) passwordWarningLabel.SetActive(false);
        if (forbiddenWordWarningLabel != null) forbiddenWordWarningLabel.SetActive(false); // ★ [추가]
    }

    public void ShowCreatePanel()
    {
        // ★ [수정] 패널을 다시 열 때마다 이전 입력값이 남아있지 않도록 초기화
        if (createLobbyNameInput != null) createLobbyNameInput.text = "";
        if (createLobbyPasswordInput != null) createLobbyPasswordInput.text = "";
        if (forbiddenWordWarningLabel != null) forbiddenWordWarningLabel.SetActive(false);

        // passwordToggle.isOn = false는 등록된 onValueChanged 리스너(OnPasswordToggleChanged)를 통해
        // createLobbyPasswordInput.gameObject를 자동으로 비활성화시킴
        if (passwordToggle != null) passwordToggle.isOn = false;

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

    // ★ 팝업 내부의 Back(취소) 버튼을 눌렀을 때의 처리
    private void OnPopupCancelClicked()
    {
        selectedLobbyIdForPopup = "";
        if (popupPasswordInput != null) popupPasswordInput.text = "";
        if (passwordWarningLabel != null) passwordWarningLabel.SetActive(false);
        if (privateJoinContent != null) privateJoinContent.SetActive(false);
        Log.D("[JOIN] 비밀번호 입력창을 닫았습니다.");
    }

    // 방에서 나가거나 초기화될 때 UI 상태를 깔끔하게 청소하는 헬퍼 함수
    public void ResetLobbyUI()
    {
        // ★ [마이그레이션] TMP_InputField는 레거시 InputField의 IME 캐럿 버그가 없지만,
        // SetActive로 인한 레이아웃 리빌드 전에 포커스를 먼저 해제해주는 습관은 안전하므로 유지
        if (createLobbyNameInput != null) createLobbyNameInput.DeactivateInputField();
        if (createLobbyPasswordInput != null) createLobbyPasswordInput.DeactivateInputField();
        if (popupPasswordInput != null) popupPasswordInput.DeactivateInputField();
        if (joinLobbyPasswordInput != null) joinLobbyPasswordInput.DeactivateInputField();

        if (createLobbyNameInput != null) createLobbyNameInput.text = "";
        if (createLobbyPasswordInput != null) createLobbyPasswordInput.text = "";
        if (passwordToggle != null) passwordToggle.isOn = false;
        
        HideCreatePanel();

        // 패스워드 관련 UI 컴포넌트 데이터도 함께 청소
        selectedLobbyIdForPopup = "";
        if (popupPasswordInput != null) popupPasswordInput.text = "";
        if (passwordWarningLabel != null) passwordWarningLabel.SetActive(false);
        if (privateJoinContent != null) privateJoinContent.SetActive(false);
        if (forbiddenWordWarningLabel != null) forbiddenWordWarningLabel.SetActive(false); // ★ [추가]
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
            Log.D("[REFRESH] Querying lobbies...");
            QueryResponse queryResponse = await LobbyService.Instance.QueryLobbiesAsync();
            availableLobbies = queryResponse.Results;
            Log.D($"[REFRESH] Found {availableLobbies.Count} lobbies");

            UpdateLobbyListUI();
        }
        catch (Exception e)
        {
            Log.E($"[ERROR] Failed to refresh lobby list: {e.Message}", this);
        }
        finally
        {
            isRefreshing = false;
        }
    }

    // ★ [추가] Refresh 버튼 클릭 처리 - 즉시 재조회 후 쿨다운 동안 버튼 비활성화
    private async Task OnRefreshButtonClicked()
    {
        if (isRefreshButtonOnCooldown)
        {
            return;
        }

        isRefreshButtonOnCooldown = true;
        if (refreshButton != null) refreshButton.interactable = false;

        await RefreshLobbyList();

        // 자동 갱신 타이머와 겹쳐서 곧바로 또 호출되지 않도록 타이밍 재조정
        nextRefreshTime = Time.time + lobbyRefreshInterval;

        _ = RefreshButtonCooldownRoutine();
    }

    // ★ [추가] Refresh 버튼 쿨다운 대기 후 재활성화
    private async Task RefreshButtonCooldownRoutine()
    {
        await Task.Delay(TimeSpan.FromSeconds(Mathf.Max(0f, refreshButtonCooldown)));

        if (this == null) return; // 오브젝트가 파괴된 경우 방어

        isRefreshButtonOnCooldown = false;
        if (refreshButton != null) refreshButton.interactable = true;
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
            LobbyListCell cell = Instantiate(lobbyListCellPrefab, lobbyListContainer);
            cell.SetLobbyInfo(lobby, OnLobbyListJoinClicked);
        }
    }

    // ★ 리스트 내부 셀의 Join 버튼을 클릭했을 때의 분기 처리
    private void OnLobbyListJoinClicked(Lobby lobby)
    {
        // 로비에 비밀번호가 설정되어 있는 경우
        if (lobby.Data != null && lobby.Data.ContainsKey(PasswordDataKey))
        {
            Log.D($"[JOIN] '{lobby.Name}' 방은 비밀번호 방입니다. 입력 팝업창을 엽니다.");
            selectedLobbyIdForPopup = lobby.Id; // 대상 방 ID 보관

            // ★ [수정] 팝업 패널 SetActive 전에 포커스된 입력창을 먼저 해제 (caret 인덱스 손상 방지)
            if (joinLobbyPasswordInput != null) joinLobbyPasswordInput.DeactivateInputField();

            if (popupPasswordInput != null) popupPasswordInput.text = "";
            if (passwordWarningLabel != null) passwordWarningLabel.SetActive(false); // 경고 레이블은 일단 숨김
            if (privateJoinContent != null) privateJoinContent.SetActive(true);      // 팝업 패널 활성화
        }
        else
        {
            // 비밀번호가 없는 일반 공개 방인 경우 -> 패스워드 없이 다이렉트 조인 시도
            Log.D($"[JOIN] '{lobby.Name}' 방은 공개 방입니다. 즉시 입장을 시도합니다.");
            _ = JoinLobbyById(lobby.Id, "");
        }
    }

    // ★ 팝업 내부의 Join 버튼을 눌렀을 때의 리스너 이벤트
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
            Log.W("Lobby name cannot be empty!", this);
            return;
        }

        // ★ [추가] 금칙어 검사 - 걸리면 방 생성 자체를 막는다
        if (ContainsForbiddenWord(lobbyName, out string matchedWord))
        {
            Log.W($"[CREATE] 방 제목에 금칙어가 포함되어 있어 생성이 차단되었습니다: '{matchedWord}'", this);

            // ★ [마이그레이션] 경고 UI(SetActive) 켜기 전에 입력창 포커스를 먼저 해제 (안전 습관 유지)
            if (createLobbyNameInput != null) createLobbyNameInput.DeactivateInputField();

            if (forbiddenWordWarningLabel != null) forbiddenWordWarningLabel.SetActive(true);
            return;
        }
        else
        {
            if (forbiddenWordWarningLabel != null) forbiddenWordWarningLabel.SetActive(false);
        }

        try
        {
            CreateLobbyOptions options = new CreateLobbyOptions
            {
                IsPrivate = false, 
                Player = BuildLocalPlayer(),
                Data = new Dictionary<string, DataObject>()
            };

            if (!string.IsNullOrEmpty(password))
            {
                options.Data.Add(PasswordDataKey, new DataObject(
                    DataObject.VisibilityOptions.Public,
                    password,
                    DataObject.IndexOptions.S1));
            }

            currentLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayersPerLobby, options);
            SessionCoordinator.Instance.SetCurrentLobby(currentLobby);

            Log.D($"[CREATE] Created lobby: {currentLobby.Name} (ID: {currentLobby.Id})");

            ResetLobbyUI();

            if (roomManager != null)
            {
                roomManager.EnterRoom(currentLobby);
            }
        }
        catch (Exception e)
        {
            Log.E($"Failed to create lobby: {e.Message}", this);
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
            if (targetLobby.Data != null && targetLobby.Data.TryGetValue(PasswordDataKey, out DataObject passwordData))
            {
                string lobbyPassword = passwordData.Value;
                if (string.IsNullOrEmpty(password) || lobbyPassword != password)
                {
                    Log.W("[JOIN] 비밀번호가 일치하지 않습니다!", this);
                    
                    if (passwordWarningLabel != null) passwordWarningLabel.SetActive(true);
                    return;
                }
            }

            JoinLobbyByIdOptions options = new JoinLobbyByIdOptions
            {
                Player = BuildLocalPlayer()
            };

            currentLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId, options);
            SessionCoordinator.Instance.SetCurrentLobby(currentLobby);
            
            if (joinLobbyPasswordInput != null) joinLobbyPasswordInput.text = "";

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
            Log.E($"Failed to join lobby by ID: {e.Message}", this);
            
            if (passwordWarningLabel != null) passwordWarningLabel.SetActive(true);
        }
    }

    #endregion

    #region Leave Lobby

    public async Task LeaveLobby()
    {
        await SessionCoordinator.Instance.ExitCurrentRoomAsync();
        if (this == null) return;

        currentLobby = null;
        ResetLobbyUI();
    }

    #endregion

    public void HandleRoomCleared()
    {
        currentLobby = null;
        ResetLobbyUI();
        nextRefreshTime = Time.time + lobbyRefreshInterval;
    }

    private Player BuildLocalPlayer()
    {
        string steamId = SteamManager.Initialized ? SteamUser.GetSteamID().ToString() : string.Empty;
        return new Player
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                { PlayerNameDataKey, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, playerName) },
                { SteamIdDataKey, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, steamId) }
            }
        };
    }

}
