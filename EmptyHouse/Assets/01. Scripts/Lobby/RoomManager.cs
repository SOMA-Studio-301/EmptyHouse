using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Border.Core;
using UnityEngine;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Steamworks;

/// <summary>
/// 방(Room) 화면 프레젠터. 로비 폴링과 게임 시작/퇴장 요청 라우팅을 담당한다.
/// 버튼 의도 배선과 화면 전환은 UIMenuManager 가 하고, 여기서는 공개 요청 API 로 받아 뷰에 데이터만 내려 그린다.
/// Relay/NGO 수명·게임 시작 절차·방 정리는 영속 SessionCoordinator(LobbySession 경유) 몫이다.
/// </summary>
public class RoomManager : MonoBehaviour
{
    [Header("Session")]
    [SerializeField] private LobbySession session; // 메뉴 씬 세션 파사드

    [Header("View")]
    [SerializeField] private UIRoom uiRoom; // 방 화면 뷰

    [Header("Audio")]
    [SerializeField] private SFXEventChannelSO sfxEventChannel;
    [SerializeField] private AudioId gameStartAudioId = AudioId.Sfx_Ui_GameStart; // 결과 피드백 — 클릭음(버튼 컴포넌트 담당)이 아니라 시작이 확정된 뒤 울린다

    [Header("Settings")]
    [SerializeField] private float lobbyPollInterval = 1.5f;

    [Header("Scene Settings")]
    [SerializeField] private string gameSceneName = "Game";

    private bool isReady;
    private bool isInRoom;
    private bool isStartingGame; // 게임 시작 절차 진행 중 UI 잠금 플래그
    private CancellationTokenSource lobbyPollCts;

    private Callback<AvatarImageLoaded_t> avatarLoadedCallback;
    private Callback<PersonaStateChange_t> personaStateCallback;

    /// <summary>세션 이벤트를 구독한다. 뷰 의도 배선은 UIMenuManager 몫이다.</summary>
    private void OnEnable()
    {
        session.SessionStarted += HandleSessionStarted;
        session.LobbyUpdated += HandleLobbyUpdated;
        session.SessionEnded += HandleSessionEnded;
    }

    /// <summary>구독을 해제한다.</summary>
    private void OnDisable()
    {
        session.SessionStarted -= HandleSessionStarted;
        session.LobbyUpdated -= HandleLobbyUpdated;
        session.SessionEnded -= HandleSessionEnded;
    }

    /// <summary>스팀 콜백을 등록한다. 아바타·닉네임이 늦게 도착하면 슬롯을 다시 그려야 한다.</summary>
    private void Start()
    {
        if (SteamManager.Initialized)
        {
            avatarLoadedCallback = Callback<AvatarImageLoaded_t>.Create(OnAvatarImageLoaded);
            personaStateCallback = Callback<PersonaStateChange_t>.Create(OnPersonaStateChange);
            Log.D("[STEAM] RoomManager 스팀 콜백 등록 완료");
        }
    }

    /// <summary>씬 전환·파괴 시 폴링을 정리한다. 퇴장/방 정리는 SessionCoordinator 가 담당한다.</summary>
    private void OnDestroy()
    {
        CancelLobbyPolling();
    }

    #region Session Events

    /// <summary>세션 시작(방 생성/입장/복귀)을 받아 슬롯을 그리고 폴링·방 네트워크 준비를 시작한다.</summary>
    /// <param name="lobby">입장한 로비</param>
    private void HandleSessionStarted(Lobby lobby)
    {
        isReady = false;
        isInRoom = true;
        isStartingGame = false;

        UpdateRoomUI();
        StartLobbyPolling();
        _ = PrepareRoomEntryAsync();

        Log.D($"[ROOM] Entered room: {lobby.Name}");

        if (SteamManager.Initialized)
        {
            SteamFriends.SetRichPresence("status", "버스 대기 중");
            SteamFriends.SetRichPresence("connect", lobby.Id);
            Log.D($"[STEAM] 리치 프레즌스 등록 완료 (LobbyID: {lobby.Id})");
        }
    }

    /// <summary>세션 로비 갱신을 받아 화면을 다시 그린다.</summary>
    /// <param name="lobby">최신 로비</param>
    private void HandleLobbyUpdated(Lobby lobby)
    {
        UpdateRoomUI();
    }

    /// <summary>세션 종료(퇴장·로비 소멸)를 받아 방 상태를 초기화한다. 방 화면 닫기는 UIMenuManager 몫이다.</summary>
    private void HandleSessionEnded()
    {
        isInRoom = false;
        isReady = false;
        isStartingGame = false;
        CancelLobbyPolling();
    }

    /// <summary>한 판 상태를 초기화하고, 진행 중인 방이면 기존 Relay 에 미리 연결한다.</summary>
    /// <returns>준비 완료를 기다리는 Task</returns>
    private async Task PrepareRoomEntryAsync()
    {
        await session.NotifyReturnedToRoomAsync();
        if (this == null || !isInRoom) return;

        try
        {
            // 한 판 이상 진행된 방에 새로 들어온 경우 Room 에서 기존 Relay 에 미리 연결한다.
            // 방장을 이관받은 상태로 들어왔다면 여기서 Relay 를 다시 연다
            await session.EnsureRoomNetworkAsync();
        }
        catch (Exception e)
        {
            Log.E($"[ROOM] Room 네트워크 정합 실패: {e.Message}", this);
        }
    }

    #endregion

    #region Public Request API

    /// <summary>Ready 토글 요청을 받는다.</summary>
    public void RequestToggleReady()
    {
        _ = ToggleReady();
    }

    /// <summary>게임 시작 요청을 받는다.</summary>
    public void RequestStartGame()
    {
        _ = StartGame();
    }

    /// <summary>방 나가기 요청을 받는다. 게임 시작 중이거나 정리 중이면 무시한다(ESC 연타 방어).</summary>
    public void RequestExitRoom()
    {
        if (isStartingGame) return;
        if (session.IsCleaningUp) return;

        isInRoom = false;
        CancelLobbyPolling();

        _ = session.LeaveAsync();
    }

    /// <summary>빈 슬롯 클릭 요청을 받아 스팀 친구 초대 오버레이를 연다.</summary>
    public void OpenInviteOverlay()
    {
        if (!SteamManager.Initialized) return;

        SteamFriends.ActivateGameOverlay("Invite");
        Log.D("[STEAM] 친구 초대 오버레이 창 활성화");
    }

    #endregion

    #region Lobby Polling

    /// <summary>현재 세션 기준으로 폴링을 (재)시작한다.</summary>
    private void StartLobbyPolling()
    {
        CancelLobbyPolling();
        lobbyPollCts = new CancellationTokenSource();
        _ = PollLobbyData(lobbyPollCts, session.CurrentLobby.Id, session.Generation);
    }

    /// <summary>진행 중인 폴링을 취소한다.</summary>
    private void CancelLobbyPolling()
    {
        CancellationTokenSource cts = lobbyPollCts;
        lobbyPollCts = null;
        if (cts == null) return;

        cts.Cancel();
        cts.Dispose();
    }

    /// <summary>
    /// 방에 있는 동안 로비를 주기적으로 조회해 세션에 반영한다. 게스트는 Relay 코드가 뜨면 미리 연결해 둔다.
    /// 세대 가드로 스테일 응답을 버리고, 404 는 방 소멸 정리 경로로 보낸다.
    /// </summary>
    /// <param name="pollingSource">이 폴링 루프의 취소 소스</param>
    /// <param name="expectedLobbyId">폴링 시작 시점의 로비 ID</param>
    /// <param name="expectedGeneration">폴링 시작 시점의 세션 세대</param>
    /// <returns>폴링 종료를 기다리는 Task</returns>
    private async Task PollLobbyData(
        CancellationTokenSource pollingSource,
        string expectedLobbyId,
        int expectedGeneration)
    {
        CancellationToken cancellationToken = pollingSource.Token;
        int rateLimitAttempt = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested && isInRoom && session.IsInSession)
            {
                try
                {
                    Lobby polledLobby = await LobbyService.Instance.GetLobbyAsync(expectedLobbyId);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (this == null || !isInRoom || session.IsCleaningUp) break;

                    // 세대가 어긋나면(그 사이 방이 바뀜) 이 루프는 수명을 다했다
                    if (!session.TryApplyLobbySnapshot(polledLobby, expectedLobbyId, expectedGeneration)) break;

                    rateLimitAttempt = 0;

                    // 게스트는 새 Relay 코드에 붙고, 이관받은 방장은 끊긴 Relay 를 다시 연다
                    try
                    {
                        await session.EnsureRoomNetworkAsync();
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        Log.E($"[ROOM] Relay 정합 실패: {e.Message}", this);
                    }
                }
                catch (LobbyServiceException e)
                {
                    if (e.Reason == LobbyExceptionReason.LobbyNotFound
                        || e.Reason == LobbyExceptionReason.EntityNotFound
                        || (!string.IsNullOrEmpty(e.Message)
                            && e.Message.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        // 호스트의 방 파괴와 비정상 종료 후 로비 소멸을 같은 정리 경로로 처리한다
                        if (!isStartingGame)
                        {
                            Log.W("[ROOM] 로비가 이미 서버에서 삭제됐다.", this);
                        }
                        isInRoom = false;
                        await session.HandleRoomDestroyedAsync();
                        break;
                    }

                    if (e.Reason == LobbyExceptionReason.RateLimited)
                    {
                        rateLimitAttempt++;
                        float retryDelay = Mathf.Min(30f, 3f * Mathf.Pow(2f, rateLimitAttempt - 1));
                        await Task.Delay(TimeSpan.FromSeconds(retryDelay), cancellationToken);
                        continue;
                    }

                    Log.E($"[ROOM] 로비 데이터 갱신 실패: {e.Message}", this);
                }

                await Task.Delay(
                    TimeSpan.FromSeconds(Mathf.Max(0.25f, lobbyPollInterval)),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // 방 퇴장·씬 전환에 따른 정상 종료
        }
        catch (Exception e)
        {
            Log.E($"[ROOM] 일반 에러: {e}", this); // 폴링 응답이 UI 를 건드리다 터지는 경우가 있어 원인 줄을 남긴다
        }
        finally
        {
            if (ReferenceEquals(lobbyPollCts, pollingSource))
                lobbyPollCts = null;

            pollingSource.Dispose();
        }
    }

    #endregion

    #region View Update

    /// <summary>현재 세션 로비 상태를 뷰에 내려준다.</summary>
    private void UpdateRoomUI()
    {
        if (!session.IsInSession) return;

        // 게스트는 StartGame 을 타지 않아 씬 언로드와 폴링 응답이 겹칠 수 있다.
        // 선택적 의존이라서가 아니라 수명이 끝났는지 보는 것 — PollLobbyData 의 `this == null` 가드와 같은 계열이다
        if (uiRoom == null) return;

        uiRoom.ShowPlayers(BuildSlots());
        UpdateInteractionButtons();
    }

    /// <summary>UGS Player 목록을 뷰가 아는 슬롯 데이터로 변환한다.</summary>
    /// <returns>표시용 슬롯 목록</returns>
    private List<RoomSlotInfo> BuildSlots()
    {
        Lobby lobby = session.CurrentLobby;
        var slots = new List<RoomSlotInfo>(lobby.Players.Count);

        foreach (Player player in lobby.Players)
        {
            string steamId = TryGetPlayerData(player, LobbyDataKeys.SteamId, out string value) ? value : string.Empty;
            slots.Add(new RoomSlotInfo(
                GetPlayerName(player),
                player.Id == lobby.HostId,
                GetPlayerReadyStatus(player),
                steamId));
        }

        return slots;
    }

    /// <summary>방장/게스트에 따라 버튼 표시와 잠금을 정한다.</summary>
    private void UpdateInteractionButtons()
    {
        bool isHost = session.IsLocalPlayerHost;
        uiRoom.SetHostMode(isHost);

        if (!isHost)
        {
            uiRoom.SetReadyInteractable(!isStartingGame); // 게임 시작 중이면 Ready 도 잠금
            return;
        }

        // 게임 시작 중일 때는 allReady 여부와 상관없이 버튼 비활성 유지
        if (isStartingGame) return;

        uiRoom.SetStartInteractable(IsEveryGuestReady()); // 게스트가 없으면(혼자) 참 — 솔로 시작 허용
    }

    /// <summary>방장을 뺀 전원이 준비됐는지 판정한다.</summary>
    /// <returns>전원 준비 완료면 true</returns>
    private bool IsEveryGuestReady()
    {
        Lobby lobby = session.CurrentLobby;

        foreach (Player player in lobby.Players)
        {
            if (player.Id == lobby.HostId) continue;

            if (!GetPlayerReadyStatus(player)) return false;
        }

        return true;
    }

    #endregion

    #region Ready / Start

    /// <summary>준비 상태를 뒤집어 UGS 에 반영한다. 실패하면 되돌린다.</summary>
    /// <returns>반영 완료를 기다리는 Task</returns>
    private async Task ToggleReady()
    {
        isReady = !isReady;

        string lobbyId = session.CurrentLobby.Id;
        int generation = session.Generation;

        try
        {
            string playerId = AuthenticationService.Instance.PlayerId;
            UpdatePlayerOptions options = new UpdatePlayerOptions
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    { LobbyDataKeys.IsReady, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, isReady.ToString()) }
                }
            };

            Lobby updated = await LobbyService.Instance.UpdatePlayerAsync(lobbyId, playerId, options);
            session.TryApplyLobbySnapshot(updated, lobbyId, generation); // 반영 성공 시 LobbyUpdated 로 화면 갱신
        }
        catch (Exception e)
        {
            Log.E($"[ROOM] Failed to update ready status: {e.Message}", this);
            isReady = !isReady;
        }
    }

    /// <summary>방장이 게임 세션 시작을 요청한다. Relay/NGO 절차는 SessionCoordinator 가 수행한다.</summary>
    /// <returns>시작 절차 완료를 기다리는 Task</returns>
    private async Task StartGame()
    {
        if (!session.IsLocalPlayerHost) return;
        if (isStartingGame) return;

        isStartingGame = true;
        uiRoom.SetStartInteractable(false); // 이탈은 RequestExitRoom 의 isStartingGame 가드가 막는다

        // 시작이 확정되면 방 UI 갱신은 의미가 없다. 씬이 언로드된 뒤 응답이 돌아와 파괴된 위젯을 건드리는 것을 막는다
        CancelLobbyPolling();

        sfxEventChannel.RaisePlayEvent(gameStartAudioId, transform.position);

        Log.D("[ROOM] Host가 게임 세션 시작을 요청한다...");

        try
        {
            await session.StartGameAsHostAsync(gameSceneName);
        }
        catch (Exception e)
        {
            Log.E($"[ROOM] 게임 시작 실패: {e.Message}", this);
            isStartingGame = false;
            uiRoom.SetStartInteractable(true);
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>플레이어 닉네임을 읽는다.</summary>
    /// <param name="player">대상 플레이어</param>
    /// <returns>닉네임. 없으면 "Unknown"</returns>
    private static string GetPlayerName(Player player)
    {
        return TryGetPlayerData(player, LobbyDataKeys.PlayerName, out string playerName) ? playerName : "Unknown";
    }

    /// <summary>플레이어의 준비 상태를 읽는다.</summary>
    /// <param name="player">대상 플레이어</param>
    /// <returns>준비 완료면 true</returns>
    private static bool GetPlayerReadyStatus(Player player)
    {
        return TryGetPlayerData(player, LobbyDataKeys.IsReady, out string value)
            && bool.TryParse(value, out bool isReadyStatus)
            && isReadyStatus;
    }

    /// <summary>플레이어 데이터에서 키 하나를 읽는다.</summary>
    /// <param name="player">대상 플레이어</param>
    /// <param name="key">데이터 키</param>
    /// <param name="value">읽은 값. 없으면 빈 문자열</param>
    /// <returns>읽기 성공 여부</returns>
    private static bool TryGetPlayerData(Player player, string key, out string value)
    {
        value = string.Empty;
        if (player?.Data == null || !player.Data.TryGetValue(key, out PlayerDataObject data)) return false;
        value = data.Value;
        return true;
    }

    #endregion

    #region 스팀 콜백 핸들러 리전

    /// <summary>아바타 이미지가 늦게 도착하면 슬롯을 다시 그린다.</summary>
    /// <param name="callback">스팀 콜백 데이터</param>
    private void OnAvatarImageLoaded(AvatarImageLoaded_t callback)
    {
        if (session.IsInSession)
        {
            UpdateRoomUI();
        }
    }

    /// <summary>닉네임 등 페르소나 정보가 갱신되면 슬롯을 다시 그린다.</summary>
    /// <param name="callback">스팀 콜백 데이터</param>
    private void OnPersonaStateChange(PersonaStateChange_t callback)
    {
        if (session.IsInSession)
        {
            UpdateRoomUI();
        }
    }

    #endregion
}
