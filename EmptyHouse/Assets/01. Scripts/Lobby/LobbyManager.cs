using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Border.Core;
using UnityEngine;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

/// <summary>
/// 로비 목록 화면 프레젠터. 목록 조회·새로고침 쿨다운·금칙어 필터와 생성/입장 요청 처리를 담당한다.
/// 버튼 의도 배선과 화면 전환은 UIMenuManager 가 하고, 여기서는 공개 요청 API 로 받아 뷰에 데이터만 내려 그린다.
/// 세션 상태(현재 로비·하트비트·인증)는 LobbySession(→SessionCoordinator) 이 소유하며, 여기서는 읽기와 요청만 한다.
/// </summary>
public class LobbyManager : MonoBehaviour
{
    [Header("Session")]
    [SerializeField] private LobbySession session; // 메뉴 씬 세션 파사드

    [Header("View")]
    [SerializeField] private UIRoomList roomListPanel;      // 목록 화면 뷰. 셀을 직접 그린다
    [SerializeField] private UICreateContent createContent; // 방 만들기 팝업 뷰. 금칙어 경고만 건드린다

    [Header("Forbidden Word Filter")]
    [SerializeField] private TextAsset forbiddenWordsCsv; // 금칙어 목록 CSV (콤마 또는 줄바꿈 구분)

    [Header("Settings")]
    [SerializeField] private int maxPlayersPerLobby = 4;
    [SerializeField] private float lobbyRefreshInterval = 10f;
    [SerializeField] private float refreshButtonCooldown = 5f; // Refresh 버튼 재사용 대기시간(초)

    private List<Lobby> availableLobbies = new List<Lobby>();
    private float nextRefreshTime;
    private bool isRefreshing = false;

    private HashSet<string> forbiddenWords = new HashSet<string>(); // 로드된 금칙어 집합
    private bool isRefreshButtonOnCooldown = false;                 // Refresh 버튼 쿨다운 상태

    /// <summary>세션 종료를 구독한다. 뷰 의도 배선은 UIMenuManager 몫이다.</summary>
    private void OnEnable()
    {
        session.SessionEnded += HandleSessionEnded;
    }

    /// <summary>구독을 해제한다.</summary>
    private void OnDisable()
    {
        session.SessionEnded -= HandleSessionEnded;
    }

    /// <summary>금칙어를 읽고 세션을 초기화한 뒤, 남아 있는 방 세션에 복귀하거나 첫 목록을 불러온다.</summary>
    private async void Start()
    {
        LoadForbiddenWords();

        try
        {
            await session.InitializeAsync();

            // Game -> Lobby 복귀: 씬 UI는 새로 만들어졌지만 방 세션은 SessionCoordinator 에 남아 있다.
            // 복귀에 성공하면 SessionStarted 가 발행돼 UIMenuManager 가 방 화면을 연다.
            bool resumed = await session.TryResumeSessionAsync();

            if (!resumed)
            {
                Log.D("[INIT] Loading initial lobby list...");
                await RefreshLobbyList();
            }

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

    #region Public Request API

    /// <summary>방 생성 요청을 받는다.</summary>
    /// <param name="lobbyName">방 이름</param>
    /// <param name="password">비밀번호. 없으면 빈 문자열</param>
    public void RequestCreate(string lobbyName, string password)
    {
        _ = CreateLobby(lobbyName, password);
    }

    /// <summary>새로고침 요청을 받는다.</summary>
    public void RequestRefresh()
    {
        _ = OnRefreshButtonClicked();
    }

    /// <summary>
    /// 리스트 셀의 입장 확정을 받는다. 비밀번호는 셀이 이미 받아 실어 보내므로 여기서는 그대로 넘긴다.
    /// </summary>
    /// <param name="lobby">대상 로비</param>
    /// <param name="password">입력된 비밀번호. 공개 방이면 빈 문자열</param>
    public void RequestJoin(Lobby lobby, string password)
    {
        Log.D($"[JOIN] '{lobby.Name}' 방 입장을 시도한다.");
        _ = JoinLobbyById(lobby.Id, password);
    }

    /// <summary>세션 종료 통지를 받아 목록을 새로 받는다. 화면 복귀는 UIMenuManager 몫이다.</summary>
    private void HandleSessionEnded()
    {
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

            roomListPanel.ShowLobbyList(availableLobbies);
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
        roomListPanel.SetRefreshInteractable(false);

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
        roomListPanel.SetRefreshInteractable(true);
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
            createContent.SetForbiddenWordWarning(true);
            return;
        }

        createContent.SetForbiddenWordWarning(false);

        try
        {
            // 성공 시 화면 전환(SessionStarted)은 UIMenuManager 가 처리한다
            await session.CreateAsync(lobbyName, maxPlayersPerLobby, password);
        }
        catch (Exception e)
        {
            // SessionStarted 구독자에서 터진 예외도 여기로 올라온다 — 원인 줄을 잃지 않게 스택째 남긴다
            Log.E($"Failed to create lobby: {e}", this);
        }
    }

    /// <summary>
    /// ID 로 방 입장을 요청한다. 비밀번호 오류면 열려 있는 셀의 입력창을 흔들고,
    /// 그 외 실패는 낡은 목록(그 사이 출발·만석·소멸) 탓일 수 있으므로 목록을 즉시 새로 받는다.
    /// </summary>
    /// <param name="lobbyId">대상 로비 ID</param>
    /// <param name="password">입력된 비밀번호</param>
    /// <returns>입장 완료를 기다리는 Task</returns>
    private async Task JoinLobbyById(string lobbyId, string password = "")
    {
        LobbySession.JoinResult result = await session.JoinByIdAsync(lobbyId, password);

        // 성공 시 UI 정리·화면 전환(SessionStarted)은 UIMenuManager 가 처리한다
        if (result == LobbySession.JoinResult.WrongPassword) roomListPanel.RejectPassword();
        else if (result == LobbySession.JoinResult.Error) await RefreshLobbyList();
    }

    #endregion
}
