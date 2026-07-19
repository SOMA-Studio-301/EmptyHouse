using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Border.Core;
using UnityEngine;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

/// <summary>
/// 로비 목록 화면 프레젠터. 목록 조회·새로고침 쿨다운·금칙어 필터와 생성/입장 요청 라우팅을 담당한다.
/// 세션 상태(현재 로비·하트비트·인증)는 LobbySession 이 단독 소유하며, 여기서는 읽기와 요청만 한다.
/// </summary>
public class LobbyManager : MonoBehaviour
{
    [Header("Session")]
    [SerializeField] private LobbySession session; // 세션 단일 소유자

    [Header("View")]
    [SerializeField] private UILobby uiLobby; // 로비 화면 뷰

    [Header("Forbidden Word Filter")]
    [SerializeField] private TextAsset forbiddenWordsCsv; // 금칙어 목록 CSV (콤마 또는 줄바꿈 구분)

    [Header("Settings")]
    [SerializeField] private int maxPlayersPerLobby = 4;
    [SerializeField] private float lobbyRefreshInterval = 10f;
    [SerializeField] private float refreshButtonCooldown = 5f; // Refresh 버튼 재사용 대기시간(초)

    private List<Lobby> availableLobbies = new List<Lobby>();
    private float nextRefreshTime;
    private bool isRefreshing = false;
    private string selectedLobbyIdForPopup; // 비밀번호 팝업이 겨냥 중인 로비 ID

    private HashSet<string> forbiddenWords = new HashSet<string>(); // 로드된 금칙어 집합
    private bool isRefreshButtonOnCooldown = false;                 // Refresh 버튼 쿨다운 상태

    /// <summary>뷰의 의도와 세션 종료를 구독한다.</summary>
    private void OnEnable()
    {
        uiLobby.CreateRequested += HandleCreateRequested;
        uiLobby.RefreshRequested += HandleRefreshRequested;
        uiLobby.LobbyJoinRequested += HandleLobbyJoinRequested;
        uiLobby.PasswordJoinConfirmed += HandlePasswordJoinConfirmed;
        uiLobby.PasswordJoinCancelled += HandlePasswordJoinCancelled;
        session.SessionEnded += HandleSessionEnded;
    }

    /// <summary>구독을 해제한다.</summary>
    private void OnDisable()
    {
        session.SessionEnded -= HandleSessionEnded;
        uiLobby.CreateRequested -= HandleCreateRequested;
        uiLobby.RefreshRequested -= HandleRefreshRequested;
        uiLobby.LobbyJoinRequested -= HandleLobbyJoinRequested;
        uiLobby.PasswordJoinConfirmed -= HandlePasswordJoinConfirmed;
        uiLobby.PasswordJoinCancelled -= HandlePasswordJoinCancelled;
    }

    /// <summary>금칙어를 읽고 세션을 초기화한 뒤 첫 목록을 불러온다.</summary>
    private async void Start()
    {
        LoadForbiddenWords();

        try
        {
            await session.InitializeAsync();

            Log.D("[INIT] Loading initial lobby list...");
            await RefreshLobbyList();

            nextRefreshTime = Time.time + lobbyRefreshInterval;
        }
        catch (Exception e)
        {
            Log.E($"[ERROR] Failed to initialize Unity Services: {e.Message}", this);
        }
    }

    /// <summary>세션에 참여 중이 아닐 때 주기적으로 목록을 갱신한다.</summary>
    private void Update()
    {
        if (!session.IsSignedIn) return;

        if (Time.time >= nextRefreshTime && !session.IsInSession && !isRefreshing)
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

    /// <summary>세션 종료 통지를 받아 로비 화면을 되살리고 목록을 새로 받는다.</summary>
    private void HandleSessionEnded()
    {
        uiLobby.ResetUI();
        uiLobby.Show();
        _ = RefreshLobbyList();
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
        if (!session.IsSignedIn || isRefreshing)
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

    #region Create / Join

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
            await session.CreateAsync(lobbyName, maxPlayersPerLobby, password);

            uiLobby.ResetUI();
            uiLobby.Hide();
        }
        catch (Exception e)
        {
            Log.E($"Failed to create lobby: {e.Message}", this);
        }
    }

    /// <summary>ID 로 방 입장을 요청한다. 실패하면 비밀번호 경고를 띄운다.</summary>
    /// <param name="lobbyId">대상 로비 ID</param>
    /// <param name="password">입력된 비밀번호</param>
    /// <returns>입장 완료를 기다리는 Task</returns>
    private async Task JoinLobbyById(string lobbyId, string password = "")
    {
        LobbySession.JoinResult result = await session.JoinByIdAsync(lobbyId, password);

        if (result != LobbySession.JoinResult.Success)
        {
            uiLobby.SetPasswordWarning(true);
            return;
        }

        uiLobby.HidePasswordPopup();
        selectedLobbyIdForPopup = "";
        uiLobby.Hide();
    }

    #endregion
}
