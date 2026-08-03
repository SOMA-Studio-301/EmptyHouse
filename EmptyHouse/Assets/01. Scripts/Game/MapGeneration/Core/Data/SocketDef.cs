namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 출입구 소켓 정의(2절) — 방 둘레의 문 가능 지점.
    /// 생성기가 문(잠김/열림)/통로/막힌 벽 중 택1로 채우며, 소켓 자리 벽 조각은 교체형이다.
    /// </summary>
    public sealed class SocketDef
    {
        public int Id; // 템플릿 내 소켓 번호
        public CellCoord LocalCell; // 템플릿 로컬 셀 좌표(둘레, 셀 경계 정렬 — G1)
        public SocketDirection Direction; // 소켓이 향하는 바깥 방향
    }
}
