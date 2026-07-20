using System;
using Border.Localization;
using TMPro;
using UnityEngine;
using Unity.Services.Lobbies.Models;

/// <summary>
/// 로비 목록 셀 뷰. 방 이름·인원·비밀번호 아이콘을 그리고 Join 클릭 의도만 이벤트로 올린다.
/// 비밀번호 방이면 셀이 슬라이드 입력창을 직접 소유해 비밀번호를 받고, 입장 확정 시 그 값을 함께 올린다.
/// 입장 가능 여부 판정(만석)은 셀이 표시 수준에서만 하고, 실제 입장 로직은 상위 몫이다.
/// </summary>
public class LobbyListCell : MonoBehaviour
{
    /// <summary>발행: 입장 확정. 표시 중인 로비와 입력된 비밀번호(공개 방이면 빈 문자열)를 싣는다.</summary>
    public event Action<Lobby, string> JoinClicked;

    /// <summary>발행: 비밀번호 입력창 여닫힘. 상위가 셀을 하나만 열어두게 자기 자신을 싣는다.</summary>
    public event Action<LobbyListCell, bool> PasswordFieldToggled;

    [SerializeField] private TMP_Text lobbyNameText;       // 방 이름 라벨. 키 미등록 문자열은 원문 그대로 출력된다
    [SerializeField] private TMP_Text playerCountText;           // 인원 수 라벨
    [SerializeField] private GameObject passwordIcon;            // 비밀번호 방 자물쇠 표시
    [SerializeField] private UIGenericButton joinButton;         // 입장 버튼. 라벨은 SetButton 으로 갱신
    [SerializeField] private UIPasswordSlideField passwordField; // 슬라이드 비밀번호 입력. 비밀번호 방에서만 노출된다

    private Lobby lobby;      // 표시 중인 로비
    private bool hasPassword; // 표시 중인 로비가 비밀번호 방인지

    /// <summary>버튼·입력창 리스너를 등록한다.</summary>
    private void Awake()
    {
        joinButton.Clicked += HandleJoinClicked;
        passwordField.ToggleChanged += HandlePasswordFieldToggled;
        passwordField.Submitted += RaiseJoinClicked; // Enter 로도 입장이 확정된다
    }

    /// <summary>리스너를 해제한다.</summary>
    private void OnDestroy()
    {
        joinButton.Clicked -= HandleJoinClicked;
        passwordField.ToggleChanged -= HandlePasswordFieldToggled;
        passwordField.Submitted -= RaiseJoinClicked;
    }

    /// <summary>로비 정보를 셀에 그린다. 만석이면 Join 버튼을 잠그고, 공개 방이면 비밀번호 토글을 감춘다.</summary>
    /// <param name="lobby">표시할 로비</param>
    public void SetLobbyInfo(Lobby lobby)
    {
        this.lobby = lobby;
        hasPassword = LobbyDataKeys.HasPassword(lobby);

        lobbyNameText.text = lobby.Name; // 방 이름은 동적 문자열 — 키 미스 폴백으로 원문이 표시된다
        playerCountText.text = $"{lobby.Players.Count}/{lobby.MaxPlayers}";
        passwordIcon.SetActive(hasPassword);

        passwordField.ResetField();
        passwordField.gameObject.SetActive(hasPassword);

        bool isFull = lobby.Players.Count >= lobby.MaxPlayers;
        joinButton.Interactable = !isFull;
        joinButton.SetButton(isFull ? "Full" : "Join"); // TODO: 로컬라이즈 테이블에 키 등록 시 실제 키로 교체
    }

    /// <summary>비밀번호 입력창을 접는다. 상위의 뒤로 가기가 쓴다.</summary>
    public void ClosePasswordField()
    {
        passwordField.Close();
    }

    /// <summary>비밀번호가 틀렸음을 알린다. 입력창을 흔들고 비운다.</summary>
    public void RejectPassword()
    {
        passwordField.ShakeAndClear();
    }

    /// <summary>
    /// 행 클릭을 처리한다. 비밀번호 방인데 입력창이 아직 닫혀 있으면 입장 대신 입력창을 먼저 연다.
    /// </summary>
    private void HandleJoinClicked()
    {
        if (hasPassword && !passwordField.IsOn)
        {
            passwordField.Open();
            return;
        }

        RaiseJoinClicked();
    }

    /// <summary>입력창 여닫힘을 자기 자신과 함께 올린다.</summary>
    /// <param name="isOpen">열림 여부</param>
    private void HandlePasswordFieldToggled(bool isOpen)
    {
        PasswordFieldToggled?.Invoke(this, isOpen);
    }

    /// <summary>입장 의도를 올린다. 공개 방이면 비밀번호는 빈 문자열이다.</summary>
    private void RaiseJoinClicked()
    {
        JoinClicked?.Invoke(lobby, hasPassword ? passwordField.Password : "");
    }
}
