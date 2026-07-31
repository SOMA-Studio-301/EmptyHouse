using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Border.Core;
using Steamworks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

/// <summary>
/// 메뉴 씬 세션 파사드. UGS 초기화·인증과 방 생성/입장(비밀번호 검증)·게임 복귀 판정을 담당하고,
/// 세션 상태(현재 로비)·하트비트·Relay/NGO 수명·퇴장 정리는 영속 SessionCoordinator 에 위임한다.
/// 매니저/미디에이터는 이 파사드의 이벤트(SessionStarted/LobbyUpdated/SessionEnded)만 본다.
/// </summary>
public class LobbySession : MonoBehaviour
{
    /// <summary>입장 시도 결과.</summary>
    public enum JoinResult
    {
        Success,       // 입장 성공
        WrongPassword, // 비밀번호 불일치(미입력 포함)
        Error,         // 그 외 실패
    }

    /// <summary>발행: 세션 시작(방 생성/입장/게임 복귀 성공). 입장한 로비를 싣는다.</summary>
    public event Action<Lobby> SessionStarted;

    /// <summary>발행: 세션 로비 갱신(폴링/쓰기 반영). 최신 로비를 싣는다.</summary>
    public event Action<Lobby> LobbyUpdated;

    /// <summary>발행: 세션 종료(퇴장·로비 소멸). SessionCoordinator.RoomCleared 를 중계한다.</summary>
    public event Action SessionEnded;

    /// <summary>발행: 게임 씬 로드 시작(전 클라이언트). SessionCoordinator.GameStarting 을 중계한다.</summary>
    public event Action GameStarting;

    public Lobby CurrentLobby => coordinator.CurrentLobby;           // 현재 세션 로비. 없으면 null
    public bool IsInSession => coordinator.HasRoom;                  // 세션 참여 여부
    public bool IsLocalPlayerHost => coordinator.IsCurrentLobbyHost; // 내가 현재 로비의 방장인지
    public int Generation => coordinator.SessionGeneration;          // 세션 세대. 폴링 반영 가드용
    public bool IsCleaningUp => coordinator.IsCleaningUp;            // 코디네이터가 정리 중인지

    public bool IsSignedIn =>                                        // UGS 초기화·로그인 완료 여부
        UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn;

    private SessionCoordinator coordinator; // 캐싱된 영속 코디네이터. 종료 중 Instance 재생성을 막으려 참조를 붙잡는다
    private string playerName = "Player";   // 로컬 닉네임. 스팀 초기화 시 페르소나 이름으로 대체

    /// <summary>코디네이터를 붙잡고 방 소멸 통지를 구독한다.</summary>
    private void Awake()
    {
        coordinator = SessionCoordinator.Instance;
        coordinator.RoomCleared += HandleRoomCleared;
        coordinator.GameStarting += HandleGameStarting;
    }

    /// <summary>구독을 해제한다.</summary>
    private void OnDestroy()
    {
        if (coordinator != null)
        {
            coordinator.RoomCleared -= HandleRoomCleared;
            coordinator.GameStarting -= HandleGameStarting;
        }
    }

    /// <summary>UGS 를 초기화하고 익명 로그인한 뒤 스팀 닉네임을 적용한다. 실패 시 예외를 그대로 던진다.</summary>
    /// <returns>초기화 완료를 기다리는 Task</returns>
    public async Task InitializeAsync()
    {
        Log.D("[SESSION] Initializing Unity Services...");
        await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
        Log.D($"[SESSION] Signed in as: {AuthenticationService.Instance.PlayerId}");

        if (SteamManager.Initialized)
        {
            playerName = SteamFriends.GetPersonaName();
            Log.D($"[SESSION] 스팀 닉네임 적용 완료: {playerName}");
        }
    }

    /// <summary>방을 만들어 세션을 시작한다. 실패 시 예외를 그대로 던진다.</summary>
    /// <param name="lobbyName">방 이름</param>
    /// <param name="maxPlayers">최대 인원</param>
    /// <param name="password">비밀번호. 없으면 빈 문자열</param>
    /// <returns>생성된 로비</returns>
    public async Task<Lobby> CreateAsync(string lobbyName, int maxPlayers, string password)
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

        Lobby created = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);
        coordinator.SetCurrentLobby(created);

        Log.D($"[SESSION] Created lobby: {created.Name} (ID: {created.Id})");

        SessionStarted?.Invoke(created);
        return created;
    }

    /// <summary>ID 로 방에 입장해 세션을 시작한다. 비밀번호 방이면 문자열 일치를 먼저 검증한다.</summary>
    /// <param name="lobbyId">대상 로비 ID</param>
    /// <param name="password">입력된 비밀번호</param>
    /// <returns>입장 결과</returns>
    public async Task<JoinResult> JoinByIdAsync(string lobbyId, string password)
    {
        try
        {
            Lobby targetLobby = await LobbyService.Instance.GetLobbyAsync(lobbyId);

            if (targetLobby.Data != null && targetLobby.Data.TryGetValue(LobbyDataKeys.Password, out DataObject passwordData))
            {
                if (string.IsNullOrEmpty(password) || passwordData.Value != password)
                {
                    Log.W("[SESSION] 비밀번호가 일치하지 않는다.", this);
                    return JoinResult.WrongPassword;
                }
            }

            JoinLobbyByIdOptions options = new JoinLobbyByIdOptions
            {
                Player = BuildLocalPlayer()
            };

            Lobby joined = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId, options);
            coordinator.SetCurrentLobby(joined);

            Log.D($"[SESSION] Joined lobby: {joined.Name}");

            SessionStarted?.Invoke(joined);
            return JoinResult.Success;
        }
        catch (Exception e)
        {
            Log.E($"[SESSION] 입장 실패: {e.Message}", this);
            return JoinResult.Error;
        }
    }

    /// <summary>
    /// Game 씬에서 돌아온 뒤 코디네이터에 남은 방 세션으로 복귀를 시도한다.
    /// 아직 방 멤버면 SessionStarted 를 발행하고, 아니면 잔류 세션을 폐기한다.
    /// </summary>
    /// <returns>복귀 성공 여부</returns>
    public async Task<bool> TryResumeSessionAsync()
    {
        if (!coordinator.HasRoom) return false;

        try
        {
            Lobby lobby = await coordinator.RefreshCurrentLobbyAsync();
            bool isStillMember = lobby?.Players != null
                && lobby.Players.Any(player => player.Id == AuthenticationService.Instance.PlayerId);

            if (lobby == null || !isStillMember)
            {
                await coordinator.DiscardStaleSessionAsync();
                return false;
            }

            Log.D($"[SESSION] 방 세션 복귀: {lobby.Name}");
            SessionStarted?.Invoke(lobby);
            return true;
        }
        catch (LobbyServiceException e) when (
            e.Reason == LobbyExceptionReason.LobbyNotFound
            || e.Reason == LobbyExceptionReason.EntityNotFound)
        {
            await coordinator.HandleRoomDestroyedAsync();
            return false;
        }
    }

    /// <summary>폴링/쓰기 결과를 세션에 반영한다. 세대가 어긋나면 버린다(스테일 응답 방어).</summary>
    /// <param name="lobby">서버에서 받은 최신 로비</param>
    /// <param name="expectedLobbyId">반영을 시작할 때의 로비 ID</param>
    /// <param name="expectedGeneration">반영을 시작할 때의 세션 세대</param>
    /// <returns>반영 성공 여부</returns>
    public bool TryApplyLobbySnapshot(Lobby lobby, string expectedLobbyId, int expectedGeneration)
    {
        if (!coordinator.TryUpdateLobby(lobby, expectedLobbyId, expectedGeneration)) return false;

        LobbyUpdated?.Invoke(lobby);
        return true;
    }

    /// <summary>방에서 나간다. 방장이면 방 전체가 파괴된다. 종료 통지는 RoomCleared→SessionEnded 로 이어진다.</summary>
    /// <returns>퇴장 완료를 기다리는 Task</returns>
    public async Task LeaveAsync()
    {
        if (!coordinator.HasRoom || coordinator.IsCleaningUp) return;

        await coordinator.ExitCurrentRoomAsync();
    }

    /// <summary>방 복귀 시 한 판 상태(Ready·SessionState)를 초기화한다.</summary>
    /// <returns>초기화 완료를 기다리는 Task</returns>
    public Task NotifyReturnedToRoomAsync()
    {
        return coordinator.NotifyReturnedToRoomAsync();
    }

    /// <summary>로비에 Relay 코드가 있고 아직 NGO 미연결이면 현재 방의 네트워크에 연결한다.</summary>
    /// <returns>연결 완료를 기다리는 Task</returns>
    public Task ConnectToRoomNetworkIfNeededAsync()
    {
        return coordinator.ConnectToRoomNetworkIfNeededAsync();
    }

    /// <summary>
    /// 방 네트워크를 현재 방장 기준으로 맞춘다. 방장이면 끊긴 Relay 를 다시 열고, 게스트면 새 코드에 붙는다.
    /// 방장 이관 직후 복구도 이 경로를 탄다.
    /// </summary>
    /// <returns>정합 완료를 기다리는 Task</returns>
    public Task EnsureRoomNetworkAsync()
    {
        return coordinator.EnsureRoomNetworkAsync();
    }

    /// <summary>호스트가 게임을 시작한다. 첫 판만 Relay/NGO 를 만들고 이후에는 연결을 재사용한다.</summary>
    /// <param name="gameSceneName">로드할 게임 씬 이름</param>
    /// <returns>시작 절차 완료를 기다리는 Task</returns>
    public Task StartGameAsHostAsync(string gameSceneName)
    {
        return coordinator.StartGameAsHostAsync(gameSceneName);
    }

    /// <summary>폴링 404 등 방 소멸 시 로컬 세션을 정리한다. RoomCleared→SessionEnded 로 이어진다.</summary>
    /// <returns>정리 완료를 기다리는 Task</returns>
    public Task HandleRoomDestroyedAsync()
    {
        return coordinator.HandleRoomDestroyedAsync();
    }

    /// <summary>코디네이터의 방 정리 통지를 세션 종료 이벤트로 중계한다.</summary>
    private void HandleRoomCleared()
    {
        SessionEnded?.Invoke();
    }

    /// <summary>코디네이터의 게임 시작 신호를 중계한다.</summary>
    private void HandleGameStarting()
    {
        GameStarting?.Invoke();
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
}
