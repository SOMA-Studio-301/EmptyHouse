using System.Collections.Generic;
using Border.Core;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 레이아웃 생성(3절) — "그래프 먼저, 기하 나중". 생성기 v1 = 단일 층(1F).
    /// 버스 입구 앵커에서 열린 소켓에 방을 직결 또는 복도 경유(복도+끝방 원자 배치)로 붙여 트리를 만들고,
    /// 인접 소켓 일부를 뚫어 루프를 더한다. 복도는 연결자로만 배치되어 막다른 끝이 구조적으로 없다.
    /// 총 방 수 예산은 방 전용 집계(복도·입구 앵커 제외).
    /// 입구 앵커의 소켓 없는 변 바깥은 확보 대역 — 어떤 풋프린트도 배치 금지(집 밖 = 버스·진입로 영역).
    /// 회전 규약: Deg90 = 시계방향(North→East). 셀 좌표계는 +X=East, +Y=North.
    /// </summary>
    public sealed class LayoutGenerator
    {
        private readonly List<RoomTemplateDef> placedTemplates = new List<RoomTemplateDef>(); // 방 인덱스 → 사용 템플릿(Rooms 와 정렬)
        private readonly Dictionary<int, HashSet<long>> occupiedByFloor = new Dictionary<int, HashSet<long>>(); // 층 서수 → 점유 셀 집합(M9-4 — 층별 격자 분리)
        private HashSet<long> occupiedCells = new HashSet<long>(); // **현재 층** 점유 셀(층 시작 시 occupiedByFloor 의 해당 집합으로 교체 — FitsAt/PlaceRoom 은 무수정, 충돌 검사 전용·열거 금지)
        private readonly HashSet<long> usedSockets = new HashSet<long>(); // 간선에 쓰인 소켓 집합(방<<32|소켓Id)
        private int countableRooms; // **현재 층** 예산 집계 방 수 — 복도·입구 앵커 제외(층 시작마다 리셋)
        private int currentFloorIndex; // 지금 생성 중인 층 서수(배치 방의 FloorIndex 원천)
        private int currentFlatBase; // 현재 층 템플릿의 평탄화 테이블 시작 인덱스(TemplateIndex 계산용)
        private readonly List<(int template, int socket)> directCandidates = new List<(int template, int socket)>(); // 직결 후보 재사용 버퍼(호출 빈도 높음 — GC 절감)
        private readonly List<(int template, int socket)> corridorCandidates = new List<(int template, int socket)>(); // 복도 후보 재사용 버퍼
        private readonly List<int> farSockets = new List<int>(); // 복도 원단 소켓 재사용 버퍼
        private readonly List<ChainSegment> chainSegments = new List<ChainSegment>(); // 복도 연쇄 세그먼트 재사용 버퍼(커밋·롤백 기록)
        private readonly List<KeepOutStrip> entranceKeepOut = new List<KeepOutStrip>(); // 입구 바깥 확보 대역(소켓 없는 변 너머 — 어떤 풋프린트도 금지)

        /// <summary>v1 호환 진입점 — 층 1개 Plan 을 합성해 위임한다(결과는 v1 과 동일 — 골든 게이트).</summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="genParams">생성 파라미터.</param>
        /// <param name="templates">템플릿 집합.</param>
        /// <param name="blueprint">Rooms/Edges 를 채울 대상 블루프린트.</param>
        /// <returns>레이아웃 완성 여부.</returns>
        public bool TryGenerate(DeterministicRng rng, MapGenParams genParams, IReadOnlyList<RoomTemplateDef> templates, MapBlueprint blueprint)
        {
            return TryGenerate(rng, MapGenPlan.FromLegacy(genParams, templates), blueprint);
        }

        /// <summary>
        /// 층 순서대로 방 배치와 간선(트리 + 루프)을 생성해 blueprint 의 Rooms/Edges/Floors 를 채운다(M9-4).
        /// 조립 후보 소진·최소 등장 횟수 미달 시 false — 호출자(MapGenerator)가 리롤한다(X3).
        /// 층별 점유 격자·예산·사이클 목표는 분리되고, 층 하나가 끝날 때마다 그 층의 잔여 소켓을 봉인한다 —
        /// 다음 층 처리 시 이전 층에 미사용 소켓이 없어 후보 수집이 자연히 현재 층으로 한정된다.
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="plan">생성 계획.</param>
        /// <param name="blueprint">Rooms/Edges/Floors 를 채울 대상 블루프린트.</param>
        /// <returns>레이아웃 완성 여부.</returns>
        public bool TryGenerate(DeterministicRng rng, MapGenPlan plan, MapBlueprint blueprint)
        {
            placedTemplates.Clear();
            occupiedByFloor.Clear();
            usedSockets.Clear();
            entranceKeepOut.Clear();

            int[] order = FloorSequencer.Order(plan); // rng 미소비 — 층 1개 구성에서도 스트림 불변(절대 규칙 3)
            for (int i = 0; i < order.Length; i++)
            {
                if (!TryGenerateFloor(rng, plan, order[i], blueprint))
                {
                    return false;
                }
            }

            // 전역 사이클 달성률 — v1 과 같은 의미(층 1개면 층 값과 동일). 층별 값은 BlueprintFloor 소관
            blueprint.Meta.CycleRoomPercentAchieved = CycleMetrics.ComputeCycleRoomPercent(blueprint, plan.FlatTemplates);
            return true;
        }

        /// <summary>
        /// 층 하나를 생성한다 — 시드 층은 입구 앵커 + 트리 성장, 비시드 층은 계단 샤프트 복사에서 자란다(M9-5).
        /// 성장 → 루프 채택 → 잔여 소켓 봉인 → 층 구간 메타 기록 순서.
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="plan">생성 계획.</param>
        /// <param name="slot">층 슬롯 인덱스.</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <returns>층 생성 성공 여부.</returns>
        private bool TryGenerateFloor(DeterministicRng rng, MapGenPlan plan, int slot, MapBlueprint blueprint)
        {
            FloorGenParams floorParams = plan.FloorParams[slot];
            RoomTemplateDef[] floorTemplates = plan.Floors[slot].Templates;
            currentFloorIndex = plan.Floors[slot].FloorIndex;
            currentFlatBase = plan.TemplateStart(slot);
            countableRooms = 0;
            if (!occupiedByFloor.TryGetValue(currentFloorIndex, out HashSet<long> floorCells))
            {
                floorCells = new HashSet<long>();
                occupiedByFloor[currentFloorIndex] = floorCells;
            }

            occupiedCells = floorCells; // FitsAt/PlaceRoom/RemoveLastRoom 은 현재 층 집합만 본다
            int roomStart = blueprint.Rooms.Count;
            int edgeStart = blueprint.Edges.Count;

            var usedCount = new int[floorTemplates.Length];
            if (slot == plan.SeedFloorSlot)
            {
                if (!TryPlaceEntranceAnchor(floorTemplates, blueprint))
                {
                    return false;
                }

                for (int i = 0; i < floorTemplates.Length; i++)
                {
                    if (floorTemplates[i].IsEntranceAnchor)
                    {
                        usedCount[i] = 1;
                    }
                }

                if (!TryAttachRooms(rng, floorParams, floorTemplates, usedCount, blueprint))
                {
                    return false;
                }
            }
            else
            {
                // 비시드 층 — 계단 샤프트 좌표 복사 + 다중 시딩 성장은 M9-5 소관. 그 전까지 다층 Plan 은 명시적 실패
                return false;
            }

            // 루프는 사이클 소속 방 비율 목표(CycleRoomPercent)로 채택 — 후보 소진 시 베스트에포트(리롤 없음, X6 경고는 검증기 소관).
            // 달성률 계산은 전 층 누적 그래프 기준 — 이전 층은 봉인 완료라 후보가 없지만, 층별 정밀 달성률 분리는 M9-5 에서 층 스코프 지표로 처리한다
            CarveLoopEdges(rng, floorParams, floorTemplates, plan.FlatTemplates, usedCount, blueprint);
            SealRemainingSockets(blueprint, roomStart);

            blueprint.Floors.Add(new BlueprintFloor
            {
                FloorIndex = currentFloorIndex,
                ThemeId = plan.Floors[slot].ThemeId,
                RoomStart = roomStart,
                RoomCount = blueprint.Rooms.Count - roomStart,
                EdgeStart = edgeStart,
                EdgeCount = blueprint.Edges.Count - edgeStart,
                CycleRoomPercentAchieved = CycleMetrics.ComputeCycleRoomPercent(blueprint, plan.FlatTemplates), // 층 1개 = 전역과 동일. 다층 층 스코프 분리는 M9-5
                ShaftCountAchieved = 0, // 샤프트는 M9-5
            });
            return true;
        }

        /// <summary>버스 입구 고정 모듈을 그리드 고정 앵커(방 0)로 배치한다(3절 1).</summary>
        /// <param name="templates">현재 층 템플릿 집합(IsEntranceAnchor 템플릿 필수).</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <returns>앵커 배치 성공 여부.</returns>
        private bool TryPlaceEntranceAnchor(IReadOnlyList<RoomTemplateDef> templates, MapBlueprint blueprint)
        {
            for (int i = 0; i < templates.Count; i++)
            {
                if (templates[i].IsEntranceAnchor)
                {
                    var origin = new CellCoord(0, 0);
                    PlaceRoom(templates[i], origin, Rotation4.Deg0, blueprint, currentFlatBase + i);
                    BuildEntranceKeepOut(templates[i], origin);
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
        /// <param name="floorParams">현재 층 파라미터(방 수 예산·복도 경유 확률).</param>
        /// <param name="templates">현재 층 템플릿 집합.</param>
        /// <param name="usedCount">템플릿별 사용 횟수(입구 1 로 초기화된 상태 — 루프 단계와 공유).</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <returns>트리 조립 성공 여부.</returns>
        private bool TryAttachRooms(DeterministicRng rng, FloorGenParams floorParams, IReadOnlyList<RoomTemplateDef> templates, int[] usedCount, MapBlueprint blueprint)
        {
            int targetRooms = rng.Next(floorParams.RoomsTotalMin, floorParams.RoomsTotalMax + 1);

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
                bool viaCorridor = !fromCorridor && !unmetOnly && rng.Next(100) < floorParams.CorridorLinkPercent;

                bool attached = viaCorridor
                    && TryAttachCorridorLink(rng, floorParams, templates, usedCount, blueprint, frontier, openRoom, openSocket, targetCell, openWorldDir);
                if (!attached)
                {
                    TryAttachDirectRoom(rng, templates, usedCount, unmetOnly, false, blueprint, frontier, openRoom, openSocket, targetCell, neededDir);
                }

                // 성공이면 소켓 소비, 실패면 죽은 소켓 — 어느 쪽이든 프런티어에서 뺀다(실패분은 나중에 봉인)
                frontier.RemoveAt(frontierIndex);
            }

            if (countableRooms < floorParams.RoomsTotalMin)
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

                int newRoom = PlaceRoom(template, origin, rotation, blueprint, currentFlatBase + templateIndex);
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
        /// 복도 경유 연결 — 복도 세그먼트 1~CorridorChainMax 개를 연쇄로 놓고 끝에 방 1개를 원자적으로 붙인다.
        /// 세그먼트 연장이 막히면 그 길이에서 끝방을 시도하고, 끝방 실패 시 마지막 세그먼트를 걷어내며
        /// 한 단계씩 짧은 체인으로 재시도한다(전부 소진 시 false — 호출자가 직결 폴백).
        /// 간선은 체인 전체 성공 후에만 추가되므로 막다른 복도(봉인된 끝)가 구조적으로 남지 않는다.
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="floorParams">현재 층 파라미터(복도 연쇄 최대 길이).</param>
        /// <param name="templates">현재 층 템플릿 집합.</param>
        /// <param name="usedCount">템플릿별 사용 횟수(세그먼트 배치 시 증가·롤백 시 원복).</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="frontier">복도·끝방의 잔여 소켓을 추가할 프런티어.</param>
        /// <param name="openRoom">열린 소켓의 방 인덱스.</param>
        /// <param name="openSocket">열린 소켓.</param>
        /// <param name="targetCell">첫 세그먼트 근단 소켓이 놓일 월드 셀.</param>
        /// <param name="openWorldDir">열린 소켓의 월드 방향(연결 진행 방향).</param>
        /// <returns>체인+끝방 배치 성공 여부.</returns>
        private bool TryAttachCorridorLink(DeterministicRng rng, FloorGenParams floorParams, IReadOnlyList<RoomTemplateDef> templates, int[] usedCount, MapBlueprint blueprint, List<(int room, int socket)> frontier, int openRoom, SocketDef openSocket, CellCoord targetCell, SocketDirection openWorldDir)
        {
            int chainTarget = rng.Next(1, floorParams.CorridorChainMax + 1);
            chainSegments.Clear();

            CellCoord segTarget = targetCell;
            SocketDirection segDir = openWorldDir;
            int lockedTemplate = -1; // 첫 세그먼트가 정한 복도 템플릿 — 연쇄 전체가 이것만 쓴다(폭이 다른 복도끼리 물리면 단부 절반이 봉인돼 통로 한가운데 벽이 선다)
            for (int i = 0; i < chainTarget; i++)
            {
                if (!TryPlaceChainSegment(rng, templates, usedCount, blueprint, segTarget, segDir, lockedTemplate, out ChainSegment segment, out CellCoord nextTarget, out SocketDirection nextDir))
                {
                    break; // 더 못 늘림 — 현재 길이에서 끝방 시도
                }

                chainSegments.Add(segment);
                lockedTemplate = segment.TemplateIndex;
                segTarget = nextTarget;
                segDir = nextDir;
            }

            // 끝방 부착 — 실패하면 마지막 세그먼트를 걷어내며 짧은 체인으로 재시도
            while (chainSegments.Count > 0)
            {
                if (TryAttachChainEndRoom(rng, templates, usedCount, blueprint, frontier, chainSegments[chainSegments.Count - 1]))
                {
                    CommitChainEdges(rng, templates, blueprint, frontier, openRoom, openSocket);
                    return true;
                }

                ChainSegment last = chainSegments[chainSegments.Count - 1];
                RemoveLastRoom(templates[last.TemplateIndex], last.Origin, last.Rotation, blueprint);
                usedCount[last.TemplateIndex]--;
                chainSegments.RemoveAt(chainSegments.Count - 1);
            }

            return false;
        }

        /// <summary>복도 연쇄 세그먼트 기록 — 커밋(간선 추가)·롤백에 필요한 배치 정보.</summary>
        private struct ChainSegment
        {
            public int Room; // 배치된 복도 방 인덱스
            public int TemplateIndex; // 복도 템플릿 인덱스
            public CellCoord Origin; // 풋프린트 원점(롤백용)
            public Rotation4 Rotation; // 회전(롤백용)
            public int NearSocketId; // 근단 소켓 Id(이전 세그먼트/방과 연결)
            public SocketDirection NearFacing; // 근단 소켓의 월드 방향(원단 판별 기준)
            public int ContinueSocketId; // 다음 세그먼트로 이어간 원단 소켓 Id(마지막 세그먼트는 미사용)
        }

        /// <summary>
        /// 연쇄 세그먼트 1개를 놓는다 — 근단을 진행 방향에 맞춰 배치하고 원단(셔플 첫 소켓)으로 다음 진행점을 계산한다.
        /// 원단 없는(막다른) 복도 배치는 후보에서 제외한다. 성공 시 usedCount 를 올린다(롤백 시 호출자가 원복).
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="templates">템플릿 집합.</param>
        /// <param name="usedCount">템플릿별 사용 횟수.</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="segTarget">근단 소켓이 놓일 월드 셀.</param>
        /// <param name="incomingDir">진행 방향(이전 원단 소켓의 월드 방향).</param>
        /// <param name="lockedTemplateIndex">연쇄가 이미 고정한 복도 템플릿 인덱스(-1 = 첫 세그먼트라 자유).</param>
        /// <param name="segment">배치된 세그먼트 기록.</param>
        /// <param name="nextTarget">다음 세그먼트/끝방의 근단이 놓일 월드 셀.</param>
        /// <param name="nextDir">다음 진행 방향.</param>
        /// <returns>배치 성공 여부.</returns>
        private bool TryPlaceChainSegment(DeterministicRng rng, IReadOnlyList<RoomTemplateDef> templates, int[] usedCount, MapBlueprint blueprint, CellCoord segTarget, SocketDirection incomingDir, int lockedTemplateIndex, out ChainSegment segment, out CellCoord nextTarget, out SocketDirection nextDir)
        {
            SocketDirection neededDir = Opposite(incomingDir);
            corridorCandidates.Clear();
            for (int t = 0; t < templates.Count; t++)
            {
                RoomTemplateDef template = templates[t];
                if (!template.IsCorridor || usedCount[t] >= CorridorTreeCap(template))
                {
                    continue;
                }

                // 연쇄 2번째부터는 첫 세그먼트와 같은 복도만 — 폭이 다르면 넓은 쪽 단부 절반이 봉인돼 통로에 벽이 선다
                if (lockedTemplateIndex >= 0 && t != lockedTemplateIndex)
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
                var origin = new CellCoord(segTarget.X - rotatedLocal.X, segTarget.Y - rotatedLocal.Y);

                if (!FitsAt(corridor, origin, rotation))
                {
                    continue;
                }

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

                if (farSockets.Count == 0)
                {
                    continue; // 원단 없는 세그먼트 = 막다른 복도 — 배치 금지
                }

                int room = PlaceRoom(corridor, origin, rotation, blueprint, currentFlatBase + templateIndex);
                usedCount[templateIndex]++;

                Shuffle(rng, farSockets);
                SocketDef continueSocket = corridor.Sockets[farSockets[0]];
                SocketDirection continueDir = CellMath.RotateDirection(continueSocket.Direction, rotation);
                CellCoord continueWorld = ToWorldCell(blueprint.Rooms[room], corridor, continueSocket);
                nextTarget = Step(continueWorld, continueDir);
                nextDir = continueDir;
                segment = new ChainSegment
                {
                    Room = room,
                    TemplateIndex = templateIndex,
                    Origin = origin,
                    Rotation = rotation,
                    NearSocketId = nearSocket.Id,
                    NearFacing = neededDir,
                    ContinueSocketId = continueSocket.Id,
                };
                return true;
            }

            segment = default;
            nextTarget = default;
            nextDir = default;
            return false;
        }

        /// <summary>마지막 세그먼트의 원단 소켓들에 끝방 직결을 시도한다 — 성공 시 끝방 간선은 내부(TryAttachDirectRoom)에서 추가된다.</summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="templates">템플릿 집합.</param>
        /// <param name="usedCount">템플릿별 사용 횟수.</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="frontier">끝방의 잔여 소켓을 추가할 프런티어.</param>
        /// <param name="last">마지막 세그먼트.</param>
        /// <returns>끝방 부착 성공 여부.</returns>
        private bool TryAttachChainEndRoom(DeterministicRng rng, IReadOnlyList<RoomTemplateDef> templates, int[] usedCount, MapBlueprint blueprint, List<(int room, int socket)> frontier, ChainSegment last)
        {
            RoomTemplateDef corridor = templates[last.TemplateIndex];
            farSockets.Clear();
            for (int s = 0; s < corridor.Sockets.Length; s++)
            {
                if (corridor.Sockets[s].Id != last.NearSocketId
                    && CellMath.RotateDirection(corridor.Sockets[s].Direction, last.Rotation) != last.NearFacing)
                {
                    farSockets.Add(s);
                }
            }

            Shuffle(rng, farSockets);

            for (int f = 0; f < farSockets.Count; f++)
            {
                SocketDef farSocket = corridor.Sockets[farSockets[f]];
                SocketDirection farWorldDir = CellMath.RotateDirection(farSocket.Direction, last.Rotation);
                CellCoord farWorld = ToWorldCell(blueprint.Rooms[last.Room], corridor, farSocket);
                CellCoord roomTarget = Step(farWorld, farWorldDir);
                if (TryAttachDirectRoom(rng, templates, usedCount, false, false, blueprint, frontier, last.Room, farSocket, roomTarget, Opposite(farWorldDir)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>체인 성공 확정 — 방↔세그먼트↔세그먼트 간선을 추가하고 각 세그먼트의 잔여 소켓을 프런티어에 올린다(끝방 간선은 기추가).</summary>
        /// <param name="rng">단일 난수 스트림(AddEdge 규약용 — 복도 간선이라 실제 소비 없음).</param>
        /// <param name="templates">템플릿 집합.</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="frontier">잔여 소켓을 추가할 프런티어.</param>
        /// <param name="openRoom">체인 시작 방 인덱스.</param>
        /// <param name="openSocket">체인 시작 소켓.</param>
        private void CommitChainEdges(DeterministicRng rng, IReadOnlyList<RoomTemplateDef> templates, MapBlueprint blueprint, List<(int room, int socket)> frontier, int openRoom, SocketDef openSocket)
        {
            int prevRoom = openRoom;
            int prevSocketId = openSocket.Id;
            for (int i = 0; i < chainSegments.Count; i++)
            {
                AddEdge(rng, blueprint, prevRoom, prevSocketId, chainSegments[i].Room, chainSegments[i].NearSocketId);
                prevRoom = chainSegments[i].Room;
                prevSocketId = chainSegments[i].ContinueSocketId;
            }

            // 세그먼트 잔여 개구 소켓 프런티어 등록 — 이후 확장은 직결만 허용된다(fromCorridor 게이트)
            for (int i = 0; i < chainSegments.Count; i++)
            {
                RoomTemplateDef corridor = templates[chainSegments[i].TemplateIndex];
                for (int s = 0; s < corridor.Sockets.Length; s++)
                {
                    if (!usedSockets.Contains(SocketKey(chainSegments[i].Room, corridor.Sockets[s].Id)))
                    {
                        frontier.Add((chainSegments[i].Room, s));
                    }
                }
            }
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
        /// 트리에 루프를 더한다(3절 3) — 사이클 소속 방 비율(CycleRoomPercent) 목표형.
        /// 1단계(의무): 복도 소켓이 포함된 인접쌍 전부 연결 — 복도 개구는 벽이 없는 단부라
        /// 마주보는 소켓을 봉인하면 물리 씬(뚫린 복도 끝)과 그래프가 어긋난다. 목표 비율과 무관하게 수행.
        /// 2단계(비율): 방↔방 인접쌍 + 복도 브리지(직선 갭에 복도 원자 삽입, 양단 동시 연결) 통합 풀을 셔플해
        /// 목표 도달까지 채택 — 채택마다 브리지 판별로 비율을 재계산한다. 후보 소진 시 베스트에포트(X6 경고는 검증기 소관).
        /// 채택된 인접쌍 간선이 지름길 자물쇠 후보가 된다(4-2절). 브리지 간선은 복도 규칙상 항상 개방 통로.
        /// **채택 순서에 지름길 이득 가중을 넣어도 결과가 바뀌지 않는다** — 목표 비율(75%)이 기하 상한(~67%)보다 높아
        /// 풀이 소진될 때까지 돌므로 후보가 전부 채택된다. 2026-08-11 시드 100 실측: 지름길 후보 시드당 평균 25.8개,
        /// 채택은 ShortcutLockCountMax(3)가 상한 — 지름길이 부족하면 이 파라미터가 병목이지 여기가 아니다.
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="floorParams">현재 층 파라미터(사이클 소속 방 목표 비율).</param>
        /// <param name="templates">현재 층 템플릿 집합(브리지용 직선 복도 탐색 — usedCount 인덱스와 계약).</param>
        /// <param name="metricTemplates">달성률 계산용 전 층 평탄화 템플릿(방→템플릿 역참조가 전 층 방을 커버해야 한다).</param>
        /// <param name="usedCount">템플릿별 사용 횟수(브리지 복도 MaxCount 준수).</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        private void CarveLoopEdges(DeterministicRng rng, FloorGenParams floorParams, IReadOnlyList<RoomTemplateDef> templates, IReadOnlyList<RoomTemplateDef> metricTemplates, int[] usedCount, MapBlueprint blueprint)
        {
            // 후보 전수 수집 — 방·소켓 인덱스 순(결정론).
            // (전역 셀, 월드 방향) → 미사용 소켓 색인을 먼저 만들어 O(R·S) 조회로 짝을 찾는다(구 O(R²·S²) 쌍대조 대체).
            // ⚠️ 딕셔너리는 **조회 전용** — 후보 append 순서는 바깥 (a, sa) 오름차순 루프가 그대로 결정하고,
            // 색인 값 리스트도 (방, 소켓 배열 인덱스) 오름차순으로 쌓여 원 구현의 (b, sb) 스캔 순서와 일치한다(결과 불변).
            var socketIndex = new Dictionary<(int x, int y, SocketDirection dir), List<(int room, int socketId)>>();
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                RoomTemplateDef template = placedTemplates[r];
                for (int s = 0; s < template.Sockets.Length; s++)
                {
                    SocketDef socket = template.Sockets[s];
                    if (usedSockets.Contains(SocketKey(r, socket.Id)))
                    {
                        continue;
                    }

                    CellCoord world = ToWorldCell(blueprint.Rooms[r], template, socket);
                    SocketDirection worldDir = CellMath.RotateDirection(socket.Direction, blueprint.Rooms[r].Rotation);
                    (int, int, SocketDirection) key = (world.X, world.Y, worldDir);
                    if (!socketIndex.TryGetValue(key, out List<(int room, int socketId)> bucket))
                    {
                        bucket = new List<(int room, int socketId)>();
                        socketIndex.Add(key, bucket);
                    }

                    bucket.Add((r, socket.Id));
                }
            }

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
                    if (!socketIndex.TryGetValue((target.X, target.Y, neededDir), out List<(int room, int socketId)> partners))
                    {
                        continue;
                    }

                    for (int p = 0; p < partners.Count; p++)
                    {
                        if (partners[p].room > a)
                        {
                            candidates.Add((a, socketA.Id, partners[p].room, partners[p].socketId));
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

            // 2단계 후보 — 복도 브리지: 비복도 방 미사용 소켓 쌍의 직선 갭에 복도를 끼워 양단을 동시에 잇는다
            var bridges = new List<BridgeCandidate>();
            CollectCorridorBridges(templates, blueprint, bridges);

            // 3단계 — 통합 풀 셔플 → 목표 비율까지 채택(음수 인덱스 = 브리지). 채택 시점 재검증으로 충돌 후보를 걸러낸다
            var pool = new List<int>();
            for (int i = 0; i < candidates.Count; i++)
            {
                pool.Add(i);
            }

            for (int i = 0; i < bridges.Count; i++)
            {
                pool.Add(-1 - i);
            }

            Shuffle(rng, pool);

            float achieved = CycleMetrics.ComputeCycleRoomPercent(blueprint, metricTemplates);
            for (int p = 0; p < pool.Count && achieved < floorParams.CycleRoomPercent; p++)
            {
                bool adopted;
                if (pool[p] >= 0)
                {
                    (int roomA, int socketA, int roomB, int socketB) = candidates[pool[p]];
                    adopted = !usedSockets.Contains(SocketKey(roomA, socketA)) && !usedSockets.Contains(SocketKey(roomB, socketB));
                    if (adopted)
                    {
                        AddEdge(rng, blueprint, roomA, socketA, roomB, socketB);
                    }
                }
                else
                {
                    adopted = TryAdoptBridge(rng, templates, usedCount, blueprint, bridges[-1 - pool[p]]);
                }

                if (adopted)
                {
                    achieved = CycleMetrics.ComputeCycleRoomPercent(blueprint, metricTemplates);
                }
            }
        }

        /// <summary>복도 브리지 후보 — 직선 갭에 끼울 복도 배치와 양단 소켓.</summary>
        private struct BridgeCandidate
        {
            public int RoomA; // 근단 방 인덱스
            public int SocketA; // 근단 방 소켓 Id
            public int RoomB; // 원단 방 인덱스
            public int SocketB; // 원단 방 소켓 Id
            public int CorridorTemplate; // 삽입할 복도 템플릿 인덱스
            public CellCoord Origin; // 복도 풋프린트 원점(회전 적용 후)
            public Rotation4 Rotation; // 복도 회전
            public int NearSocketId; // 복도 근단 소켓 Id(RoomA 쪽)
            public int FarSocketId; // 복도 원단 소켓 Id(RoomB 쪽)
        }

        /// <summary>
        /// 복도 브리지 후보를 수집한다 — 직선 복도(소켓 2·정반대 방향)의 길이만큼 떨어져 마주보는
        /// 비복도 방의 미사용 소켓 쌍 + 빈 갭. 채택 시점에 재검증하므로 여기서는 기하 성립만 확인한다.
        /// </summary>
        /// <param name="templates">템플릿 집합.</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="bridges">후보 수집 목록.</param>
        private void CollectCorridorBridges(IReadOnlyList<RoomTemplateDef> templates, MapBlueprint blueprint, List<BridgeCandidate> bridges)
        {
            // 직선 복도 목록 — 갭 길이 = 소켓 간 셀 거리 + 1(hallway 1×2 = 2, dev corridor 1×3 = 3)
            var straightCorridors = new List<(int template, int gap)>();
            for (int t = 0; t < templates.Count; t++)
            {
                RoomTemplateDef corridor = templates[t];
                if (!corridor.IsCorridor || corridor.Sockets.Length != 2
                    || corridor.Sockets[1].Direction != Opposite(corridor.Sockets[0].Direction))
                {
                    continue;
                }

                int distance = System.Math.Abs(corridor.Sockets[1].LocalCell.X - corridor.Sockets[0].LocalCell.X)
                    + System.Math.Abs(corridor.Sockets[1].LocalCell.Y - corridor.Sockets[0].LocalCell.Y);
                straightCorridors.Add((t, distance + 1));
            }

            if (straightCorridors.Count == 0)
            {
                return;
            }

            // 비복도 방의 미사용 소켓 전수 — 방·소켓 인덱스 순(결정론)
            var openSockets = new List<(int room, int socketId, CellCoord cell, SocketDirection dir)>();
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                RoomTemplateDef template = placedTemplates[r];
                if (template.IsCorridor)
                {
                    continue;
                }

                for (int s = 0; s < template.Sockets.Length; s++)
                {
                    if (usedSockets.Contains(SocketKey(r, template.Sockets[s].Id)))
                    {
                        continue;
                    }

                    openSockets.Add((r, template.Sockets[s].Id,
                        ToWorldCell(blueprint.Rooms[r], template, template.Sockets[s]),
                        CellMath.RotateDirection(template.Sockets[s].Direction, blueprint.Rooms[r].Rotation)));
                }
            }

            for (int a = 0; a < openSockets.Count; a++)
            {
                for (int b = a + 1; b < openSockets.Count; b++)
                {
                    if (openSockets[b].dir != Opposite(openSockets[a].dir))
                    {
                        continue;
                    }

                    for (int c = 0; c < straightCorridors.Count; c++)
                    {
                        (int corridorTemplate, int gap) = straightCorridors[c];
                        CellCoord expectedB = StepBy(openSockets[a].cell, openSockets[a].dir, gap + 1);
                        if (openSockets[b].cell.X != expectedB.X || openSockets[b].cell.Y != expectedB.Y)
                        {
                            continue;
                        }

                        RoomTemplateDef corridor = templates[corridorTemplate];
                        CellCoord nearCell = Step(openSockets[a].cell, openSockets[a].dir);
                        CellCoord expectedFar = StepBy(openSockets[a].cell, openSockets[a].dir, gap);

                        // 근단 소켓 방향을 맞추면 배치가 유도된다 — 대칭 복도는 두 방향이 같은 배치라 첫 성립에서 중단
                        for (int sn = 0; sn < 2; sn++)
                        {
                            SocketDef nearSocket = corridor.Sockets[sn];
                            SocketDef farSocket = corridor.Sockets[1 - sn];
                            var rotation = (Rotation4)(((int)Opposite(openSockets[a].dir) - (int)nearSocket.Direction + 4) % 4);
                            CellCoord rotatedNear = CellMath.RotateLocalCell(nearSocket.LocalCell, corridor.WidthCells, corridor.HeightCells, rotation);
                            var origin = new CellCoord(nearCell.X - rotatedNear.X, nearCell.Y - rotatedNear.Y);

                            CellCoord rotatedFar = CellMath.RotateLocalCell(farSocket.LocalCell, corridor.WidthCells, corridor.HeightCells, rotation);
                            if (origin.X + rotatedFar.X != expectedFar.X || origin.Y + rotatedFar.Y != expectedFar.Y
                                || CellMath.RotateDirection(farSocket.Direction, rotation) != openSockets[a].dir
                                || !FitsAt(corridor, origin, rotation))
                            {
                                continue;
                            }

                            bridges.Add(new BridgeCandidate
                            {
                                RoomA = openSockets[a].room,
                                SocketA = openSockets[a].socketId,
                                RoomB = openSockets[b].room,
                                SocketB = openSockets[b].socketId,
                                CorridorTemplate = corridorTemplate,
                                Origin = origin,
                                Rotation = rotation,
                                NearSocketId = nearSocket.Id,
                                FarSocketId = farSocket.Id,
                            });
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 브리지 후보를 재검증 후 채택한다 — 소켓 미소비·복도 MaxCount·풋프린트를 다시 확인한다
        /// (앞선 채택이 소켓·셀을 소비해 무효화했을 수 있다). 복도 배치와 양단 간선 추가가 한 번에 일어나
        /// 막다른 끝이 생기지 않는다.
        /// </summary>
        /// <param name="rng">단일 난수 스트림(AddEdge 규약용 — 복도 간선이라 실제 소비 없음).</param>
        /// <param name="templates">템플릿 집합.</param>
        /// <param name="usedCount">템플릿별 사용 횟수(성공 시 갱신).</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="bridge">채택할 브리지 후보.</param>
        /// <returns>채택 성공 여부.</returns>
        private bool TryAdoptBridge(DeterministicRng rng, IReadOnlyList<RoomTemplateDef> templates, int[] usedCount, MapBlueprint blueprint, BridgeCandidate bridge)
        {
            RoomTemplateDef corridor = templates[bridge.CorridorTemplate];
            if (usedCount[bridge.CorridorTemplate] >= corridor.MaxCount
                || usedSockets.Contains(SocketKey(bridge.RoomA, bridge.SocketA))
                || usedSockets.Contains(SocketKey(bridge.RoomB, bridge.SocketB))
                || !FitsAt(corridor, bridge.Origin, bridge.Rotation))
            {
                return false;
            }

            int corridorRoom = PlaceRoom(corridor, bridge.Origin, bridge.Rotation, blueprint, currentFlatBase + bridge.CorridorTemplate);
            usedCount[bridge.CorridorTemplate]++;
            AddEdge(rng, blueprint, bridge.RoomA, bridge.SocketA, corridorRoom, bridge.NearSocketId);
            AddEdge(rng, blueprint, corridorRoom, bridge.FarSocketId, bridge.RoomB, bridge.SocketB);
            return true;
        }

        /// <summary>
        /// 트리(경유 연결) 단계의 복도 사용 상한 — 직선 복도(브리지 재료)는 MaxCount 의 1/3 을
        /// 사이클 브리지 예약분으로 남긴다. 경유 확률 100% 트리가 MaxCount 를 먼저 소진하면
        /// 브리지가 전부 탈락해 사이클 목표 상단이 잘리기 때문(2026-08-06 스윕 실측).
        /// </summary>
        /// <param name="corridor">복도 템플릿.</param>
        /// <returns>트리 단계 사용 상한.</returns>
        private static int CorridorTreeCap(RoomTemplateDef corridor)
        {
            bool straight = corridor.Sockets.Length == 2
                && corridor.Sockets[1].Direction == Opposite(corridor.Sockets[0].Direction);
            return straight ? corridor.MaxCount - corridor.MaxCount / 3 : corridor.MaxCount;
        }

        /// <summary>방향으로 n 칸 이동한 셀.</summary>
        /// <param name="cell">기준 셀.</param>
        /// <param name="direction">이동 방향.</param>
        /// <param name="count">이동 칸 수.</param>
        /// <returns>이동한 셀.</returns>
        private static CellCoord StepBy(CellCoord cell, SocketDirection direction, int count)
        {
            switch (direction)
            {
                case SocketDirection.North: return new CellCoord(cell.X, cell.Y + count);
                case SocketDirection.East: return new CellCoord(cell.X + count, cell.Y);
                case SocketDirection.South: return new CellCoord(cell.X, cell.Y - count);
                default: return new CellCoord(cell.X - count, cell.Y);
            }
        }

        /// <summary>현재 층(roomStart 이후 방)의 남은 열린 소켓 전부를 막힌 벽으로 봉인한다(3절 3 — 빈 소켓 0, AC-05).</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="roomStart">현재 층 방 구간 시작 인덱스.</param>
        private void SealRemainingSockets(MapBlueprint blueprint, int roomStart)
        {
            for (int r = roomStart; r < blueprint.Rooms.Count; r++)
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

        /// <summary>방을 블루프린트에 추가하고 현재 층 풋프린트 셀을 점유 처리한다 — 층 서수·평탄화 템플릿 인덱스를 함께 기록한다(M9-4).</summary>
        /// <param name="template">배치할 템플릿.</param>
        /// <param name="origin">회전 적용 후 풋프린트 원점(셀).</param>
        /// <param name="rotation">회전.</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="flatTemplateIndex">평탄화 테이블 기준 템플릿 인덱스.</param>
        /// <returns>새 방 인덱스.</returns>
        private int PlaceRoom(RoomTemplateDef template, CellCoord origin, Rotation4 rotation, MapBlueprint blueprint, int flatTemplateIndex)
        {
            (int width, int height) = CellMath.RotatedSize(template.WidthCells, template.HeightCells, rotation);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    occupiedCells.Add(CellKey(origin.X + x, origin.Y + y));
                }
            }

            blueprint.Rooms.Add(new BlueprintRoom { TemplateId = template.TemplateId, Cell = origin, Rotation = rotation, FloorIndex = currentFloorIndex, TemplateIndex = flatTemplateIndex });
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

        /// <summary>
        /// 입구 앵커의 "소켓 없는 변" 바깥을 영구 확보 대역으로 등록한다 — 그 변은 집 밖(버스·진입로)이라
        /// 어떤 방·복도도 뒤로 돌아 들어오면 안 된다(정면 차폐 방지). 변 폭만큼 잘린 무한 반평면.
        /// 소켓 유무에서 파생하므로 카탈로그에 그 변 소켓이 생기면 대역도 자동으로 사라진다.
        /// </summary>
        /// <param name="entrance">입구 템플릿.</param>
        /// <param name="origin">입구 풋프린트 원점(셀) — 회전 Deg0 고정이라 로컬 = 월드 방향.</param>
        private void BuildEntranceKeepOut(RoomTemplateDef entrance, CellCoord origin)
        {
            Log.D("[LayoutGenerator] BuildEntranceKeepOut");
            int maxX = origin.X + entrance.WidthCells - 1;
            int maxY = origin.Y + entrance.HeightCells - 1;
            for (int d = 0; d < 4; d++)
            {
                var side = (SocketDirection)d;
                bool hasSocket = false;
                for (int s = 0; s < entrance.Sockets.Length; s++)
                {
                    hasSocket |= entrance.Sockets[s].Direction == side;
                }

                if (hasSocket)
                {
                    continue;
                }

                bool alongX = side == SocketDirection.North || side == SocketDirection.South;
                entranceKeepOut.Add(new KeepOutStrip
                {
                    Side = side,
                    Boundary = side == SocketDirection.North ? maxY
                        : side == SocketDirection.South ? origin.Y
                        : side == SocketDirection.East ? maxX
                        : origin.X,
                    SpanMin = alongX ? origin.X : origin.Y,
                    SpanMax = alongX ? maxX : maxY,
                });
            }
        }

        /// <summary>해당 셀이 입구 바깥 확보 대역에 드는지 판정한다.</summary>
        /// <param name="x">셀 X.</param>
        /// <param name="y">셀 Y.</param>
        /// <returns>확보 대역 여부(true = 배치 금지).</returns>
        private bool IsEntranceKeepOut(int x, int y)
        {
            for (int i = 0; i < entranceKeepOut.Count; i++)
            {
                KeepOutStrip strip = entranceKeepOut[i];
                switch (strip.Side)
                {
                    case SocketDirection.North:
                        if (y > strip.Boundary && x >= strip.SpanMin && x <= strip.SpanMax) return true;
                        break;
                    case SocketDirection.South:
                        if (y < strip.Boundary && x >= strip.SpanMin && x <= strip.SpanMax) return true;
                        break;
                    case SocketDirection.East:
                        if (x > strip.Boundary && y >= strip.SpanMin && y <= strip.SpanMax) return true;
                        break;
                    case SocketDirection.West:
                        if (x < strip.Boundary && y >= strip.SpanMin && y <= strip.SpanMax) return true;
                        break;
                }
            }

            return false;
        }

        /// <summary>해당 원점·회전으로 템플릿 풋프린트가 기존 점유·입구 확보 대역과 겹치지 않는지 검사한다.</summary>
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
                    if (occupiedCells.Contains(CellKey(origin.X + x, origin.Y + y))
                        || IsEntranceKeepOut(origin.X + x, origin.Y + y))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>입구 바깥 확보 대역 한 줄 — 변 너머 무한 반평면을 변 폭(SpanMin~SpanMax)으로 자른 띠.</summary>
        private struct KeepOutStrip
        {
            public SocketDirection Side; // 입구에서 바깥을 향하는 변
            public int Boundary; // 그 변의 셀 좌표(넘어서면 금지)
            public int SpanMin; // 변에 평행한 축의 하한
            public int SpanMax; // 변에 평행한 축의 상한
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
