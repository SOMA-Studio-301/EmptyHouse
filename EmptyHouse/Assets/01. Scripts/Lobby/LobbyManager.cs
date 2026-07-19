using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Border.Core;
using Steamworks;
using Unity.Services.Authentication;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

/// <summary>
/// UGS 로비 세션 소유자. 초기화·인증·목록 조회·생성/입장/퇴장·하트비트를 담당한다.
/// 화면은 UILobby 가 소유하므로 여기서는 위젯을 직접 만지지 않는다 — 의도를 구독하고 결과를 내려줄 뿐이다.
/// </summary>
public class LobbyManager : MonoBehaviour
{
    private const float HeartbeatIntervalSeconds = 15f;

    [Header("View")]
    [SerializeField] private UILobby uiLobby; // 로비 화면 뷰

    [Header("Forbidden Word Filter")]
    [SerializeField] private TextAsset forbiddenWordsCsv; // 금칙어 목록 CSV (콤마 또는 줄바꿈 구분)

    [Header("Settings")]
    [SerializeField] private int maxPlayersPerLobby = 4;
    [SerializeField] private float lobbyRefreshInterval = 10f;
    [SerializeField] private float refreshButtonCooldown = 5f; // Refresh 버튼 재사용 대기시간(초)

    [Header("Room Manager")]
    [SerializeField] private RoomManager roomManager;

    private string playerName = "Player";
    private Lobby currentLobby;
    private List<Lobby> availableLobbies = new List<Lobby>();
    private float nextRefreshTime;
    private bool isRefreshing = false;
    private string selectedLobbyIdForPopup; // 비밀번호 팝업이 겨냥 중인 로비 ID

    private HashSet<string> forbiddenWords = new HashSet<string>(); // 로드된 금칙어 집합
    private bool isRefreshButtonOnCooldown = false;                 // Refresh 버튼 쿨다운 상태

    /// <summary>뷰의 의도를 구독한다.</summary>
    private void OnEnable()
    {
        uiLobby.CreateRequested += HandleCreateRequested;
        uiLobby.RefreshRequested += HandleRefreshRequested;
        uiLobby.LobbyJoinRequested += HandleLobbyJoinRequested;
        uiLobby.PasswordJoinConfirmed += HandlePasswordJoinConfirmed;
        uiLobby.PasswordJoinCancelled += HandlePasswordJoinCancelled;
        roomManager.RoomExited += HandleRoomExited;
    }

    /// <summary>구독을 해제한다.</summary>
    private void OnDisable()
    {
        roomManager.RoomExited -= HandleRoomExited;
        uiLobby.CreateRequested -= HandleCreateRequested;
        uiLobby.RefreshRequested -= HandleRefreshRequested;
        uiLobby.LobbyJoinRequested -= HandleLobbyJoinRequested;
        uiLobby.PasswordJoinConfirmed -= HandlePasswordJoinConfirmed;
        uiLobby.PasswordJoinCancelled -= HandlePasswordJoinCancelled;
    }

    /// <summary>금칙어를 읽고 UGS 를 초기화한다.</summary>
    private async void Start()
    {
        LoadForbiddenWords();
        await InitializeUnityServices();
    }

    /// <summary>방에 들어가 있지 않을 때 주기적으로 목록을 갱신한다.</summary>
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

    #region View Intents

    /// <summary>방 생성 의도를 받는다.</summary>
    /// <param name="lobbyName">방 이름</param>
    /// <param name="password">비밀번호. 없으면 빈 문자열</param>
    private void HandleCreateRequested(string lobbyName, string password)
    {
        _ = CreateLobby(lobbyName, password);
    }

    /// <summary>새로고침 의도를 받는다.</summary>
    private void HandleRefreshRequested()
    {
        _ = OnRefreshButtonClicked();
    }

    /// <summary>
    /// 리스트 셀의 Join 의도를 받는다. 비밀번호 방이면 팝업을 열고, 공개 방이면 곧바로 입장한다.
    /// </summary>
    /// <param name="lobby">대상 로비</param>
    private void HandleLobbyJoinRequested(Lobby lobby)
    {
        if (LobbyDataKeys.HasPassword(lobby))
        {
            Log.D($"[JOIN] '{lobby.Name}' 방은 비밀번호 방이다. 입력 팝업을 연다.");
            selectedLobbyIdForPopup = lobby.Id;
            uiLobby.ShowPasswordPopup();
            return;
        }

        Log.D($"[JOIN] '{lobby.Name}' 방은 공개 방이다. 즉시 입장을 시도한다.");
        _ = JoinLobbyById(lobby.Id, "");
    }

    /// <summary>팝업의 Join 확정 의도를 받는다.</summary>
    /// <param name="password">입력된 비밀번호</param>
    private void HandlePasswordJoinConfirmed(string password)
    {
        if (string.IsNullOrEmpty(selectedLobbyIdForPopup)) return;

        _ = JoinLobbyById(selectedLobbyIdForPopup, password);
    }

    /// <summary>팝업 취소 의도를 받아 겨냥 중이던 로비를 놓는다.</summary>
    private void HandlePasswordJoinCancelled()
    {
        selectedLobbyIdForPopup = "";
    }

    /// <summary>방에서 나왔다는 통지를 받아 로비 화면을 되살리고 목록을 새로 받는다.</summary>
    private void HandleRoomExited()
    {
        uiLobby.Show();
        _ = RefreshLobbyList();
    }

    #endregion

    #region Unity Services Initialization

    /// <summary>UGS 초기화·익명 로그인·스팀 닉네임 적용 후 첫 목록을 불러온다.</summary>
    /// <returns>초기화 완료를 기다리는 Task</returns>
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

    #region Forbidden Word Filter

    /// <summary>CSV(TextAsset)에서 금칙어를 읽어 소문자로 정규화해 담는다.</summary>
    private void LoadForbiddenWords()
    {
        forbiddenWords.Clear();

        if (forbiddenWordsCsv == null)
        {
            Log.W("[FILTER] 금칙어 CSV 가 할당되지 않았다. 필터링이 동작하지 않는다.", this);
            return;
        }

        // 콤마와 줄바꿈 둘 다 구분자로 처리 -> "욕설1,욕설2\n욕설3" 형태 모두 지원
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

    /// <summary>텍스트에 금칙어가 포함됐는지 검사한다(부분 일치, 대소문자 무시).</summary>
    /// <param name="text">검사 대상</param>
    /// <param name="matchedWord">걸린 금칙어. 없으면 null</param>
    /// <returns>금칙어 포함 여부</returns>
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

    #region Lobby List Management

    /// <summary>UGS 에 로비 목록을 조회해 뷰에 내려준다.</summary>
    /// <returns>조회 완료를 기다리는 Task</returns>
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

            uiLobby.ShowLobbyList(availableLobbies);
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

    /// <summary>새로고침 버튼 처리 — 즉시 재조회하고 쿨다운 동안 버튼을 잠근다.</summary>
    /// <returns>재조회 완료를 기다리는 Task</returns>
    private async Task OnRefreshButtonClicked()
    {
        if (isRefreshButtonOnCooldown)
        {
            return;
        }

        isRefreshButtonOnCooldown = true;
        uiLobby.SetRefreshInteractable(false);

        await RefreshLobbyList();

        // 자동 갱신 타이머와 겹쳐 곧바로 또 호출되지 않도록 타이밍 재조정
        nextRefreshTime = Time.time + lobbyRefreshInterval;

        _ = RefreshButtonCooldownRoutine();
    }

    /// <summary>쿨다운을 기다린 뒤 새로고침 버튼을 되살린다.</summary>
    /// <returns>쿨다운 완료를 기다리는 Task</returns>
    private async Task RefreshButtonCooldownRoutine()
    {
        await Task.Delay(TimeSpan.FromSeconds(Mathf.Max(0f, refreshButtonCooldown)));

        if (this == null) return; // 오브젝트가 파괴된 경우 방어

        isRefreshButtonOnCooldown = false;
        uiLobby.SetRefreshInteractable(true);
    }

    #endregion

    #region Create Lobby

    /// <summary>방을 만든다. 이름이 비었거나 금칙어가 걸리면 생성하지 않는다.</summary>
    /// <param name="lobbyName">방 이름</param>
    /// <param name="password">비밀번호. 없으면 빈 문자열</param>
    /// <returns>생성 완료를 기다리는 Task</returns>
    private async Task CreateLobby(string lobbyName, string password)
    {
        if (string.IsNullOrEmpty(lobbyName))
        {
            Log.W("Lobby name cannot be empty!", this);
            return;
        }

        if (ContainsForbiddenWord(lobbyName, out string matchedWord))
        {
            Log.W($"[CREATE] 방 제목에 금칙어가 포함돼 생성이 차단됐다: '{matchedWord}'", this);
            uiLobby.SetForbiddenWordWarning(true);
            return;
        }

        uiLobby.SetForbiddenWordWarning(false);

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
                options.Data.Add(LobbyDataKeys.Password, new DataObject(
                    DataObject.VisibilityOptions.Public,
                    password,
                    DataObject.IndexOptions.S1));
            }

            currentLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayersPerLobby, options);

            Log.D($"[CREATE] Created lobby: {currentLobby.Name} (ID: {currentLobby.Id})");

            uiLobby.ResetUI();
            uiLobby.Hide();

            StartHeartbeatInstance();

            roomManager.EnterRoom(currentLobby);
        }
        catch (Exception e)
        {
            Log.E($"Failed to create lobby: {e.Message}", this);
        }
    }

    /// <summary>방장 자격으로 하트비트를 보낸다. 자격을 잃으면 타이머를 멈춘다.</summary>
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
            if (IsHeartbeatOwnershipFailure(e))
            {
                CancelInvoke(nameof(SendLobbyHeartbeat));
                return;
            }
        }
        catch (Exception e)
        {
            if (IsHeartbeatOwnershipFailure(e))
            {
                CancelInvoke(nameof(SendLobbyHeartbeat));
                return;
            }
        }
    }

    #endregion

    #region Join Lobby

    /// <summary>ID 로 방에 입장한다. 비밀번호 방이면 문자열 일치를 먼저 검증한다.</summary>
    /// <param name="lobbyId">대상 로비 ID</param>
    /// <param name="password">입력된 비밀번호</param>
    /// <returns>입장 완료를 기다리는 Task</returns>
    private async Task JoinLobbyById(string lobbyId, string password = "")
    {
        try
        {
            Lobby targetLobby = await LobbyService.Instance.GetLobbyAsync(lobbyId);

            if (targetLobby.Data != null && targetLobby.Data.TryGetValue(LobbyDataKeys.Password, out DataObject passwordData))
            {
                string lobbyPassword = passwordData.Value;
                if (string.IsNullOrEmpty(password) || lobbyPassword != password)
                {
                    Log.W("[JOIN] 비밀번호가 일치하지 않는다!", this);
                    uiLobby.SetPasswordWarning(true);
                    return;
                }
            }

            JoinLobbyByIdOptions options = new JoinLobbyByIdOptions
            {
                Player = BuildLocalPlayer()
            };

            currentLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId, options);

            uiLobby.HidePasswordPopup();
            selectedLobbyIdForPopup = "";
            uiLobby.Hide();

            roomManager.EnterRoom(currentLobby);
        }
        catch (Exception e)
        {
            Log.E($"Failed to join lobby by ID: {e.Message}", this);
            uiLobby.SetPasswordWarning(true);
        }
    }

    #endregion

    #region Leave Lobby

    /// <summary>방에서 나가고 화면을 초기 상태로 되돌린다.</summary>
    /// <returns>퇴장 완료를 기다리는 Task</returns>
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
            Log.D($"Left lobby: {currentLobby.Name}");
        }
        catch (Exception e)
        {
            if (this == null) return;
            Log.E($"Failed to leave lobby: {e.Message}", this);
        }

        if (this != null)
        {
            currentLobby = null;
            uiLobby.ResetUI();
        }
    }

    #endregion

    /// <summary>게임 시작 중이 아니라면 파괴 시 방에서 빠져나온다.</summary>
    private void OnDestroy()
    {
        bool isStarting = roomManager != null && roomManager.IsStartingGame;

        if (currentLobby != null && !isStarting)
        {
            _ = LeaveLobby();
        }
    }

    /// <summary>하트비트 타이머를 (재)가동한다. 방장 권한을 넘겨받았을 때도 쓴다.</summary>
    public void StartHeartbeatInstance()
    {
        CancelInvoke(nameof(SendLobbyHeartbeat));
        InvokeRepeating(nameof(SendLobbyHeartbeat), HeartbeatIntervalSeconds, HeartbeatIntervalSeconds);
        Log.D("[HEARTBEAT] 새 방장 권한을 위임받아 하트비트 타이머를 가동한다.");
    }

    /// <summary>UGS 에 실을 로컬 플레이어 정보를 만든다.</summary>
    /// <returns>닉네임·스팀 ID 를 담은 Player</returns>
    private Player BuildLocalPlayer()
    {
        string steamId = SteamManager.Initialized ? SteamUser.GetSteamID().ToString() : string.Empty;
        return new Player
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                { LobbyDataKeys.PlayerName, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, playerName) },
                { LobbyDataKeys.SteamId, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, steamId) }
            }
        };
    }

    /// <summary>하트비트 실패가 방장 자격 상실 때문인지 판정한다.</summary>
    /// <param name="exception">발생한 예외</param>
    /// <returns>자격 상실로 볼 수 있으면 true</returns>
    private static bool IsHeartbeatOwnershipFailure(Exception exception)
    {
        if (exception is LobbyServiceException lobbyException
            && (lobbyException.ErrorCode == 400 || lobbyException.ErrorCode == 403 || lobbyException.ErrorCode == 404))
        {
            return true;
        }

        string message = exception.Message ?? string.Empty;
        return message.IndexOf("host", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
