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
        /// 조립 말미에 입구 앵커 방 transform 이 parent 위치에 정확히 오도록 맵 루트를 평행이동한다 —
        /// 씬에 (0,0,0) 앵커를 두면 입구가 항상 그 자리라 스폰 포인트를 씬 고정 좌표로 둘 수 있다.
        /// </summary>
        /// <param name="blueprint">조립할 블루프린트.</param>
        /// <param name="templates">생성에 사용한 템플릿 목록.</param>
        /// <param name="definition">빈 집 정의(M10-1) — 층별 환경 프리팹·셀 실측·층고·조명의 단일 원천.</param>
        /// <param name="parent">맵 루트를 붙일 부모(씬 배치 앵커 — 입구 앵커 방이 이 위치에 온다).</param>
        /// <param name="instantiate">프리팹 인스턴스화기 — null 이면 Object.Instantiate(런타임 기본). 에디터 빌더는 PrefabUtility 경로를 주입해 씬 프리팹 링크를 보존한다(기하 코드는 단일 유지).</param>
        /// <param name="flatTemplateAssets">평탄화 인덱스 → 템플릿 SO(MapPlanBuilder.Build 산출물 — 접미사 ID 의 유일한 역참조 경로).</param>
        /// <returns>조립된 맵 루트.</returns>
        public static GameObject Assemble(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, MapDefinitionSO definition, Transform parent, System.Func<GameObject, Transform, GameObject> instantiate, RoomTemplateSO[] flatTemplateAssets)
        {
            Log.D($"[MapRuntimeAssembler] Assemble 시드={blueprint.Meta.Seed}");
            instantiate = instantiate ?? DefaultInstantiate;
            var mapRoot = new GameObject($"GeneratedMap_Seed{blueprint.Meta.Seed}");
            mapRoot.transform.SetParent(parent, false);
            mapRoot.transform.localPosition = Vector3.zero;

            // 셀 바운드 정규화 — 맵 로컬 (0,0) 이 최소 셀에 오게(XZ 는 전역 하나 — 층 루트는 Y 만 이동, 설계 D)
            (int minX, int minY) = MinCellBounds(blueprint);
            Dictionary<int, float> floorPlanes = BuildFloorPlanes(blueprint, definition);
            float cellMeters = definition.Floors[0].CellMeters; // 계단 연결 층 쌍은 린트가 동일성을 강제 — 전역 하나로 충분

            var roomInstances = new GameObject[blueprint.Rooms.Count];
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                GameObject prefab = flatTemplateAssets[blueprint.Rooms[r].TemplateIndex].SelectPrefab(blueprint.Meta.Seed, r);
                roomInstances[r] = PlaceRoom(blueprint.Rooms[r], prefab, mapRoot.transform, minX, minY, cellMeters, floorPlanes[blueprint.Rooms[r].FloorIndex], instantiate);
                // 방 인덱스를 이름에 박는다 — 스포너가 아이템 앵커(MapItemAnchor)를 방 단위로 찾을 때 쓰는 유일한 연결고리
                roomInstances[r].name = $"Room_{r}_{blueprint.Rooms[r].TemplateId}";
            }

            // 간선 처리 순서·컨테이너 이름은 에디터 빌더와 동일 — 스포너·감사가 이름으로 조회한다.
            // 개구 기하는 그 간선 방들의 층 평면 기준(floorOrigin) — 수직 간선(-2)은 기하 작업이 없다(계단실 프리팹이 관통 구조)
            var doorsRoot = new GameObject("Doors");
            doorsRoot.transform.SetParent(mapRoot.transform, false);
            var sealsRoot = new GameObject("Seals");
            sealsRoot.transform.SetParent(mapRoot.transform, false);
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                BlueprintEdge edge = blueprint.Edges[e];
                if (edge.SocketA == -2)
                {
                    continue; // 수직 간선(M9-5) — 소켓 없는 층간 연결. 벽 절단·앵커 없음
                }

                Vector3 floorOrigin = mapRoot.transform.position + Vector3.up * floorPlanes[blueprint.Rooms[edge.RoomA].FloorIndex];
                FloorDefinitionSO floorDef = definition.FloorOf(blueprint.Rooms[edge.RoomA].FloorIndex); // 환경 프리팹은 테마 종속 — 그 간선 층의 정의가 원천
                if (edge.State == EdgeState.ReturnExit)
                {
                    // 탈출문 — 잎 방 바깥 벽을 문 개구로 뚫고 앵커만 남긴다(문 오브젝트는 서버 스폰)
                    PlaceOuterExitOpening(blueprint, templates, edge, e, roomInstances, floorDef, doorsRoot.transform, floorOrigin, minX, minY, instantiate);
                    continue;
                }

                if (edge.RoomB < 0)
                {
                    // 방 봉인 소켓 = 벽 유지. 복도 봉인 소켓 = 단부에 벽이 없어 물리 처리 필요:
                    // 맞은편이 이미 연결된 방이면 그 벽을 절단해 전폭 개방(hallway_x2 반쪽 입), 아니면 벽 프리팹 봉인
                    if (FindTemplate(templates, blueprint.Rooms[edge.RoomA].TemplateId).IsCorridor
                        && !TryOpenSealedHalfMouth(blueprint, templates, edge, e, roomInstances, floorDef, doorsRoot.transform, floorOrigin, minX, minY, instantiate))
                    {
                        PlaceCorridorSealWall(blueprint, templates, edge, floorDef, sealsRoot.transform, floorOrigin, minX, minY, instantiate);
                    }

                    continue;
                }

                if (edge.State == EdgeState.BlockedWall)
                {
                    continue;
                }

                PlaceOpening(blueprint, templates, edge, e, roomInstances, floorDef, floorOrigin, minX, minY, instantiate);
            }

            var columnsRoot = new GameObject("Columns");
            columnsRoot.transform.SetParent(mapRoot.transform, false);
            for (int f = 0; f < blueprint.Floors.Count; f++)
            {
                BlueprintFloor floor = blueprint.Floors[f];
                Vector3 floorOrigin = mapRoot.transform.position + Vector3.up * floorPlanes[floor.FloorIndex];
                PlaceCornerColumns(blueprint, templates, definition.FloorOf(floor.FloorIndex), columnsRoot.transform, floorOrigin, minX, minY, instantiate, floor.RoomStart, floor.RoomCount);
            }

            // 계단 조립(M9-10) — 위층이 있는 계단실마다 완성 계단 삽입 + 천장·위층 바닥 절개
            if (blueprint.Floors.Count > 1)
            {
                var stairsRoot = new GameObject("Stairs");
                stairsRoot.transform.SetParent(mapRoot.transform, false);
                PlaceStairs(blueprint, templates, definition, stairsRoot.transform, roomInstances, floorPlanes, mapRoot.transform.position, minX, minY, cellMeters, instantiate);
            }

            // 입구 고정 — 최소 셀 정규화는 시드마다 입구 위치를 흔든다. 입구 앵커 방(코어가 셀 (0,0)·Deg0 고정)의
            // 실측 transform 이 앵커 위치에 오도록 루트를 통째로 이동한다. 자식 전체가 강체 이동이라
            // "방 = 루트 위치 + (셀−최소셀)×셀m" 불변식이 유지돼 스포너·베이커·감사는 무수정으로 따라온다.
            int entranceIndex = EntranceRoomIndex(blueprint, templates);
            mapRoot.transform.position += parent.position - roomInstances[entranceIndex].transform.position;

            AssignPhysicsLayers(mapRoot);

            // 조명 컬러 — 방 그룹 수집·기준점 계산은 컬러가 첫 Update 에서 처리한다(배치 확정 이후)
            mapRoot.AddComponent<EmptyHouse.Environment.MapLightCuller>().Initialize(definition.LightingProfile);

            return mapRoot;
        }

        /// <summary>
        /// 계단 조립(M9-10) — 위층이 있는 계단실마다 완성 계단 프리팹(4×12×9)을 삽입하고 그 층 천장을,
        /// 아래층이 있는 계단실은 자기 바닥(도착 개구)을 절개한다. 계단실 프리팹은 닫힌 방 — 층 위치
        /// (최하·중간·최상)마다 필요한 구멍이 달라 프리팹에 구울 수 없어 전부 여기서 뚫는다.
        /// 로컬 규약(Deg0): 계단 스트립 = 서쪽 열 (0,1)~(0,3)·북(+Z) 상승·진입 어프론 (0,0),
        /// 천장 절개 = (0,2)·(0,3)(헤드룸 y≥4 구간), 바닥 절개 = (0,3)(y≥7 구간 + 도착). 방 회전을 그대로 따른다.
        /// 램프 플레이트·상단 브리지는 NavMesh 접속용(M9-9 실측 — 계단 메시 복셀화 단절 대책).
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="templates">템플릿 목록(IsStairAnchor 판별).</param>
        /// <param name="definition">빈 집 정의(층별 StairPrefab 원천).</param>
        /// <param name="stairsRoot">계단 인스턴스 부모.</param>
        /// <param name="roomInstances">방 인스턴스 배열(절개 대상).</param>
        /// <param name="floorPlanes">층 서수 → 바닥면 Y.</param>
        /// <param name="mapOrigin">맵 루트 월드 위치.</param>
        /// <param name="minX">맵 최소 셀 X.</param>
        /// <param name="minY">맵 최소 셀 Y.</param>
        /// <param name="cellMeters">셀 실측(m).</param>
        /// <param name="instantiate">프리팹 인스턴스화기.</param>
        private static void PlaceStairs(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, MapDefinitionSO definition, Transform stairsRoot, GameObject[] roomInstances, Dictionary<int, float> floorPlanes, Vector3 mapOrigin, int minX, int minY, float cellMeters, System.Func<GameObject, Transform, GameObject> instantiate)
        {
            var presentFloors = new HashSet<int>();
            for (int f = 0; f < blueprint.Floors.Count; f++)
            {
                presentFloors.Add(blueprint.Floors[f].FloorIndex);
            }

            int stairs = 0;
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                RoomTemplateDef template = FindTemplate(templates, blueprint.Rooms[r].TemplateId);
                if (!template.IsStairAnchor)
                {
                    continue;
                }

                BlueprintRoom room = blueprint.Rooms[r];
                Vector3 floorOrigin = mapOrigin + Vector3.up * floorPlanes[room.FloorIndex];
                GameObject ownStair = null;

                if (presentFloors.Contains(room.FloorIndex + 1))
                {
                    FloorDefinitionSO entry = definition.FloorOf(room.FloorIndex);
                    if (entry == null || entry.StairPrefab == null)
                    {
                        Log.W($"[MapRuntimeAssembler] 층 {room.FloorIndex} StairPrefab 미배선 — 계단실 방 {r} 계단 생략(위층 보행 접근 불가)");
                    }
                    else
                    {
                        // 계단 삽입 — 스트립 셀 (0,1)~(0,3) 회전 후 월드 AABB 민 코너에 바운즈 정렬
                        GameObject stair = instantiate(entry.StairPrefab, stairsRoot);
                        ownStair = stair;
                        stair.name = $"Stair_r{r}_f{room.FloorIndex}";
                        stair.transform.rotation = Quaternion.Euler(0f, 90f * (int)room.Rotation, 0f);
                        Bounds strip = CellSpanBounds(room, template, 0, 1, 0, 3, floorOrigin, minX, minY, cellMeters);
                        Bounds current = CombinedRendererBounds(stair);
                        stair.transform.position += new Vector3(strip.min.x - current.min.x, floorOrigin.y - current.min.y, strip.min.z - current.min.z);

                        // 램프 플레이트 비활성(2026-08-13 기획 반영) — 좀비 층간 이동이 기획상 차단이라 계단면 내비 연속성이 불필요.
                        // 다층 좀비 이동이 열리면 아래 한 줄 복원(없으면 계단 복셀화 단절로 층간 경로 PathPartial — M9-9 실측).
                        // AddStairRamps(stair, stairsRoot);
                        AddTopBridge(stair, stairsRoot, room.Rotation, floorOrigin.y + FloorGeometry.StairRise(definition, room.FloorIndex));

                        // 천장 절개 — 헤드룸 구간 (0,2)·(0,3), 천장고 6m 기준 밴드(벽은 min.y 가 바닥이라 안 걸린다)
                        Bounds ceilingArea = CellSpanBounds(room, template, 0, 2, 0, 3, floorOrigin, minX, minY, cellMeters);
                        ceilingArea.Expand(new Vector3(-0.2f, 0f, -0.2f));
                        ceilingArea.center = new Vector3(ceilingArea.center.x, floorOrigin.y + 6f, ceilingArea.center.z);
                        ceilingArea.size = new Vector3(ceilingArea.size.x, 1.6f, ceilingArea.size.z);
                        int ceilingCut = CutRenderersIntersecting(roomInstances[r], ceilingArea, floorOrigin.y + 5f);
                        Log.D($"[MapRuntimeAssembler] 계단실 방 {r}: 천장 절개 {ceilingCut}조각");
                        stairs++;
                    }
                }

                if (presentFloors.Contains(room.FloorIndex - 1))
                {
                    // 아래층 계단의 도착 개구 — 자기 바닥 (0,3) 절개(바닥 토큰만, 얇은 y 밴드)
                    Bounds voidArea = CellSpanBounds(room, template, 0, 3, 0, 3, floorOrigin, minX, minY, cellMeters);
                    voidArea.Expand(new Vector3(-0.2f, 0f, -0.2f));
                    voidArea.center = new Vector3(voidArea.center.x, floorOrigin.y, voidArea.center.z);
                    voidArea.size = new Vector3(voidArea.size.x, 0.8f, voidArea.size.z);
                    int floorCut = 0;
                    foreach (Renderer renderer in roomInstances[r].GetComponentsInChildren<Renderer>(false))
                    {
                        if (IsFloorRenderer(renderer.name) && renderer.bounds.Intersects(voidArea))
                        {
                            renderer.gameObject.SetActive(false);
                            floorCut++;
                        }
                    }

                    // 도착 클리어런스(중간층 전용) — 자기 계단이 같은 자리에 수직 반복(SSA)이라, 계단 하부
                    // 스커트·마감판이 도착 셀을 y 0~2.95 벽처럼 감싼다(M9-10 실측: PathPartial 의 원인).
                    // 이 방 자기 계단 조각 중 클리어런스 볼륨(도착 셀 XZ × y 0~2.2)과 겹치는 것을 걷어낸다.
                    // 아래층 계단의 램프·플라이트는 별개 인스턴스라 건드리지 않는다
                    int clearanceCut = 0;
                    if (ownStair != null)
                    {
                        // 볼륨은 셀 원본 크기 + 바깥 0.2 확장 — 스커트·마감판이 셀 경계면 위(두께 0)에 놓여
                        // 안쪽으로 줄인 박스로는 비껴간다(M9-10 실측). 절개 대상이 자기 계단 조각뿐이라 과확장 부작용 없음
                        Bounds clearance = CellSpanBounds(room, template, 0, 3, 0, 3, floorOrigin, minX, minY, cellMeters);
                        clearance.center = new Vector3(clearance.center.x, floorOrigin.y + 1.15f, clearance.center.z);
                        clearance.size = new Vector3(clearance.size.x + 0.4f, 2.2f, clearance.size.z + 0.4f);
                        foreach (Renderer renderer in ownStair.GetComponentsInChildren<Renderer>(false))
                        {
                            if (renderer.bounds.min.y < floorOrigin.y + 2.2f && renderer.bounds.Intersects(clearance))
                            {
                                renderer.gameObject.SetActive(false);
                                clearanceCut++;
                            }
                        }
                    }

                    Log.D($"[MapRuntimeAssembler] 계단실 방 {r}: 도착 개구 바닥 절개 {floorCut}·클리어런스 절개 {clearanceCut}조각");
                }
            }

            Log.D($"[MapRuntimeAssembler] 계단 삽입 {stairs}건");
        }

        /// <summary>방 로컬 셀 구간(사각 범위)을 회전 적용해 월드 XZ AABB(바닥면 y)로 만든다.</summary>
        /// <param name="room">배치된 방.</param>
        /// <param name="template">방 템플릿.</param>
        /// <param name="lx0">로컬 셀 X 시작.</param>
        /// <param name="ly0">로컬 셀 Y 시작.</param>
        /// <param name="lx1">로컬 셀 X 끝(포함).</param>
        /// <param name="ly1">로컬 셀 Y 끝(포함).</param>
        /// <param name="floorOrigin">층 평면 원점(맵 루트 + 층 Y).</param>
        /// <param name="minX">맵 최소 셀 X.</param>
        /// <param name="minY">맵 최소 셀 Y.</param>
        /// <param name="cellMeters">셀 실측(m).</param>
        /// <returns>월드 AABB(높이 0).</returns>
        private static Bounds CellSpanBounds(BlueprintRoom room, RoomTemplateDef template, int lx0, int ly0, int lx1, int ly1, Vector3 floorOrigin, int minX, int minY, float cellMeters)
        {
            Bounds bounds = default;
            bool first = true;
            for (int lx = lx0; lx <= lx1; lx++)
            {
                for (int ly = ly0; ly <= ly1; ly++)
                {
                    CellCoord world = CellMath.WorldCell(room, template, new CellCoord(lx, ly));
                    Vector3 center = floorOrigin + new Vector3((world.X - minX + 0.5f) * cellMeters, 0f, (world.Y - minY + 0.5f) * cellMeters);
                    var cell = new Bounds(center, new Vector3(cellMeters, 0f, cellMeters));
                    if (first)
                    {
                        bounds = cell;
                        first = false;
                    }
                    else
                    {
                        bounds.Encapsulate(cell);
                    }
                }
            }

            return bounds;
        }

        /// <summary>영역과 교차하는 렌더러를 끈다 — 바닥 기준 높이 이상에서 시작하는 것만(천장 절개 전용, 벽 보호).</summary>
        /// <param name="roomInstance">방 인스턴스.</param>
        /// <param name="area">절개 영역(월드).</param>
        /// <param name="minStartY">렌더러 bounds.min.y 하한 — 이보다 낮게 시작하는 렌더러(벽·바닥)는 건드리지 않는다.</param>
        /// <returns>끈 렌더러 수.</returns>
        private static int CutRenderersIntersecting(GameObject roomInstance, Bounds area, float minStartY)
        {
            int cut = 0;
            foreach (Renderer renderer in roomInstance.GetComponentsInChildren<Renderer>(false))
            {
                if (renderer.bounds.min.y >= minStartY && renderer.bounds.Intersects(area))
                {
                    renderer.gameObject.SetActive(false);
                    cut++;
                }
            }

            return cut;
        }

        /// <summary>
        /// 계단 인스턴스의 플라이트마다 램프 플레이트를 깐다 — 계단 메시는 복셀화 시 내비가 조각나(M9-9 실측)
        /// 트레드 라인 위 얇은 경사판이 내비의 실체 면이 된다("Stair" 토큰 → 베이커 Walkable 태깅).
        /// </summary>
        /// <param name="stair">계단 인스턴스(자식 Hall_Stairs 플라이트 탐색).</param>
        /// <param name="stairsRoot">플레이트 부모.</param>
        private static void AddStairRamps(GameObject stair, Transform stairsRoot)
        {
            foreach (Transform flight in stair.transform)
            {
                if (!flight.name.StartsWith("Hall_Stairs"))
                {
                    continue;
                }

                Bounds bounds = CombinedRendererBounds(flight.gameObject);
                Vector3 forward = -flight.forward; // 메시가 로컬 -Z 상승이라 진행 방향 = -forward
                float run = Mathf.Abs(Vector3.Dot(bounds.size, forward));
                Vector3 baseFoot = bounds.center - forward * (run * 0.5f);
                baseFoot.y = bounds.min.y;
                Vector3 top = bounds.center + forward * (run * 0.5f);
                top.y = bounds.max.y;

                Vector3 slope = top - baseFoot;
                var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plate.name = $"StairRamp_{stair.name}_{flight.GetSiblingIndex()}";
                // 렌더러는 켜 둔다 — 베이커가 RenderMeshes 수집이라 꺼진 렌더러는 베이크에서 빠진다.
                // 계단 재질을 입혀 트레드 위 얇은 덮개(스트링거)처럼 보이게 한다
                Renderer plateRenderer = plate.GetComponent<Renderer>();
                Renderer flightRenderer = flight.GetComponentInChildren<Renderer>();
                plateRenderer.sharedMaterial = flightRenderer.sharedMaterial;
                plateRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                plate.transform.SetParent(stairsRoot, false);
                plate.transform.localScale = new Vector3(3.8f, 0.02f, slope.magnitude + 0.4f); // 양끝 0.2m 연장 — 바닥·다음 플라이트와 내비 접속
                plate.transform.rotation = Quaternion.LookRotation(slope.normalized);
                plate.transform.position = (baseFoot + top) * 0.5f + Vector3.up * 0.03f;
            }
        }

        /// <summary>계단 꼭대기와 위층 바닥 사이 브리지 플레이트 — 도착 개구 경계의 내비 접속 보장.</summary>
        /// <param name="stair">계단 인스턴스.</param>
        /// <param name="stairsRoot">플레이트 부모.</param>
        /// <param name="rotation">방 회전(상승 방향 산출).</param>
        /// <param name="topY">계단 꼭대기 월드 Y(= 위층 바닥면).</param>
        private static void AddTopBridge(GameObject stair, Transform stairsRoot, Rotation4 rotation, float topY)
        {
            Bounds bounds = CombinedRendererBounds(stair);
            Vector3 ascend = Quaternion.Euler(0f, 90f * (int)rotation, 0f) * Vector3.forward;
            float runExtent = Mathf.Abs(Vector3.Dot(bounds.extents, ascend));
            Vector3 topFace = bounds.center + ascend * runExtent;

            var bridge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bridge.name = $"StairTopBridge_{stair.name}";
            Renderer bridgeRenderer = bridge.GetComponent<Renderer>();
            Renderer stairRenderer = stair.GetComponentInChildren<Renderer>();
            bridgeRenderer.sharedMaterial = stairRenderer.sharedMaterial; // 렌더러 유지 — RenderMeshes 베이크 수집 대상이어야 한다
            bridgeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bridge.transform.SetParent(stairsRoot, false);
            bridge.transform.rotation = Quaternion.LookRotation(ascend);
            bridge.transform.localScale = new Vector3(3.8f, 0.1f, 0.8f); // 경계 양쪽 0.4m 걸침
            bridge.transform.position = new Vector3(topFace.x, topY - 0.05f, topFace.z);
        }

        /// <summary>
        /// 조립된 지오메트리에 물리 레이어를 찍는다. 아트 팩 프리팹은 전부 Default 로 들어오는데,
        /// 게임플레이 판정 여럿이 레이어로 바닥·벽을 구분한다 — 좀비 시야 차단
        /// (<see cref="ZombieSensorySystem"/> obstructionMask = Wall|Door), 소음 벽 감쇠
        /// (NoisePropagationSystem occlusionMask = Wall|Door), 접지 판정(groundMask) 등.
        /// 아트 프리팹 수백 개를 칠하는 대신 조립 시점에 코드로 찍어, 맵젠이 어떤 아트 세트를 쓰든
        /// 규칙이 한 곳에 남게 한다.
        ///
        /// 판별 토큰은 조립기가 이미 기하 판단에 쓰는 것과 같다 — 바닥은 <see cref="IsFloorRenderer"/>,
        /// 벽은 이름의 "Wall"(DisableWallsIntersecting 과 동일 규칙). 새 테마 토큰이 생기면 세 곳을 함께 갱신한다.
        /// 그 외(천장·기둥·소품)는 Default 로 남긴다 — 벽으로 칠하면 소음 차폐가 소품 하나마다
        /// WallAttenuationDb 를 먹어 유인이 방 하나를 못 넘는다.
        /// </summary>
        /// <param name="mapRoot">조립된 맵 루트.</param>
        private static void AssignPhysicsLayers(GameObject mapRoot)
        {
            int groundLayer = LayerMask.NameToLayer("Ground");
            int wallLayer = LayerMask.NameToLayer("Wall");
            if (groundLayer < 0 || wallLayer < 0)
            {
                Log.W($"[MapRuntimeAssembler] 레이어 미정의(Ground={groundLayer} Wall={wallLayer}) — 배정 생략. TagManager 확인 필요");
                return;
            }

            int floors = 0;
            int walls = 0;

            // 비활성 렌더러(개구부로 잘려 꺼진 벽)도 포함한다 — 다시 켜질 때 레이어가 남아 있어야 한다.
            foreach (Renderer renderer in mapRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (IsFloorRenderer(renderer.name))
                {
                    ApplyLayerRecursively(renderer.transform, groundLayer);
                    floors++;
                }
                else if (renderer.name.Contains("Wall"))
                {
                    ApplyLayerRecursively(renderer.transform, wallLayer);
                    walls++;
                }
            }

            Log.D($"[MapRuntimeAssembler] 레이어 배정 — 바닥 {floors} · 벽 {walls}");
        }

        /// <summary>
        /// 대상과 그 하위 전체에 레이어를 적용한다. 물리 질의가 보는 것은 콜라이더의 레이어인데,
        /// 아트 조각에 따라 콜라이더가 렌더러의 자식에 달려 있어 대상만 바꾸면 누락된다.
        /// </summary>
        /// <param name="target">시작 Transform.</param>
        /// <param name="layer">적용할 레이어 인덱스.</param>
        private static void ApplyLayerRecursively(Transform target, int layer)
        {
            target.gameObject.layer = layer;
            for (int i = 0; i < target.childCount; i++)
            {
                ApplyLayerRecursively(target.GetChild(i), layer);
            }
        }

        /// <summary>입구 앵커 방(IsEntranceAnchor 템플릿) 인덱스 — 레이아웃 1단계가 항상 배치한다.</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="templates">템플릿 목록.</param>
        /// <returns>입구 방 인덱스.</returns>
        public static int EntranceRoomIndex(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates)
        {
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                if (FindTemplate(templates, blueprint.Rooms[r].TemplateId).IsEntranceAnchor)
                {
                    return r;
                }
            }

            return -1;
        }

        /// <summary>빈 집 정의 → 층 서수별 Y 오프셋 테이블(단층은 {0: 0}).</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="definition">빈 집 정의.</param>
        /// <returns>층 서수 → Y 오프셋(m).</returns>
        private static Dictionary<int, float> BuildFloorPlanes(MapBlueprint blueprint, MapDefinitionSO definition)
        {
            var planes = new Dictionary<int, float>();
            for (int f = 0; f < blueprint.Floors.Count; f++)
            {
                int floorIndex = blueprint.Floors[f].FloorIndex;
                planes[floorIndex] = FloorGeometry.FloorPlaneY(definition, floorIndex);
            }

            if (planes.Count == 0)
            {
                planes[0] = 0f; // 층 메타 없는 구식 블루프린트 방어 — 전 방 층 0 취급
            }

            return planes;
        }

        /// <summary>방/복도 프리팹을 셀 원점·층 평면에 정렬 배치한다(빌더 PlaceRoom 이관 — 바닥 실측 바운드 정렬·내장 라이트 정책 포함).</summary>
        /// <param name="room">배치할 방.</param>
        /// <param name="prefab">배치 프리팹(변형 선택 완료본).</param>
        /// <param name="mapRoot">맵 루트.</param>
        /// <param name="minX">맵 최소 셀 X(정규화 기준).</param>
        /// <param name="minY">맵 최소 셀 Y.</param>
        /// <param name="cellMeters">셀 실측(m — 레지스트리 값).</param>
        /// <param name="floorY">층 바닥면 Y 오프셋(m — FloorGeometry 누적합).</param>
        /// <param name="instantiate">프리팹 인스턴스화기.</param>
        /// <returns>배치된 인스턴스.</returns>
        private static GameObject PlaceRoom(BlueprintRoom room, GameObject prefab, Transform mapRoot, int minX, int minY, float cellMeters, float floorY, System.Func<GameObject, Transform, GameObject> instantiate)
        {
            GameObject instance = instantiate(prefab, mapRoot);
            // 셀 회전은 시계방향(North→East) — 위에서 본 Unity Y+ 회전과 방향 일치
            instance.transform.localRotation = Quaternion.Euler(0f, 90f * (int)room.Rotation, 0f);
            instance.transform.localPosition = Vector3.zero;

            // 내장 라이트는 여기서 끄지 않는다 — 조립 후 맵 루트의 MapLightCuller 가
            // 카메라 주변 방만 켜 Forward+ 라이트 한도를 지킨다(방 프리팹의 RoomLightGroup 단위)

            Bounds floor = FloorBounds(instance);
            float cell = cellMeters;
            float targetX = mapRoot.position.x + (room.Cell.X - minX) * cell;
            float targetZ = mapRoot.position.z + (room.Cell.Y - minY) * cell;
            instance.transform.position += new Vector3(targetX - floor.min.x, floorY, targetZ - floor.min.z);
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
        /// <param name="floorDef">층 정의(환경 프리팹·셀 실측 원천).</param>
        /// <param name="mapOrigin">맵 원점 월드 좌표.</param>
        /// <param name="minX">맵 최소 셀 X.</param>
        /// <param name="minY">맵 최소 셀 Y.</param>
        private static void PlaceOpening(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, BlueprintEdge edge, int edgeIndex, GameObject[] roomInstances, FloorDefinitionSO floorDef, Vector3 mapOrigin, int minX, int minY, System.Func<GameObject, Transform, GameObject> instantiate)
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

            float cell = floorDef.CellMeters;
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
            CoverOpeningSlits(cutA, profile, boundaryAlongX, floorDef, doorsRoot, mapOrigin.y, edgeIndex, instantiate);
            CoverOpeningSlits(cutB, profile, boundaryAlongX, floorDef, doorsRoot, mapOrigin.y, edgeIndex, instantiate);

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
        /// <param name="floorDef">층 정의(환경 프리팹·셀 실측 원천).</param>
        /// <param name="sealsRoot">봉인 벽 부모.</param>
        /// <param name="mapOrigin">맵 원점 월드 좌표.</param>
        /// <param name="minX">맵 최소 셀 X.</param>
        /// <param name="minY">맵 최소 셀 Y.</param>
        private static void PlaceCorridorSealWall(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, BlueprintEdge edge, FloorDefinitionSO floorDef, Transform sealsRoot, Vector3 mapOrigin, int minX, int minY, System.Func<GameObject, Transform, GameObject> instantiate)
        {
            RoomTemplateDef template = FindTemplate(templates, blueprint.Rooms[edge.RoomA].TemplateId);
            SocketDef socket = FindSocket(template, edge.SocketA);
            CellCoord worldCell = CellMath.WorldCell(blueprint.Rooms[edge.RoomA], template, socket.LocalCell);
            SocketDirection dir = CellMath.RotateDirection(socket.Direction, blueprint.Rooms[edge.RoomA].Rotation);

            float cell = floorDef.CellMeters;
            Vector3 cellCenter = mapOrigin + new Vector3((worldCell.X - minX + 0.5f) * cell, 0f, (worldCell.Y - minY + 0.5f) * cell);
            Vector3 dirVec = DirectionVector(dir);
            Vector3 boundaryCenter = cellCenter + dirVec * (cell * 0.5f);

            bool boundaryAlongX = dir == SocketDirection.North || dir == SocketDirection.South;
            Vector3 along = boundaryAlongX ? Vector3.right : Vector3.forward;
            for (int k = -1; k <= 1; k += 2)
            {
                GameObject piece = instantiate(floorDef.SealWallPrefab, sealsRoot);
                // 프리팹 forward(+Z)가 맵 안쪽(플레이어 시야)을 향하도록 소켓 바깥 방향(dir)의 반대로 회전한다
                piece.transform.rotation = Quaternion.Euler(0f, YawFor(Opposite(dir)), 0f);
                Vector3 target = boundaryCenter + along * k;
                piece.transform.position = target;
                Bounds bounds = RendererBounds(piece);
                piece.transform.position += new Vector3(target.x - bounds.center.x, mapOrigin.y - bounds.min.y, target.z - bounds.center.z);

                // 깊이축은 바깥 면을 경계(바닥 가장자리)에 맞춘다 — 중심 정렬이면 두께 절반이 바닥 밖으로 나온다(빌더 동일 규칙)
                float thickness = boundaryAlongX ? bounds.size.z : bounds.size.x;
                piece.transform.position -= dirVec * (thickness * 0.5f);
                piece.name = $"SealWall_{edge.RoomA}_{edge.SocketA}";
            }
        }

        /// <summary>서로 다른 방·복도가 만나는 노출 코너에 이음 기둥을 세운다(빌더 PlaceCornerColumns 이관). 층 스코프(M9-8) — 이 층 방 구간만 대상.</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="templates">템플릿 목록.</param>
        /// <param name="floorDef">층 정의(환경 프리팹·셀 실측 원천).</param>
        /// <param name="columnsRoot">기둥 부모.</param>
        /// <param name="mapOrigin">이 층 평면 원점(XZ = 맵 원점, Y = 층 바닥면).</param>
        /// <param name="minX">맵 최소 셀 X.</param>
        /// <param name="minY">맵 최소 셀 Y.</param>
        /// <param name="instantiate">프리팹 인스턴스화기.</param>
        /// <param name="roomStart">이 층 방 구간 시작.</param>
        /// <param name="roomCount">이 층 방 수.</param>
        private static void PlaceCornerColumns(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, FloorDefinitionSO floorDef, Transform columnsRoot, Vector3 mapOrigin, int minX, int minY, System.Func<GameObject, Transform, GameObject> instantiate, int roomStart, int roomCount)
        {
            // 정규화 셀 → 소유 방 맵 — **이 층 방만**(층별 격자 분리)
            int roomEnd = roomStart + roomCount;
            var owner = new Dictionary<long, int>();
            int maxX = 0;
            int maxY = 0;
            for (int r = roomStart; r < roomEnd; r++)
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

            // 개구 경계 집합 — 이 셀 쌍 사이 경계는 4m 슬롯 전체가 열려 있어 벽선이 아니다.
            // 방↔방 개방 통로 경계는 따로 모은다 — 평행 이음(이중벽)이 통로로 끊기는 잼 지점 판정용(문 간선은 문틀이 덮는다)
            var openPairs = new HashSet<(long, long)>();
            var roomPassagePairs = new HashSet<(long, long)>();
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                BlueprintEdge edge = blueprint.Edges[e];
                if (edge.RoomB < 0 || edge.State == EdgeState.BlockedWall || edge.SocketA == -2
                    || edge.RoomA < roomStart || edge.RoomA >= roomEnd)
                {
                    continue; // 타 층·수직 간선은 이 층 벽선 판정과 무관
                }

                RoomTemplateDef template = FindTemplate(templates, blueprint.Rooms[edge.RoomA].TemplateId);
                SocketDef socket = FindSocket(template, edge.SocketA);
                CellCoord worldCell = CellMath.WorldCell(blueprint.Rooms[edge.RoomA], template, socket.LocalCell);
                SocketDirection dir = CellMath.RotateDirection(socket.Direction, blueprint.Rooms[edge.RoomA].Rotation);
                CellCoord facing = StepCell(worldCell, dir);
                (long, long) pair = CellPair(worldCell.X - minX, worldCell.Y - minY, facing.X - minX, facing.Y - minY);
                openPairs.Add(pair);
                if (edge.State == EdgeState.OpenPassage
                    && !template.IsCorridor
                    && !FindTemplate(templates, blueprint.Rooms[edge.RoomB].TemplateId).IsCorridor)
                {
                    roomPassagePairs.Add(pair);
                }
            }

            // 격자점별 벽선 판정 — L자(직각 2개) + 잼(벽선 1개가 방↔방 통로로 끊기는 점) 채택(빌더 동일 규칙)
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

                    // 잼 — 평행 이음의 이중벽 단면이 통로에서 노출되는 지점(벽선의 직선 연장이 방↔방 개방 통로)
                    bool jamb = false;
                    if (lineCount == 1)
                    {
                        if (north)
                        {
                            jamb = roomPassagePairs.Contains(CellPair(px - 1, py - 1, px, py - 1));
                        }
                        else if (south)
                        {
                            jamb = roomPassagePairs.Contains(CellPair(px - 1, py, px, py));
                        }
                        else if (east)
                        {
                            jamb = roomPassagePairs.Contains(CellPair(px - 1, py, px - 1, py - 1));
                        }
                        else if (west)
                        {
                            jamb = roomPassagePairs.Contains(CellPair(px, py, px, py - 1));
                        }
                    }

                    if (!jamb && (lineCount != 2 || (north && south) || (east && west)))
                    {
                        continue; // 벽 없음·순수 벽 끝·일직선 통과·T자·십자
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

            // 슬릿 충진 기둥 위치 수집 — 같은 이음을 코너 기둥이 겹쳐 가리는 중복 방지(0.9m 내 생략, 빌더 동일 규칙)
            var slitColumns = new List<Vector3>();
            Transform doors = columnsRoot.parent.Find("Doors");
            for (int i = 0; i < doors.childCount; i++)
            {
                if (doors.GetChild(i).name.StartsWith("SlitColumn"))
                {
                    slitColumns.Add(doors.GetChild(i).position);
                }
            }

            float cell = floorDef.CellMeters;
            foreach (long key in SortedKeys(columnPoints))
            {
                int px = (int)(key >> 32);
                int py = (int)(uint)(key & 0xFFFFFFFF);
                Vector3 target = mapOrigin + new Vector3(px * cell, 0f, py * cell);
                bool nearSlit = false;
                for (int i = 0; i < slitColumns.Count && !nearSlit; i++)
                {
                    float dx = slitColumns[i].x - target.x;
                    float dz = slitColumns[i].z - target.z;
                    nearSlit = dx * dx + dz * dz < 0.81f && Mathf.Abs(slitColumns[i].y - target.y) < 3f; // 같은 층(Y 근접)만 중복으로 본다(M9-8)
                }

                if (nearSlit)
                {
                    continue;
                }

                GameObject column = instantiate(floorDef.CornerColumnPrefab, columnsRoot);

                // 프리팹 pivot 쏠림 대비 — 실측 바운드 중심을 격자점(경계 교점)에 정렬(빌더 동일 규칙)
                column.transform.position = target;
                Bounds columnBounds = CombinedRendererBounds(column);
                column.transform.position += new Vector3(target.x - columnBounds.center.x, 0f, target.z - columnBounds.center.z);
                column.name = $"Column_{px}_{py}";
            }
        }

        /// <summary>인스턴스의 렌더러 합성 월드 바운드 — pivot 쏠린 프리팹(기둥)을 목표점에 중앙 정렬할 때 쓴다.</summary>
        /// <param name="instance">대상 인스턴스.</param>
        /// <returns>합성 월드 바운드.</returns>
        private static Bounds CombinedRendererBounds(GameObject instance)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(false);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        /// <summary>
        /// 탈출문 개구 — 잎 방의 바깥 향 소켓(맞은편 = 맵 바깥) 벽을 문 슬롯만큼 절단하고
        /// 스포너용 앵커 ReturnAnchor_e{n} 을 남긴다. 상대 방이 없으므로 절단은 한쪽뿐이고,
        /// 문은 열리지 않아(홀드 즉시 탈출) 너머가 비어 있어도 무방하다.
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="templates">템플릿 목록.</param>
        /// <param name="edge">탈출문 간선(RoomB = -1).</param>
        /// <param name="edgeIndex">간선 인덱스(앵커 이름).</param>
        /// <param name="roomInstances">방 인스턴스 배열.</param>
        /// <param name="floorDef">층 정의(환경 프리팹·셀 실측 원천).</param>
        /// <param name="doorsRoot">앵커·충진 조각 컨테이너.</param>
        /// <param name="mapOrigin">맵 원점 월드 좌표.</param>
        /// <param name="minX">맵 최소 셀 X.</param>
        /// <param name="minY">맵 최소 셀 Y.</param>
        private static void PlaceOuterExitOpening(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, BlueprintEdge edge, int edgeIndex, GameObject[] roomInstances, FloorDefinitionSO floorDef, Transform doorsRoot, Vector3 mapOrigin, int minX, int minY, System.Func<GameObject, Transform, GameObject> instantiate)
        {
            RoomTemplateDef template = FindTemplate(templates, blueprint.Rooms[edge.RoomA].TemplateId);
            SocketDef socket = FindSocket(template, edge.SocketA);
            CellCoord worldCell = CellMath.WorldCell(blueprint.Rooms[edge.RoomA], template, socket.LocalCell);
            SocketDirection dir = CellMath.RotateDirection(socket.Direction, blueprint.Rooms[edge.RoomA].Rotation);

            float cell = floorDef.CellMeters;
            Vector3 cellCenter = mapOrigin + new Vector3((worldCell.X - minX + 0.5f) * cell, 0f, (worldCell.Y - minY + 0.5f) * cell);
            Vector3 gateCenter = cellCenter + DirectionVector(dir) * (cell * 0.5f);
            bool boundaryAlongX = dir == SocketDirection.North || dir == SocketDirection.South;
            var gate = new Bounds(gateCenter + Vector3.up * 3f, boundaryAlongX ? new Vector3(3.9f, 5.8f, 1.6f) : new Vector3(1.6f, 5.8f, 3.9f));
            var cut = new List<Bounds>();
            DisableWallsIntersecting(roomInstances[edge.RoomA], gate, boundaryAlongX, cut);

            // 잔여 슬릿 충진 — 공칭 4m 슬롯 프로파일(문 간선과 동일 수치)
            float cellCenterAxis = boundaryAlongX ? gateCenter.x : gateCenter.z;
            Vector3 profileCenter = boundaryAlongX
                ? new Vector3(cellCenterAxis, mapOrigin.y + 3f, gateCenter.z)
                : new Vector3(gateCenter.x, mapOrigin.y + 3f, cellCenterAxis);
            var profile = new Bounds(profileCenter, boundaryAlongX ? new Vector3(4f, 6f, 1.6f) : new Vector3(1.6f, 6f, 4f));
            CoverOpeningSlits(cut, profile, boundaryAlongX, floorDef, doorsRoot, mapOrigin.y, edgeIndex, instantiate);

            var anchor = new GameObject($"ReturnAnchor_e{edgeIndex}");
            anchor.transform.SetParent(doorsRoot, false);
            anchor.transform.position = gateCenter;
            anchor.transform.rotation = Quaternion.Euler(0f, YawFor(dir), 0f);
        }

        /// <summary>
        /// 봉인된 복도 소켓(hallway_x2 반쪽 입)의 맞은편 셀이 이 복도와 이미 연결된 방이면
        /// Seal 대신 그 방 벽을 절단해 전폭 개구로 만든다 — 같은 두 공간이라 그래프 연결성 불변.
        /// 맞은편이 빈 공간·미연결 방이면 false(호출자가 Seal 배치 — 외부 구멍·그래프 밖 연결 방지).
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="templates">템플릿 목록.</param>
        /// <param name="sealedEdge">봉인 간선(RoomB = -1, RoomA = 복도).</param>
        /// <param name="edgeIndex">간선 인덱스(충진 조각 이름용).</param>
        /// <param name="roomInstances">방 인스턴스 배열.</param>
        /// <param name="floorDef">층 정의(환경 프리팹·셀 실측 원천).</param>
        /// <param name="doorsRoot">충진 조각 컨테이너.</param>
        /// <param name="mapOrigin">맵 원점 월드 좌표.</param>
        /// <param name="minX">맵 최소 셀 X.</param>
        /// <param name="minY">맵 최소 셀 Y.</param>
        /// <returns>전폭 개방 성공 여부(false = Seal 필요).</returns>
        private static bool TryOpenSealedHalfMouth(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, BlueprintEdge sealedEdge, int edgeIndex, GameObject[] roomInstances, FloorDefinitionSO floorDef, Transform doorsRoot, Vector3 mapOrigin, int minX, int minY, System.Func<GameObject, Transform, GameObject> instantiate)
        {
            int corridorRoom = sealedEdge.RoomA;
            RoomTemplateDef corridorTemplate = FindTemplate(templates, blueprint.Rooms[corridorRoom].TemplateId);
            SocketDef socket = FindSocket(corridorTemplate, sealedEdge.SocketA);
            CellCoord worldCell = CellMath.WorldCell(blueprint.Rooms[corridorRoom], corridorTemplate, socket.LocalCell);
            SocketDirection dir = CellMath.RotateDirection(socket.Direction, blueprint.Rooms[corridorRoom].Rotation);
            CellCoord facing = StepCell(worldCell, dir);

            // 맞은편 셀의 소유 방(비복도) 탐색 — 복도끼리는 인접 의무 연결이 이미 처리한다
            int ownerRoom = -1;
            for (int r = 0; r < blueprint.Rooms.Count && ownerRoom < 0; r++)
            {
                RoomTemplateDef template = FindTemplate(templates, blueprint.Rooms[r].TemplateId);
                if (template.IsCorridor)
                {
                    continue;
                }

                (int w, int h) = CellMath.RotatedSize(template.WidthCells, template.HeightCells, blueprint.Rooms[r].Rotation);
                if (facing.X >= blueprint.Rooms[r].Cell.X && facing.X < blueprint.Rooms[r].Cell.X + w
                    && facing.Y >= blueprint.Rooms[r].Cell.Y && facing.Y < blueprint.Rooms[r].Cell.Y + h)
                {
                    ownerRoom = r;
                }
            }

            if (ownerRoom < 0)
            {
                return false;
            }

            // 이 복도와 이미 연결된 방만 개방 — 미연결 방을 뚫으면 그래프에 없는 연결이 생긴다
            bool connected = false;
            for (int e = 0; e < blueprint.Edges.Count && !connected; e++)
            {
                BlueprintEdge other = blueprint.Edges[e];
                connected = other.RoomB >= 0 && other.State != EdgeState.BlockedWall
                    && ((other.RoomA == corridorRoom && other.RoomB == ownerRoom)
                        || (other.RoomA == ownerRoom && other.RoomB == corridorRoom));
            }

            if (!connected)
            {
                return false;
            }

            // 방 벽 게이트 절단 + 잔여 슬릿 충진 — 개방 통로 프로파일(4×6m, 셀 중심 고정)과 동일 수치
            float cell = floorDef.CellMeters;
            Vector3 cellCenter = mapOrigin + new Vector3((worldCell.X - minX + 0.5f) * cell, 0f, (worldCell.Y - minY + 0.5f) * cell);
            Vector3 dirVec = DirectionVector(dir);
            Vector3 gateCenter = cellCenter + dirVec * (cell * 0.5f);
            bool boundaryAlongX = dir == SocketDirection.North || dir == SocketDirection.South;
            var gate = new Bounds(gateCenter + Vector3.up * 3f, boundaryAlongX ? new Vector3(3.9f, 5.8f, 1.6f) : new Vector3(1.6f, 5.8f, 3.9f));
            var cut = new List<Bounds>();
            DisableWallsIntersecting(roomInstances[ownerRoom], gate, boundaryAlongX, cut);

            float cellCenterAxis = boundaryAlongX ? gateCenter.x : gateCenter.z;
            Vector3 profileCenter = boundaryAlongX
                ? new Vector3(cellCenterAxis, mapOrigin.y + 3f, gateCenter.z)
                : new Vector3(gateCenter.x, mapOrigin.y + 3f, cellCenterAxis);
            var profile = new Bounds(profileCenter, boundaryAlongX ? new Vector3(4f, 6f, 1.6f) : new Vector3(1.6f, 6f, 4f));
            CoverOpeningSlits(cut, profile, boundaryAlongX, floorDef, doorsRoot, mapOrigin.y, edgeIndex, instantiate);
            return true;
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
        /// <param name="floorDef">층 정의(환경 프리팹·셀 실측 원천).</param>
        /// <param name="doorsRoot">기둥 부모.</param>
        /// <param name="floorY">바닥 월드 Y.</param>
        /// <param name="edgeIndex">간선 인덱스(추적용 이름 표기).</param>
        /// <param name="instantiate">프리팹 인스턴스화기.</param>
        private static void CoverOpeningSlits(List<Bounds> sideCuts, Bounds profile, bool boundaryAlongX, FloorDefinitionSO floorDef, Transform doorsRoot, float floorY, int edgeIndex, System.Func<GameObject, Transform, GameObject> instantiate)
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
                    GameObject column = instantiate(floorDef.CornerColumnPrefab, doorsRoot);

                    // 코너 기둥과 동일 — pivot 쏠림 보정(실측 바운드 중심을 슬릿 중심에 정렬)
                    Vector3 target = boundaryAlongX
                        ? new Vector3(axis, floorY, perpCenter)
                        : new Vector3(perpCenter, floorY, axis);
                    column.transform.position = target;
                    Bounds slitBounds = CombinedRendererBounds(column);
                    column.transform.position += new Vector3(target.x - slitBounds.center.x, 0f, target.z - slitBounds.center.z);
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

        /// <summary>인스턴스의 바닥 타일(테마 바닥 토큰 매칭) 합산 월드 바운드 — 없으면 전체 렌더러 바운드 폴백.</summary>
        /// <param name="instance">방 인스턴스.</param>
        /// <returns>바닥 월드 바운드.</returns>
        private static Bounds FloorBounds(GameObject instance)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(false);
            Bounds bounds = default;
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!IsFloorRenderer(renderers[i].name))
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

        /// <summary>바닥 타일 렌더러 판정 — 테마별 바닥 토큰(Hall/Wards). 새 테마 세트 도입 시 여기와 빌더의 동일 토큰을 함께 갱신한다(셋업 린트는 이 함수를 공유 — 에디터 어셈블리 접근용 public).</summary>
        /// <param name="rendererName">렌더러 이름.</param>
        /// <returns>바닥 타일 여부.</returns>
        public static bool IsFloorRenderer(string rendererName)
        {
            return rendererName.Contains("Hall_Floor") || rendererName.Contains("Wards_Floor");
        }

        /// <summary>문 조립체에서 여닫이 문짝(Hall_Door_L/R)을 제외한 문틀 월드 바운드 — 스폰 정렬 기준(에디터 빌더 FrameBounds 동일 규칙).</summary>
        /// <param name="doorInstance">문 인스턴스.</param>
        /// <returns>문틀 월드 바운드.</returns>
        public static Bounds FrameBounds(GameObject doorInstance)
        {
            Renderer[] renderers = doorInstance.GetComponentsInChildren<Renderer>(false);
            Bounds bounds = default;
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].name.StartsWith("Hall_Door_L") || renderers[i].name.StartsWith("Hall_Door_R"))
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

            return found ? bounds : RendererBounds(doorInstance);
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
        public static (int, int) MinCellBounds(MapBlueprint blueprint)
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

        /// <summary>기본 프리팹 인스턴스화기 — 런타임 경로(Object.Instantiate). 에디터 빌더는 PrefabUtility 경로를 주입한다.</summary>
        /// <param name="prefab">인스턴스화할 프리팹.</param>
        /// <param name="parent">부모 Transform.</param>
        /// <returns>인스턴스.</returns>
        private static GameObject DefaultInstantiate(GameObject prefab, Transform parent)
        {
            return Object.Instantiate(prefab, parent, false);
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
        public static RoomTemplateDef FindTemplate(IReadOnlyList<RoomTemplateDef> templates, string templateId)
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
        public static SocketDef FindSocket(RoomTemplateDef template, int socketId)
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
