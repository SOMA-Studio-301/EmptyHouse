using Steamworks;
using UnityEngine;
using UnityEngine.UI;

public class UserPanel : MonoBehaviour
{
    [SerializeField] private RawImage avatarRawImage;
    [SerializeField] private Text hostText;
    [SerializeField] private Text nameText;
    [SerializeField] private Text statusText;

    private CSteamID currentSteamID;
    private Callback<AvatarImageLoaded_t> avatarLoadedCallback;
    private Callback<PersonaStateChange_t> personaStateCallback;

    private void Awake()
    {
        // 아바타가 늦게 로드될 경우를 대비한 콜백 등록
        avatarLoadedCallback = Callback<AvatarImageLoaded_t>.Create(OnAvatarImageLoaded);
        personaStateCallback = Callback<PersonaStateChange_t>.Create(OnPersonaStateChange);
    }

    public void SetPlayerInfo(string playerName, bool isHost, bool isReady, string steamIdStr)
    {
        if (nameText != null)
            nameText.text = playerName;

        if (hostText != null)
            hostText.text = isHost ? "Host" : "";

        if (statusText != null)
        {
            statusText.text = isReady ? "Ready" : "Not Ready";
            statusText.color = isReady ? Color.green : Color.red;
        }

        if (!string.IsNullOrEmpty(steamIdStr) && ulong.TryParse(steamIdStr, out ulong steamIdId))
        {
            currentSteamID = new CSteamID(steamIdId);

            // 정보가 아직 로컬에 없으면 서버에 요청 (콜백으로 나중에 도착)
            SteamFriends.RequestUserInformation(currentSteamID, false);

            TryLoadAvatar(currentSteamID);
        }
    }

    private void TryLoadAvatar(CSteamID steamID)
    {
        int avatarInt = SteamFriends.GetMediumFriendAvatar(steamID);

        // -1: 아직 로딩 안 됨, 0: 아바타 없음
        if (avatarInt <= 0)
        {
            Debug.Log($"[UserPanel] 아바타 아직 준비 안됨 (avatarInt={avatarInt}), 콜백 대기");
            return;
        }

        Texture2D avatarTex = GetSteamAvatar(avatarInt);
        if (avatarTex != null && avatarRawImage != null)
        {
            avatarRawImage.texture = avatarTex;
            avatarRawImage.uvRect = new Rect(0, 1, 1, -1);
        }
    }

    private void OnAvatarImageLoaded(AvatarImageLoaded_t callback)
    {
        if (callback.m_steamID != currentSteamID) return;
        TryLoadAvatar(callback.m_steamID);
    }

    private void OnPersonaStateChange(PersonaStateChange_t callback)
    {
        if (callback.m_ulSteamID != currentSteamID.m_SteamID) return;
        TryLoadAvatar(currentSteamID);
    }

    private Texture2D GetSteamAvatar(int avatarInt)
    {
        uint width, height;
        if (!SteamUtils.GetImageSize(avatarInt, out width, out height) || width == 0 || height == 0)
        {
            Debug.LogWarning("[UserPanel] GetImageSize 실패");
            return null;
        }

        byte[] avatarStream = new byte[width * height * 4];
        if (!SteamUtils.GetImageRGBA(avatarInt, avatarStream, (int)(width * height * 4)))
        {
            Debug.LogWarning("[UserPanel] GetImageRGBA 실패");
            return null;
        }

        Texture2D texture = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false);
        texture.LoadRawTextureData(avatarStream);
        texture.Apply();

        return texture;
    }
}