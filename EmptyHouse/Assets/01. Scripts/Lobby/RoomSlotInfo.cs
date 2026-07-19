/// <summary>
/// 방 유저 슬롯 하나를 그리는 데 필요한 표시 데이터. UGS Player 를 뷰에 넘기지 않기 위한 경계 타입이다.
/// Player 파싱은 RoomManager 가 맡고, UIRoom 은 이 구조체만 안다.
/// </summary>
public readonly struct RoomSlotInfo
{
    public readonly string PlayerName; // 표시 닉네임
    public readonly bool IsHost;       // 방장 여부
    public readonly bool IsReady;      // 준비 완료 여부
    public readonly string SteamId;    // 아바타 조회용 스팀 ID. 없으면 빈 문자열

    /// <summary>슬롯 표시 데이터를 만든다.</summary>
    /// <param name="playerName">표시 닉네임</param>
    /// <param name="isHost">방장 여부</param>
    /// <param name="isReady">준비 완료 여부</param>
    /// <param name="steamId">아바타 조회용 스팀 ID</param>
    public RoomSlotInfo(string playerName, bool isHost, bool isReady, string steamId)
    {
        PlayerName = playerName;
        IsHost = isHost;
        IsReady = isReady;
        SteamId = steamId;
    }
}
