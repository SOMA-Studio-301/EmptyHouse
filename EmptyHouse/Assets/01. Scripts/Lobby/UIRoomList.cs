using System;
using System.Collections.Generic;
using Unity.Services.Lobbies.Models;
using UnityEngine;

/// <summary>
/// 로비 목록 화면 뷰. 엔트리 재그리기와 툴바(새로고침·방 만들기)를 소유하고 의도만 액션으로 올린다.
/// 비밀번호 입장은 별도 팝업 없이 엔트리가 직접 슬라이드 입력창으로 받고, 여기서는 한 번에 한 엔트리만 열리도록 조율한다.
/// 화면 여닫기와 뒤로 가기 경로는 부모(UILobby)가 정하고, 목록 데이터는 LobbyManager 가 직접 내려 그린다.
/// </summary>
public class UIRoomList : MonoBehaviour
{
    [Header("Toolbar")]
    [SerializeField] private UIGenericButton openCreatePanelButton; // 방 만들기 팝업을 여는 버튼
    [SerializeField] private UIGenericButton refreshButton;         // 리스트 새로고침 버튼

    [Header("List")]
    [SerializeField] private Transform lobbyListContainer;   // 리스트 엔트리가 붙는 부모
    [SerializeField] private UILobbyEntry lobbyEntryPrefab;  // 리스트 엔트리 프리팹

    public event Action RefreshRequested;             // 새로고침 버튼 클릭
    public event Action CreatePanelRequested;         // 방 만들기 버튼 클릭
    public event Action<Lobby, string> JoinRequested; // 엔트리의 입장 확정. (대상 로비, 비밀번호. 공개 방이면 빈 문자열)

    private UILobbyEntry openPasswordEntry;      // 비밀번호 입력창이 열려 있는 엔트리. 한 번에 하나만 허용한다
    private IReadOnlyList<Lobby> pendingLobbies; // 비밀번호 입력 중 보류된 목록. 입력창이 닫히면 그때 그린다

    /// <summary>레이아웃 확인용으로 씬에 박아둔 더미 엔트리를 걷어낸다. 첫 목록이 내려오기 전에 한 번만 돈다.</summary>
    private void Awake()
    {
        ClearEntries();
    }

    /// <summary>툴바 리스너를 등록하고 입력창 조율 상태를 비운다. 방에 들어가 있는 동안은 화면이 꺼져 구독도 없다.</summary>
    private void OnEnable()
    {
        openCreatePanelButton.Clicked += RaiseCreatePanelRequested;
        refreshButton.Clicked += RaiseRefreshRequested;

        openPasswordEntry = null;
        pendingLobbies = null;
    }

    /// <summary>리스너를 해제한다.</summary>
    private void OnDisable()
    {
        openCreatePanelButton.Clicked -= RaiseCreatePanelRequested;
        refreshButton.Clicked -= RaiseRefreshRequested;
    }

    /// <summary>목록 화면을 연다.</summary>
    public void Show()
    {
        gameObject.SetActive(true);
    }

    /// <summary>목록 화면을 닫는다. 방에 들어가 있는 동안 쓴다.</summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 로비 목록을 엔트리로 다시 그린다. 엔트리의 입장 확정은 JoinRequested 로 올린다.
    /// 비밀번호 입력 중에는 엔트리를 파괴하면 입력이 날아가므로 목록을 보류했다가 입력창이 닫힐 때 그린다.
    /// </summary>
    /// <param name="lobbies">표시할 로비 목록</param>
    public void ShowLobbyList(IReadOnlyList<Lobby> lobbies)
    {
        if (openPasswordEntry != null)
        {
            pendingLobbies = lobbies;
            return;
        }

        pendingLobbies = null;

        ClearEntries();

        foreach (Lobby lobby in lobbies)
        {
            UILobbyEntry entry = Instantiate(lobbyEntryPrefab, lobbyListContainer);
            entry.SetLobbyInfo(lobby);

            // 엔트리는 재그리기마다 파괴되므로 해제 불요
            entry.JoinClicked += RaiseJoinRequested;
            entry.PasswordFieldToggled += HandleEntryPasswordFieldToggled;
        }
    }

    /// <summary>비밀번호가 틀렸음을 열려 있는 엔트리에 알린다. 입력창이 흔들리고 비워진다.</summary>
    public void RejectPassword()
    {
        if (openPasswordEntry != null) openPasswordEntry.RejectPassword();
    }

    /// <summary>새로고침 버튼의 입력 허용 여부를 바꾼다. 쿨다운 표시용.</summary>
    /// <param name="value">허용 여부</param>
    public void SetRefreshInteractable(bool value)
    {
        refreshButton.Interactable = value;
    }

    /// <summary>열려 있는 비밀번호 입력창을 접는다. 부모의 뒤로 가기가 첫 단계로 쓴다.</summary>
    /// <returns>접을 입력창이 있었으면 true</returns>
    public bool TryCloseOpenPasswordField()
    {
        if (openPasswordEntry == null) return false;

        openPasswordEntry.ClosePasswordField(); // 엔트리가 토글을 끄면 HandleEntryPasswordFieldToggled 로 되돌아온다
        return true;
    }

    /// <summary>입력창 조율 상태만 비운다. 엔트리는 곧 재그리기로 파괴되므로 입력창을 접지 않고 참조만 놓는다.</summary>
    public void ResetPasswordState()
    {
        openPasswordEntry = null;
        pendingLobbies = null;
    }

    /// <summary>컨테이너에 붙어 있는 엔트리를 전부 파괴한다. 씬에 박아둔 더미도 함께 걷힌다.</summary>
    private void ClearEntries()
    {
        foreach (Transform child in lobbyListContainer)
        {
            Destroy(child.gameObject);
        }
    }

    /// <summary>엔트리의 입장 확정을 상위용 이벤트로 올린다.</summary>
    /// <param name="lobby">대상 로비</param>
    /// <param name="password">입력된 비밀번호. 공개 방이면 빈 문자열</param>
    private void RaiseJoinRequested(Lobby lobby, string password)
    {
        JoinRequested?.Invoke(lobby, password);
    }

    /// <summary>새로고침 의도를 올린다.</summary>
    private void RaiseRefreshRequested()
    {
        RefreshRequested?.Invoke();
    }

    /// <summary>방 만들기 팝업 열기 의도를 올린다.</summary>
    private void RaiseCreatePanelRequested()
    {
        CreatePanelRequested?.Invoke();
    }

    /// <summary>
    /// 엔트리의 비밀번호 입력창 여닫힘을 받아 한 번에 하나만 열려 있게 조율한다.
    /// 마지막 입력창이 닫히면 그동안 보류해 둔 목록을 그린다.
    /// </summary>
    /// <param name="entry">상태가 바뀐 엔트리</param>
    /// <param name="isOpen">열림 여부</param>
    private void HandleEntryPasswordFieldToggled(UILobbyEntry entry, bool isOpen)
    {
        if (isOpen)
        {
            // 다른 엔트리가 열려 있었다면 먼저 접는다. 그 엔트리의 닫힘 통지가 여기로 다시 들어오지만
            // openPasswordEntry 이 이미 새 엔트리로 바뀐 뒤라 아래 분기에서 걸러진다
            UILobbyEntry previous = openPasswordEntry;
            openPasswordEntry = entry;

            if (previous != null) previous.ClosePasswordField();

            return;
        }

        if (openPasswordEntry != entry) return;

        openPasswordEntry = null;

        if (pendingLobbies != null) ShowLobbyList(pendingLobbies);
    }
}
