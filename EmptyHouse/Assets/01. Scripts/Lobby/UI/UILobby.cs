using System;
using System.Collections.Generic;
using Border.Core;
using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로비 화면 뷰. 위젯을 소유하고 사용자의 의도만 이벤트로 올린다.
/// UGS 호출·세션 상태는 LobbyManager 몫이라 여기서는 알지 못한다 — 이 클래스는 네트워크를 만지지 않는다.
/// Create 패널 여닫기처럼 화면 안에서 닫히는 내비게이션은 매니저를 거치지 않고 여기서 직접 처리한다.
/// </summary>
public class UILobby : MonoBehaviour
{
    /// <summary>발행: 방 생성 요청. (방 이름, 비밀번호) — 비밀번호를 쓰지 않으면 빈 문자열이다.</summary>
    public event Action<string, string> CreateRequested;

    /// <summary>발행: 새로고침 버튼 클릭.</summary>
    public event Action RefreshRequested;

    /// <summary>발행: 리스트 셀의 Join 클릭. 비밀번호 방인지 판단은 매니저가 한다.</summary>
    public event Action<Lobby> LobbyJoinRequested;

    /// <summary>발행: 비밀번호 팝업의 Join 확정. 입력된 비밀번호를 싣는다.</summary>
    public event Action<string> PasswordJoinConfirmed;

    /// <summary>발행: 비밀번호 팝업 취소. 매니저가 잡아둔 대상 로비를 놓게 한다.</summary>
    public event Action PasswordJoinCancelled;

    [Header("Navigation")]
    [SerializeField] private GameObject createPanel;                // 방 만들기 입력 패널
    [SerializeField] private UIGenericButton openCreatePanelButton; // Create 패널을 여는 버튼
    [SerializeField] private UIGenericButton backButton;            // Create 패널을 닫는 버튼

    [Header("Join Password Popup")]
    [SerializeField] private GameObject privateJoinContent;            // 비밀번호 입력 팝업 전체
    [SerializeField] private TMP_InputField popupPasswordInput;         // 팝업 내부 비밀번호 입력창
    [SerializeField] private UIGenericButton popupConfirmJoinButton;    // 팝업 내부 Join 버튼
    [SerializeField] private UIGenericButton popupCancelJoinButton;     // 팝업 내부 Back 버튼
    [SerializeField] private GameObject passwordWarningLabel;           // 비밀번호 불일치 경고

    [Header("Join Tab")]
    [SerializeField] private Transform lobbyListContainer;      // 리스트 셀이 붙는 부모
    [SerializeField] private LobbyListCell lobbyListCellPrefab; // 리스트 셀 프리팹
    [SerializeField] private UIGenericButton refreshButton;     // 리스트 새로고침 버튼

    [Header("Create Tab")]
    [SerializeField] private TMP_InputField createLobbyNameInput;     // 방 이름 입력창
    [SerializeField] private TMP_InputField createLobbyPasswordInput; // 방 비밀번호 입력창
    [SerializeField] private Toggle passwordToggle;                   // 비밀번호 방 여부
    [SerializeField] private UIGenericButton createButton;            // 방 생성 확정 버튼
    [SerializeField] private GameObject forbiddenWordWarningLabel;    // 금칙어 경고

    /// <summary>위젯 리스너를 등록하고 팝업·경고를 모두 닫은 상태로 초기화한다.</summary>
    private void Awake()
    {
        openCreatePanelButton.Clicked += ShowCreatePanel;
        backButton.Clicked += HideCreatePanel;
        createButton.Clicked += RaiseCreateRequested;
        refreshButton.Clicked += RaiseRefreshRequested;
        popupConfirmJoinButton.Clicked += RaisePasswordJoinConfirmed;
        popupCancelJoinButton.Clicked += HandlePasswordJoinCancelled;
        passwordToggle.onValueChanged.AddListener(HandlePasswordToggleChanged);

        HandlePasswordToggleChanged(passwordToggle.isOn);
        HideCreatePanel();
        privateJoinContent.SetActive(false);
        passwordWarningLabel.SetActive(false);
        forbiddenWordWarningLabel.SetActive(false);
    }

    /// <summary>리스너를 해제한다.</summary>
    private void OnDestroy()
    {
        openCreatePanelButton.Clicked -= ShowCreatePanel;
        backButton.Clicked -= HideCreatePanel;
        createButton.Clicked -= RaiseCreateRequested;
        refreshButton.Clicked -= RaiseRefreshRequested;
        popupConfirmJoinButton.Clicked -= RaisePasswordJoinConfirmed;
        popupCancelJoinButton.Clicked -= HandlePasswordJoinCancelled;
        passwordToggle.onValueChanged.RemoveAllListeners();
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
            cell.SetLobbyInfo(lobby, l => LobbyJoinRequested?.Invoke(l));
        }
    }

    /// <summary>비밀번호 입력 팝업을 연다.</summary>
    public void ShowPasswordPopup()
    {
        popupPasswordInput.text = "";
        passwordWarningLabel.SetActive(false);
        privateJoinContent.SetActive(true);
    }

    /// <summary>비밀번호 입력 팝업을 닫고 입력·경고를 청소한다.</summary>
    public void HidePasswordPopup()
    {
        popupPasswordInput.text = "";
        passwordWarningLabel.SetActive(false);
        privateJoinContent.SetActive(false);
        Log.D("[UILobby] 비밀번호 입력창을 닫았다.");
    }

    /// <summary>비밀번호 불일치 경고를 토글한다.</summary>
    /// <param name="visible">표시 여부</param>
    public void SetPasswordWarning(bool visible)
    {
        passwordWarningLabel.SetActive(visible);
    }

    /// <summary>금칙어 경고를 토글한다. 켤 때는 입력창 포커스를 먼저 푼다.</summary>
    /// <param name="visible">표시 여부</param>
    public void SetForbiddenWordWarning(bool visible)
    {
        if (visible) createLobbyNameInput.DeactivateInputField();

        forbiddenWordWarningLabel.SetActive(visible);
    }

    /// <summary>새로고침 버튼의 입력 허용 여부를 바꾼다. 쿨다운 표시용.</summary>
    /// <param name="value">허용 여부</param>
    public void SetRefreshInteractable(bool value)
    {
        refreshButton.Interactable = value;
    }

    /// <summary>입력·팝업·경고를 전부 초기 상태로 되돌린다. 방 입장/퇴장 직후에 쓴다.</summary>
    public void ResetUI()
    {
        createLobbyNameInput.DeactivateInputField();
        createLobbyPasswordInput.DeactivateInputField();
        popupPasswordInput.DeactivateInputField();

        createLobbyNameInput.text = "";
        createLobbyPasswordInput.text = "";
        passwordToggle.isOn = false;

        HideCreatePanel();
        HidePasswordPopup();
        forbiddenWordWarningLabel.SetActive(false);
    }

    /// <summary>Create 패널을 연다. 이전 입력이 남지 않도록 매번 비운다.</summary>
    public void ShowCreatePanel()
    {
        createLobbyNameInput.text = "";
        createLobbyPasswordInput.text = "";
        forbiddenWordWarningLabel.SetActive(false);

        // isOn 을 끄면 onValueChanged 를 타고 비밀번호 입력창도 함께 닫힌다.
        passwordToggle.isOn = false;

        createPanel.SetActive(true);
    }

    /// <summary>Create 패널을 닫는다.</summary>
    public void HideCreatePanel()
    {
        createPanel.SetActive(false);
    }

    /// <summary>새로고침 의도를 올린다.</summary>
    private void RaiseRefreshRequested()
    {
        RefreshRequested?.Invoke();
    }

    /// <summary>입력값을 읽어 방 생성 의도를 올린다. 유효성·금칙어 판정은 매니저 몫이다.</summary>
    private void RaiseCreateRequested()
    {
        string password = passwordToggle.isOn ? createLobbyPasswordInput.text.Trim() : "";
        CreateRequested?.Invoke(createLobbyNameInput.text.Trim(), password);
    }

    /// <summary>팝업을 닫고 취소 의도를 올린다.</summary>
    private void HandlePasswordJoinCancelled()
    {
        HidePasswordPopup();
        PasswordJoinCancelled?.Invoke();
    }

    /// <summary>팝업에 입력된 비밀번호를 실어 Join 확정 의도를 올린다.</summary>
    private void RaisePasswordJoinConfirmed()
    {
        PasswordJoinConfirmed?.Invoke(popupPasswordInput.text.Trim());
    }

    /// <summary>비밀번호 사용 여부에 따라 비밀번호 입력창을 여닫는다. 화면 안에서 닫히는 동작이다.</summary>
    /// <param name="isOn">비밀번호 방 여부</param>
    private void HandlePasswordToggleChanged(bool isOn)
    {
        createLobbyPasswordInput.gameObject.SetActive(isOn);
    }
}
