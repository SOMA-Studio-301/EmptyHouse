using Border.Core;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 블루프린트 결정론 해시(AC-02 검증용) — 서버·클라이언트가 같은 시드로 재생성한 블루프린트의
    /// 동일성을 해시 비교로 판정한다. 고정 순회 순서(리스트 인덱스)·고정 포맷으로만 계산한다.
    /// 테스트의 BlueprintDump(문자열 덤프)와 역할 분담 — 런타임 왕복 비교는 32비트 해시 하나로 충분하다.
    /// </summary>
    public static class BlueprintHash
    {
        private const uint FnvOffsetBasis = 2166136261; // FNV-1a 32비트 오프셋
        private const uint FnvPrime = 16777619; // FNV-1a 32비트 소수

        /// <summary>
        /// Rooms/Edges/Spawns 전 필드를 FNV-1a 로 접어 32비트 해시를 만든다.
        /// Meta 는 제외한다(시드·생성기 버전은 별도 필드로 이미 비교 — 파라미터 스냅샷 참조 비교 문제 회피).
        /// </summary>
        /// <param name="blueprint">해시할 블루프린트.</param>
        /// <returns>결정론 32비트 해시.</returns>
        public static uint Compute(MapBlueprint blueprint)
        {
            Log.D("[BlueprintHash] Compute");
            uint hash = FnvOffsetBasis;

            hash = FoldInt(hash, blueprint.Rooms.Count);
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                BlueprintRoom room = blueprint.Rooms[r];
                hash = FoldString(hash, room.TemplateId);
                hash = FoldInt(hash, room.Cell.X);
                hash = FoldInt(hash, room.Cell.Y);
                hash = FoldInt(hash, (int)room.Rotation);
            }

            hash = FoldInt(hash, blueprint.Edges.Count);
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                BlueprintEdge edge = blueprint.Edges[e];
                hash = FoldInt(hash, edge.RoomA);
                hash = FoldInt(hash, edge.SocketA);
                hash = FoldInt(hash, edge.RoomB);
                hash = FoldInt(hash, edge.SocketB);
                hash = FoldInt(hash, (int)edge.State);
                hash = FoldInt(hash, edge.LockNumber);
                hash = FoldInt(hash, (int)edge.LockKind);
            }

            hash = FoldInt(hash, blueprint.Spawns.Count);
            for (int s = 0; s < blueprint.Spawns.Count; s++)
            {
                BlueprintSpawn spawn = blueprint.Spawns[s];
                hash = FoldInt(hash, spawn.RoomIndex);
                hash = FoldInt(hash, spawn.MarkerId);
                hash = FoldInt(hash, (int)spawn.Kind);
                hash = FoldInt(hash, System.BitConverter.SingleToInt32Bits(spawn.WanderRadiusCells)); // 비트 표현 접기 — 부동소수 문자열화 없이 결정론 유지
                hash = FoldInt(hash, spawn.KeyNumber);
            }

            return hash;
        }

        /// <summary>32비트 정수를 리틀엔디언 4바이트로 FNV-1a 접기한다.</summary>
        /// <param name="hash">누적 해시.</param>
        /// <param name="value">접을 값.</param>
        /// <returns>갱신된 해시.</returns>
        private static uint FoldInt(uint hash, int value)
        {
            unchecked
            {
                uint v = (uint)value;
                hash = (hash ^ (v & 0xFF)) * FnvPrime;
                hash = (hash ^ ((v >> 8) & 0xFF)) * FnvPrime;
                hash = (hash ^ ((v >> 16) & 0xFF)) * FnvPrime;
                hash = (hash ^ ((v >> 24) & 0xFF)) * FnvPrime;
                return hash;
            }
        }

        /// <summary>문자열을 UTF-16 코드 유닛(2바이트) 단위로 FNV-1a 접기한다 — 널은 길이 -1 로 구분.</summary>
        /// <param name="hash">누적 해시.</param>
        /// <param name="value">접을 문자열.</param>
        /// <returns>갱신된 해시.</returns>
        private static uint FoldString(uint hash, string value)
        {
            if (value == null)
            {
                return FoldInt(hash, -1);
            }

            hash = FoldInt(hash, value.Length);
            unchecked
            {
                for (int i = 0; i < value.Length; i++)
                {
                    uint c = value[i];
                    hash = (hash ^ (c & 0xFF)) * FnvPrime;
                    hash = (hash ^ ((c >> 8) & 0xFF)) * FnvPrime;
                }
            }

            return hash;
        }
    }
}
