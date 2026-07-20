using Unity.Services.Lobbies.Models;

/// <summary>
/// UGS 로비 커스텀 데이터 키의 단일 출처. LobbyManager·RoomManager·UILobbyEntry 에 복제돼 있던 문자열을 모은다.
/// </summary>
public static class LobbyDataKeys
{
    public const string Password = "Password";           // 로비 비밀번호 (Public, S1 인덱스)
    public const string PlayerName = "PlayerName";       // 플레이어 닉네임
    public const string SteamId = "SteamID";             // 플레이어 스팀 ID
    public const string IsReady = "IsReady";             // 플레이어 준비 상태
    public const string RelayJoinCode = "RelayJoinCode"; // 호스트가 실은 Relay Join 코드

    /// <summary>로비가 비밀번호 방인지 판정한다.</summary>
    /// <param name="lobby">대상 로비</param>
    /// <returns>비밀번호 데이터가 있으면 true</returns>
    public static bool HasPassword(Lobby lobby)
    {
        return lobby.Data != null && lobby.Data.ContainsKey(Password);
    }
}
