namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 셀 격자 회전·전역 변환 유틸(2·3절). 생성기(LayoutGenerator)·기획자 툴(10절)·인스턴스화 어댑터가
    /// 같은 변환을 공유한다 — 회전 해석이 갈라지면 미리보기와 실제 맵이 어긋난다(AC-02·AC-21 과 같은 근거).
    /// 순수 수학 핫패스(생성 루프·리페인트)라 Log.D 트레이스를 두지 않는다.
    /// </summary>
    public static class CellMath
    {
        /// <summary>템플릿 로컬 셀을 회전된 풋프린트 좌표로 변환한다(원점 = 회전 후 풋프린트 원점 유지).</summary>
        /// <param name="cell">템플릿 로컬 셀.</param>
        /// <param name="width">템플릿 원본 가로(셀).</param>
        /// <param name="height">템플릿 원본 세로(셀).</param>
        /// <param name="rotation">적용 회전.</param>
        /// <returns>회전 적용 로컬 셀.</returns>
        public static CellCoord RotateLocalCell(CellCoord cell, int width, int height, Rotation4 rotation)
        {
            switch (rotation)
            {
                case Rotation4.Deg90: return new CellCoord(cell.Y, width - 1 - cell.X);
                case Rotation4.Deg180: return new CellCoord(width - 1 - cell.X, height - 1 - cell.Y);
                case Rotation4.Deg270: return new CellCoord(height - 1 - cell.Y, cell.X);
                default: return cell;
            }
        }

        /// <summary>회전 적용 후 풋프린트 크기를 구한다 — 90/270도는 가로·세로가 뒤바뀐다.</summary>
        /// <param name="width">템플릿 원본 가로(셀).</param>
        /// <param name="height">템플릿 원본 세로(셀).</param>
        /// <param name="rotation">적용 회전.</param>
        /// <returns>회전 적용 (가로, 세로).</returns>
        public static (int width, int height) RotatedSize(int width, int height, Rotation4 rotation)
        {
            return rotation == Rotation4.Deg90 || rotation == Rotation4.Deg270 ? (height, width) : (width, height);
        }

        /// <summary>소켓 방향을 회전시킨다(북→동→남→서 순환).</summary>
        /// <param name="direction">원본 방향.</param>
        /// <param name="rotation">적용 회전.</param>
        /// <returns>회전 적용 방향.</returns>
        public static SocketDirection RotateDirection(SocketDirection direction, Rotation4 rotation)
        {
            return (SocketDirection)(((int)direction + (int)rotation) % 4);
        }

        /// <summary>방 배치(원점·회전)를 적용해 템플릿 로컬 셀의 전역 셀 좌표를 구한다 — 소켓·마커 위치 조회의 공용 경로.</summary>
        /// <param name="room">배치된 방.</param>
        /// <param name="template">그 방의 템플릿.</param>
        /// <param name="localCell">템플릿 로컬 셀.</param>
        /// <returns>전역 셀 좌표.</returns>
        public static CellCoord WorldCell(BlueprintRoom room, RoomTemplateDef template, CellCoord localCell)
        {
            CellCoord rotated = RotateLocalCell(localCell, template.WidthCells, template.HeightCells, room.Rotation);
            return new CellCoord(room.Cell.X + rotated.X, room.Cell.Y + rotated.Y);
        }
    }
}
