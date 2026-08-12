using System.Collections.Generic;
using Border.Core;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 탈출구(Door-Return) 배치 — 입구(방 0 = 버스 쪽)와 별개인 "맵 어딘가의 출구"(세션루프 0-3 귀환).
    /// 가장 깊은 잎 방(연결 간선 1개·비복도) 을 ReturnExitCount 개 골라, 각 방의 봉인 소켓 중
    /// <b>맞은편 셀이 빈 공간인 것만</b> 탈출문으로 전환한다(EdgeState.ReturnExit) — 문 너머는 맵 바깥이라
    /// 다른 방으로 뚫릴 위험이 없다. 문은 열리지 않고 홀드 즉시 탈출이라 너머가 비어 있어도 무방하다.
    /// 잎 방·유효 소켓이 모자라면 있는 만큼만 배치한다(검증기가 X6 경고).
    /// </summary>
    public static class ReturnExitPlacer
    {
        /// <summary>
        /// 탈출문 간선을 전환한다 — 깊이 내림차순 잎 방에서 유효 소켓(바깥 향 봉인)이 있는 방만 채택.
        /// </summary>
        /// <param name="genParams">생성 파라미터(탈출구 개수).</param>
        /// <param name="blueprint">대상 블루프린트(레이아웃 완료 상태).</param>
        /// <param name="templates">템플릿 집합.</param>
        /// <param name="depths">입구 기준 방 깊이(DangerGradeCalculator).</param>
        /// <returns>전환한 탈출문 수.</returns>
        public static int Place(MapGenParams genParams, MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, int[] depths)
        {
            Log.D("[ReturnExitPlacer] Place");

            // 탈출문은 입구 층 한정(M9-6) — 위·아래 층의 바깥은 공중/지하라 탈출 동선이 성립하지 않는다.
            // 점유 집합도 입구 층 방만 모은다(층별 격자 분리 — 타 층 풋프린트가 이 층의 빈 공간 판정을 오염시키면 안 된다)
            int entranceFloor = blueprint.Rooms[0].FloorIndex;
            var occupied = new HashSet<long>();
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                if (blueprint.Rooms[r].FloorIndex != entranceFloor)
                {
                    continue;
                }

                RoomTemplateDef template = FindTemplate(templates, blueprint.Rooms[r].TemplateId);
                (int width, int height) = CellMath.RotatedSize(template.WidthCells, template.HeightCells, blueprint.Rooms[r].Rotation);
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        occupied.Add(CellKey(blueprint.Rooms[r].Cell.X + x, blueprint.Rooms[r].Cell.Y + y));
                    }
                }
            }

            // 방별 연결 차수 — 잎 방 = 차수 1(비복도·비입구)
            var degrees = new int[blueprint.Rooms.Count];
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                BlueprintEdge edge = blueprint.Edges[e];
                if (edge.RoomB < 0 || edge.State == EdgeState.BlockedWall)
                {
                    continue;
                }

                degrees[edge.RoomA]++;
                degrees[edge.RoomB]++;
            }

            // 잎 방 후보 — 입구 층 한정(M9-6)·깊이 내림차순, 동률은 방 인덱스 오름차순(결정론)
            var leaves = new List<int>();
            for (int r = 1; r < blueprint.Rooms.Count; r++)
            {
                if (blueprint.Rooms[r].FloorIndex != entranceFloor)
                {
                    continue;
                }

                RoomTemplateDef template = FindTemplate(templates, blueprint.Rooms[r].TemplateId);
                if (!template.IsCorridor && !template.IsEntranceAnchor && degrees[r] == 1 && depths[r] >= 0)
                {
                    leaves.Add(r);
                }
            }

            leaves.Sort((a, b) => depths[a] != depths[b] ? depths[b].CompareTo(depths[a]) : a.CompareTo(b));

            int placed = 0;
            for (int i = 0; i < leaves.Count && placed < genParams.ReturnExitCount; i++)
            {
                if (TryConvertOuterSeal(blueprint, templates, occupied, leaves[i]))
                {
                    placed++;
                }
            }

            Log.D($"[ReturnExitPlacer] 탈출문 {placed}/{genParams.ReturnExitCount} 개 배치(잎 방 후보 {leaves.Count})");
            return placed;
        }

        /// <summary>
        /// 방의 봉인 간선 중 맞은편 셀이 빈 공간인 첫 소켓(소켓 Id 오름차순)을 탈출문으로 전환한다.
        /// 맞은편이 다른 방·복도면 건너뛴다 — 뚫으면 그래프에 없는 연결이 생긴다.
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="templates">템플릿 집합.</param>
        /// <param name="occupied">점유 셀 집합.</param>
        /// <param name="room">대상 방 인덱스.</param>
        /// <returns>전환 성공 여부.</returns>
        private static bool TryConvertOuterSeal(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, HashSet<long> occupied, int room)
        {
            RoomTemplateDef template = FindTemplate(templates, blueprint.Rooms[room].TemplateId);
            int bestEdge = -1;
            int bestSocket = int.MaxValue;
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                BlueprintEdge edge = blueprint.Edges[e];
                if (edge.RoomA != room || edge.RoomB >= 0 || edge.State != EdgeState.BlockedWall || edge.SocketA >= bestSocket)
                {
                    continue;
                }

                SocketDef socket = FindSocket(template, edge.SocketA);
                CellCoord worldCell = CellMath.WorldCell(blueprint.Rooms[room], template, socket.LocalCell);
                SocketDirection dir = CellMath.RotateDirection(socket.Direction, blueprint.Rooms[room].Rotation);
                CellCoord facing = Step(worldCell, dir);
                if (occupied.Contains(CellKey(facing.X, facing.Y)))
                {
                    continue; // 맞은편이 다른 방·복도 — 대체 금지
                }

                bestEdge = e;
                bestSocket = edge.SocketA;
            }

            if (bestEdge < 0)
            {
                return false;
            }

            blueprint.Edges[bestEdge].State = EdgeState.ReturnExit;
            return true;
        }

        /// <summary>템플릿 ID 로 템플릿을 찾는다.</summary>
        /// <param name="templates">템플릿 집합.</param>
        /// <param name="templateId">찾을 ID.</param>
        /// <returns>템플릿(없으면 null).</returns>
        private static RoomTemplateDef FindTemplate(IReadOnlyList<RoomTemplateDef> templates, string templateId)
        {
            for (int t = 0; t < templates.Count; t++)
            {
                if (templates[t].TemplateId == templateId)
                {
                    return templates[t];
                }
            }

            return null;
        }

        /// <summary>템플릿에서 소켓 Id 로 소켓을 찾는다.</summary>
        /// <param name="template">대상 템플릿.</param>
        /// <param name="socketId">소켓 Id.</param>
        /// <returns>소켓(없으면 null).</returns>
        private static SocketDef FindSocket(RoomTemplateDef template, int socketId)
        {
            for (int s = 0; s < template.Sockets.Length; s++)
            {
                if (template.Sockets[s].Id == socketId)
                {
                    return template.Sockets[s];
                }
            }

            return null;
        }

        /// <summary>방향으로 한 칸 이동한 셀(+X=East, +Y=North).</summary>
        /// <param name="cell">기준 셀.</param>
        /// <param name="direction">이동 방향.</param>
        /// <returns>이동한 셀.</returns>
        private static CellCoord Step(CellCoord cell, SocketDirection direction)
        {
            switch (direction)
            {
                case SocketDirection.North: return new CellCoord(cell.X, cell.Y + 1);
                case SocketDirection.East: return new CellCoord(cell.X + 1, cell.Y);
                case SocketDirection.South: return new CellCoord(cell.X, cell.Y - 1);
                default: return new CellCoord(cell.X - 1, cell.Y);
            }
        }

        /// <summary>셀 좌표 → 점유 집합 키.</summary>
        /// <param name="x">셀 X.</param>
        /// <param name="y">셀 Y.</param>
        /// <returns>64비트 키.</returns>
        private static long CellKey(int x, int y)
        {
            return ((long)x << 32) ^ (uint)y;
        }
    }
}
