namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 층 구간 메타(M8) — 블루프린트의 Rooms/Edges 안에서 이 층이 차지하는 연속 구간.
    /// **방 구간 연속성(RoomStart ~ RoomStart+RoomCount)이 층 국소 되감기의 전제 불변식**이라,
    /// 되감기를 켤 때 "마지막 층만" 어서션의 근거가 된다.
    /// </summary>
    public sealed class BlueprintFloor
    {
        public int FloorIndex; // 층 서수(부호)
        public string ThemeId; // 이 층에 적용된 테마 키 — 어댑터 프리팹 세트 조회·해시 폴딩
        public int RoomStart; // Rooms 내 이 층 구간 시작 인덱스
        public int RoomCount; // 이 층 방 수(복도·계단실 포함 — 예산 집계와 다르다)
        public int EdgeStart; // Edges 내 이 층 구간 시작 인덱스(층내 간선만. 수직 간선은 아래 층 구간에 귀속)
        public int EdgeCount; // 이 층 간선 수
        public float CycleRoomPercentAchieved; // 달성한 사이클 소속 방 비율 %(진단값 — 해시 미포함)
        public int ShaftCountAchieved; // 이 층을 관통하는 샤프트 수(진단값)
    }
}
