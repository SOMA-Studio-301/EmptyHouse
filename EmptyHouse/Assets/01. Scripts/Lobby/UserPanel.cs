using System;
using Border.Core;
using UnityEngine;
using UnityEngine.UI;
using Steamworks;

/// <summary>
/// 방 유저 슬롯 뷰. 유저 정보/빈 슬롯 표시를 전환하고, 빈 슬롯 클릭(친구 초대) 의도만 이벤트로 올린다.
/// 스팀 아바타는 슬롯이 스스로 로드해 그린다.
/// </summary>
public class UserPanel : MonoBehaviour
{
    /// <summary>발행: 빈 슬롯 클릭(친구 초대). 채워진 슬롯에서는 발행하지 않는다.</summary>
    public event Action InviteClicked;

    [Header("Content Panels")]
    [SerializeField] private GameObject blankContent; // 빈칸용 패널
    [SerializeField] private GameObject userContent;  // 유저 정보용 패널

    [Header("User UI Elements")]
    [SerializeField] private Text nameText;
    [SerializeField] private Text hostText;
    [SerializeField] private Text readyText;
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
        hostText.text = isHost ? "Host" : "";
        readyText.text = isReady ? "Ready" : "Not Ready";

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

        if (!SteamManager.Initialized || !ulong.TryParse(steamIdString, out ulong steamIdValue))
        {
            SetAvatarTexture(null);
            return;
        }

        try
        {
            int avatarHandle = SteamFriends.GetMediumFriendAvatar(new CSteamID(steamIdValue));
            SetAvatarTexture(CreateSteamAvatarTexture(avatarHandle));
        }
        catch (Exception e)
        {
            SetAvatarTexture(null);
            Log.E($"[UserPanel] 아바타 로딩 중 예외 발생: {e.Message}", this);
        }
    }

    /// <summary>스팀 아바타 핸들에서 상하 반전을 보정한 Texture2D를 만든다.</summary>
    /// <param name="avatarHandle">스팀 이미지 핸들</param>
    /// <returns>생성된 텍스처. 실패 시 null</returns>
    private static Texture2D CreateSteamAvatarTexture(int avatarHandle)
    {
        // 핸들러 ID가 0 이하이거나 이미지 사이즈를 가져오지 못하면 실패
        if (avatarHandle <= 0) return null;

        if (SteamUtils.GetImageSize(avatarHandle, out uint width, out uint height))
        {
            // RGBA 데이터 크기만큼 버퍼 할당 (가로 * 세로 * 4바이트)
            byte[] imageBuffer = new byte[width * height * 4];

            if (SteamUtils.GetImageRGBA(avatarHandle, imageBuffer, imageBuffer.Length))
            {
                Texture2D texture = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false);

                // 스팀 텍스처 데이터는 상하가 뒤집혀서 들어오므로 완벽하게 뒤집어서 보정 작업 수행
                byte[] flippedBuffer = new byte[imageBuffer.Length];
                int rowLength = (int)width * 4;
                for (int y = 0; y < height; y++)
                {
                    Array.Copy(imageBuffer, y * rowLength, flippedBuffer, ((int)height - 1 - y) * rowLength, rowLength);
                }

                texture.LoadRawTextureData(flippedBuffer);
                texture.Apply();
                return texture;
            }
        }
        return null;
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
