using System;
using System.Collections.Generic;
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
    [SerializeField] private Transform userListContainer; // 슬롯이 붙는 부모. 자식으로 UserPanel 이 미리 배치돼 있다

    private readonly List<UserPanel> userPanels = new List<UserPanel>();

    /// <summary>버튼 리스너를 등록하고 미리 배치된 유저 슬롯을 수집한다.</summary>
    private void Awake()
    {
        readyButton.Clicked += RaiseReadyRequested;
        startButton.Clicked += RaiseStartRequested;

        foreach (Transform child in userListContainer)
        {
            UserPanel panel = child.GetComponent<UserPanel>();
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

    /// <summary>방 화면을 연다. 버튼 잠금은 재입장마다 초기화한다.</summary>
    public void Show()
    {
        gameObject.SetActive(true);
        startButton.Interactable = true;
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
        for (int i = 0; i < userPanels.Count; i++)
        {
            if (i < slots.Count)
            {
                RoomSlotInfo slot = slots[i];
                userPanels[i].SetPlayerInfo(slot.PlayerName, slot.IsHost, slot.IsReady, slot.SteamId);
                continue;
            }

            userPanels[i].SetEmptySlot();
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
