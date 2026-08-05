using System.Collections.Generic;
using Border.Core;
using EmptyHouse.MapGen.Core;
using UnityEngine;

namespace EmptyHouse.MapGen.Runtime
{
    /// <summary>
    /// 런타임 정적 지오메트리 조립기(1절) — 수신한 시드로 재생성한 블루프린트를 각 클라이언트가
    /// 로컬 조립한다(네트워크 비용 0). 문·아이템·좀비 등 상태 오브젝트는 배치하지 않는다 —
    /// 서버의 MapStateObjectSpawner 소관(1절 분리).
    /// 구현 = MapGenSceneBuilder 의 BuildMap 계열 이관: PrefabUtility → Object.Instantiate,
    /// AssetDatabase 경로 → MapPrefabRegistrySO. 개구부 기하 가드(대면 검증)도 그대로 가져온다.
    /// 문 자리에는 빈 앵커(DoorAnchor_e{간선}) 를 남긴다 — 서버 스포너가 같은 위치·회전으로 문을 스폰해
    /// 클라 개구부와 대면 정합한다(개구 실측 축 보정을 조립 시점에 공유).
    /// 결정론: 같은 블루프린트 = 같은 조립 결과(AC-02) — 순회는 리스트 인덱스 순서만 사용.
    /// </summary>
    public static class MapRuntimeAssembler
    {
        /// <summary>
        /// 블루프린트의 정적 지오메트리(방·개구부·봉인 벽·기둥)를 조립하고 맵 루트를 반환한다.
        /// 셀 바운드 정규화·배치 좌표 계산은 에디터 빌더와 동일 규칙(드리프트 금지).
        /// </summary>
        /// <param name="blueprint">조립할 블루프린트.</param>
        /// <param name="templates">생성에 사용한 템플릿 목록(MapTemplateCatalog).</param>
        /// <param name="registry">프리팹 레지스트리.</param>
        /// <param name="parent">맵 루트를 붙일 부모(씬 배치 앵커 — 위치가 맵 원점이 된다).</param>
        /// <returns>조립된 맵 루트.</returns>
        public static GameObject Assemble(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, MapPrefabRegistrySO registry, Transform parent)
        {
            Log.D($"[MapRuntimeAssembler] Assemble 시드={blueprint.Meta.Seed}");
            var mapRoot = new GameObject($"GeneratedMap_Seed{blueprint.Meta.Seed}");
            mapRoot.transform.SetParent(parent, false);
            mapRoot.transform.localPosition = Vector3.zero;

            // 셀 바운드 정규화 — 맵 로컬 (0,0) 이 최소 셀에 오게
            (int minX, int minY) = MinCellBounds(blueprint);

            var roomInstances = new GameObject[blueprint.Rooms.Count];
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                roomInstances[r] = PlaceRoom(blueprint.Rooms[r], FindTemplate(templates, blueprint.Rooms[r].TemplateId), registry, mapRoot.transform, minX, minY);
            }

            // 간선 처리 순서·컨테이너 이름은 에디터 빌더와 동일 — 스포너·감사가 이름으로 조회한다
            var doorsRoot = new GameObject("Doors");
            doorsRoot.transform.SetParent(mapRoot.transform, false);
            var sealsRoot = new GameObject("Seals");
            sealsRoot.transform.SetParent(mapRoot.transform, false);
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                BlueprintEdge edge = blueprint.Edges[e];
                if (edge.RoomB < 0)
                {
                    // 방 봉인 소켓 = 벽 유지. 복도 봉인 소켓 = 단부에 벽이 없어 벽 프리팹으로 물리 봉인
                    if (FindTemplate(templates, blueprint.Rooms[edge.RoomA].TemplateId).IsCorridor)
                    {
                        PlaceCorridorSealWall(blueprint, templates, edge, registry, sealsRoot.transform, mapRoot.transform.position, minX, minY);
                    }

                    continue;
                }

                if (edge.State == EdgeState.BlockedWall)
                {
                    continue;
                }

                PlaceOpening(blueprint, templates, edge, e, roomInstances, registry, mapRoot.transform.position, minX, minY);
            }

            var columnsRoot = new GameObject("Columns");
            columnsRoot.transform.SetParent(mapRoot.transform, false);
            PlaceCornerColumns(blueprint, templates, registry, columnsRoot.transform, mapRoot.transform.position, minX, minY);

            return mapRoot;
        }

        /// <summary>방/복도 프리팹을 셀 원점에 정렬 배치한다(빌더 PlaceRoom 이관 — 바닥 실측 바운드 정렬·내장 라이트 정책 포함).</summary>
        /// <param name="room">배치할 방.</param>
        /// <param name="template">방 템플릿.</param>
        /// <param name="registry">프리팹 레지스트리.</param>
        /// <param name="mapRoot">맵 루트.</param>
        /// <param name="minX">맵 최소 셀 X(정규화 기준).</param>
        /// <param name="minY">맵 최소 셀 Y.</param>
        /// <returns>배치된 인스턴스.</returns>
        private static GameObject PlaceRoom(BlueprintRoom room, RoomTemplateDef template, MapPrefabRegistrySO registry, Transform mapRoot, int minX, int minY)
        {
            GameObject instance = Object.Instantiate(FindRoomPrefab(registry, template.TemplateId), mapRoot, false);
            // 셀 회전은 시계방향(North→East) — 위에서 본 Unity Y+ 회전과 방향 일치
            instance.transform.localRotation = Quaternion.Euler(0f, 90f * (int)room.Rotation, 0f);
            instance.transform.localPosition = Vector3.zero;

            // 프리팹 내장 라이트 비활성화 — 90+ 노드(방 60 + 복도)면 URP Forward+ 라이트 한도를 넘는다.
            // 절차 맵 라이팅은 P5(템플릿 SO 세트) 소관 — 에디터 빌더와 같은 정책
            foreach (Light light in instance.GetComponentsInChildren<Light>(true))
            {
                light.enabled = false;
            }

            Bounds floor = FloorBounds(instance);
            float cell = registry.CellMeters;
            float targetX = mapRoot.position.x + (room.Cell.X - minX) * cell;
            float targetZ = mapRoot.position.z + (room.Cell.Y - minY) * cell;
            instance.transform.position += new Vector3(targetX - floor.min.x, 0f, targetZ - floor.min.z);
            return instance;
        }

        /// <summary>
        /// 간선 개구부를 만든다 — 소켓 쌍 대면 검증(낭떠러지 방지 가드) 후 양쪽 벽 모듈 비활성화·잔재 충진.
        /// 문 프리팹 배치는 하지 않는다(상태 오브젝트 — 서버 스폰). 개구 프로파일은 빌더와 동일(폭 4m × 높이 6m).
        /// 문 간선(DoorOpen/DoorLocked)에는 실측 보정 위치의 빈 앵커를 남겨 서버 스폰과 위치를 공유한다.
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="templates">템플릿 목록.</param>
        /// <param name="edge">처리할 연결 간선.</param>
        /// <param name="edgeIndex">간선 인덱스(로그 추적용).</param>
        /// <param name="roomInstances">방 인스턴스 배열.</param>
        /// <param name="registry">프리팹 레지스트리.</param>
        /// <param name="mapOrigin">맵 원점 월드 좌표.</param>
        /// <param name="minX">맵 최소 셀 X.</param>
        /// <param name="minY">맵 최소 셀 Y.</param>
        private static void PlaceOpening(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, BlueprintEdge edge, int edgeIndex, GameObject[] roomInstances, MapPrefabRegistrySO registry, Vector3 mapOrigin, int minX, int minY)
        {
            RoomTemplateDef templateA = FindTemplate(templates, blueprint.Rooms[edge.RoomA].TemplateId);
            SocketDef socketA = FindSocket(templateA, edge.SocketA);
            CellCoord worldCell = CellMath.WorldCell(blueprint.Rooms[edge.RoomA], templateA, socketA.LocalCell);
            SocketDirection dir = CellMath.RotateDirection(socketA.Direction, blueprint.Rooms[edge.RoomA].Rotation);

            // 낭떠러지 방지 가드 — B 소켓이 A 소켓의 바로 건너편 셀에서 마주보고 있어야 개구를 뚫는다
            RoomTemplateDef templateB = FindTemplate(templates, blueprint.Rooms[edge.RoomB].TemplateId);
            SocketDef socketB = FindSocket(templateB, edge.SocketB);
            CellCoord worldCellB = CellMath.WorldCell(blueprint.Rooms[edge.RoomB], templateB, socketB.LocalCell);
            CellCoord facing = StepCell(worldCell, dir);
            SocketDirection dirB = CellMath.RotateDirection(socketB.Direction, blueprint.Rooms[edge.RoomB].Rotation);
            if (worldCellB.X != facing.X || worldCellB.Y != facing.Y || dirB != Opposite(dir))
            {
                Log.E($"[MapRuntimeAssembler] 간선 e{edgeIndex} 기하 불일치 — 방{edge.RoomA}s{edge.SocketA}({worldCell.X},{worldCell.Y})→{dir} 이 방{edge.RoomB}s{edge.SocketB}({worldCellB.X},{worldCellB.Y},{dirB}) 와 대면하지 않는다. 개구 생략(낭떠러지 방지).");
                return;
            }

            float cell = registry.CellMeters;
            Vector3 cellCenter = mapOrigin + new Vector3((worldCell.X - minX + 0.5f) * cell, 0f, (worldCell.Y - minY + 0.5f) * cell);
            Vector3 dirVec = DirectionVector(dir);
            Vector3 gateCenter = cellCenter + dirVec * (cell * 0.5f);

            // 경계선 방향 폭 3.9m × 높이 5.8m 게이트 — 문 슬롯(4×6m)에 해당하는 벽만 자른다(빌더 동일 수치)
            bool boundaryAlongX = dir == SocketDirection.North || dir == SocketDirection.South;
            var gate = new Bounds(gateCenter + Vector3.up * 3f, boundaryAlongX ? new Vector3(3.9f, 5.8f, 1.6f) : new Vector3(1.6f, 5.8f, 3.9f));
            var cutA = new List<Bounds>();
            var cutB = new List<Bounds>();
            DisableWallsIntersecting(roomInstances[edge.RoomA], gate, boundaryAlongX, cutA);
            DisableWallsIntersecting(roomInstances[edge.RoomB], gate, boundaryAlongX, cutB);

            // 문 기준점 = 절단 개구 실측 중심(셀 중심 ±0.4m 클램프) — 빌더와 동일 보정.
            // 중심 계산은 전고 구조 벽(높이 ≥ 4m)만 사용 — 상·하단 장식 조각은 서브 그리드가 달라 중심을 끌고 간다
            float cellCenterAxis = boundaryAlongX ? gateCenter.x : gateCenter.z;
            Bounds hole = default;
            bool holeFound = false;
            foreach (List<Bounds> side in new[] { cutA, cutB })
            {
                for (int i = 0; i < side.Count; i++)
                {
                    if (side[i].size.y < 4f)
                    {
                        continue;
                    }

                    if (!holeFound)
                    {
                        hole = side[i];
                        holeFound = true;
                    }
                    else
                    {
                        hole.Encapsulate(side[i]);
                    }
                }
            }

            if (holeFound)
            {
                float holeCenterAxis = boundaryAlongX ? hole.center.x : hole.center.z;
                float doorAxis = Mathf.Clamp(holeCenterAxis, cellCenterAxis - 0.4f, cellCenterAxis + 0.4f);
                if (boundaryAlongX)
                {
                    gateCenter.x = doorAxis;
                }
                else
                {
                    gateCenter.z = doorAxis;
                }
            }

            Transform doorsRoot = roomInstances[edge.RoomA].transform.parent.Find("Doors");

            // 잔여 슬릿 기준 = 공칭 4m 슬롯 프로파일(문 = 보정 축 중심, 통로 = 셀 중심 고정)
            float profileAxis = edge.State == EdgeState.OpenPassage ? cellCenterAxis : (boundaryAlongX ? gateCenter.x : gateCenter.z);
            Vector3 profileCenter = boundaryAlongX
                ? new Vector3(profileAxis, mapOrigin.y + 3f, gateCenter.z)
                : new Vector3(gateCenter.x, mapOrigin.y + 3f, profileAxis);
            var profile = new Bounds(profileCenter, boundaryAlongX ? new Vector3(4f, 6f, 1.6f) : new Vector3(1.6f, 6f, 4f));
            CoverOpeningSlits(cutA, profile, boundaryAlongX, registry, doorsRoot, mapOrigin.y, edgeIndex);
            CoverOpeningSlits(cutB, profile, boundaryAlongX, registry, doorsRoot, mapOrigin.y, edgeIndex);

            if (edge.State == EdgeState.OpenPassage)
            {
                return;
            }

            // 문 앵커 — 서버 스포너(MapStateObjectSpawner.SpawnDoors)가 이 위치·회전으로 문 NetworkObject 를 스폰한다
            var anchor = new GameObject($"DoorAnchor_e{edgeIndex}");
            anchor.transform.SetParent(doorsRoot, false);
            anchor.transform.position = gateCenter;
            anchor.transform.rotation = Quaternion.Euler(0f, YawFor(dir), 0f);
        }

        /// <summary>봉인 복도 소켓(RoomB = -1)에 봉인 벽을 세운다(빌더 PlaceCorridorSealWall 이관).</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="templates">템플릿 목록.</param>
        /// <param name="edge">봉인 간선.</param>
        /// <param name="registry">프리팹 레지스트리.</param>
        /// <param name="sealsRoot">봉인 벽 부모.</param>
        /// <param name="mapOrigin">맵 원점 월드 좌표.</param>
        /// <param name="minX">맵 최소 셀 X.</param>
        /// <param name="minY">맵 최소 셀 Y.</param>
        private static void PlaceCorridorSealWall(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, BlueprintEdge edge, MapPrefabRegistrySO registry, Transform sealsRoot, Vector3 mapOrigin, int minX, int minY)
        {
            RoomTemplateDef template = FindTemplate(templates, blueprint.Rooms[edge.RoomA].TemplateId);
            SocketDef socket = FindSocket(template, edge.SocketA);
            CellCoord worldCell = CellMath.WorldCell(blueprint.Rooms[edge.RoomA], template, socket.LocalCell);
            SocketDirection dir = CellMath.RotateDirection(socket.Direction, blueprint.Rooms[edge.RoomA].Rotation);

            float cell = registry.CellMeters;
            Vector3 cellCenter = mapOrigin + new Vector3((worldCell.X - minX + 0.5f) * cell, 0f, (worldCell.Y - minY + 0.5f) * cell);
            Vector3 dirVec = DirectionVector(dir);
            Vector3 boundaryCenter = cellCenter + dirVec * (cell * 0.5f);

            bool boundaryAlongX = dir == SocketDirection.North || dir == SocketDirection.South;
            Vector3 along = boundaryAlongX ? Vector3.right : Vector3.forward;
            for (int k = -1; k <= 1; k += 2)
            {
                GameObject piece = Object.Instantiate(registry.SealWallPrefab, sealsRoot, false);
                // 프리팹 forward(+Z)가 맵 안쪽(플레이어 시야)을 향하도록 소켓 바깥 방향(dir)의 반대로 회전한다
                piece.transform.rotation = Quaternion.Euler(0f, YawFor(Opposite(dir)), 0f);
                Vector3 target = boundaryCenter + along * k;
                piece.transform.position = target;
                Bounds bounds = RendererBounds(piece);
                piece.transform.position += new Vector3(target.x - bounds.center.x, mapOrigin.y - bounds.min.y, target.z - bounds.center.z);
                piece.name = $"SealWall_{edge.RoomA}_{edge.SocketA}";
            }
        }

        /// <summary>서로 다른 방·복도가 만나는 노출 코너에 이음 기둥을 세운다(빌더 PlaceCornerColumns 이관).</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="templates">템플릿 목록.</param>
        /// <param name="registry">프리팹 레지스트리.</param>
        /// <param name="columnsRoot">기둥 부모.</param>
        /// <param name="mapOrigin">맵 원점 월드 좌표.</param>
        /// <param name="minX">맵 최소 셀 X.</param>
        /// <param name="minY">맵 최소 셀 Y.</param>
        private static void PlaceCornerColumns(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, MapPrefabRegistrySO registry, Transform columnsRoot, Vector3 mapOrigin, int minX, int minY)
        {
            // 정규화 셀 → 소유 방 맵
            var owner = new Dictionary<long, int>();
            int maxX = 0;
            int maxY = 0;
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                RoomTemplateDef template = FindTemplate(templates, blueprint.Rooms[r].TemplateId);
                (int w, int h) = CellMath.RotatedSize(template.WidthCells, template.HeightCells, blueprint.Rooms[r].Rotation);
                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        int cx = blueprint.Rooms[r].Cell.X - minX + x;
                        int cy = blueprint.Rooms[r].Cell.Y - minY + y;
                        owner[CellKey(cx, cy)] = r;
                        maxX = Mathf.Max(maxX, cx);
                        maxY = Mathf.Max(maxY, cy);
                    }
                }
            }

            // 개구 경계 집합 — 이 셀 쌍 사이 경계는 4m 슬롯 전체가 열려 있어 벽선이 아니다
            var openPairs = new HashSet<(long, long)>();
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                BlueprintEdge edge = blueprint.Edges[e];
                if (edge.RoomB < 0 || edge.State == EdgeState.BlockedWall)
                {
                    continue;
                }

                RoomTemplateDef template = FindTemplate(templates, blueprint.Rooms[edge.RoomA].TemplateId);
                SocketDef socket = FindSocket(template, edge.SocketA);
                CellCoord worldCell = CellMath.WorldCell(blueprint.Rooms[edge.RoomA], template, socket.LocalCell);
                SocketDirection dir = CellMath.RotateDirection(socket.Direction, blueprint.Rooms[edge.RoomA].Rotation);
                CellCoord facing = StepCell(worldCell, dir);
                openPairs.Add(CellPair(worldCell.X - minX, worldCell.Y - minY, facing.X - minX, facing.Y - minY));
            }

            // 격자점별 벽선 판정 — 정확히 2개가 직각으로 만나는 점(L자)만 채택
            var columnPoints = new HashSet<long>();
            for (int px = 0; px <= maxX + 1; px++)
            {
                for (int py = 0; py <= maxY + 1; py++)
                {
                    bool north = IsWallLine(owner, openPairs, px - 1, py, px, py);
                    bool south = IsWallLine(owner, openPairs, px - 1, py - 1, px, py - 1);
                    bool east = IsWallLine(owner, openPairs, px, py, px, py - 1);
                    bool west = IsWallLine(owner, openPairs, px - 1, py, px - 1, py - 1);
                    int lineCount = (north ? 1 : 0) + (south ? 1 : 0) + (east ? 1 : 0) + (west ? 1 : 0);
                    if (lineCount != 2 || (north && south) || (east && west))
                    {
                        continue; // 벽 없음·벽 끝·일직선 통과·T자·십자
                    }

                    // 서로 다른 방 2개 이상이 얽힌 이음만 — 단일 방 코너는 자체 마감
                    var owners = new HashSet<int>();
                    for (int ox = -1; ox <= 0; ox++)
                    {
                        for (int oy = -1; oy <= 0; oy++)
                        {
                            if (owner.TryGetValue(CellKey(px + ox, py + oy), out int room))
                            {
                                owners.Add(room);
                            }
                        }
                    }

                    if (owners.Count >= 2)
                    {
                        columnPoints.Add(CellKey(px, py));
                    }
                }
            }

            float cell = registry.CellMeters;
            foreach (long key in SortedKeys(columnPoints))
            {
                int px = (int)(key >> 32);
                int py = (int)(uint)(key & 0xFFFFFFFF);
                GameObject column = Object.Instantiate(registry.CornerColumnPrefab, columnsRoot, false);
                column.transform.position = mapOrigin + new Vector3(px * cell, 0f, py * cell);
                column.name = $"Column_{px}_{py}";
            }
        }

        /// <summary>
        /// 게이트와 교차하며 통로를 실제로 막는 방향의 벽 모듈만 비활성화한다 — 옆벽·모서리 조각(겹침 ≤ 0.5m)은 보존(빌더 동일 규칙).
        /// </summary>
        /// <param name="roomInstance">대상 방 인스턴스.</param>
        /// <param name="gate">개구부 게이트 박스(월드).</param>
        /// <param name="boundaryAlongX">경계선이 X 축 방향인지(개구 방향 N/S = 참).</param>
        /// <param name="cutBounds">비활성화한 벽 바운드 수집 목록(개구 실측 중심 계산용).</param>
        private static void DisableWallsIntersecting(GameObject roomInstance, Bounds gate, bool boundaryAlongX, List<Bounds> cutBounds)
        {
            Renderer[] renderers = roomInstance.GetComponentsInChildren<Renderer>(false);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i].name.Contains("Wall"))
                {
                    continue;
                }

                // 막는 벽 = 경계선과 같은 축으로 길게 놓인 벽(N/S 개구면 X 로 긴 벽)
                Bounds bounds = renderers[i].bounds;
                Vector3 size = bounds.size;
                bool blocking = boundaryAlongX ? size.x > size.z : size.z > size.x;
                if (!blocking || !bounds.Intersects(gate))
                {
                    continue;
                }

                float overlap = boundaryAlongX
                    ? Mathf.Min(bounds.max.x, gate.max.x) - Mathf.Max(bounds.min.x, gate.min.x)
                    : Mathf.Min(bounds.max.z, gate.max.z) - Mathf.Max(bounds.min.z, gate.min.z);
                if (overlap <= 0.5f)
                {
                    continue; // 모서리에 걸친 이웃 세그먼트 — 자르면 구멍이 문틀(4m)보다 넓어진다
                }

                cutBounds.Add(bounds);
                renderers[i].gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 한 벽 평면에서 프로파일(공칭 4m 슬롯)보다 넓게 잘린 개구 잔여 슬릿을 이음 기둥으로 가린다.
        /// 슬릿 폭이 기둥 폭을 넘으면 0.8m 간격으로 여러 개를 세운다(빌더 동일 규칙).
        /// </summary>
        /// <param name="sideCuts">이 평면에서 비활성화한 벽 바운드 목록.</param>
        /// <param name="profile">개구 프로파일 월드 바운드.</param>
        /// <param name="boundaryAlongX">경계선이 X 축 방향인지.</param>
        /// <param name="registry">프리팹 레지스트리.</param>
        /// <param name="doorsRoot">기둥 부모.</param>
        /// <param name="floorY">바닥 월드 Y.</param>
        /// <param name="edgeIndex">간선 인덱스(추적용 이름 표기).</param>
        private static void CoverOpeningSlits(List<Bounds> sideCuts, Bounds profile, bool boundaryAlongX, MapPrefabRegistrySO registry, Transform doorsRoot, float floorY, int edgeIndex)
        {
            Bounds hole = default;
            bool found = false;
            for (int i = 0; i < sideCuts.Count; i++)
            {
                if (sideCuts[i].size.y < 4f)
                {
                    continue;
                }

                if (!found)
                {
                    hole = sideCuts[i];
                    found = true;
                }
                else
                {
                    hole.Encapsulate(sideCuts[i]);
                }
            }

            if (!found)
            {
                return;
            }

            float holeMin = boundaryAlongX ? hole.min.x : hole.min.z;
            float holeMax = boundaryAlongX ? hole.max.x : hole.max.z;
            float profileMin = boundaryAlongX ? profile.min.x : profile.min.z;
            float profileMax = boundaryAlongX ? profile.max.x : profile.max.z;
            float perpCenter = boundaryAlongX ? hole.center.z : hole.center.x;

            foreach ((float min, float max) in new[] { (holeMin, profileMin), (profileMax, holeMax) })
            {
                float width = max - min;
                if (width < 0.05f)
                {
                    continue;
                }

                int count = Mathf.CeilToInt(width / 0.8f);
                for (int k = 0; k < count; k++)
                {
                    float axis = min + width * (k + 0.5f) / count;
                    GameObject column = Object.Instantiate(registry.CornerColumnPrefab, doorsRoot, false);
                    column.transform.position = boundaryAlongX
                        ? new Vector3(axis, floorY, perpCenter)
                        : new Vector3(perpCenter, floorY, axis);
                    column.name = $"SlitColumn_e{edgeIndex}";
                }
            }
        }

        /// <summary>두 이웃 셀 경계가 벽선인지 — 소유가 다르고(방|방·방|빈칸) 개구 경계가 아니면 벽선이다.</summary>
        /// <param name="owner">정규화 셀 → 소유 방 맵.</param>
        /// <param name="openPairs">개구(문·통로) 경계 셀 쌍 집합.</param>
        /// <param name="ax">셀 A 정규화 X.</param>
        /// <param name="ay">셀 A 정규화 Y.</param>
        /// <param name="bx">셀 B 정규화 X.</param>
        /// <param name="by">셀 B 정규화 Y.</param>
        /// <returns>벽선 여부.</returns>
        private static bool IsWallLine(Dictionary<long, int> owner, HashSet<(long, long)> openPairs, int ax, int ay, int bx, int by)
        {
            int ownerA = owner.TryGetValue(CellKey(ax, ay), out int a) ? a : -1;
            int ownerB = owner.TryGetValue(CellKey(bx, by), out int b) ? b : -1;
            if (ownerA == ownerB)
            {
                return false;
            }

            return !openPairs.Contains(CellPair(ax, ay, bx, by));
        }

        /// <summary>순서 무관 셀 쌍 키(개구 경계 식별용).</summary>
        /// <param name="ax">셀 A 정규화 X.</param>
        /// <param name="ay">셀 A 정규화 Y.</param>
        /// <param name="bx">셀 B 정규화 X.</param>
        /// <param name="by">셀 B 정규화 Y.</param>
        /// <returns>정규화된 (작은 키, 큰 키) 쌍.</returns>
        private static (long, long) CellPair(int ax, int ay, int bx, int by)
        {
            long ka = CellKey(ax, ay);
            long kb = CellKey(bx, by);
            return ka <= kb ? (ka, kb) : (kb, ka);
        }

        /// <summary>기둥 지점 키 집합을 정렬해 반환한다 — 배치 순서 결정론(HashSet 열거 순서 의존 금지).</summary>
        /// <param name="keys">지점 키 집합.</param>
        /// <returns>정렬된 키 목록.</returns>
        private static List<long> SortedKeys(HashSet<long> keys)
        {
            var list = new List<long>(keys);
            list.Sort();
            return list;
        }

        /// <summary>레지스트리에서 템플릿 ID 로 방 프리팹을 찾는다(미등록 = 데이터 결함 — NRE 표면화).</summary>
        /// <param name="registry">프리팹 레지스트리.</param>
        /// <param name="templateId">템플릿 ID.</param>
        /// <returns>방 프리팹 — 미등록이면 null.</returns>
        private static GameObject FindRoomPrefab(MapPrefabRegistrySO registry, string templateId)
        {
            for (int i = 0; i < registry.RoomPrefabs.Length; i++)
            {
                if (registry.RoomPrefabs[i].TemplateId == templateId)
                {
                    return registry.RoomPrefabs[i].Prefab;
                }
            }

            return null;
        }

        /// <summary>인스턴스의 바닥 타일(Hall_Floor) 합산 월드 바운드 — 없으면 전체 렌더러 바운드 폴백.</summary>
        /// <param name="instance">방 인스턴스.</param>
        /// <returns>바닥 월드 바운드.</returns>
        private static Bounds FloorBounds(GameObject instance)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(false);
            Bounds bounds = default;
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i].name.Contains("Hall_Floor"))
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderers[i].bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            if (found)
            {
                return bounds;
            }

            return RendererBounds(instance);
        }

        /// <summary>인스턴스의 전체 렌더러 합산 월드 바운드(피벗 보정 정렬용).</summary>
        /// <param name="instance">대상 인스턴스.</param>
        /// <returns>월드 바운드.</returns>
        private static Bounds RendererBounds(GameObject instance)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(false);
            Bounds bounds = default;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (i == 0)
                {
                    bounds = renderers[i].bounds;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            return bounds;
        }

        /// <summary>블루프린트 전 방 풋프린트의 최소 셀 좌표(정규화 기준점)를 구한다.</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <returns>(최소 X, 최소 Y).</returns>
        internal static (int, int) MinCellBounds(MapBlueprint blueprint)
        {
            int minX = int.MaxValue;
            int minY = int.MaxValue;
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                minX = Mathf.Min(minX, blueprint.Rooms[r].Cell.X);
                minY = Mathf.Min(minY, blueprint.Rooms[r].Cell.Y);
            }

            return (minX, minY);
        }

        /// <summary>셀에서 방향으로 한 칸 이동한 셀.</summary>
        /// <param name="cell">기준 셀.</param>
        /// <param name="dir">이동 방향.</param>
        /// <returns>이동한 셀.</returns>
        private static CellCoord StepCell(CellCoord cell, SocketDirection dir)
        {
            switch (dir)
            {
                case SocketDirection.North: return new CellCoord(cell.X, cell.Y + 1);
                case SocketDirection.East: return new CellCoord(cell.X + 1, cell.Y);
                case SocketDirection.South: return new CellCoord(cell.X, cell.Y - 1);
                default: return new CellCoord(cell.X - 1, cell.Y);
            }
        }

        /// <summary>반대 방향.</summary>
        /// <param name="dir">기준 방향.</param>
        /// <returns>180도 반대 방향.</returns>
        private static SocketDirection Opposite(SocketDirection dir)
        {
            return (SocketDirection)(((int)dir + 2) % 4);
        }

        /// <summary>소켓 방향의 월드 단위 벡터(+X=East, +Z=North).</summary>
        /// <param name="dir">소켓 방향.</param>
        /// <returns>단위 벡터.</returns>
        private static Vector3 DirectionVector(SocketDirection dir)
        {
            switch (dir)
            {
                case SocketDirection.North: return Vector3.forward;
                case SocketDirection.East: return Vector3.right;
                case SocketDirection.South: return Vector3.back;
                default: return Vector3.left;
            }
        }

        /// <summary>문·봉인 벽 프리팹의 Y 회전각 — 통로 축이 소켓 방향과 나란하도록.</summary>
        /// <param name="dir">개구부 방향.</param>
        /// <returns>Y 오일러 각.</returns>
        private static float YawFor(SocketDirection dir)
        {
            switch (dir)
            {
                case SocketDirection.North: return 0f;
                case SocketDirection.East: return 90f;
                case SocketDirection.South: return 180f;
                default: return 270f;
            }
        }

        /// <summary>정규화 셀 좌표 → 소유 맵 키.</summary>
        /// <param name="x">셀 X.</param>
        /// <param name="y">셀 Y.</param>
        /// <returns>64비트 키.</returns>
        private static long CellKey(int x, int y)
        {
            return ((long)x << 32) ^ (uint)y;
        }

        /// <summary>TemplateId 로 템플릿을 찾는다(없으면 데이터 결함 — NRE 표면화).</summary>
        /// <param name="templates">템플릿 목록.</param>
        /// <param name="templateId">찾을 ID.</param>
        /// <returns>일치 템플릿.</returns>
        internal static RoomTemplateDef FindTemplate(IReadOnlyList<RoomTemplateDef> templates, string templateId)
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
        /// <returns>소켓 정의.</returns>
        internal static SocketDef FindSocket(RoomTemplateDef template, int socketId)
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
    }
}
