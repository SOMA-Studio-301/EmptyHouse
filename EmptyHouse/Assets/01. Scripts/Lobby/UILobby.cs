using System;
using TMPro;
using System.Collections.Generic;
using Unity.Services.Lobbies.Models;
using UnityEngine;

/// <summary>
/// 로비 화면의 셸. 세 서브뷰(<see cref="UIRoomList"/>·<see cref="UICreateContent"/>·<see cref="UIRoom"/>)의
/// 여닫기와 뒤로 가기 경로, 그리고 공용 헤더의 서브타이틀만 소유한다.
/// 서브뷰 내부 로직은 각자가 갖고, 이 클래스는 그 오브젝트를 켜고 끄며 자식이 올린 의도를 매니저용 이벤트로 다시 올린다.
/// 목록·방의 데이터 갱신은 각 매니저가 서브뷰를 직접 참조해 그리므로 여기를 거치지 않는다.
/// 팝업은 스택으로 쌓이고 <see cref="Back"/> 가 위에서부터 한 겹씩 걷어낸다. 걷어낼 것이 없을 때만 메뉴로 나간다.
/// </summary>
public class UILobby : MonoBehaviour
{
    [Header("Navigation")]
    [SerializeField] private UIGenericButton backButton; // 뒤로 가기 버튼(공용 헤더). ESC 와 같은 경로를 탄다
    [SerializeField] private TMP_Text subTitleText;      // 공용 헤더의 부제목. 떠 있는 서브뷰에 따라 갈아 끼운다

    [Header("Sub Views")]
    [SerializeField] private UIRoomList roomListPanel;      // 목록 화면(툴바 포함). 방에 들어가면 통째로 끈다
    [SerializeField] private UICreateContent createContent; // 방 만들기 팝업
    [SerializeField] private UIRoom roomPanel;              // 방 화면

    public event Action<string, string> CreateRequested; // 방 생성 요청. (방 이름, 비밀번호)
    public event Action RefreshRequested; // 새로고침 버튼 클릭
    public event Action<Lobby, string> LobbyJoinRequested; // 셀의 입장 확정. (대상 로비, 비밀번호. 공개 방이면 빈 문자열)
    public event Action BackButtonClicked; // 걷어낼 팝업이 없을 때의 뒤로 가기. Menu 패널 활성화

    public event Action RoomReadyRequested;  // 방 Ready 버튼 클릭
    public event Action RoomStartRequested;  // 방 Start 버튼 클릭
    public event Action RoomInviteRequested; // 방 빈 슬롯 클릭(친구 초대)
    public event Action RoomExitRequested;   // 방에서의 뒤로 가기. 실제 이탈/화면 복귀는 세션 이벤트가 처리한다

    // 열려 있는 팝업의 닫기 동작을 쌓아둔다. Back 이 위에서부터 하나씩 꺼내 실행한다
    private readonly Stack<Action> popupStack = new Stack<Action>();

    private bool wasRoomOpenBeforeCreate; // 방 만들기 팝업을 연 시점에 방 화면이 떠 있었는지. 닫을 때 되돌릴 화면을 고른다

    /// <summary>위젯 리스너를 등록하고 자식 이벤트를 구독한 뒤 팝업·방 화면을 닫은 상태로 초기화한다.</summary>
    private void OnEnable()
    {
        // 액션 할당
        backButton.Clicked += Back;

        roomListPanel.CreatePanelRequested += ShowCreatePanel;
        roomListPanel.RefreshRequested += RaiseRefreshRequested;
        roomListPanel.JoinRequested += RaiseLobbyJoinRequested;

        createContent.CreateConfirmed += HandleCreateConfirmed;

        roomPanel.ReadyRequested += RaiseRoomReadyRequested;
        roomPanel.StartRequested += RaiseRoomStartRequested;
        roomPanel.InviteRequested += RaiseRoomInviteRequested;

        popupStack.Clear();
        createContent.Hide();
        HideRoom();
    }

    /// <summary>리스너를 해제한다.</summary>
    private void OnDisable()
    {
        backButton.Clicked -= Back;

        roomListPanel.CreatePanelRequested -= ShowCreatePanel;
        roomListPanel.RefreshRequested -= RaiseRefreshRequested;
        roomListPanel.JoinRequested -= RaiseLobbyJoinRequested;

        createContent.CreateConfirmed -= HandleCreateConfirmed;

        roomPanel.ReadyRequested -= RaiseRoomReadyRequested;
        roomPanel.StartRequested -= RaiseRoomStartRequested;
        roomPanel.InviteRequested -= RaiseRoomInviteRequested;
    }

    /// <summary>
    /// 뒤로 가기 한 단계. 열린 비밀번호 입력창 → 팝업 스택 → 방 화면 → 메뉴 순으로 한 겹씩 걷어낸다.
    /// 입력창은 스택에 쌓지 않고 여기서 먼저 본다 — 토글로 직접 닫을 수 있어 스택에 두면 빈 항목이 남는다.
    /// 방도 스택에 쌓지 않는다 — 이탈은 비동기라 지금 닫아 버리면 세션이 살아 있는 채로 화면만 닫힌다.
    /// 방 화면을 스택보다 뒤에 두는 이유는 방 위에 팝업이 뜰 수 있어서다(이탈 확인 팝업 등).
    /// 헤더의 Back 버튼과 ESC(UIMenuManager 가 중계)가 함께 쓰는 유일한 경로다.
    /// </summary>
    public void Back()
    {
        if (roomListPanel.TryCloseOpenPasswordField()) return;

        if (popupStack.Count > 0)
        {
            popupStack.Pop().Invoke();
            return;
        }

        // TODO: 이탈 확인 팝업이 만들어지면 여기서 팝업을 띄우고, 확정 시 RoomExitRequested 를 올리도록 바꾼다
        if (roomPanel.gameObject.activeSelf)
        {
            RoomExitRequested?.Invoke(); // 화면 복귀는 세션 종료 통지를 받은 UIMenuManager 가 HideRoom 으로 처리한다
            return;
        }

        BackButtonClicked?.Invoke();
    }

    /// <summary>방 화면을 열고 목록 화면을 접는다. 목록이 켜져 있으면 방 뒤에서 툴바·셀이 그대로 눌린다.</summary>
    public void ShowRoom()
    {
        roomListPanel.Hide();
        roomPanel.Show();
        RefreshSubTitle();
    }

    /// <summary>방 화면을 접고 목록 화면을 되살린다.</summary>
    public void HideRoom()
    {
        roomPanel.Hide();
        roomListPanel.Show();
        RefreshSubTitle();
    }

    // 서브뷰의 데이터 갱신(목록 셀·방 슬롯)은 각 매니저가 UIRoomList / UIRoom 을 직접 참조해 그린다.
    // 여기서는 여닫기와 뒤로 가기 경로, 서브타이틀만 소유한다.

    /// <summary>셀의 입장 확정을 매니저용 이벤트로 올린다.</summary>
    /// <param name="lobby">대상 로비</param>
    /// <param name="password">입력된 비밀번호. 공개 방이면 빈 문자열</param>
    private void RaiseLobbyJoinRequested(Lobby lobby, string password)
    {
        LobbyJoinRequested?.Invoke(lobby, password);
    }

    /// <summary>팝업과 목록의 비밀번호 입력 상태를 초기로 되돌리고 스택을 비운다. 방 입장/퇴장 직후에 쓴다.</summary>
    public void ResetUI()
    {
        popupStack.Clear();
        HideCreatePanel();
        roomListPanel.ResetPasswordState();
    }

    /// <summary>
    /// 방 만들기 팝업을 열고 스택에 쌓는다. 이미 열려 있으면 중복으로 쌓지 않는다.
    /// 뒤 화면은 통째로 끈다 — 켜둔 채 덮으면 팝업 뒤에서 목록 셀·방 슬롯이 그대로 눌린다.
    /// 어느 화면 위에서 열렸는지 기억해 뒀다가 닫을 때 그 화면으로 되돌린다.
    /// </summary>
    private void ShowCreatePanel()
    {
        if (createContent.gameObject.activeSelf) return;

        wasRoomOpenBeforeCreate = roomPanel.gameObject.activeSelf;

        roomListPanel.Hide();
        roomPanel.Hide();

        createContent.Show();
        popupStack.Push(HideCreatePanel);
        RefreshSubTitle();
    }

    /// <summary>방 만들기 팝업을 닫고 열기 직전의 화면으로 되돌린다. 열려 있지 않으면 뒤 화면을 건드리지 않는다.</summary>
    private void HideCreatePanel()
    {
        if (!createContent.gameObject.activeSelf) return;

        createContent.Hide();

        if (wasRoomOpenBeforeCreate) roomPanel.Show();
        else roomListPanel.Show();

        wasRoomOpenBeforeCreate = false;
        RefreshSubTitle();
    }

    /// <summary>
    /// 떠 있는 서브뷰에 맞춰 서브타이틀을 갈아 끼운다. 겹칠 수 있으므로 위에 뜨는 것부터 본다(팝업 → 방 → 목록).
    /// </summary>
    private void RefreshSubTitle()
    {
        // TODO: 로컬라이즈 테이블에 키 등록 시 실제 키로 교체
        if (createContent.gameObject.activeSelf)
        {
            subTitleText.text = "CREATE BUS";
            return;
        }

        subTitleText.text = roomPanel.gameObject.activeSelf ? "IN BUS" : "BUS LIST";
    }

    /// <summary>새로고침 의도를 올린다.</summary>
    private void RaiseRefreshRequested()
    {
        RefreshRequested?.Invoke();
    }

    /// <summary>방 Ready 의도를 매니저용 이벤트로 올린다.</summary>
    private void RaiseRoomReadyRequested()
    {
        RoomReadyRequested?.Invoke();
    }

    /// <summary>방 Start 의도를 매니저용 이벤트로 올린다.</summary>
    private void RaiseRoomStartRequested()
    {
        RoomStartRequested?.Invoke();
    }

    /// <summary>방 친구 초대 의도를 매니저용 이벤트로 올린다.</summary>
    private void RaiseRoomInviteRequested()
    {
        RoomInviteRequested?.Invoke();
    }

    /// <summary>자식의 방 생성 확정을 매니저용 이벤트로 올린다.</summary>
    /// <param name="lobbyName">방 이름</param>
    /// <param name="password">비밀번호. 없으면 빈 문자열</param>
    private void HandleCreateConfirmed(string lobbyName, string password)
    {
        CreateRequested?.Invoke(lobbyName, password);
    }
}
