using Unity.Netcode;

/// <summary>
/// 결과 확정 시 서버가 클라로 보내는 플레이어 1명분 칭호 페이로드. ServerGameManager 의 ClientRpc 에 배열로 실린다.
/// clientId 키다 — UGS 로비 슬롯과의 매핑은 스텁 단계 미구현(전원 동일 칭호라 무해). 실제 칭호 분기 시 매핑을 함께 넣는다.
/// </summary>
public struct PlayerTitle : INetworkSerializable
{
    public ulong ClientId; // 칭호 대상 클라이언트 ID
    public TitleId Title;  // 배정된 칭호

    /// <summary>NGO 직렬화 — ClientId·Title 을 순서대로 읽고 쓴다.</summary>
    /// <param name="serializer">NGO 리더/라이터 직렬화기.</param>
    /// <typeparam name="T">리더 또는 라이터.</typeparam>
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref Title);
    }
}
