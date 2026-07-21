using System;
using System.Collections.Generic;
using Border.Core;
using UnityEngine;

/// <summary>
/// 방 화면 뷰. 유저 슬롯과 Ready/Start 버튼을 소유하고 의도만 이벤트로 올린다.
/// 방 이탈은 자체 버튼 없이 부모 <see cref="UILobby"/> 의 뒤로 가기(ESC·Back) 경로가 처리한다.
/// UGS·Relay·Netcode·스팀 호출은 RoomManager 몫이라 여기서는 알지 못한다.
/// </summary>
public class UIRoom : MonoBehaviour
{
    public event Action ReadyRequested; // Ready 버튼 클릭
    public event Action StartRequested; // Start 버튼 클릭
    public event Action InviteRequested; // 빈 슬롯 클릭(친구 초대). 스팀 오버레이는 매니저가 연다.

    [Header("Interaction Panel")]
    [SerializeField] private UIGenericButton readyButton; // 준비 토글 (게스트)
    [SerializeField] private UIGenericButton startButton; // 게임 시작 (방장)

    [Header("User List")]
    [SerializeField] private Transform userListContainer; // 슬롯이 붙는 부모. 자식으로 UIUserPanel 이 미리 배치돼 있다

    [Header("Loading")]
    [SerializeField] private UILoadingIndicator loadingIndicator; // 스팀 정보 대기 중 유저 목록 대신 띄우는 표시
    [SerializeField] private float steamLoadTimeout = 5f;         // 이 시간을 넘기면 아바타가 비어도 목록을 공개한다

    private const float SteamCheckInterval = 0.25f; // 스팀 준비 여부 폴링 간격

    private readonly List<UIUserPanel> userPanels = new List<UIUserPanel>();
    private readonly List<RoomSlotInfo> cachedSlots = new List<RoomSlotInfo>(); // 공개 시점에 다시 그리기 위한 최신 슬롯 데이터

    private bool isWaitingSteamInfo; // 스팀 정보 대기 중 여부. 한 번 공개하면 재입장까지 다시 켜지 않는다
    private float steamWaitDeadline;
    private float nextSteamCheckTime;

    /// <summary>버튼 리스너를 등록하고 미리 배치된 유저 슬롯을 수집한다.</summary>
    private void Awake()
    {
        readyButton.Clicked += RaiseReadyRequested;
        startButton.Clicked += RaiseStartRequested;

        foreach (Transform child in userListContainer)
        {
            UIUserPanel panel = child.GetComponent<UIUserPanel>();
            if (panel == null) continue;

            panel.InviteClicked += RaiseInviteRequested; // 슬롯은 UIRoom 과 수명을 같이하므로 해제 불요
            userPanels.Add(panel);
        }
    }

    /// <summary>리스너를 해제한다.</summary>
    private void OnDestroy()
    {
        readyButton.Clicked -= RaiseReadyRequested;
        startButton.Clicked -= RaiseStartRequested;
    }

    /// <summary>방 화면을 연다. 버튼 잠금과 스팀 대기 상태는 재입장마다 초기화한다.</summary>
    public void Show()
    {
        gameObject.SetActive(true);
        startButton.Interactable = true;

        BeginSteamInfoWait();
    }

    /// <summary>방 화면을 닫는다.</summary>
    public void Hide()
    {
        isWaitingSteamInfo = false;
        gameObject.SetActive(false);
    }

    /// <summary>스팀 정보가 다 오기 전이면 목록을 감춘 채 진행 상황만 확인한다.</summary>
    private void Update()
    {
        if (!isWaitingSteamInfo) return;
        if (Time.unscaledTime < nextSteamCheckTime) return;

        nextSteamCheckTime = Time.unscaledTime + SteamCheckInterval;
        TryRevealUserList();
    }

    /// <summary>유저 슬롯을 다시 그린다. 남는 자리는 초대 슬롯이 된다.</summary>
    /// <param name="slots">현재 방 인원의 표시 데이터</param>
    public void ShowPlayers(IReadOnlyList<RoomSlotInfo> slots)
    {
        cachedSlots.Clear();
        for (int i = 0; i < slots.Count; i++)
        {
            cachedSlots.Add(slots[i]);
        }

        DrawPanels();

        if (isWaitingSteamInfo) TryRevealUserList();
    }

    /// <summary>캐시된 슬롯 데이터를 패널에 반영한다.</summary>
    private void DrawPanels()
    {
        for (int i = 0; i < userPanels.Count; i++)
        {
            if (i < cachedSlots.Count)
            {
                RoomSlotInfo slot = cachedSlots[i];
                userPanels[i].SetPlayerInfo(slot.PlayerName, slot.IsHost, slot.IsReady, slot.SteamId);
                continue;
            }

            userPanels[i].SetEmptySlot();
        }
    }

    /// <summary>유저 목록을 감추고 로딩 표시를 띄운 뒤 대기 타이머를 건다.</summary>
    private void BeginSteamInfoWait()
    {
        isWaitingSteamInfo = true;
        steamWaitDeadline = Time.unscaledTime + steamLoadTimeout;
        nextSteamCheckTime = Time.unscaledTime + SteamCheckInterval;

        SetUserPanelsActive(false);
        loadingIndicator.Show();
    }

    /// <summary>스팀 아바타가 모두 준비됐거나 대기 시간이 다 되면 유저 목록을 공개한다.</summary>
    private void TryRevealUserList()
    {
        bool isTimedOut = Time.unscaledTime >= steamWaitDeadline;
        if (!isTimedOut && !IsEverySteamInfoReady()) return;

        if (isTimedOut) Log.W("[ROOM] 스팀 계정 정보 대기가 시간 초과됐다. 받은 정보만으로 유저 목록을 표시한다.", this);

        isWaitingSteamInfo = false;

        DrawPanels(); // 대기 중 도착한 아바타를 공개 직전에 한 번 더 반영한다
        loadingIndicator.Hide();
        SetUserPanelsActive(true);
    }

    /// <summary>유저 슬롯만 켜고 끈다. Ready·Start 버튼도 같은 부모에 있어 컨테이너째로 끄지 않는다.</summary>
    /// <param name="value">활성 여부</param>
    private void SetUserPanelsActive(bool value)
    {
        for (int i = 0; i < userPanels.Count; i++)
        {
            userPanels[i].gameObject.SetActive(value);
        }
    }

    /// <summary>표시할 모든 슬롯의 스팀 아바타가 도착했는지 판정한다.</summary>
    /// <returns>전부 준비됐으면 true. 아직 슬롯 데이터조차 없으면 false</returns>
    private bool IsEverySteamInfoReady()
    {
        if (cachedSlots.Count == 0) return false;

        foreach (RoomSlotInfo slot in cachedSlots)
        {
            if (!SteamAvatarUtility.IsAvatarReady(slot.SteamId)) return false;
        }

        return true;
    }

    /// <summary>방장/게스트에 따라 Start·Ready 버튼 표시를 전환한다.</summary>
    /// <param name="isHost">방장 여부</param>
    public void SetHostMode(bool isHost)
    {
        startButton.gameObject.SetActive(isHost);
        readyButton.gameObject.SetActive(!isHost);
    }

    /// <summary>Start 버튼의 입력 허용 여부를 바꾼다.</summary>
    /// <param name="value">허용 여부</param>
    public void SetStartInteractable(bool value)
    {
        startButton.Interactable = value;
    }

    /// <summary>Ready 버튼의 입력 허용 여부를 바꾼다.</summary>
    /// <param name="value">허용 여부</param>
    public void SetReadyInteractable(bool value)
    {
        readyButton.Interactable = value;
    }

    /// <summary>Ready 의도를 올린다.</summary>
    private void RaiseReadyRequested()
    {
        ReadyRequested?.Invoke();
    }

    /// <summary>Start 의도를 올린다.</summary>
    private void RaiseStartRequested()
    {
        StartRequested?.Invoke();
    }

    /// <summary>친구 초대 의도를 올린다.</summary>
    private void RaiseInviteRequested()
    {
        InviteRequested?.Invoke();
    }
}
