namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 잠긴 문의 자물쇠 면(접근측) 선택 규칙 — 입구 홉이 얕은 방(동률은 RoomA).
    /// 자물쇠는 한 면에만 스폰되므로, 스포너(스폰 위치)와 검증기(면 도달성 패스 6)가
    /// 반드시 이 규칙 하나를 공유해야 한다 — 규칙이 갈라지면 검증이 통과한 면과 다른 면에 스폰되는 사고가 난다.
    /// </summary>
    public static class LockFaceRule
    {
        /// <summary>
        /// 자물쇠 면이 향할 방을 고른다 — 입구 홉 거리가 얕은 쪽(동률은 RoomA).
        /// </summary>
        /// <param name="edge">잠긴 간선(DoorLocked).</param>
        /// <param name="hopDistances">DangerGradeCalculator.ComputeHopDistances 결과(잠긴 문 통과 기준).</param>
        /// <returns>자물쇠 면 방 인덱스.</returns>
        public static int FaceRoom(BlueprintEdge edge, int[] hopDistances)
        {
            return hopDistances[edge.RoomA] <= hopDistances[edge.RoomB] ? edge.RoomA : edge.RoomB;
        }
    }
}
