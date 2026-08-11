using System.Globalization;
using System.Text;
using Border.Core;

namespace EmptyHouse.MapGen.Core.Tests
{
    /// <summary>
    /// 블루프린트 → 정규화 문자열 덤프 — 결정론 비교(AC-01·AC-02 근거) 전용 테스트 헬퍼.
    /// 같은 블루프린트는 항상 같은 문자열이 되도록 고정 포맷·고정 순서(리스트 인덱스 순)로 직렬화한다.
    /// </summary>
    public static class BlueprintDump
    {
        /// <summary>
        /// 블루프린트 전체(meta 제외한 rooms/edges/spawns)를 정규화 문자열로 덤프한다.
        /// 두 생성 결과의 완전 동일성은 이 문자열 비교로 판정한다.
        /// 줄바꿈은 \n 고정, 실수는 InvariantCulture — 플랫폼·로케일 무관 동일 문자열 보장.
        /// </summary>
        /// <param name="blueprint">덤프할 블루프린트.</param>
        /// <returns>정규화 덤프 문자열.</returns>
        public static string Dump(MapBlueprint blueprint)
        {
            Log.D("[BlueprintDump] Dump");
            var sb = new StringBuilder();

            for (int i = 0; i < blueprint.Rooms.Count; i++)
            {
                BlueprintRoom room = blueprint.Rooms[i];
                sb.Append("room ").Append(i)
                  .Append('|').Append(room.TemplateId)
                  .Append('|').Append(room.Cell.X).Append(',').Append(room.Cell.Y)
                  .Append('|').Append(room.Rotation)
                  .Append("|f").Append(room.FloorIndex) // 층 서수(M9-2) — 층 1개 구성은 전부 0이라 골든 안정
                  .Append('\n');
            }

            for (int i = 0; i < blueprint.Edges.Count; i++)
            {
                BlueprintEdge edge = blueprint.Edges[i];
                sb.Append("edge ").Append(i)
                  .Append('|').Append(edge.RoomA).Append('.').Append(edge.SocketA)
                  .Append('-').Append(edge.RoomB).Append('.').Append(edge.SocketB)
                  .Append('|').Append(edge.State)
                  .Append("|lock=").Append(edge.LockNumber).Append('.').Append(edge.LockKind)
                  .Append('\n');
            }

            for (int i = 0; i < blueprint.Spawns.Count; i++)
            {
                BlueprintSpawn spawn = blueprint.Spawns[i];
                sb.Append("spawn ").Append(i)
                  .Append("|room=").Append(spawn.RoomIndex)
                  .Append("|marker=").Append(spawn.MarkerId)
                  .Append('|').Append(spawn.Kind)
                  .Append("|wander=").Append(spawn.WanderRadiusCells.ToString("0.###", CultureInfo.InvariantCulture))
                  .Append("|key=").Append(spawn.KeyNumber)
                  .Append('\n');
            }

            return sb.ToString();
        }
    }
}
