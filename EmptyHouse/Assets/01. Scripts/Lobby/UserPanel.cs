using System;
using Border.Core;
using UnityEngine;
using UnityEngine.UI;
using Steamworks;

public class UserPanel : MonoBehaviour
{
    [Header("Content Panels")]
    [SerializeField] private GameObject blankContent; // 빈칸용 패널
    [SerializeField] private GameObject userContent;  // 유저 정보용 패널

    [Header("User UI Elements")]
    [SerializeField] private Text nameText;
    [SerializeField] private Text hostText;
    [SerializeField] private Text readyText;
    [SerializeField] private RawImage avatarImage;     // ★ 아바타를 그릴 RawImage 컴포넌트
    [SerializeField] private Button slotButton;
    private Texture2D runtimeAvatarTexture;
    private string currentSteamId;

    public Button SlotButton => slotButton;

    // 1. 유저가 존재할 때 호출되는 함수
    public void SetPlayerInfo(string playerName, bool isHost, bool isReady, string steamId)
    {
        // 패널 활성화 스위칭
        if (blankContent != null) blankContent.SetActive(false);
        if (userContent != null) userContent.SetActive(true);

        // 유저 데이터 매핑
        if (nameText != null) nameText.text = playerName;
        if (hostText != null) hostText.text = isHost ? "Host" : "";
        if (readyText != null) readyText.text = isReady ? "Ready" : "Not Ready";
        
        // ★ [복구] 스팀 아바타 이미지 로드 함수 호출
        LoadSteamAvatar(steamId);
    }

    // 2. 빈 슬롯일 때 호출되는 함수
    public void SetEmptySlot()
    {
        // 패널 활성화 스위칭
        if (blankContent != null) blankContent.SetActive(true);
        if (userContent != null) userContent.SetActive(false);
        currentSteamId = null;
        SetAvatarTexture(null);
    }

    // ★ [추가] 스팀 ID를 기반으로 아바타 텍스처를 불러와 처리하는 함수
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

    private void SetAvatarTexture(Texture2D texture)
    {
        if (runtimeAvatarTexture != null && runtimeAvatarTexture != texture)
        {
            Destroy(runtimeAvatarTexture);
        }

        runtimeAvatarTexture = texture;
        if (avatarImage != null) avatarImage.texture = texture;
    }

    private void OnDestroy() => SetAvatarTexture(null);
}
