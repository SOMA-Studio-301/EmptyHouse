using System.Collections.Generic;
using Border.Core;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 레이아웃 생성(3절) — "그래프 먼저, 기하 나중". 생성기 v1 = 단일 층(1F).
    /// 버스 입구 앵커에서 열린 소켓에 템플릿을 붙여 트리를 만들고, 인접 소켓 일부를 뚫어 루프를 더한다.
    /// 회전 규약: Deg90 = 시계방향(North→East). 셀 좌표계는 +X=East, +Y=North.
    /// </summary>
    public sealed class LayoutGenerator
    {
        private readonly List<RoomTemplateDef> placedTemplates = new List<RoomTemplateDef>(); // 방 인덱스 → 사용 템플릿(Rooms 와 정렬)
        private readonly HashSet<long> occupiedCells = new HashSet<long>(); // 점유 셀 집합(충돌 검사 전용 — 열거 금지)
        private readonly HashSet<long> usedSockets = new HashSet<long>(); // 간선에 쓰인 소켓 집합(방<<32|소켓Id)

        /// <summary>
        /// 방 배치와 간선(트리 + 루프)을 생성해 blueprint 의 Rooms/Edges 를 채운다.
        /// 조립 후보 소진·최소 등장 횟수 미달 시 false — 호출자(MapGenerator)가 리롤한다(X3).
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="genParams">생성 파라미터.</param>
        /// <param name="templates">템플릿 집합.</param>
        /// <param name="blueprint">Rooms/Edges 를 채울 대상 블루프린트.</param>
        /// <returns>레이아웃 완성 여부.</returns>
        public bool TryGenerate(DeterministicRng rng, MapGenParams genParams, IReadOnlyList<RoomTemplateDef> templates, MapBlueprint blueprint)
        {
            Log.D("[LayoutGenerator] TryGenerate");
            placedTemplates.Clear();
            occupiedCells.Clear();
            usedSockets.Clear();

            if (!TryPlaceEntranceAnchor(templates, blueprint))
            {
                return false;
            }

            if (!TryAttachRooms(rng, genParams, templates, blueprint))
            {
                return false;
            }

            CarveLoopEdges(rng, genParams, blueprint);

            // CarveLoopEdges 는 void — 루프 하한(AC-07)은 여기서 판정한다. 봉인 전이라 모든 간선이 연결 간선이다.
            int loopCount = blueprint.Edges.Count - (blueprint.Rooms.Count - 1);
            if (loopCount < genParams.LoopEdgeCountMin)
            {
                return false;
            }

            SealRemainingSockets(blueprint);
            return true;
        }

        /// <summary>버스 입구 고정 모듈을 그리드 고정 앵커(방 0)로 배치한다(3절 1).</summary>
        /// <param name="templates">템플릿 집합(IsEntranceAnchor 템플릿 필수).</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <returns>앵커 배치 성공 여부.</returns>
        private bool TryPlaceEntranceAnchor(IReadOnlyList<RoomTemplateDef> templates, MapBlueprint blueprint)
        {
            Log.D("[LayoutGenerator] TryPlaceEntranceAnchor");
            for (int i = 0; i < templates.Count; i++)
            {
                if (templates[i].IsEntranceAnchor)
                {
                    PlaceRoom(templates[i], new CellCoord(0, 0), Rotation4.Deg0, blueprint);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 열린 소켓에 방/복도 템플릿을 총 방 수 예산만큼 붙여 트리를 만든다(3절 2).
        /// 풋프린트 충돌 시 다른 후보를 시도하고, 후보 소진 시 false.
        /// MinCount 미달 템플릿이 있으면 그 템플릿만 후보로 삼아 최소 등장을 먼저 채운다.
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="genParams">생성 파라미터(총 방 수 예산).</param>
        /// <param name="templates">템플릿 집합.</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <returns>트리 조립 성공 여부.</returns>
        private bool TryAttachRooms(DeterministicRng rng, MapGenParams genParams, IReadOnlyList<RoomTemplateDef> templates, MapBlueprint blueprint)
        {
            Log.D("[LayoutGenerator] TryAttachRooms");
            int targetRooms = rng.Next(genParams.RoomsTotalMin, genParams.RoomsTotalMax + 1);

            var usedCount = new int[templates.Count];
            for (int i = 0; i < templates.Count; i++)
            {
                if (templates[i].IsEntranceAnchor)
                {
                    usedCount[i] = 1;
                }
            }

            // 프런티어 = (방 인덱스, 소켓 배열 인덱스). 입구 앵커의 소켓으로 시작한다.
            var frontier = new List<(int room, int socket)>();
            for (int s = 0; s < placedTemplates[0].Sockets.Length; s++)
            {
                frontier.Add((0, s));
            }

            var candidates = new List<(int template, int socket)>();
            while (blueprint.Rooms.Count < targetRooms && frontier.Count > 0)
            {
                int frontierIndex = rng.Next(frontier.Count);
                (int openRoom, int openSocketIndex) = frontier[frontierIndex];
                SocketDef openSocket = placedTemplates[openRoom].Sockets[openSocketIndex];
                BlueprintRoom openRoomData = blueprint.Rooms[openRoom];
                CellCoord openWorldCell = ToWorldCell(openRoomData, placedTemplates[openRoom], openSocket);
                SocketDirection openWorldDir = CellMath.RotateDirection(openSocket.Direction, openRoomData.Rotation);
                CellCoord targetCell = Step(openWorldCell, openWorldDir);
                SocketDirection neededDir = Opposite(openWorldDir);

                // 후보 수집 — MinCount 미달분은 남은 예산이 미달 합계 이하로 줄었을 때만 강제한다.
                // 초장부터 강제하면 MinCount 템플릿이 항상 입구 옆 관문 자리를 차지해
                // 배치 다양성이 죽고, HerdArea 파훼 쌍(6절) 같은 "앞 구역" 요구가 구조적으로 깨진다.
                candidates.Clear();
                int minCountDeficit = 0;
                for (int t = 0; t < templates.Count; t++)
                {
                    if (!templates[t].IsEntranceAnchor && usedCount[t] < templates[t].MinCount)
                    {
                        minCountDeficit += templates[t].MinCount - usedCount[t];
                    }
                }

                bool unmetOnly = minCountDeficit > 0 && targetRooms - blueprint.Rooms.Count <= minCountDeficit;

                for (int t = 0; t < templates.Count; t++)
                {
                    RoomTemplateDef template = templates[t];
                    if (template.IsEntranceAnchor || usedCount[t] >= template.MaxCount)
                    {
                        continue;
                    }

                    if (unmetOnly && usedCount[t] >= template.MinCount)
                    {
                        continue;
                    }

                    for (int s = 0; s < template.Sockets.Length; s++)
                    {
                        candidates.Add((t, s));
                    }
                }

                Shuffle(rng, candidates);

                bool attached = false;
                for (int c = 0; c < candidates.Count && !attached; c++)
                {
                    (int templateIndex, int socketIndex) = candidates[c];
                    RoomTemplateDef template = templates[templateIndex];
                    SocketDef newSocket = template.Sockets[socketIndex];

                    // 회전은 소켓 방향 맞대기로 유도된다(neededDir 를 향하도록 90도 단위 회전)
                    var rotation = (Rotation4)(((int)neededDir - (int)newSocket.Direction + 4) % 4);
                    CellCoord rotatedLocal = CellMath.RotateLocalCell(newSocket.LocalCell, template.WidthCells, template.HeightCells, rotation);
                    var origin = new CellCoord(targetCell.X - rotatedLocal.X, targetCell.Y - rotatedLocal.Y);

                    if (!FitsAt(template, origin, rotation))
                    {
                        continue;
                    }

                    int newRoom = PlaceRoom(template, origin, rotation, blueprint);
                    usedCount[templateIndex]++;

                    // 복도↔복도는 항상 개방 통로 — 복도 단부에는 문틀이 없다(문 배치 불가 물리 제약)
                    bool corridorPair = placedTemplates[openRoom].IsCorridor && template.IsCorridor;
                    blueprint.Edges.Add(new BlueprintEdge
                    {
                        RoomA = openRoom,
                        SocketA = openSocket.Id,
                        RoomB = newRoom,
                        SocketB = newSocket.Id,
                        State = corridorPair ? EdgeState.OpenPassage : (rng.Next(2) == 0 ? EdgeState.DoorOpen : EdgeState.OpenPassage),
                        LockNumber = 0,
                    });
                    usedSockets.Add(SocketKey(openRoom, openSocket.Id));
                    usedSockets.Add(SocketKey(newRoom, newSocket.Id));

                    for (int s = 0; s < template.Sockets.Length; s++)
                    {
                        if (s != socketIndex)
                        {
                            frontier.Add((newRoom, s));
                        }
                    }

                    attached = true;
                }

                // 성공이면 소켓 소비, 실패면 죽은 소켓 — 어느 쪽이든 프런티어에서 뺀다(실패분은 나중에 봉인)
                frontier.RemoveAt(frontierIndex);
            }

            if (blueprint.Rooms.Count < genParams.RoomsTotalMin)
            {
                return false;
            }

            for (int t = 0; t < templates.Count; t++)
            {
                if (usedCount[t] < templates[t].MinCount)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 인접했는데 연결 안 된 소켓 쌍 일부를 추가로 뚫어 루프 간선을 만든다(3절 3).
        /// 이 루프 간선이 지름길 자물쇠 후보가 된다(4-2절). 간선 수는 파라미터 범위 안(AC-07).
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="genParams">생성 파라미터(루프 간선 min/max).</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        private void CarveLoopEdges(DeterministicRng rng, MapGenParams genParams, MapBlueprint blueprint)
        {
            Log.D("[LayoutGenerator] CarveLoopEdges");

            // 후보 전수 수집 — 방·소켓 인덱스 순(결정론)
            var candidates = new List<(int roomA, int socketA, int roomB, int socketB)>();
            for (int a = 0; a < blueprint.Rooms.Count; a++)
            {
                RoomTemplateDef templateA = placedTemplates[a];
                for (int sa = 0; sa < templateA.Sockets.Length; sa++)
                {
                    SocketDef socketA = templateA.Sockets[sa];
                    if (usedSockets.Contains(SocketKey(a, socketA.Id)))
                    {
                        continue;
                    }

                    CellCoord worldA = ToWorldCell(blueprint.Rooms[a], templateA, socketA);
                    SocketDirection dirA = CellMath.RotateDirection(socketA.Direction, blueprint.Rooms[a].Rotation);
                    CellCoord target = Step(worldA, dirA);
                    SocketDirection neededDir = Opposite(dirA);

                    for (int b = a + 1; b < blueprint.Rooms.Count; b++)
                    {
                        RoomTemplateDef templateB = placedTemplates[b];
                        for (int sb = 0; sb < templateB.Sockets.Length; sb++)
                        {
                            SocketDef socketB = templateB.Sockets[sb];
                            if (usedSockets.Contains(SocketKey(b, socketB.Id)))
                            {
                                continue;
                            }

                            CellCoord worldB = ToWorldCell(blueprint.Rooms[b], templateB, socketB);
                            if (worldB.X == target.X && worldB.Y == target.Y
                                && CellMath.RotateDirection(socketB.Direction, blueprint.Rooms[b].Rotation) == neededDir)
                            {
                                candidates.Add((a, socketA.Id, b, socketB.Id));
                            }
                        }
                    }
                }
            }

            int loopTarget = rng.Next(genParams.LoopEdgeCountMin, genParams.LoopEdgeCountMax + 1);
            int carved = 0;
            while (carved < loopTarget && candidates.Count > 0)
            {
                int pick = rng.Next(candidates.Count);
                (int roomA, int socketA, int roomB, int socketB) = candidates[pick];

                // 복도↔복도는 항상 개방 통로 — 복도 단부에는 문틀이 없다(TryAttachRooms 와 동일 규칙)
                bool corridorPair = placedTemplates[roomA].IsCorridor && placedTemplates[roomB].IsCorridor;
                blueprint.Edges.Add(new BlueprintEdge
                {
                    RoomA = roomA,
                    SocketA = socketA,
                    RoomB = roomB,
                    SocketB = socketB,
                    State = corridorPair ? EdgeState.OpenPassage : (rng.Next(2) == 0 ? EdgeState.DoorOpen : EdgeState.OpenPassage),
                    LockNumber = 0,
                });
                usedSockets.Add(SocketKey(roomA, socketA));
                usedSockets.Add(SocketKey(roomB, socketB));
                carved++;

                // 방금 소비한 소켓을 공유하는 후보 제거(뒤에서부터 — 인덱스 안정)
                for (int i = candidates.Count - 1; i >= 0; i--)
                {
                    (int ca, int csa, int cb, int csb) = candidates[i];
                    if ((ca == roomA && csa == socketA) || (cb == roomB && csb == socketB)
                        || (ca == roomB && csa == socketB) || (cb == roomA && csb == socketA))
                    {
                        candidates.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>남은 열린 소켓 전부를 막힌 벽으로 봉인한다(3절 3 — 빈 소켓 0, AC-05).</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        private void SealRemainingSockets(MapBlueprint blueprint)
        {
            Log.D("[LayoutGenerator] SealRemainingSockets");
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                RoomTemplateDef template = placedTemplates[r];
                for (int s = 0; s < template.Sockets.Length; s++)
                {
                    int socketId = template.Sockets[s].Id;
                    if (usedSockets.Contains(SocketKey(r, socketId)))
                    {
                        continue;
                    }

                    blueprint.Edges.Add(new BlueprintEdge
                    {
                        RoomA = r,
                        SocketA = socketId,
                        RoomB = -1,
                        SocketB = -1,
                        State = EdgeState.BlockedWall,
                        LockNumber = 0,
                    });
                    usedSockets.Add(SocketKey(r, socketId));
                }
            }
        }

        /// <summary>방을 블루프린트에 추가하고 풋프린트 셀을 점유 처리한다.</summary>
        /// <param name="template">배치할 템플릿.</param>
        /// <param name="origin">회전 적용 후 풋프린트 원점(셀).</param>
        /// <param name="rotation">회전.</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <returns>새 방 인덱스.</returns>
        private int PlaceRoom(RoomTemplateDef template, CellCoord origin, Rotation4 rotation, MapBlueprint blueprint)
        {
            (int width, int height) = CellMath.RotatedSize(template.WidthCells, template.HeightCells, rotation);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    occupiedCells.Add(CellKey(origin.X + x, origin.Y + y));
                }
            }

            blueprint.Rooms.Add(new BlueprintRoom { TemplateId = template.TemplateId, Cell = origin, Rotation = rotation });
            placedTemplates.Add(template);
            return blueprint.Rooms.Count - 1;
        }

        /// <summary>해당 원점·회전으로 템플릿 풋프린트가 기존 점유와 겹치지 않는지 검사한다.</summary>
        /// <param name="template">검사할 템플릿.</param>
        /// <param name="origin">풋프린트 원점(셀).</param>
        /// <param name="rotation">회전.</param>
        /// <returns>배치 가능 여부.</returns>
        private bool FitsAt(RoomTemplateDef template, CellCoord origin, Rotation4 rotation)
        {
            (int width, int height) = CellMath.RotatedSize(template.WidthCells, template.HeightCells, rotation);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (occupiedCells.Contains(CellKey(origin.X + x, origin.Y + y)))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>소켓의 월드 셀 좌표(방 원점 + 회전 적용 로컬 셀) — CellMath.WorldCell 위임.</summary>
        /// <param name="room">배치된 방.</param>
        /// <param name="template">방 템플릿.</param>
        /// <param name="socket">대상 소켓.</param>
        /// <returns>소켓 월드 셀.</returns>
        private static CellCoord ToWorldCell(BlueprintRoom room, RoomTemplateDef template, SocketDef socket)
        {
            return CellMath.WorldCell(room, template, socket.LocalCell);
        }

        /// <summary>반대 방향.</summary>
        /// <param name="direction">기준 방향.</param>
        /// <returns>180도 반대 방향.</returns>
        private static SocketDirection Opposite(SocketDirection direction)
        {
            return (SocketDirection)(((int)direction + 2) % 4);
        }

        /// <summary>방향의 단위 셀 벡터를 더한 셀(+X=East, +Y=North).</summary>
        /// <param name="cell">기준 셀.</param>
        /// <param name="direction">이동 방향.</param>
        /// <returns>한 칸 이동한 셀.</returns>
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

        /// <summary>리스트를 Fisher-Yates 로 제자리 셔플한다(단일 rng 스트림).</summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="list">셔플 대상.</param>
        private static void Shuffle<T>(DeterministicRng rng, List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
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

        /// <summary>(방, 소켓 Id) → 사용 소켓 집합 키.</summary>
        /// <param name="room">방 인덱스.</param>
        /// <param name="socketId">소켓 Id.</param>
        /// <returns>64비트 키.</returns>
        private static long SocketKey(int room, int socketId)
        {
            return ((long)room << 32) | (uint)socketId;
        }
    }
}
