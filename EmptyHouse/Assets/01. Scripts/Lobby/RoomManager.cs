using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Border.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Steamworks;

/// <summary>
/// 방(Room) 화면 프레젠터 겸 게임 시작 오케스트레이터. 로비 폴링·Relay 할당·Netcode 연결·게임 씬 전환을 담당한다.
/// 세션 상태(현재 로비·게임 시작 플래그·하트비트)는 LobbySession 이 단독 소유하며, 여기서는 읽기와 쓰기 경유만 한다.
/// </summary>
public class RoomManager : MonoBehaviour
{
    private const string RelayConnectionType = "dtls";

    [Header("Session")]
    [SerializeField] private LobbySession session; // 세션 단일 소유자

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
    private bool isPolling;

    // 씬 전환 시작 시점의 기대 인원 수를 저장해두고, 전원 로드 완료 시 로비 삭제
    private int expectedPlayerCount;
    private bool isSceneLoadEventSubscribed;

    private Callback<AvatarImageLoaded_t> avatarLoadedCallback;
    private Callback<PersonaStateChange_t> personaStateCallback;

    /// <summary>뷰의 의도와 세션 이벤트를 구독한다.</summary>
    private void OnEnable()
    {
        uiRoom.ReadyRequested += HandleReadyRequested;
        uiRoom.StartRequested += HandleStartRequested;
        uiRoom.ExitRequested += HandleExitRequested;
        uiRoom.InviteRequested += HandleInviteRequested;
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
        uiRoom.ReadyRequested -= HandleReadyRequested;
        uiRoom.StartRequested -= HandleStartRequested;
        uiRoom.ExitRequested -= HandleExitRequested;
        uiRoom.InviteRequested -= HandleInviteRequested;
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

    #region Session Events

    /// <summary>세션 시작(방 생성/입장)을 받아 방 화면을 열고 폴링을 시작한다.</summary>
    /// <param name="lobby">입장한 로비</param>
    private void HandleSessionStarted(Lobby lobby)
    {
        isReady = false;
        isInRoom = true;

        uiRoom.Show();
        UpdateRoomUI();

        _ = PollLobbyData();

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

    /// <summary>세션 종료(퇴장·로비 소멸)를 받아 방 화면을 닫는다.</summary>
    private void HandleSessionEnded()
    {
        isInRoom = false;
        isReady = false;
        uiRoom.Hide();
    }

    #endregion

    #region View Intents

    /// <summary>Ready 의도를 받는다.</summary>
    private void HandleReadyRequested()
    {
        _ = ToggleReady();
    }

    /// <summary>Start 의도를 받는다.</summary>
    private void HandleStartRequested()
    {
        _ = StartGame();
    }

    /// <summary>Exit 의도를 받는다. 게임 시작 중이면 버튼 잠금을 우회해도 막는다.</summary>
    private void HandleExitRequested()
    {
        if (session.IsStartingGame) return;

        _ = session.LeaveAsync();
    }

    /// <summary>빈 슬롯 클릭을 받아 스팀 친구 초대 오버레이를 연다.</summary>
    private void HandleInviteRequested()
    {
        if (!SteamManager.Initialized) return;

        SteamFriends.ActivateGameOverlay("Invite");
        Log.D("[STEAM] 친구 초대 오버레이 창 활성화");
    }

    #endregion

    #region Lobby Polling

    /// <summary>방에 있는 동안 로비를 주기적으로 조회해 세션에 반영하고, Relay 코드가 뜨면 게임에 합류한다.</summary>
    /// <returns>폴링 종료를 기다리는 Task</returns>
    private async Task PollLobbyData()
    {
        if (isPolling) return;
        isPolling = true;

        try
        {
            while (isInRoom && session.IsInSession)
            {
                try
                {
                    Lobby updated = await LobbyService.Instance.GetLobbyAsync(session.CurrentLobby.Id);
                    if (updated == null) continue;

                    // 세션 경유 반영 — LobbyUpdated 이벤트로 화면이 갱신되고, 방장 승격 시 하트비트가 재가동된다
                    session.UpdateLobby(updated);

                    if (!session.IsLocalPlayerHost
                        && updated.Data != null
                        && updated.Data.TryGetValue(LobbyDataKeys.RelayJoinCode, out DataObject relayData))
                    {
                        string joinCode = relayData.Value;
                        if (!string.IsNullOrEmpty(joinCode) && !session.IsStartingGame)
                        {
                            session.MarkGameStarting();
                            uiRoom.SetExitInteractable(false); // 게임 조인 시작 시 Exit 잠금
                            _ = JoinGameAsync(joinCode);
                            break;
                        }
                    }
                }
                catch (LobbyServiceException e)
                {
                    if (e.ErrorCode == 404 || e.Message.ToLower().Contains("not found"))
                    {
                        // 호스트가 게임 시작 후 정상적으로 로비를 삭제한 경우도 여기로 들어온다.
                        // 게임 시작 중이면 정상 흐름이라 경고를 띄우지 않고, 세션도 종료 이벤트를 억제한다.
                        if (!session.IsStartingGame)
                        {
                            Log.W("[ROOM] 로비가 이미 서버에서 삭제됐다.", this);
                        }
                        isInRoom = false;
                        session.NotifyLobbyGone();
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

    #region View Update

    /// <summary>현재 세션 로비 상태를 뷰에 내려준다.</summary>
    private void UpdateRoomUI()
    {
        if (!session.IsInSession) return;

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
            uiRoom.SetReadyInteractable(!session.IsStartingGame); // 게임 시작 중이면 Ready 도 잠금
            return;
        }

        // 게임 시작 중일 때는 allReady 여부와 상관없이 버튼 비활성 유지
        if (session.IsStartingGame) return;

        uiRoom.SetStartInteractable(IsEveryGuestReady() && session.CurrentLobby.Players.Count > 1);
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

            Lobby updated = await LobbyService.Instance.UpdatePlayerAsync(session.CurrentLobby.Id, playerId, options);
            session.UpdateLobby(updated); // LobbyUpdated 이벤트로 화면 갱신
        }
        catch (Exception e)
        {
            Log.E($"[ROOM] Failed to update ready status: {e.Message}", this);
            isReady = !isReady;
        }
    }

    /// <summary>방장이 Relay 를 할당하고 호스트를 띄운 뒤 전원 접속을 기다렸다가 게임 씬으로 넘어간다.</summary>
    /// <returns>시작 절차 완료를 기다리는 Task</returns>
    private async Task StartGame()
    {
        if (!session.IsLocalPlayerHost) return;
        if (session.IsStartingGame) return;

        session.MarkGameStarting();
        uiRoom.SetStartInteractable(false);
        uiRoom.SetExitInteractable(false); // Start 누르는 순간 Exit 잠금

        sfxEventChannel.RaisePlayEvent(gameStartAudioId, transform.position);

        Log.D("[ROOM] Host가 Relay 서버 생성을 시작한다...");

        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(session.CurrentLobby.MaxPlayers - 1);
            string relayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Log.D($"[ROOM] Relay 생성 완료! JoinCode: {relayJoinCode}");

            RelayServerData serverData = BuildRelayServerData(allocation, RelayConnectionType);
            GetUnityTransport().SetRelayServerData(serverData);

            UpdateLobbyOptions options = new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    { LobbyDataKeys.RelayJoinCode, new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode) }
                }
            };
            Lobby updated = await LobbyService.Instance.UpdateLobbyAsync(session.CurrentLobby.Id, options);
            session.UpdateLobby(updated);

            expectedPlayerCount = updated.Players.Count;

            NetworkManager.Singleton.StartHost();

            // 바로 LoadScene 하지 않고 전원 Netcode 연결될 때까지 로비에서 대기한다
            //   -> 세션이 안 죽으니 하트비트도 안 끊긴다
            await WaitForClientsToConnect(expectedPlayerCount, 15f);

            if (!isSceneLoadEventSubscribed)
            {
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnAllClientsLoaded;
                isSceneLoadEventSubscribed = true;
            }

            NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        }
        catch (Exception e)
        {
            Log.E($"[ROOM] 게임 시작 실패 (Relay 에러): {e.Message}", this);
            session.ClearGameStarting();
            uiRoom.SetStartInteractable(true);
            uiRoom.SetExitInteractable(true);
        }
    }

    /// <summary>전원이 Netcode 에 붙을 때까지 기다린다. 타임아웃돼도 진행한다.</summary>
    /// <param name="expectedClients">호스트를 포함한 기대 인원</param>
    /// <param name="timeoutSec">최대 대기 시간(초)</param>
    /// <returns>대기 완료를 기다리는 Task</returns>
    private async Task WaitForClientsToConnect(int expectedClients, float timeoutSec = 15f)
    {
        float elapsed = 0f;
        while (NetworkManager.Singleton.ConnectedClientsList.Count < expectedClients && elapsed < timeoutSec)
        {
            await Task.Delay(200);
            elapsed += 0.2f;
        }

        if (NetworkManager.Singleton.ConnectedClientsList.Count < expectedClients)
            Log.W($"[ROOM] 타임아웃: {NetworkManager.Singleton.ConnectedClientsList.Count}/{expectedClients}명만 연결됨. 그래도 진행한다.", this);
        else
            Log.D($"[ROOM] 전원({expectedClients}명) 연결 완료. 씬 전환 시작.");
    }

    #endregion

    #region Game Scene Transition (Host)

    /// <summary>
    /// 전원 씬 로드가 끝나면 세션에 로비 삭제를 맡긴다.
    /// 캐릭터 스폰은 GameScene 전용 스포너 몫이라 여기서는 로비 정리만 한다.
    /// </summary>
    /// <param name="sceneName">로드된 씬 이름</param>
    /// <param name="loadSceneMode">로드 모드</param>
    /// <param name="clientsCompleted">로드를 마친 클라이언트</param>
    /// <param name="clientsTimedOut">타임아웃된 클라이언트</param>
    private async void OnAllClientsLoaded(string sceneName, LoadSceneMode loadSceneMode,
        List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        if (clientsTimedOut != null && clientsTimedOut.Count > 0)
        {
            Log.W($"[ROOM] 씬 로드에 실패(타임아웃)한 클라이언트가 있다: {clientsTimedOut.Count}명. " +
                             $"그래도 로비를 정리하고 게임을 진행한다.");
        }

        Log.D($"[ROOM] 씬 로드 완료 클라이언트 수: {clientsCompleted.Count} / 예상 인원: {expectedPlayerCount}");

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnAllClientsLoaded;
        isSceneLoadEventSubscribed = false;

        isInRoom = false;

        await session.DeleteAsync();
    }

    #endregion

    #region Game Start (Client)

    /// <summary>게스트가 Relay 코드로 호스트에 붙는다.</summary>
    /// <param name="joinCode">호스트가 로비에 실은 Relay Join 코드</param>
    /// <returns>연결 시도 완료를 기다리는 Task</returns>
    private async Task JoinGameAsync(string joinCode)
    {
        Log.D($"[ROOM] Client가 Relay 연결을 시도한다. 코드: {joinCode}");

        try
        {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            RelayServerData clientServerData = BuildRelayServerData(joinAllocation, RelayConnectionType);
            GetUnityTransport().SetRelayServerData(clientServerData);

            NetworkManager.Singleton.OnClientConnectedCallback += OnNetcodeConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnNetcodeDisconnected;

            NetworkManager.Singleton.StartClient();
            isInRoom = false;
        }
        catch (Exception e)
        {
            Log.E($"[ROOM] Relay 연결 실패: {e.Message}", this);
            session.ClearGameStarting();
            uiRoom.SetExitInteractable(true); // 실패 시 Exit 복구
        }
    }

    /// <summary>호스트와 연결됐다.</summary>
    /// <param name="clientId">내 클라이언트 ID</param>
    private void OnNetcodeConnected(ulong clientId)
    {
        Log.D($"[NETCODE] 호스트와 연결 성공! 내 ID: {clientId}");
        UnsubscribeNetcodeEvents();
    }

    /// <summary>호스트와 연결이 끊겼다. 방에 남을 수 있도록 Exit 를 되살린다.</summary>
    /// <param name="clientId">끊긴 클라이언트 ID</param>
    private void OnNetcodeDisconnected(ulong clientId)
    {
        Log.E("[NETCODE] 호스트와의 연결에 실패했거나 끊어졌다!", this);
        session.ClearGameStarting();
        uiRoom.SetExitInteractable(true);
        UnsubscribeNetcodeEvents();
    }

    /// <summary>Netcode 연결 콜백을 해제한다.</summary>
    private void UnsubscribeNetcodeEvents()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnNetcodeConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnNetcodeDisconnected;
        }
    }

    #endregion

    #region Relay Helpers

    /// <summary>호스트 할당에서 dtls 엔드포인트를 찾아 RelayServerData 를 만든다.</summary>
    /// <param name="allocation">Relay 할당</param>
    /// <param name="connectionType">연결 방식</param>
    /// <returns>구성된 RelayServerData</returns>
    private RelayServerData BuildRelayServerData(Allocation allocation, string connectionType = "dtls")
    {
        var endpoint = allocation.ServerEndpoints.First(ep => ep.ConnectionType == connectionType);

        return new RelayServerData(
            endpoint.Host,
            (ushort)endpoint.Port,
            allocation.AllocationIdBytes,
            allocation.ConnectionData,
            allocation.ConnectionData,
            allocation.Key,
            endpoint.Secure // isSecure 를 엔드포인트에서 그대로 가져와 불일치를 막는다
        );
    }

    /// <summary>게스트 할당에서 dtls 엔드포인트를 찾아 RelayServerData 를 만든다.</summary>
    /// <param name="joinAllocation">Relay 조인 할당</param>
    /// <param name="connectionType">연결 방식</param>
    /// <returns>구성된 RelayServerData</returns>
    private RelayServerData BuildRelayServerData(JoinAllocation joinAllocation, string connectionType = "dtls")
    {
        var endpoint = joinAllocation.ServerEndpoints.First(ep => ep.ConnectionType == connectionType);

        return new RelayServerData(
            endpoint.Host,
            (ushort)endpoint.Port,
            joinAllocation.AllocationIdBytes,
            joinAllocation.ConnectionData,
            joinAllocation.HostConnectionData,
            joinAllocation.Key,
            endpoint.Secure
        );
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

    /// <summary>NetworkManager 의 UnityTransport 를 가져온다.</summary>
    /// <returns>구성된 UnityTransport</returns>
    /// <exception cref="InvalidOperationException">NetworkManager 에 UnityTransport 가 없을 때</exception>
    private static UnityTransport GetUnityTransport()
    {
        UnityTransport transport = NetworkManager.Singleton?.NetworkConfig.NetworkTransport as UnityTransport;
        if (transport == null) throw new InvalidOperationException("UnityTransport is not configured on NetworkManager.");
        return transport;
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

    /// <summary>씬 로드 구독을 정리한다. 퇴장 처리는 세션(LobbySession.OnDestroy)이 담당한다.</summary>
    private void OnDestroy()
    {
        if (isSceneLoadEventSubscribed && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnAllClientsLoaded;
        }
    }
}
