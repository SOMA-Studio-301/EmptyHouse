using System;
using System.Collections.Generic;
using Unity.Services.Lobbies.Models;
using UnityEngine;

/// <summary>
/// 로비 화면 뷰. 방 목록·새로고침과 두 팝업(방 만들기·비밀번호 입장)의 여닫기만 담당한다.
/// 팝업 내부 로직은 UICreateContent·UIPrivateJoinContent 가 갖고, 이 클래스는 그 오브젝트를 켜고 끄며
/// 자식이 올린 의도를 매니저용 이벤트로 다시 올린다. UGS 호출·세션 상태는 LobbyManager 몫이라 여기서는 알지 못한다.
/// </summary>
public class UILobby : MonoBehaviour
{
    [Header("Navigation")]
    [SerializeField] private UIGenericButton openCreatePanelButton; // 방 만들기 팝업을 여는 버튼
    [SerializeField] private UIGenericButton backButton;            // 방 만들기 팝업을 닫는 버튼(목록 화면 툴바)

    [Header("Sub Views")]
    [SerializeField] private UICreateContent createContent;             // 방 만들기 팝업
    [SerializeField] private UIPrivateJoinContent privateJoinContent;   // 비밀번호 입장 팝업

    [Header("Join Tab")]
    [SerializeField] private Transform lobbyListContainer;      // 리스트 셀이 붙는 부모
    [SerializeField] private LobbyListCell lobbyListCellPrefab; // 리스트 셀 프리팹
    [SerializeField] private UIGenericButton refreshButton;     // 리스트 새로고침 버튼

    public event Action<string, string> CreateRequested; // 방 생성 요청. (방 이름, 비밀번호)
    public event Action RefreshRequested; // 새로고침 버튼 클릭
    public event Action<Lobby> LobbyJoinRequested; // 리스트 셀의 Join 클릭. 비밀번호 방인지 판단은 매니저가
    public event Action<string> PasswordJoinConfirmed; // 비밀번호 팝업의 Join 확정. 입력된 비밀번호를 싣는다.
    public event Action PasswordJoinCancelled; // 비밀번호 팝업 취소. 매니저가 잡아둔 대상 로비를 놓게 한다.
    public event Action BackButtonClicked; // 뒤로 가기 버튼 누르면 Menu 패널 활성화

    /// <summary>위젯 리스너를 등록하고 자식 이벤트를 구독한 뒤 두 팝업을 닫은 상태로 초기화한다.</summary>
    private void OnEnable()
    {
        // 액션 할당
        openCreatePanelButton.Clicked += ShowCreatePanel;
        backButton.Clicked += BackToMenu;
        refreshButton.Clicked += RaiseRefreshRequested;

        createContent.CreateConfirmed += HandleCreateConfirmed;
        createContent.CloseRequested += HideCreatePanel;
        privateJoinContent.JoinConfirmed += HandlePasswordJoinConfirmed;
        privateJoinContent.JoinCancelled += HandlePasswordJoinCancelled;

        createContent.Hide();
        privateJoinContent.Hide();
    }

    /// <summary>리스너를 해제한다.</summary>
    private void OnDisable()
    {
        openCreatePanelButton.Clicked -= ShowCreatePanel;
        backButton.Clicked -= HideCreatePanel;
        refreshButton.Clicked -= RaiseRefreshRequested;

        createContent.CreateConfirmed -= HandleCreateConfirmed;
        createContent.CloseRequested -= HideCreatePanel;
        privateJoinContent.JoinConfirmed -= HandlePasswordJoinConfirmed;
        privateJoinContent.JoinCancelled -= HandlePasswordJoinCancelled;
    }

    /// <summary>로비 화면을 연다.</summary>
    public void Show()
    {
        gameObject.SetActive(true);
    }

    /// <summary>로비 화면을 닫는다. 방에 들어가 있는 동안 쓴다.</summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    /// <summary>로비 목록을 셀로 다시 그린다. 셀의 Join 클릭은 LobbyJoinRequested 로 올린다.</summary>
    /// <param name="lobbies">표시할 로비 목록</param>
    public void ShowLobbyList(IReadOnlyList<Lobby> lobbies)
    {
        foreach (Transform child in lobbyListContainer)
        {
            Destroy(child.gameObject);
        }

        foreach (Lobby lobby in lobbies)
        {
            LobbyListCell cell = Instantiate(lobbyListCellPrefab, lobbyListContainer);
            cell.SetLobbyInfo(lobby);
            cell.JoinClicked += RaiseLobbyJoinRequested; // 셀은 재그리기마다 파괴되므로 해제 불요
        }
    }

    /// <summary>셀의 Join 클릭을 매니저용 이벤트로 올린다.</summary>
    /// <param name="lobby">대상 로비</param>
    private void RaiseLobbyJoinRequested(Lobby lobby)
    {
        LobbyJoinRequested?.Invoke(lobby);
    }

    /// <summary>비밀번호 입력 팝업을 연다.</summary>
    public void ShowPasswordPopup()
    {
        privateJoinContent.Show();
    }

    /// <summary>비밀번호 입력 팝업을 닫는다.</summary>
    public void HidePasswordPopup()
    {
        privateJoinContent.Hide();
    }

    /// <summary>비밀번호 불일치 경고를 토글한다.</summary>
    /// <param name="visible">표시 여부</param>
    public void SetPasswordWarning(bool visible)
    {
        privateJoinContent.SetPasswordWarning(visible);
    }

    /// <summary>금칙어 경고를 토글한다.</summary>
    /// <param name="visible">표시 여부</param>
    public void SetForbiddenWordWarning(bool visible)
    {
        createContent.SetForbiddenWordWarning(visible);
    }

    /// <summary>새로고침 버튼의 입력 허용 여부를 바꾼다. 쿨다운 표시용.</summary>
    /// <param name="value">허용 여부</param>
    public void SetRefreshInteractable(bool value)
    {
        refreshButton.Interactable = value;
    }

    /// <summary>두 팝업을 전부 초기 상태로 되돌린다. 방 입장/퇴장 직후에 쓴다.</summary>
    public void ResetUI()
    {
        HideCreatePanel();
        HidePasswordPopup();
    }

    /// <summary>방 만들기 팝업을 연다.</summary>
    private void ShowCreatePanel()
    {
        createContent.Show();
    }

    /// <summary>방 만들기 팝업을 닫는다.</summary>
    private void HideCreatePanel()
    {
        createContent.Hide();
    }

    /// <summary>새로고침 의도를 올린다.</summary>
    private void RaiseRefreshRequested()
    {
        RefreshRequested?.Invoke();
    }

    /// <summary>자식의 방 생성 확정을 매니저용 이벤트로 올린다.</summary>
    /// <param name="lobbyName">방 이름</param>
    /// <param name="password">비밀번호. 없으면 빈 문자열</param>
    private void HandleCreateConfirmed(string lobbyName, string password)
    {
        CreateRequested?.Invoke(lobbyName, password);
    }

    /// <summary>자식의 Join 확정을 매니저용 이벤트로 올린다.</summary>
    /// <param name="password">입력된 비밀번호</param>
    private void HandlePasswordJoinConfirmed(string password)
    {
        PasswordJoinConfirmed?.Invoke(password);
    }

    /// <summary>팝업을 닫고 취소 의도를 올린다.</summary>
    private void HandlePasswordJoinCancelled()
    {
        HidePasswordPopup();
        PasswordJoinCancelled?.Invoke();
    }

    /// <summary> 메뉴 패널 활성화 </summary>
    private void BackToMenu()
    {
        BackButtonClicked?.Invoke();
    }
}
