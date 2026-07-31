using System;
using System.Collections.Generic;
using Border.Core;
using Border.Localization;
using DG.Tweening;
using Febucci.UI;
using TMPro;
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
    [SerializeField] private TextAnimator_TMP departText; // Depart(출발) 클릭 시 켜지는 애니메이션 텍스트. TextAnimator 가 태그를 파싱한다

    [Header("Localize Keys")]
    [LocalizeKey] public string DepartKey = "UI_ROOM_DEPARTING";         // Depart 텍스트 로컬라이즈 키("출발중...")
    [LocalizeKey] public string ReadyKey = "UI_ROOM_READY";             // 준비 전 Ready 버튼 라벨 키("준비")
    [LocalizeKey] public string CancelReadyKey = "UI_ROOM_CANCEL_READY"; // 준비 후 Ready 버튼 라벨 키("준비 취소")

    [Header("Depart Text Animation")]
    [SerializeField] private string departAnimOpenTag = "<wave a=0.4 f=0.5>"; // Depart 텍스트를 감싸는 TextAnimator 여는 태그(a=진폭·f=속도 배수. 로컬라이즈 키 아님)
    [SerializeField] private string departAnimCloseTag = "</wave>"; // Depart 텍스트를 감싸는 TextAnimator 닫는 태그

    [Header("Start Warning")]
    [SerializeField] private TMP_Text allReadyWarningText;             // 전원 준비 전 Start 시 잠깐 뜨는 경고. 알파를 페이드한다(문구는 UILocalizeText 가 그린다)
    [SerializeField] private float allReadyWarningHoldDuration = 2f;   // 경고를 그대로 두는 시간(초)
    [SerializeField] private float allReadyWarningFadeDuration = 0.8f; // 경고 페이드 아웃 시간(초)

    [Header("User List")]
    [SerializeField] private Transform userListContainer; // 슬롯이 붙는 부모. 자식으로 UIUserPanel 이 미리 배치돼 있다

    [Header("Loading")]
    [SerializeField] private UILoadingIndicator loadingIndicator; // 스팀 정보 대기 중 유저 목록 대신 띄우는 표시
    [SerializeField] private float steamLoadTimeout = 5f;         // 이 시간을 넘기면 아바타가 비어도 목록을 공개한다

    private const float SteamCheckInterval = 0.25f; // 스팀 준비 여부 폴링 간격

    private readonly List<UIUserPanel> userPanels = new List<UIUserPanel>();
    private readonly List<RoomSlotInfo> cachedSlots = new List<RoomSlotInfo>(); // 공개 시점에 다시 그리기 위한 최신 슬롯 데이터

    private bool isWaitingSteamInfo; // 스팀 정보 대기 중 여부. 한 번 공개하면 재입장까지 다시 켜지 않는다
    private float steamWaitDeadline;
    private float nextSteamCheckTime;

    private Tween allReadyWarningTween; // 경고 알파 페이드 아웃 트윈. 유지 시간만큼 지연 뒤 알파 1 → 0

    /// <summary>버튼 리스너를 등록하고, 미리 배치된 유저 슬롯을 수집하고, 경고 페이드 트윈을 만든다.</summary>
    private void Awake()
    {
        readyButton.Clicked += RaiseReadyRequested;
        startButton.Clicked += RaiseStartRequested;

        foreach (Transform child in userListContainer)
        {
            UIUserPanel panel = child.GetComponent<UIUserPanel>();
            if (panel == null) continue;

            panel.InviteClicked += RaiseInviteRequested; // 슬롯은 UIRoom 과 수명을 같이하므로 해제 불요
            userPanels.Add(panel);
        }

        BuildAllReadyWarningTween();
    }

    /// <summary>경고 페이드 아웃 트윈을 1회 만들어 캐싱한다. DOTween 의 TMP 모듈이 꺼져 있어 알파를 직접 트윈한다.</summary>
    private void BuildAllReadyWarningTween()
    {
        allReadyWarningText.alpha = 1f;

        allReadyWarningTween = DOTween.To(() => allReadyWarningText.alpha, value => allReadyWarningText.alpha = value, 0f, allReadyWarningFadeDuration)
            .SetDelay(allReadyWarningHoldDuration)
            .SetAutoKill(false)
            .SetLink(gameObject)
            .SetUpdate(true)
            .OnComplete(() => allReadyWarningText.gameObject.SetActive(false)) // 다 사라진 뒤에 꺼서 레이아웃 비용을 없앤다
            .Pause();

        // 알파 1 을 시작값으로 확정시킨다. 건너뛰면 첫 재생 시점의 알파가 시작값으로 잡힌다
        allReadyWarningTween.Complete();
        allReadyWarningText.gameObject.SetActive(false); // Complete 는 콜백을 타지 않으므로 직접 끈다
    }

    /// <summary>리스너를 해제한다.</summary>
    private void OnDestroy()
    {
        readyButton.Clicked -= RaiseReadyRequested;
        startButton.Clicked -= RaiseStartRequested;
    }

    /// <summary>방 화면을 연다. Start 버튼은 항상 활성이며, Depart 텍스트·경고·스팀 대기 상태는 재입장마다 초기화한다.</summary>
    public void Show()
    {
        gameObject.SetActive(true);
        startButton.Interactable = true;
        departText.gameObject.SetActive(false);
        HideAllReadyWarning();

        BeginSteamInfoWait();
    }

    /// <summary>방 화면을 닫는다.</summary>
    public void Hide()
    {
        isWaitingSteamInfo = false;
        gameObject.SetActive(false);
    }

    /// <summary>스팀 정보가 다 오기 전이면 목록을 감춘 채 진행 상황만 확인한다.</summary>
    private void Update()
    {
        if (!isWaitingSteamInfo) return;
        if (Time.unscaledTime < nextSteamCheckTime) return;

        nextSteamCheckTime = Time.unscaledTime + SteamCheckInterval;
        TryRevealUserList();
    }

    /// <summary>유저 슬롯을 다시 그린다. 남는 자리는 초대 슬롯이 된다.</summary>
    /// <param name="slots">현재 방 인원의 표시 데이터</param>
    public void ShowPlayers(IReadOnlyList<RoomSlotInfo> slots)
    {
        cachedSlots.Clear();
        for (int i = 0; i < slots.Count; i++)
        {
            cachedSlots.Add(slots[i]);
        }

        DrawPanels();

        if (isWaitingSteamInfo) TryRevealUserList();
    }

    /// <summary>캐시된 슬롯 데이터를 패널에 반영한다.</summary>
    private void DrawPanels()
    {
        for (int i = 0; i < userPanels.Count; i++)
        {
            if (i < cachedSlots.Count)
            {
                RoomSlotInfo slot = cachedSlots[i];
                userPanels[i].SetPlayerInfo(slot.PlayerName, slot.IsHost, slot.IsReady, slot.SteamId);
                continue;
            }

            userPanels[i].SetEmptySlot();
        }
    }

    /// <summary>유저 목록을 감추고 로딩 표시를 띄운 뒤 대기 타이머를 건다.</summary>
    private void BeginSteamInfoWait()
    {
        isWaitingSteamInfo = true;
        steamWaitDeadline = Time.unscaledTime + steamLoadTimeout;
        nextSteamCheckTime = Time.unscaledTime + SteamCheckInterval;

        SetUserPanelsActive(false);
        loadingIndicator.Show();
    }

    /// <summary>스팀 아바타가 모두 준비됐거나 대기 시간이 다 되면 유저 목록을 공개한다.</summary>
    private void TryRevealUserList()
    {
        bool isTimedOut = Time.unscaledTime >= steamWaitDeadline;
        if (!isTimedOut && !IsEverySteamInfoReady()) return;

        if (isTimedOut) Log.W("[ROOM] 스팀 계정 정보 대기가 시간 초과됐다. 받은 정보만으로 유저 목록을 표시한다.", this);

        isWaitingSteamInfo = false;

        DrawPanels(); // 대기 중 도착한 아바타를 공개 직전에 한 번 더 반영한다
        loadingIndicator.Hide();
        SetUserPanelsActive(true);
    }

    /// <summary>유저 슬롯만 켜고 끈다. Ready·Start 버튼도 같은 부모에 있어 컨테이너째로 끄지 않는다.</summary>
    /// <param name="value">활성 여부</param>
    private void SetUserPanelsActive(bool value)
    {
        for (int i = 0; i < userPanels.Count; i++)
        {
            userPanels[i].gameObject.SetActive(value);
        }
    }

    /// <summary>표시할 모든 슬롯의 스팀 아바타가 도착했는지 판정한다.</summary>
    /// <returns>전부 준비됐으면 true. 아직 슬롯 데이터조차 없으면 false</returns>
    private bool IsEverySteamInfoReady()
    {
        if (cachedSlots.Count == 0) return false;

        foreach (RoomSlotInfo slot in cachedSlots)
        {
            if (!SteamAvatarUtility.IsAvatarReady(slot.SteamId)) return false;
        }

        return true;
    }

    /// <summary>방장/게스트에 따라 Start·Ready 버튼 표시를 전환한다.</summary>
    /// <param name="isHost">방장 여부</param>
    public void SetHostMode(bool isHost)
    {
        startButton.gameObject.SetActive(isHost);
        readyButton.gameObject.SetActive(!isHost);
    }

    /// <summary>전원 준비 전 Start 를 눌렀을 때 경고를 즉시 띄우고 페이드 아웃 트윈을 처음부터 재생한다. 연타하면 유지 시간부터 다시 센다.</summary>
    public void FlashAllReadyWarning()
    {
        allReadyWarningText.gameObject.SetActive(true);
        allReadyWarningText.alpha = 1f;
        allReadyWarningTween.Restart(); // 캐싱한 트윈을 되감아 재생한다 — 지연(유지 시간)도 함께 초기화된다
    }

    /// <summary>경고를 연출 없이 즉시 감춘다. 방 재입장 시 남은 문구를 지운다.</summary>
    private void HideAllReadyWarning()
    {
        allReadyWarningTween.Pause();
        allReadyWarningText.gameObject.SetActive(false);
    }

    /// <summary>Ready 버튼의 입력 허용 여부를 바꾼다.</summary>
    /// <param name="value">허용 여부</param>
    public void SetReadyInteractable(bool value)
    {
        readyButton.Interactable = value;
    }

    /// <summary>Ready 버튼 라벨을 준비 상태에 맞춰 토글한다.</summary>
    /// <param name="isReady">현재 준비 완료 여부</param>
    public void SetReadyLabel(bool isReady)
    {
        readyButton.SetButton(isReady ? CancelReadyKey : ReadyKey);
    }

    /// <summary>Ready 의도를 올린다.</summary>
    private void RaiseReadyRequested()
    {
        ReadyRequested?.Invoke();
    }

    /// <summary>Start(Depart) 의도만 올린다. Depart 텍스트 표시는 실제 게임 시작(씬 전환) 시점에 <see cref="ShowDepartText"/> 로 처리한다.</summary>
    private void RaiseStartRequested()
    {
        StartRequested?.Invoke();
    }

    /// <summary>Depart 연출 텍스트를 켠다. 전원 준비 후 실제 씬 전환이 시작될 때 RoomManager 가 호출한다(전 클라이언트).</summary>
    public void ShowDepartText()
    {
        departText.gameObject.SetActive(true);
        departText.SetText(BuildDepartText());
    }

    /// <summary>Depart 로컬라이즈 문자열을 현재 언어로 해석해 애니메이션 태그로 감싼다. TMP 직접 출력이 아니라 TextAnimator 파싱용이다.</summary>
    /// <returns>애니메이션 태그로 감싼 최종 문자열</returns>
    private string BuildDepartText()
    {
        ILocalizationProvider manager = LocalizationManager.Current;
        string body = manager != null ? manager.Get(DepartKey) : DepartKey; // 매니저 미준비 시 키 원문 폴백(UILocalizeText 와 동일)
        return departAnimOpenTag + body + departAnimCloseTag;
    }

    /// <summary>친구 초대 의도를 올린다.</summary>
    private void RaiseInviteRequested()
    {
        InviteRequested?.Invoke();
    }
}
