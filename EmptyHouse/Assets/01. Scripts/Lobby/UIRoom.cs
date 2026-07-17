using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 방 화면 뷰. 유저 슬롯과 Ready/Start/Exit 버튼을 소유하고 의도만 이벤트로 올린다.
/// UGS·Relay·Netcode·스팀 호출은 RoomManager 몫이라 여기서는 알지 못한다.
/// </summary>
public class UIRoom : MonoBehaviour
{
    /// <summary>슬롯 수. 빈 자리는 초대 슬롯으로 그린다.</summary>
    private const int MaxRoomSlots = 4;

    /// <summary>발행: Ready 버튼 클릭.</summary>
    public event Action ReadyRequested;

    /// <summary>발행: Start 버튼 클릭.</summary>
    public event Action StartRequested;

    /// <summary>발행: Exit 버튼 클릭.</summary>
    public event Action ExitRequested;

    /// <summary>발행: 빈 슬롯 클릭(친구 초대). 스팀 오버레이는 매니저가 연다.</summary>
    public event Action InviteRequested;

    [Header("Interaction Panel")]
    [SerializeField] private UIGenericButton readyButton; // 준비 토글 (게스트)
    [SerializeField] private UIGenericButton startButton; // 게임 시작 (방장)
    [SerializeField] private UIGenericButton exitButton;  // 방 나가기

    [Header("User List")]
    [SerializeField] private Transform userListContainer; // 슬롯이 붙는 부모
    [SerializeField] private UserPanel userPanelPrefab;   // 유저 슬롯 프리팹

    private readonly List<UserPanel> userPanels = new List<UserPanel>(MaxRoomSlots);
    private bool slotsInitialized;

    /// <summary>버튼 리스너를 등록한다.</summary>
    private void Awake()
    {
        readyButton.Clicked += RaiseReadyRequested;
        startButton.Clicked += RaiseStartRequested;
        exitButton.Clicked += RaiseExitRequested;
    }

    /// <summary>리스너를 해제한다.</summary>
    private void OnDestroy()
    {
        readyButton.Clicked -= RaiseReadyRequested;
        startButton.Clicked -= RaiseStartRequested;
        exitButton.Clicked -= RaiseExitRequested;
    }

    /// <summary>방 화면을 연다. 버튼 잠금은 재입장마다 초기화한다.</summary>
    public void Show()
    {
        gameObject.SetActive(true);
        startButton.Interactable = true;
        exitButton.Interactable = true;
    }

    /// <summary>방 화면을 닫는다.</summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    /// <summary>유저 슬롯을 다시 그린다. 남는 자리는 초대 슬롯이 된다.</summary>
    /// <param name="slots">현재 방 인원의 표시 데이터</param>
    public void ShowPlayers(IReadOnlyList<RoomSlotInfo> slots)
    {
        EnsureUserSlots();

        for (int i = 0; i < MaxRoomSlots; i++)
        {
            UserPanel userPanel = userPanels[i];
            var slotButton = userPanel.SlotButton;

            if (i < slots.Count)
            {
                RoomSlotInfo slot = slots[i];
                userPanel.SetPlayerInfo(slot.PlayerName, slot.IsHost, slot.IsReady, slot.SteamId);

                slotButton.onClick.RemoveAllListeners();
                slotButton.enabled = false;
                continue;
            }

            userPanel.SetEmptySlot();
            slotButton.enabled = true;
            slotButton.onClick.RemoveAllListeners();
            slotButton.onClick.AddListener(RaiseInviteRequested);
        }
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

    /// <summary>Exit 버튼의 입력 허용 여부를 바꾼다. 게임 시작 중에는 잠근다.</summary>
    /// <param name="value">허용 여부</param>
    public void SetExitInteractable(bool value)
    {
        exitButton.Interactable = value;
    }

    /// <summary>슬롯 풀을 처음 한 번 만든다. 컨테이너에 미리 놓인 더미는 걷어낸다.</summary>
    private void EnsureUserSlots()
    {
        if (!slotsInitialized)
        {
            foreach (Transform child in userListContainer)
            {
                Destroy(child.gameObject);
            }

            slotsInitialized = true;
        }

        while (userPanels.Count < MaxRoomSlots)
        {
            userPanels.Add(Instantiate(userPanelPrefab, userListContainer));
        }
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

    /// <summary>Exit 의도를 올린다.</summary>
    private void RaiseExitRequested()
    {
        ExitRequested?.Invoke();
    }

    /// <summary>친구 초대 의도를 올린다.</summary>
    private void RaiseInviteRequested()
    {
        InviteRequested?.Invoke();
    }
}
