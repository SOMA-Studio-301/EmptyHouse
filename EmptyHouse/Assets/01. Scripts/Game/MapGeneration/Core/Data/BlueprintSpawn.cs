namespace EmptyHouse.MapGen.Core
{
    /// <summary>스폰 항목(1절 spawns[]) — 마커 참조 + 종류 + 파라미터. 마커 밖 좌표 스폰은 없다(AC-15).</summary>
    public sealed class BlueprintSpawn
    {
        public int RoomIndex; // 방 인덱스
        public int MarkerId; // 방 템플릿 내 마커 Id
        public SpawnKind Kind; // 스폰 종류
        public float WanderRadiusCells; // 좀비 배회 반경(셀) — 좀비 외 항목은 0
        public int KeyNumber; // 열쇠 번호(열쇠_XX ↔ 자물쇠_XX) — Kind 가 Key 가 아니면 0. 어댑터 pairId·툴 라벨 재료
    }
}
