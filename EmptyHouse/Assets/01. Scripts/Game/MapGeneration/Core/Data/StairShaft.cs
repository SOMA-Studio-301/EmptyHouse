namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 계단 샤프트(M8 SSA) — 시드 층에서 소켓 접합으로 심은 계단실의 좌표를 전 층이 공유하는 레코드.
    /// 비시드 층은 이 좌표에 자기 층 테마의 계단실을 **복사 배치**하므로 층간 정합이 탐색이 아니라 복사가 되고,
    /// 배치 실패가 원리적으로 발생하지 않는다(층 롤백 장치 불필요).
    /// v2 규약: 전 층 관통(BottomFloor = 최하 서수, TopFloor = 최상 서수).
    /// </summary>
    public sealed class StairShaft
    {
        public int ShaftId; // 샤프트 번호(0부터) — 로그·툴 표시용
        public CellCoord Cell; // 전 층 공유 그리드 좌표(셀) — 계단실 풋프린트 원점
        public Rotation4 Rotation; // 전 층 공유 회전
        public int BottomFloor; // 관통 최하 층 서수
        public int TopFloor; // 관통 최상 층 서수
    }
}
