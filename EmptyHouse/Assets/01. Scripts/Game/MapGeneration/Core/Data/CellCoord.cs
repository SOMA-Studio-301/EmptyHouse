namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 셀 단위 정수 그리드 좌표(절차적맵생성 2절 — 셀 = 바닥 타일 1칸).
    /// 코어는 실측 m를 모른다 — 배율 환산은 인스턴스화 계층 소관(G1).
    /// </summary>
    public readonly struct CellCoord
    {
        public readonly int X; // 그리드 X(셀)
        public readonly int Y; // 그리드 Y(셀)

        /// <summary>셀 좌표를 생성한다.</summary>
        /// <param name="x">그리드 X.</param>
        /// <param name="y">그리드 Y.</param>
        public CellCoord(int x, int y)
        {
            X = x;
            Y = y;
        }
    }
}
