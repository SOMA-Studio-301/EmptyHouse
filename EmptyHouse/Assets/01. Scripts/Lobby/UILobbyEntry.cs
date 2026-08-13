using System;
using Border.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.Services.Lobbies.Models;

/// <summary>
/// 로비 목록 엔트리 뷰. 방 이름·인원을 그리고 입장 클릭 의도만 이벤트로 올린다.
/// 입장 위젯은 하나만 뜬다 — 공개방은 입장 버튼, 비밀번호 방은 슬라이드 입력창(자체 토글·Enter 확정)이다.
/// 출발(게임 진행 중)·만석은 예외로 비밀번호 방이라도 잠긴 버튼을 써서 상태를 알린다. 우선순위는 출발 > 만석 > 탑승.
/// 입장 가능 여부 판정은 표시 수준에서만 하고, 실제 입장 로직은 상위 몫이다.
/// 선택 상태는 배경 스프라이트로만 드러낸다 — 내부 위젯이 포커스를 쥐고 있거나 비밀번호 입력창이 열려 있으면 선택이다.
/// 엔트리 간 배타는 따로 관리하지 않는다. EventSystem 이 포커스를 하나만 유지하고, 입력창 배타는 UIRoomList 가 이미 조율한다.
/// </summary>
public class UILobbyEntry : MonoBehaviour
{
    /// <summary>발행: 입장 확정. 표시 중인 로비와 입력된 비밀번호(공개 방이면 빈 문자열)를 싣는다.</summary>
    public event Action<Lobby, string> JoinClicked;

    /// <summary>발행: 비밀번호 입력창 여닫힘. 상위가 엔트리를 하나만 열어두게 자기 자신을 싣는다.</summary>
    public event Action<UILobbyEntry, bool> PasswordFieldToggled;

    [LocalizeKey] public string JoinKey;     // 탑승(입장 가능) 라벨 키
    [LocalizeKey] public string FullKey;     // 만차(만석) 라벨 키
    [LocalizeKey] public string DepartedKey; // 출발(게임 진행 중) 라벨 키

    [SerializeField] private TMP_Text lobbyNameText;             // 방 이름 라벨. 키 미등록 문자열은 원문 그대로 출력된다
    [SerializeField] private TMP_Text playerCountText;           // 인원 수 라벨
    [SerializeField] private UIGenericButton joinButton;         // 입장 버튼. 공개방·출발·만석에서 노출된다
    [SerializeField] private UIPasswordSlideField passwordField; // 슬라이드 비밀번호 입력. 비밀번호 방에서만 노출된다

    [Header("Button Colors")]
    [SerializeField] private Color activeTextColor = Color.white;                    // 탑승(입장 가능) 라벨 글자 색
    [SerializeField] private Color inactiveTextColor = new Color(0.5f, 0.5f, 0.5f); // 출발함·만차 라벨 글자 색

    [Header("Selection")]
    [SerializeField] private Image background;            // 선택 상태를 드러내는 엔트리 배경
    [SerializeField] private Sprite normalSprite;         // 비선택 배경
    [SerializeField] private Sprite selectedSprite;       // 선택 배경
    [SerializeField] private Image glow;                  // 배경 위에 겹치는 글로우
    [SerializeField] private Sprite normalGlowSprite;     // 비선택 글로우
    [SerializeField] private Sprite selectedGlowSprite;   // 선택 글로우
    [SerializeField] private UIFocusRelay[] focusRelays;  // 포커스를 감시할 내부 위젯 릴레이. 입장 버튼·자물쇠 토글·입력창

    private Lobby lobby;         // 표시 중인 로비
    private bool hasPassword;    // 표시 중인 로비가 비밀번호 방인지
    private bool isPasswordOpen; // 비밀번호 입력창이 열려 있는지. 포커스가 밖으로 나가도 선택을 유지시킨다

    /// <summary>버튼·입력창·포커스 릴레이 리스너를 등록한다.</summary>
    private void Awake()
    {
        joinButton.Clicked += RaiseJoinClicked;
        passwordField.ToggleChanged += HandlePasswordFieldToggled;
        passwordField.Submitted += RaiseJoinClicked; // Enter 로도 입장이 확정된다

        foreach (UIFocusRelay relay in focusRelays)
        {
            relay.FocusChanged += HandleFocusChanged;
        }
    }

    /// <summary>리스너를 해제한다.</summary>
    private void OnDestroy()
    {
        joinButton.Clicked -= RaiseJoinClicked;
        passwordField.ToggleChanged -= HandlePasswordFieldToggled;
        passwordField.Submitted -= RaiseJoinClicked;

        foreach (UIFocusRelay relay in focusRelays)
        {
            relay.FocusChanged -= HandleFocusChanged;
        }
    }

    /// <summary>
    /// 로비 정보를 엔트리에 그린다. 입장 위젯은 버튼과 입력창 중 하나만 켠다.
    /// 출발·만석이면 비밀번호 방이라도 자물쇠 대신 잠긴 버튼을 켠다 — 입력창에는 상태를 알릴 자리가 없다.
    /// 출발 판정은 IsLocked 로 한다. SessionCoordinator 가 게임 시작 절차부터 로비를 잠그므로 잠김 == 게임 진행 중이다.
    /// </summary>
    /// <param name="lobby">표시할 로비</param>
    public void SetLobbyInfo(Lobby lobby)
    {
        this.lobby = lobby;
        hasPassword = LobbyDataKeys.HasPassword(lobby);

        lobbyNameText.text = lobby.Name; // 방 이름은 동적 문자열 — 키 미스 폴백으로 원문이 표시된다
        playerCountText.text = $"{lobby.Players.Count}/{lobby.MaxPlayers}";
        passwordField.ResetField();

        bool isDeparted = lobby.IsLocked;
        bool isFull = lobby.Players.Count >= lobby.MaxPlayers;
        bool useButton = isDeparted || isFull || !hasPassword;

        joinButton.gameObject.SetActive(useButton);
        passwordField.gameObject.SetActive(!useButton);

        // 위젯을 껐다 켜는 사이 릴레이가 포커스 상실을 올리므로, 배경은 그게 끝난 뒤에 칠한다
        isPasswordOpen = false;
        ApplySelectionSprite();

        if (!useButton) return;

        bool isJoinable = !isDeparted && !isFull;
        joinButton.Interactable = isJoinable;
        joinButton.SetButton(isDeparted ? DepartedKey : isFull ? FullKey : JoinKey);
        joinButton.SetLabelColor(isJoinable ? activeTextColor : inactiveTextColor);
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

    /// <summary>입력창 여닫힘을 배경에 반영하고 자기 자신과 함께 올린다.</summary>
    /// <param name="isOpen">열림 여부</param>
    private void HandlePasswordFieldToggled(bool isOpen)
    {
        isPasswordOpen = isOpen;
        ApplySelectionSprite();

        PasswordFieldToggled?.Invoke(this, isOpen);
    }

    /// <summary>내부 위젯의 포커스 변화를 배경에 반영한다.</summary>
    /// <param name="_">얻음 여부. 집계는 릴레이 전체를 훑으므로 쓰지 않는다</param>
    private void HandleFocusChanged(bool _)
    {
        ApplySelectionSprite();
    }

    /// <summary>
    /// 선택 여부에 맞춰 배경과 글로우를 칠한다. 포커스가 엔트리 안에 있거나 입력창이 열려 있으면 선택이다.
    /// 엔트리 안에서 포커스가 옮겨갈 때 상실·획득이 같은 프레임에 오지만, 화면에 나가는 건 프레임 끝 상태라 깜빡이지 않는다.
    /// </summary>
    private void ApplySelectionSprite()
    {
        bool isSelected = isPasswordOpen || IsAnyRelayFocused();
        background.sprite = isSelected ? selectedSprite : normalSprite;
        glow.sprite = isSelected ? selectedGlowSprite : normalGlowSprite;
    }

    /// <summary>내부 위젯 중 하나라도 포커스를 쥐고 있는지 확인한다.</summary>
    /// <returns>하나라도 포커스가 있으면 true</returns>
    private bool IsAnyRelayFocused()
    {
        foreach (UIFocusRelay relay in focusRelays)
        {
            if (relay.HasFocus) return true;
        }

        return false;
    }

    /// <summary>입장 의도를 올린다. 공개 방이면 비밀번호는 빈 문자열이다.</summary>
    private void RaiseJoinClicked()
    {
        JoinClicked?.Invoke(lobby, hasPassword ? passwordField.Password : "");
    }
}
