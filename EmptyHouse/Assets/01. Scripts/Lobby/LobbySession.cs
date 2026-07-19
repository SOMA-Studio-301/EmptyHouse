using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Border.Core;
using Steamworks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

/// <summary>
/// UGS 로비 세션의 단일 소유자. 현재 로비·호스트 판정·게임 시작 플래그·하트비트·생성/입장/퇴장/삭제를 전담한다.
/// 세션 상태는 이 클래스 안에서만 쓰며, 매니저들은 이벤트와 읽기 전용 프로퍼티로만 상태를 본다.
/// 파괴 순서가 미정의이므로 OnDestroy 에서는 이벤트를 발행하지 않는다.
/// </summary>
public class LobbySession : MonoBehaviour
{
    private const float HeartbeatIntervalSeconds = 15f;

    /// <summary>입장 시도 결과.</summary>
    public enum JoinResult
    {
        Success,       // 입장 성공
        WrongPassword, // 비밀번호 불일치(미입력 포함)
        Error,         // 그 외 실패
    }

    /// <summary>발행: 세션 시작(방 생성/입장 성공). 입장한 로비를 싣는다.</summary>
    public event Action<Lobby> SessionStarted;

    /// <summary>발행: 세션 로비 갱신(폴링/쓰기 반영). 최신 로비를 싣는다.</summary>
    public event Action<Lobby> LobbyUpdated;

    /// <summary>발행: 세션 종료(퇴장·로비 소멸). 게임 시작 중에는 발행하지 않는다.</summary>
    public event Action SessionEnded;

    public Lobby CurrentLobby { get; private set; }  // 현재 세션 로비. 없으면 null
    public bool IsInSession => CurrentLobby != null; // 세션 참여 여부
    public bool IsStartingGame { get; private set; } // 게임 시작 절차 진행 여부

    public bool IsSignedIn =>                        // UGS 초기화·로그인 완료 여부
        UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn;

    public bool IsLocalPlayerHost =>                 // 내가 현재 로비의 방장인지
        CurrentLobby != null && AuthenticationService.Instance.PlayerId == CurrentLobby.HostId;

    private string playerName = "Player"; // 로컬 닉네임. 스팀 초기화 시 페르소나 이름으로 대체
    private bool isHeartbeatRunning;      // 하트비트 타이머 가동 여부

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

    /// <summary>방을 만들어 세션을 시작하고 하트비트를 가동한다. 실패 시 예외를 그대로 던진다.</summary>
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

        CurrentLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);
        IsStartingGame = false;

        Log.D($"[SESSION] Created lobby: {CurrentLobby.Name} (ID: {CurrentLobby.Id})");

        EnsureHeartbeat();
        SessionStarted?.Invoke(CurrentLobby);
        return CurrentLobby;
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

            CurrentLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId, options);
            IsStartingGame = false;

            Log.D($"[SESSION] Joined lobby: {CurrentLobby.Name}");

            SessionStarted?.Invoke(CurrentLobby);
            return JoinResult.Success;
        }
        catch (Exception e)
        {
            Log.E($"[SESSION] 입장 실패: {e.Message}", this);
            return JoinResult.Error;
        }
    }

    /// <summary>폴링/쓰기 결과를 세션에 반영하고 갱신 이벤트를 발행한다. 방장 승격을 감지해 하트비트를 재가동한다.</summary>
    /// <param name="updated">서버에서 받은 최신 로비</param>
    public void UpdateLobby(Lobby updated)
    {
        CurrentLobby = updated;
        EnsureHeartbeat();
        LobbyUpdated?.Invoke(updated);
    }

    /// <summary>로비가 서버에서 사라졌음을 반영한다. 게임 시작 중이면 정상 경로라 종료 이벤트를 억제한다.</summary>
    public void NotifyLobbyGone()
    {
        StopHeartbeat();
        CurrentLobby = null;

        if (IsStartingGame) return;

        SessionEnded?.Invoke();
    }

    /// <summary>게임 시작 절차 개시를 표시한다. 이후 세션 종료 이벤트가 억제된다.</summary>
    public void MarkGameStarting()
    {
        IsStartingGame = true;
    }

    /// <summary>게임 시작 절차 실패를 표시해 세션을 평상시 상태로 되돌린다.</summary>
    public void ClearGameStarting()
    {
        IsStartingGame = false;
    }

    /// <summary>방에서 나가고 세션을 종료한다. 상태를 먼저 비워 이중 퇴장을 막는다.</summary>
    /// <returns>퇴장 완료를 기다리는 Task</returns>
    public async Task LeaveAsync()
    {
        if (CurrentLobby == null) return;

        StopHeartbeat();

        string lobbyId = CurrentLobby.Id;
        string playerId = AuthenticationService.Instance.PlayerId;
        CurrentLobby = null;

        try
        {
            await LobbyService.Instance.RemovePlayerAsync(lobbyId, playerId);
            Log.D($"[SESSION] Left lobby: {lobbyId}");
        }
        catch (Exception e)
        {
            if (this == null) return;
            Log.E($"[SESSION] 퇴장 실패: {e.Message}", this);
        }

        if (this == null) return;
        SessionEnded?.Invoke();
    }

    /// <summary>방장이 로비를 삭제한다. 게임 시작 후 정리 전용이라 세션 이벤트는 발행하지 않는다.</summary>
    /// <returns>삭제 완료를 기다리는 Task</returns>
    public async Task DeleteAsync()
    {
        if (CurrentLobby == null) return;

        string lobbyId = CurrentLobby.Id;
        CurrentLobby = null;

        // 씬 전환으로 이미 파괴됐으면 인보크는 파괴가 정리했으므로 생존 시에만 정지한다
        if (this != null) StopHeartbeat();

        try
        {
            await LobbyService.Instance.DeleteLobbyAsync(lobbyId);
            Log.D("[SESSION] 게임 시작 완료 - 로비를 정상적으로 삭제했다.");
        }
        catch (LobbyServiceException e)
        {
            Log.W($"[SESSION] 로비 삭제 중 예외 (무시 가능): {e.Message}");
        }
    }

    /// <summary>방장일 때 하트비트 타이머를 가동한다. 이미 돌고 있으면 무시한다(멱등).</summary>
    public void EnsureHeartbeat()
    {
        if (!IsLocalPlayerHost || isHeartbeatRunning) return;

        InvokeRepeating(nameof(SendHeartbeat), HeartbeatIntervalSeconds, HeartbeatIntervalSeconds);
        isHeartbeatRunning = true;
        Log.D("[SESSION] 하트비트 타이머 가동");
    }

    /// <summary>하트비트 타이머를 정지한다.</summary>
    private void StopHeartbeat()
    {
        if (!isHeartbeatRunning) return;

        CancelInvoke(nameof(SendHeartbeat));
        isHeartbeatRunning = false;
    }

    /// <summary>방장 자격으로 하트비트를 보낸다. 자격을 잃었으면 타이머를 멈춘다.</summary>
    private async void SendHeartbeat()
    {
        if (CurrentLobby == null || !AuthenticationService.Instance.IsSignedIn)
        {
            StopHeartbeat();
            return;
        }

        try
        {
            await LobbyService.Instance.SendHeartbeatPingAsync(CurrentLobby.Id);
        }
        catch (Exception e)
        {
            if (IsHeartbeatOwnershipFailure(e))
            {
                if (this != null) StopHeartbeat();
            }
        }
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

    /// <summary>게임 시작 중이 아니면 파괴 시 방에서 빠져나온다. 문자열만 캡처해 파괴 후에도 안전하다.</summary>
    private void OnDestroy()
    {
        if (CurrentLobby == null || IsStartingGame) return;

        string lobbyId = CurrentLobby.Id;
        string playerId = AuthenticationService.Instance.PlayerId;
        CurrentLobby = null;
        _ = LeaveDetachedAsync(lobbyId, playerId);
    }

    /// <summary>오브젝트 수명과 분리된 퇴장 처리. Unity API 를 건드리지 않는다.</summary>
    /// <param name="lobbyId">떠날 로비 ID</param>
    /// <param name="playerId">내 플레이어 ID</param>
    /// <returns>퇴장 완료를 기다리는 Task</returns>
    private static async Task LeaveDetachedAsync(string lobbyId, string playerId)
    {
        try
        {
            await LobbyService.Instance.RemovePlayerAsync(lobbyId, playerId);
        }
        catch (Exception e)
        {
            Log.W($"[SESSION] 파괴 시 퇴장 실패 (무시 가능): {e.Message}");
        }
    }
}
