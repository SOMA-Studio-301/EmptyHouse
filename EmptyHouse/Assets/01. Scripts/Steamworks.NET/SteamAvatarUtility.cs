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
    /// <summary>스팀 ID 문자열로 큰 크기(184x184) 아바타 텍스처를 만든다. 스팀이 제공하는 최대 해상도다.</summary>
    /// <param name="steamId">대상 스팀 ID 문자열.</param>
    /// <returns>생성된 텍스처. 스팀 미초기화·ID 파싱 실패·이미지 미도착이면 null.</returns>
    public static Texture2D Load(string steamId)
    {
        if (!SteamManager.Initialized || !ulong.TryParse(steamId, out ulong steamIdValue)) return null;

        try
        {
            CSteamID targetId = new CSteamID(steamIdValue);

            // -1 은 큰 이미지가 아직 안 왔다는 뜻. 그동안은 중간 크기(64x64)로 대체해 빈 칸을 피한다
            int avatarHandle = SteamFriends.GetLargeFriendAvatar(targetId);
            if (avatarHandle == -1) avatarHandle = SteamFriends.GetMediumFriendAvatar(targetId);

            return CreateTexture(avatarHandle);
        }
        catch (Exception e)
        {
            Log.E($"[SteamAvatarUtility] 아바타 로딩 중 예외 발생: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 해당 스팀 ID의 아바타를 지금 그릴 수 있는지 본다. 아직이면 스팀에 유저 정보 요청을 걸어 둔다.
    /// 스팀 미초기화·ID 없음처럼 애초에 기다릴 대상이 아니면 true 로 취급한다.
    /// </summary>
    /// <param name="steamId">대상 스팀 ID 문자열.</param>
    /// <returns>더 기다릴 필요가 없으면 true, 이미지 도착 대기 중이면 false.</returns>
    public static bool IsAvatarReady(string steamId)
    {
        if (!SteamManager.Initialized || !ulong.TryParse(steamId, out ulong steamIdValue)) return true;

        try
        {
            CSteamID targetId = new CSteamID(steamIdValue);

            // -1 은 "이미지가 아직 안 왔다"는 뜻. 0(아바타 없음)이나 유효 핸들은 더 기다릴 게 없다
            // Load 가 큰 크기를 쓰므로 판정 기준도 같은 크기여야 한다 — 중간 크기로 재면 저해상도인 채 통과한다
            if (SteamFriends.GetLargeFriendAvatar(targetId) != -1) return true;

            SteamFriends.RequestUserInformation(targetId, false); // 아바타까지 받아오도록 요청만 걸어 둔다
            return false;
        }
        catch (Exception e)
        {
            Log.E($"[SteamAvatarUtility] 아바타 준비 상태 확인 중 예외 발생: {e.Message}");
            return true;
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
        texture.filterMode = FilterMode.Bilinear; // 슬롯이 원본보다 크면 업스케일되므로 계단 현상을 막는다
        texture.LoadRawTextureData(flippedBuffer);
        texture.Apply();
        return texture;
    }
}
