using System;
using Border.Localization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 방 유저 슬롯 뷰. 유저 정보/빈 슬롯 표시를 전환하고, 빈 슬롯 클릭(친구 초대) 의도만 이벤트로 올린다.
/// 스팀 아바타는 슬롯이 스스로 로드해 그린다.
/// </summary>
public class UIUserPanel : MonoBehaviour
{
    [LocalizeKey] public string DriverRoleKey;       // 방장 역할 라벨 키
    [LocalizeKey] public string PassengerRoleKey; // 일반 참가자 역할 라벨 키
    [LocalizeKey] public string ReadyKey;         // 준비 완료 라벨 키
    [LocalizeKey] public string NotReadyKey;      // 준비 미완료 라벨 키

    /// <summary>발행: 빈 슬롯 클릭(친구 초대). 채워진 슬롯에서는 발행하지 않는다.</summary>
    public event Action InviteClicked;

    [Header("Content Panels")]
    [SerializeField] private GameObject blankContent; // 빈칸용 패널
    [SerializeField] private GameObject userContent;  // 유저 정보용 패널

    [Header("User UI Elements")]
    [SerializeField] private UILocalizeText roleText; // 방장/참가자 역할 라벨
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private UILocalizeText readyText; // 준비 상태 라벨
    [SerializeField] private RawImage avatarImage;       // 아바타를 그릴 RawImage
    [SerializeField] private UIGenericButton slotButton; // 슬롯 전체를 덮는 초대 버튼

    private Texture2D runtimeAvatarTexture;
    private string currentSteamId;
    private bool isEmptySlot; // 빈 슬롯 여부. 초대 클릭 발행 게이트

    /// <summary>버튼 리스너를 등록한다.</summary>
    private void Awake()
    {
        slotButton.Clicked += RaiseInviteClicked;
    }

    /// <summary>리스너를 해제하고 런타임 아바타 텍스처를 정리한다.</summary>
    private void OnDestroy()
    {
        slotButton.Clicked -= RaiseInviteClicked;
        SetAvatarTexture(null);
    }

    /// <summary>유저 정보를 슬롯에 그린다. 채워진 슬롯은 초대 클릭을 잠근다.</summary>
    /// <param name="playerName">닉네임</param>
    /// <param name="isHost">방장 여부</param>
    /// <param name="isReady">준비 완료 여부</param>
    /// <param name="steamId">아바타 로드용 스팀 ID. 없으면 빈 문자열</param>
    public void SetPlayerInfo(string playerName, bool isHost, bool isReady, string steamId)
    {
        isEmptySlot = false;
        slotButton.Interactable = false;

        blankContent.SetActive(false);
        userContent.SetActive(true);

        nameText.text = playerName;
        roleText.SetKey(isHost ? DriverRoleKey : PassengerRoleKey);

        // 방장은 항상 준비 상태로 취급하므로 Ready 라벨 자체를 숨긴다
        readyText.gameObject.SetActive(!isHost);
        readyText.SetKey(isReady ? ReadyKey : NotReadyKey);

        LoadSteamAvatar(steamId);
    }

    /// <summary>빈 슬롯(초대 가능) 상태로 그린다.</summary>
    public void SetEmptySlot()
    {
        isEmptySlot = true;
        slotButton.Interactable = true;

        blankContent.SetActive(true);
        userContent.SetActive(false);
        currentSteamId = null;
        SetAvatarTexture(null);
    }

    /// <summary>빈 슬롯일 때만 초대 의도를 올린다.</summary>
    private void RaiseInviteClicked()
    {
        if (!isEmptySlot) return;

        InviteClicked?.Invoke();
    }

    /// <summary>스팀 ID로 아바타 텍스처를 불러와 적용한다. 동일 ID 재요청은 건너뛴다.</summary>
    /// <param name="steamIdString">대상 스팀 ID 문자열</param>
    private void LoadSteamAvatar(string steamIdString)
    {
        if (avatarImage == null) return;
        if (currentSteamId == steamIdString && runtimeAvatarTexture != null) return;
        currentSteamId = steamIdString;

        SetAvatarTexture(SteamAvatarUtility.Load(steamIdString));
    }

    /// <summary>아바타 텍스처를 교체하고 이전 런타임 텍스처를 파괴한다.</summary>
    /// <param name="texture">적용할 텍스처. null이면 비움</param>
    private void SetAvatarTexture(Texture2D texture)
    {
        if (runtimeAvatarTexture != null && runtimeAvatarTexture != texture)
        {
            Destroy(runtimeAvatarTexture);
        }

        runtimeAvatarTexture = texture;
        if (avatarImage != null) avatarImage.texture = texture;
    }
}
