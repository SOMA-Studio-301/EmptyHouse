using System.Collections.Generic;
using Border.Core;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 레이아웃 생성(3절) — "그래프 먼저, 기하 나중". 생성기 v1 = 단일 층(1F).
    /// 버스 입구 앵커에서 열린 소켓에 방을 직결 또는 복도 경유(복도+끝방 원자 배치)로 붙여 트리를 만들고,
    /// 인접 소켓 일부를 뚫어 루프를 더한다. 복도는 연결자로만 배치되어 막다른 끝이 구조적으로 없다.
    /// 총 방 수 예산은 방 전용 집계(복도·입구 앵커 제외).
    /// 회전 규약: Deg90 = 시계방향(North→East). 셀 좌표계는 +X=East, +Y=North.
    /// </summary>
    public sealed class LayoutGenerator
    {
        private readonly List<RoomTemplateDef> placedTemplates = new List<RoomTemplateDef>(); // 방 인덱스 → 사용 템플릿(Rooms 와 정렬)
        private readonly HashSet<long> occupiedCells = new HashSet<long>(); // 점유 셀 집합(충돌 검사 전용 — 열거 금지)
        private readonly HashSet<long> usedSockets = new HashSet<long>(); // 간선에 쓰인 소켓 집합(방<<32|소켓Id)
        private int countableRooms; // 예산 집계 방 수 — 복도·입구 앵커 제외(RoomsTotalMin/Max 판정 기준)
        private readonly List<(int template, int socket)> directCandidates = new List<(int template, int socket)>(); // 직결 후보 재사용 버퍼(호출 빈도 높음 — GC 절감)
        private readonly List<(int template, int socket)> corridorCandidates = new List<(int template, int socket)>(); // 복도 후보 재사용 버퍼
        private readonly List<int> farSockets = new List<int>(); // 복도 원단 소켓 재사용 버퍼

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
            countableRooms = 0;

            if (!TryPlaceEntranceAnchor(templates, blueprint))
            {
                return false;
            }

            if (!TryAttachRooms(rng, genParams, templates, blueprint))
            {
                return false;
            }

            // 루프 하한(AC-07)은 랜덤(비복도) 루프 기준 — 의무 연결(복도 개구)은 예산 밖이다
            int randomLoops = CarveLoopEdges(rng, genParams, blueprint);
            if (randomLoops < genParams.LoopEdgeCountMin)
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
        /// 열린 소켓에 방을 직결 또는 복도 경유로 총 방 수 예산(방 전용 집계)만큼 붙여 트리를 만든다(3절 2).
        /// 확장마다 CorridorLinkPercent 확률로 복도 경유(복도+끝방 원자 배치)를 시도하고, 실패 시 직결로 폴백한다.
        /// MinCount 미달 방 템플릿이 있으면 남은 예산이 미달 합계 이하일 때 그 템플릿만 직결 후보로 강제한다.
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="genParams">생성 파라미터(총 방 수 예산·복도 경유 확률).</param>
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

            // 의무 문 소켓(입구 기존 개구)은 어떤 확장보다 먼저 연결한다 — 실패 시 리롤(X3)
            var frontier = new List<(int room, int socket)>();
            if (!TryConnectMandatoryDoors(rng, templates, usedCount, blueprint, frontier))
            {
                return false;
            }

            // 프런티어 = (방 인덱스, 소켓 배열 인덱스). 입구 앵커의 잔여(미소비) 소켓으로 시작한다.
            for (int s = 0; s < placedTemplates[0].Sockets.Length; s++)
            {
                if (!usedSockets.Contains(SocketKey(0, placedTemplates[0].Sockets[s].Id)))
                {
                    frontier.Add((0, s));
                }
            }

            while (countableRooms < targetRooms && frontier.Count > 0)
            {
                int frontierIndex = rng.Next(frontier.Count);
                (int openRoom, int openSocketIndex) = frontier[frontierIndex];
                SocketDef openSocket = placedTemplates[openRoom].Sockets[openSocketIndex];
                BlueprintRoom openRoomData = blueprint.Rooms[openRoom];
                CellCoord openWorldCell = ToWorldCell(openRoomData, placedTemplates[openRoom], openSocket);
                SocketDirection openWorldDir = CellMath.RotateDirection(openSocket.Direction, openRoomData.Rotation);
                CellCoord targetCell = Step(openWorldCell, openWorldDir);
                SocketDirection neededDir = Opposite(openWorldDir);

                // MinCount 미달분(방 전용)은 남은 예산이 미달 합계 이하로 줄었을 때만 강제한다.
                // 초장부터 강제하면 MinCount 템플릿이 항상 입구 옆 관문 자리를 차지해
                // 배치 다양성이 죽고, HerdArea 파훼 쌍(6절) 같은 "앞 구역" 요구가 구조적으로 깨진다.
                int minCountDeficit = 0;
                for (int t = 0; t < templates.Count; t++)
                {
                    RoomTemplateDef tpl = templates[t];
                    if (!tpl.IsEntranceAnchor && !tpl.IsCorridor && usedCount[t] < tpl.MinCount)
                    {
                        minCountDeficit += tpl.MinCount - usedCount[t];
                    }
                }

                bool unmetOnly = minCountDeficit > 0 && targetRooms - countableRooms <= minCountDeficit;

                // 연결 방식 결정 — 복도 소켓에서의 확장·MinCount 강제 구간은 직결만(복도→복도 체인 금지)
                bool fromCorridor = placedTemplates[openRoom].IsCorridor;
                bool viaCorridor = !fromCorridor && !unmetOnly && rng.Next(100) < genParams.CorridorLinkPercent;

                bool attached = viaCorridor
                    && TryAttachCorridorLink(rng, templates, usedCount, blueprint, frontier, openRoom, openSocket, targetCell, openWorldDir);
                if (!attached)
                {
                    TryAttachDirectRoom(rng, templates, usedCount, unmetOnly, false, blueprint, frontier, openRoom, openSocket, targetCell, neededDir);
                }

                // 성공이면 소켓 소비, 실패면 죽은 소켓 — 어느 쪽이든 프런티어에서 뺀다(실패분은 나중에 봉인)
                frontier.RemoveAt(frontierIndex);
            }

            if (countableRooms < genParams.RoomsTotalMin)
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
        /// 입구 앵커의 의무 문 소켓을 방 직결 + 문 고정으로 연결한다 — 하나라도 실패하면 false(리롤).
        /// 복도 경유를 금지하는 이유: 복도 연결부는 문 금지(개방 통로)라 "항상 문" 요구와 모순이고,
        /// 입구 개구의 문틀 아트는 절단 후 문 프리팹이 전고(4×6m 슬롯)를 채워야 완성된다.
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="templates">템플릿 집합.</param>
        /// <param name="usedCount">템플릿별 사용 횟수(성공 시 갱신).</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="frontier">새 방의 잔여 소켓을 추가할 프런티어.</param>
        /// <returns>의무 소켓 전부 연결 성공 여부.</returns>
        private bool TryConnectMandatoryDoors(DeterministicRng rng, IReadOnlyList<RoomTemplateDef> templates, int[] usedCount, MapBlueprint blueprint, List<(int room, int socket)> frontier)
        {
            RoomTemplateDef entrance = placedTemplates[0];
            BlueprintRoom entranceRoom = blueprint.Rooms[0];
            for (int s = 0; s < entrance.Sockets.Length; s++)
            {
                SocketDef socket = entrance.Sockets[s];
                if (!socket.MandatoryDoor)
                {
                    continue;
                }

                CellCoord worldCell = ToWorldCell(entranceRoom, entrance, socket);
                SocketDirection worldDir = CellMath.RotateDirection(socket.Direction, entranceRoom.Rotation);
                CellCoord targetCell = Step(worldCell, worldDir);
                if (!TryAttachDirectRoom(rng, templates, usedCount, false, true, blueprint, frontier, 0, socket, targetCell, Opposite(worldDir)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 방 템플릿(비복도·비앵커)을 열린 소켓에 직결로 붙인다 — 풋프린트 충돌 시 다른 후보, 소진 시 false.
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="templates">템플릿 집합.</param>
        /// <param name="usedCount">템플릿별 사용 횟수(성공 시 갱신).</param>
        /// <param name="unmetOnly">MinCount 미달 템플릿만 후보로 삼을지.</param>
        /// <param name="forceDoor">간선 상태를 문으로 고정할지(의무 문 소켓 전용).</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="frontier">새 방의 잔여 소켓을 추가할 프런티어.</param>
        /// <param name="openRoom">열린 소켓의 방 인덱스.</param>
        /// <param name="openSocket">열린 소켓.</param>
        /// <param name="targetCell">새 방 소켓이 놓일 월드 셀.</param>
        /// <param name="neededDir">새 방 소켓이 향해야 할 월드 방향.</param>
        /// <returns>부착 성공 여부.</returns>
        private bool TryAttachDirectRoom(DeterministicRng rng, IReadOnlyList<RoomTemplateDef> templates, int[] usedCount, bool unmetOnly, bool forceDoor, MapBlueprint blueprint, List<(int room, int socket)> frontier, int openRoom, SocketDef openSocket, CellCoord targetCell, SocketDirection neededDir)
        {
            directCandidates.Clear();
            for (int t = 0; t < templates.Count; t++)
            {
                RoomTemplateDef template = templates[t];
                if (template.IsEntranceAnchor || template.IsCorridor || usedCount[t] >= template.MaxCount)
                {
                    continue;
                }

                if (unmetOnly && usedCount[t] >= template.MinCount)
                {
                    continue;
                }

                for (int s = 0; s < template.Sockets.Length; s++)
                {
                    directCandidates.Add((t, s));
                }
            }

            Shuffle(rng, directCandidates);

            for (int c = 0; c < directCandidates.Count; c++)
            {
                (int templateIndex, int socketIndex) = directCandidates[c];
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
                AddEdge(rng, blueprint, openRoom, openSocket.Id, newRoom, newSocket.Id, forceDoor);

                for (int s = 0; s < template.Sockets.Length; s++)
                {
                    if (s != socketIndex)
                    {
                        frontier.Add((newRoom, s));
                    }
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// 복도 경유 연결 — 복도 1개와 그 원단(진행 방향) 소켓의 끝방 1개를 원자적으로 배치한다.
        /// 끝방이 어느 원단 소켓에도 못 붙으면 복도를 롤백하고 다음 복도 후보, 전부 소진 시 false(호출자가 직결 폴백).
        /// 이 트랜잭션이 막다른 복도(봉인된 끝)를 구조적으로 차단한다.
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="templates">템플릿 집합.</param>
        /// <param name="usedCount">템플릿별 사용 횟수(성공 시 갱신).</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="frontier">복도·끝방의 잔여 소켓을 추가할 프런티어.</param>
        /// <param name="openRoom">열린 소켓의 방 인덱스.</param>
        /// <param name="openSocket">열린 소켓.</param>
        /// <param name="targetCell">복도 근단 소켓이 놓일 월드 셀.</param>
        /// <param name="openWorldDir">열린 소켓의 월드 방향(연결 진행 방향).</param>
        /// <returns>복도+끝방 배치 성공 여부.</returns>
        private bool TryAttachCorridorLink(DeterministicRng rng, IReadOnlyList<RoomTemplateDef> templates, int[] usedCount, MapBlueprint blueprint, List<(int room, int socket)> frontier, int openRoom, SocketDef openSocket, CellCoord targetCell, SocketDirection openWorldDir)
        {
            SocketDirection neededDir = Opposite(openWorldDir);
            corridorCandidates.Clear();
            for (int t = 0; t < templates.Count; t++)
            {
                RoomTemplateDef template = templates[t];
                if (!template.IsCorridor || usedCount[t] >= template.MaxCount)
                {
                    continue;
                }

                for (int s = 0; s < template.Sockets.Length; s++)
                {
                    corridorCandidates.Add((t, s));
                }
            }

            Shuffle(rng, corridorCandidates);

            for (int c = 0; c < corridorCandidates.Count; c++)
            {
                (int templateIndex, int socketIndex) = corridorCandidates[c];
                RoomTemplateDef corridor = templates[templateIndex];
                SocketDef nearSocket = corridor.Sockets[socketIndex];
                var rotation = (Rotation4)(((int)neededDir - (int)nearSocket.Direction + 4) % 4);
                CellCoord rotatedLocal = CellMath.RotateLocalCell(nearSocket.LocalCell, corridor.WidthCells, corridor.HeightCells, rotation);
                var origin = new CellCoord(targetCell.X - rotatedLocal.X, targetCell.Y - rotatedLocal.Y);

                if (!FitsAt(corridor, origin, rotation))
                {
                    continue;
                }

                int corridorRoom = PlaceRoom(corridor, origin, rotation, blueprint);

                // 원단 소켓 = 근단(源 방향)을 되보지 않는 소켓 전부 — 직선은 반대 단부, 코너는 수직 단부.
                // 전 개구 변 연결 보장은 개구 2변 복도 전제 — 3변 이상(T자)은 X4 가 사전 차단한다(v1 미지원)
                farSockets.Clear();
                for (int s = 0; s < corridor.Sockets.Length; s++)
                {
                    if (s != socketIndex && CellMath.RotateDirection(corridor.Sockets[s].Direction, rotation) != neededDir)
                    {
                        farSockets.Add(s);
                    }
                }

                Shuffle(rng, farSockets);

                for (int f = 0; f < farSockets.Count; f++)
                {
                    SocketDef farSocket = corridor.Sockets[farSockets[f]];
                    SocketDirection farWorldDir = CellMath.RotateDirection(farSocket.Direction, rotation);
                    CellCoord farWorld = ToWorldCell(blueprint.Rooms[corridorRoom], corridor, farSocket);
                    CellCoord roomTarget = Step(farWorld, farWorldDir);
                    if (!TryAttachDirectRoom(rng, templates, usedCount, false, false, blueprint, frontier, corridorRoom, farSocket, roomTarget, Opposite(farWorldDir)))
                    {
                        continue;
                    }

                    usedCount[templateIndex]++;
                    AddEdge(rng, blueprint, openRoom, openSocket.Id, corridorRoom, nearSocket.Id);

                    // 복도의 잔여 개구 소켓도 프런티어에 — 이후 확장은 직결만 허용된다(fromCorridor 게이트)
                    for (int s = 0; s < corridor.Sockets.Length; s++)
                    {
                        if (s != socketIndex && !usedSockets.Contains(SocketKey(corridorRoom, corridor.Sockets[s].Id)))
                        {
                            frontier.Add((corridorRoom, s));
                        }
                    }

                    return true;
                }

                // 끝방 실패 — 복도 롤백(끝이 봉인된 복도를 남기지 않는다)
                RemoveLastRoom(corridor, origin, rotation, blueprint);
            }

            return false;
        }

        /// <summary>연결 간선을 추가하고 양쪽 소켓을 소비 처리한다 — 복도가 한쪽이라도 끼면 항상 개방 통로(복도 연결부에는 문 금지), 의무 문은 문 고정, 그 외 방↔방은 문/통로 랜덤.</summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="roomA">A 방 인덱스.</param>
        /// <param name="socketA">A 소켓 Id.</param>
        /// <param name="roomB">B 방 인덱스.</param>
        /// <param name="socketB">B 소켓 Id.</param>
        /// <param name="forceDoor">문 고정 여부(의무 문 소켓 전용 — 복도 간선에는 적용 불가).</param>
        private void AddEdge(DeterministicRng rng, MapBlueprint blueprint, int roomA, int socketA, int roomB, int socketB, bool forceDoor = false)
        {
            bool corridorInvolved = placedTemplates[roomA].IsCorridor || placedTemplates[roomB].IsCorridor;
            blueprint.Edges.Add(new BlueprintEdge
            {
                RoomA = roomA,
                SocketA = socketA,
                RoomB = roomB,
                SocketB = socketB,
                State = corridorInvolved ? EdgeState.OpenPassage
                    : forceDoor ? EdgeState.DoorOpen
                    : rng.Next(2) == 0 ? EdgeState.DoorOpen : EdgeState.OpenPassage,
                LockNumber = 0,
            });
            usedSockets.Add(SocketKey(roomA, socketA));
            usedSockets.Add(SocketKey(roomB, socketB));
        }

        /// <summary>
        /// 인접했는데 연결 안 된 소켓 쌍을 추가로 뚫어 루프 간선을 만든다(3절 3).
        /// 1단계(의무): 복도 소켓이 포함된 쌍은 전부 연결한다 — 복도 개구는 벽이 없는 단부라
        /// 마주보는 소켓을 봉인하면 물리 씬(뚫린 복도 끝)과 그래프가 어긋난다. 루프 예산에 세지 않는다.
        /// 2단계(랜덤): 남은 비복도 쌍에서 예산(min~max)만큼 선택 — 이 간선이 지름길 자물쇠 후보가 된다(4-2절).
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="genParams">생성 파라미터(루프 간선 min/max).</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <returns>랜덤 단계 채택 수(AC-07 예산 판정용 — 의무 연결 미포함).</returns>
        private int CarveLoopEdges(DeterministicRng rng, MapGenParams genParams, MapBlueprint blueprint)
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

            // 1단계 — 의무 연결: 복도 소켓이 포함된 쌍 전부(수집 순서 = 결정론, 예산 미포함)
            for (int i = 0; i < candidates.Count; i++)
            {
                (int roomA, int socketA, int roomB, int socketB) = candidates[i];
                bool corridorA = placedTemplates[roomA].IsCorridor;
                bool corridorB = placedTemplates[roomB].IsCorridor;
                if (!corridorA && !corridorB)
                {
                    continue;
                }

                if (usedSockets.Contains(SocketKey(roomA, socketA)) || usedSockets.Contains(SocketKey(roomB, socketB)))
                {
                    continue; // 앞선 의무 연결이 이미 소비한 소켓
                }

                AddEdge(rng, blueprint, roomA, socketA, roomB, socketB);
            }

            // 소비되었거나 복도가 낀 쌍 제거 — 랜덤 단계 후보는 전부 비복도 쌍만 남는다
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                (int roomA, int socketA, int roomB, int socketB) = candidates[i];
                if (placedTemplates[roomA].IsCorridor || placedTemplates[roomB].IsCorridor
                    || usedSockets.Contains(SocketKey(roomA, socketA)) || usedSockets.Contains(SocketKey(roomB, socketB)))
                {
                    candidates.RemoveAt(i);
                }
            }

            // 2단계 — 랜덤 예산 채택
            int loopTarget = rng.Next(genParams.LoopEdgeCountMin, genParams.LoopEdgeCountMax + 1);
            int carved = 0;
            while (carved < loopTarget && candidates.Count > 0)
            {
                int pick = rng.Next(candidates.Count);
                (int roomA, int socketA, int roomB, int socketB) = candidates[pick];
                AddEdge(rng, blueprint, roomA, socketA, roomB, socketB);
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

            return carved;
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
            if (!template.IsCorridor && !template.IsEntranceAnchor)
            {
                countableRooms++;
            }

            return blueprint.Rooms.Count - 1;
        }

        /// <summary>마지막으로 배치한 방을 롤백한다 — 점유 셀·목록·방 수 집계 원복(복도 경유 트랜잭션 실패 전용, 간선 추가 전에만 호출 가능).</summary>
        /// <param name="template">롤백할 템플릿(마지막 배치분).</param>
        /// <param name="origin">배치 시 풋프린트 원점(셀).</param>
        /// <param name="rotation">배치 시 회전.</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        private void RemoveLastRoom(RoomTemplateDef template, CellCoord origin, Rotation4 rotation, MapBlueprint blueprint)
        {
            (int width, int height) = CellMath.RotatedSize(template.WidthCells, template.HeightCells, rotation);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    occupiedCells.Remove(CellKey(origin.X + x, origin.Y + y));
                }
            }

            blueprint.Rooms.RemoveAt(blueprint.Rooms.Count - 1);
            placedTemplates.RemoveAt(placedTemplates.Count - 1);
            if (!template.IsCorridor && !template.IsEntranceAnchor)
            {
                countableRooms--;
            }
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
