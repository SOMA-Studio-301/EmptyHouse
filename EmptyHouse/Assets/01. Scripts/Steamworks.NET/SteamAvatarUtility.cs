using System;
using Border.Core;
using Steamworks;
using UnityEngine;

/// <summary>
/// 스팀 아바타 텍스처 생성 유틸. "스팀 ID → Texture2D" 순수 변환만 담당한다.
/// 반환 텍스처는 매 호출마다 새로 생성되므로 <b>파괴 책임은 호출한 뷰가 진다</b>(뷰 교체·OnDestroy 시 Destroy).
/// </summary>
public static class SteamAvatarUtility
{
    /// <summary>스팀 ID 문자열로 중간 크기 아바타 텍스처를 만든다.</summary>
    /// <param name="steamId">대상 스팀 ID 문자열.</param>
    /// <returns>생성된 텍스처. 스팀 미초기화·ID 파싱 실패·이미지 미도착이면 null.</returns>
    public static Texture2D Load(string steamId)
    {
        if (!SteamManager.Initialized || !ulong.TryParse(steamId, out ulong steamIdValue)) return null;

        try
        {
            int avatarHandle = SteamFriends.GetMediumFriendAvatar(new CSteamID(steamIdValue));
            return CreateTexture(avatarHandle);
        }
        catch (Exception e)
        {
            Log.E($"[SteamAvatarUtility] 아바타 로딩 중 예외 발생: {e.Message}");
            return null;
        }
    }

    /// <summary>스팀 이미지 핸들에서 상하 반전을 보정한 텍스처를 만든다.</summary>
    /// <param name="avatarHandle">스팀 이미지 핸들.</param>
    /// <returns>생성된 텍스처. 핸들 무효·크기 조회 실패면 null.</returns>
    private static Texture2D CreateTexture(int avatarHandle)
    {
        // 핸들러 ID가 0 이하이거나 이미지 사이즈를 가져오지 못하면 실패
        if (avatarHandle <= 0) return null;

        if (!SteamUtils.GetImageSize(avatarHandle, out uint width, out uint height)) return null;

        // RGBA 데이터 크기만큼 버퍼 할당 (가로 * 세로 * 4바이트)
        byte[] imageBuffer = new byte[width * height * 4];
        if (!SteamUtils.GetImageRGBA(avatarHandle, imageBuffer, imageBuffer.Length)) return null;

        // 스팀 텍스처 데이터는 상하가 뒤집혀서 들어오므로 완벽하게 뒤집어서 보정 작업 수행
        byte[] flippedBuffer = new byte[imageBuffer.Length];
        int rowLength = (int)width * 4;
        for (int y = 0; y < height; y++)
        {
            Array.Copy(imageBuffer, y * rowLength, flippedBuffer, ((int)height - 1 - y) * rowLength, rowLength);
        }

        Texture2D texture = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false);
        texture.LoadRawTextureData(flippedBuffer);
        texture.Apply();
        return texture;
    }
}
