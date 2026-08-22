using System;
using System.Collections;
using Border.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Unity.Services.Lobbies.Models;

/// <summary>
/// 로비 목록 엔트리 뷰. 방 이름·인원을 그리고 입장 클릭 의도만 이벤트로 올린다.
/// 입장 위젯은 하나만 뜬다 — 공개방은 입장 버튼, 비밀번호 방은 슬라이드 입력창(자체 토글·Enter 확정)이다.
/// 출발(게임 진행 중)·만석은 예외로 비밀번호 방이라도 잠긴 버튼을 써서 상태를 알린다. 우선순위는 출발 > 만석 > 탑승.
/// 입장 가능 여부 판정은 표시 수준에서만 하고, 실제 입장 로직은 상위 몫이다.
/// 선택 상태는 배경·글로우 스프라이트로만 드러낸다 — 마우스가 엔트리 위에 있거나 비밀번호 입력창이 열려 있으면 선택이다.
/// 자식 위 호버는 부모 체인으로 전파되므로 루트에서 받으면 내부 위젯 어디에 올려도 잡힌다. EventSystem 포커스는 보지 않는다 —
/// 클릭으로 잡힌 포커스는 다른 게 선택될 때까지 남아 선택 표시가 고착되기 때문이다.
/// 엔트리 간 배타는 따로 관리하지 않는다. 호버는 본래 배타적이고, 입력창 배타는 UIRoomList 가 이미 조율한다.
/// 행 클릭은 공개방이면 더블 = 입장이고, 비밀번호 방이면 단일 클릭으로 입력창을 펼치는 것으로 끝난다.
/// 공개방의 클릭 판은 행 전체로 늘린 입장 버튼이다 — 입장 확정은 UIDoubleClickRelay 가 더블 클릭으로 걸러 올린다.
/// 비밀번호 방은 버튼이 꺼져 배경이 클릭을 받는다.
/// </summary>
public class UILobbyEntry : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    /// <summary>발행: 입장 확정. 표시 중인 로비와 입력된 비밀번호(공개 방이면 빈 문자열)를 싣는다.</summary>
    public event Action<Lobby, string> JoinClicked;

    /// <summary>발행: 비밀번호 입력창 여닫힘. 상위가 엔트리를 하나만 열어두게 자기 자신을 싣는다.</summary>
    public event Action<UILobbyEntry, bool> PasswordFieldToggled;

    /// <summary>발행: 비밀번호 입력창의 편집 포커스 변화. 상위가 목록 재그리기를 멈출지 정하는 데 쓴다.</summary>
    public event Action<UILobbyEntry, bool> PasswordFocusChanged;

    [LocalizeKey] public string JoinKey;     // 탑승(입장 가능) 라벨 키
    [LocalizeKey] public string FullKey;     // 만차(만석) 라벨 키
    [LocalizeKey] public string DepartedKey; // 출발(게임 진행 중) 라벨 키

    [SerializeField] private TMP_Text lobbyNameText;             // 방 이름 라벨. 키 미등록 문자열은 원문 그대로 출력된다
    [SerializeField] private TMP_Text playerCountText;           // 인원 수 라벨
    [SerializeField] private UIGenericButton joinButton;         // 입장 버튼. 공개방·출발·만석에서 노출되며 행 전체를 덮는다
    [SerializeField] private UIDoubleClickRelay joinDoubleClick; // 입장 버튼의 더블 클릭 릴레이. 단일 클릭은 선택으로만 쓰고 입장은 이쪽이 확정한다
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

    public bool IsPasswordFocused => passwordField.IsFocused; // 비밀번호 입력창이 편집 포커스를 쥐고 있는지

    private Lobby lobby;            // 표시 중인 로비
    private bool hasPassword;       // 표시 중인 로비가 비밀번호 방인지
    private bool isPasswordOpen;    // 비밀번호 입력창이 열려 있는지. 마우스가 벗어나도 선택을 유지시킨다
    private bool isHovered;         // 마우스가 엔트리 위에 있는지. 자식 위젯 위도 포함된다
    private bool usePasswordField;  // 입장 위젯으로 입력창을 쓰는지. 행 클릭 동작이 갈린다
    private bool isJoinable;        // 지금 입장할 수 있는 방인지. 출발·만석이면 false

    /// <summary>버튼·입력창 리스너를 등록한다.</summary>
    private void Awake()
    {
        joinDoubleClick.DoubleClicked += RaiseJoinClicked; // 버튼의 단일 클릭은 흘린다
        passwordField.ToggleChanged += HandlePasswordFieldToggled;
        passwordField.Submitted += RaiseJoinClicked; // Enter 로도 입장이 확정된다
        passwordField.FocusChanged += HandlePasswordFocusChanged;
    }

    /// <summary>리스너를 해제한다.</summary>
    private void OnDestroy()
    {
        joinDoubleClick.DoubleClicked -= RaiseJoinClicked;
        passwordField.ToggleChanged -= HandlePasswordFieldToggled;
        passwordField.Submitted -= RaiseJoinClicked;
        passwordField.FocusChanged -= HandlePasswordFocusChanged;
    }

    /// <summary>비활성화될 때 호버가 켜진 채로 남지 않게 초기화한다. 목록 재활용 대비.</summary>
    private void OnDisable()
    {
        isHovered = false;
        ApplySelectionSprite();
    }

    /// <summary>
    /// 배경으로 들어온 행 클릭을 받는다. 비밀번호 방에서 단일 클릭으로 입력창을 펼치는 경로 하나뿐이다.
    /// 공개방·출발·만석은 입장 버튼이 행 전체를 덮고 있어 클릭이 그쪽에서 소비되므로 여기까지 오지 않는다.
    /// </summary>
    /// <param name="eventData">EventSystem 이 전달하는 포인터 데이터. 연속 클릭 횟수를 읽는다</param>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (!usePasswordField) return;
        if (eventData.clickCount != 1) return; // 이미 펼친 뒤의 연타는 흘린다

        passwordField.Open();
    }

    /// <summary>마우스가 엔트리에 들어오면 선택 표시를 켠다. 자식 위 호버도 부모로 전파되어 여기로 온다.</summary>
    /// <param name="eventData">EventSystem 이 전달하는 포인터 데이터. 쓰지 않는다</param>
    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovered = true;
        ApplySelectionSprite();
    }

    /// <summary>마우스가 엔트리를 벗어나면 선택 표시를 끈다. 입력창이 열려 있으면 선택은 유지된다.</summary>
    /// <param name="eventData">EventSystem 이 전달하는 포인터 데이터. 쓰지 않는다</param>
    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        ApplySelectionSprite();
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

        isJoinable = !isDeparted && !isFull;
        usePasswordField = !useButton; // 행 클릭이 입장인지 입력창 펼치기인지를 가른다

        joinButton.gameObject.SetActive(useButton);
        passwordField.gameObject.SetActive(!useButton);

        isPasswordOpen = false;
        ApplySelectionSprite();

        if (!useButton) return;

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

    /// <summary>
    /// 입력창의 편집 포커스 변화를 자기 자신과 함께 올린다. 상위가 목록 재그리기를 멈출지 정한다.
    /// 포커스를 잃으면 한 프레임 뒤 새 포커스 위치를 보고 엔트리 밖이면 입력창을 접는다 — 선택 표시가 고아로 남지 않게.
    /// </summary>
    /// <param name="isFocused">포커스를 얻었는지 여부</param>
    private void HandlePasswordFocusChanged(bool isFocused)
    {
        PasswordFocusChanged?.Invoke(this, isFocused);

        if (!isFocused && isActiveAndEnabled)
        {
            StartCoroutine(CloseIfFocusLeftEntry());
        }
    }

    /// <summary>
    /// 한 프레임 뒤 EventSystem 의 새 선택 대상을 확인하고, 엔트리 밖이면 입력창을 접는다.
    /// 지연하는 이유: 포커스 상실 순간엔 EventSystem 이 아직 이전 선택을 가리키고,
    /// 자기 자물쇠 토글로 닫는 경우 즉시 닫으면 토글 클릭이 도로 여는 레이스가 생긴다.
    /// </summary>
    private IEnumerator CloseIfFocusLeftEntry()
    {
        yield return null;

        if (!passwordField.IsOn) yield break; // 그 사이 이미 닫혔으면(토글·배타 조율) 할 일이 없다

        GameObject selected = EventSystem.current.currentSelectedGameObject;
        if (selected != null && selected.transform.IsChildOf(transform)) yield break; // 포커스가 엔트리 안에서 움직인 것뿐이다

        passwordField.Close();
    }

    /// <summary>선택 여부에 맞춰 배경과 글로우를 칠한다. 마우스가 엔트리 위에 있거나 입력창이 열려 있으면 선택이다.</summary>
    private void ApplySelectionSprite()
    {
        bool isSelected = isPasswordOpen || isHovered;
        background.sprite = isSelected ? selectedSprite : normalSprite;
        glow.sprite = isSelected ? selectedGlowSprite : normalGlowSprite;
    }

    /// <summary>입장 의도를 올린다. 공개 방이면 비밀번호는 빈 문자열이다.</summary>
    private void RaiseJoinClicked()
    {
        JoinClicked?.Invoke(lobby, hasPassword ? passwordField.Password : "");
    }
}
