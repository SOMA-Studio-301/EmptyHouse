using System;
using Border.Localization;
using TMPro;
using UnityEngine;
using Unity.Services.Lobbies.Models;

/// <summary>
/// 로비 목록 엔트리 뷰. 방 이름·인원·비밀번호 아이콘을 그리고 입장 클릭 의도만 이벤트로 올린다.
/// 입장 위젯은 하나만 뜬다 — 공개방은 입장 버튼, 비밀번호 방은 슬라이드 입력창(자체 토글·Enter 확정)이다.
/// 만석은 예외로 잠긴 버튼을 써서 상태를 알린다. 입장 가능 여부 판정은 표시 수준에서만 하고, 실제 입장 로직은 상위 몫이다.
/// </summary>
public class UILobbyEntry : MonoBehaviour
{
    /// <summary>발행: 입장 확정. 표시 중인 로비와 입력된 비밀번호(공개 방이면 빈 문자열)를 싣는다.</summary>
    public event Action<Lobby, string> JoinClicked;

    /// <summary>발행: 비밀번호 입력창 여닫힘. 상위가 엔트리를 하나만 열어두게 자기 자신을 싣는다.</summary>
    public event Action<UILobbyEntry, bool> PasswordFieldToggled;

    [LocalizeKey] public string JoinKey; // 입장 버튼 라벨 키
    [LocalizeKey] public string FullKey; // 만석 라벨 키

    [SerializeField] private TMP_Text lobbyNameText;             // 방 이름 라벨. 키 미등록 문자열은 원문 그대로 출력된다
    [SerializeField] private TMP_Text playerCountText;           // 인원 수 라벨
    [SerializeField] private GameObject passwordIcon;            // 비밀번호 방 자물쇠 표시
    [SerializeField] private UIGenericButton joinButton;         // 입장 버튼. 공개방과 만석에서만 노출된다
    [SerializeField] private UIPasswordSlideField passwordField; // 슬라이드 비밀번호 입력. 비밀번호 방에서만 노출된다

    private Lobby lobby;      // 표시 중인 로비
    private bool hasPassword; // 표시 중인 로비가 비밀번호 방인지

    /// <summary>버튼·입력창 리스너를 등록한다.</summary>
    private void Awake()
    {
        joinButton.Clicked += RaiseJoinClicked;
        passwordField.ToggleChanged += HandlePasswordFieldToggled;
        passwordField.Submitted += RaiseJoinClicked; // Enter 로도 입장이 확정된다
    }

    /// <summary>리스너를 해제한다.</summary>
    private void OnDestroy()
    {
        joinButton.Clicked -= RaiseJoinClicked;
        passwordField.ToggleChanged -= HandlePasswordFieldToggled;
        passwordField.Submitted -= RaiseJoinClicked;
    }

    /// <summary>
    /// 로비 정보를 엔트리에 그린다. 입장 위젯은 버튼과 입력창 중 하나만 켠다.
    /// 만석이면 비밀번호 방이라도 잠긴 버튼을 켠다 — 입력창에는 만석을 알릴 자리가 없다.
    /// </summary>
    /// <param name="lobby">표시할 로비</param>
    public void SetLobbyInfo(Lobby lobby)
    {
        this.lobby = lobby;
        hasPassword = LobbyDataKeys.HasPassword(lobby);

        lobbyNameText.text = lobby.Name; // 방 이름은 동적 문자열 — 키 미스 폴백으로 원문이 표시된다
        playerCountText.text = $"{lobby.Players.Count}/{lobby.MaxPlayers}";
        passwordIcon.SetActive(hasPassword);

        passwordField.ResetField();

        bool isFull = lobby.Players.Count >= lobby.MaxPlayers;
        bool useButton = isFull || !hasPassword;

        joinButton.gameObject.SetActive(useButton);
        passwordField.gameObject.SetActive(!useButton);

        if (!useButton) return;

        joinButton.Interactable = !isFull;
        joinButton.SetButton(isFull ? FullKey : JoinKey);
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
