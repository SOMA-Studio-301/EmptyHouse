namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 방 소켓 쌍의 연결 상태(1절 edges[]).
    /// 짝 없는 소켓의 막힌 벽 봉인은 RoomB = -1 인 간선으로 표현한다(AC-05 — 빈 소켓 0).
    /// </summary>
    public sealed class BlueprintEdge
    {
        public int RoomA; // 방 A 인덱스
        public int SocketA; // 방 A 소켓 Id
        public int RoomB; // 방 B 인덱스(짝 없는 봉인이면 -1)
        public int SocketB; // 방 B 소켓 Id(짝 없는 봉인이면 -1)
        public EdgeState State; // 통로 / 문 열림 / 문 잠김 / 막힌 벽
        public int LockNumber; // 자물쇠 번호(열쇠_XX ↔ 자물쇠_XX 넘버링). 0 = 자물쇠 없음
    }
}
