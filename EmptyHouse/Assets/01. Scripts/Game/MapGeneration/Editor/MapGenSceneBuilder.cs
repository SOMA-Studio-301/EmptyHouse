using System.Collections.Generic;
using Border.Core;
using EmptyHouse.MapGen.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EmptyHouse.MapGen.Editor
{
    /// <summary>
    /// 예시 맵 씬 빌더 — MapGenerator.Generate 블루프린트를 실제 방 프리팹으로 활성 씬에 조립한다(AC-21 — 동일 생성 라이브러리).
    /// 그레이박스 시연용: 방 배치·문 개구부(벽 비활성화)·문 프리팹·스폰 표식까지. NavMesh·게임 로직 연결은 어댑터(P5) 소관.
    /// 재실행 시 기존 GeneratedMaps 루트를 통째로 지우고 다시 만든다(멱등 — 원복 = 루트 삭제).
    /// </summary>
    public static class MapGenSceneBuilder
    {
        private const int mapCount = 5; // 예시 맵 수
        private const int roomsMin = 28; // 방 30개 기준 하한
        private const int roomsMax = 32; // 방 30개 기준 상한
        private const int baseSeed = 101; // 시드 탐색 시작값(재현 고정)
        private const float mapSpacing = 280f; // 맵 루트 간 X 간격(m) — 32방 바운드(~160m)와 여유
        private const float mapZOffset = -400f; // 수작업 맵(Z 0~160)과 겹치지 않는 남쪽 오프셋
        private const string rootName = "GeneratedMaps"; // 생성물 루트 이름(통째 삭제 = 원복)
        private const string markerMaterialFolder = "Assets/02. Prefab/Map/MapGenMarkers"; // 표식 머티리얼 폴더

        /// <summary>
        /// 활성 씬에 방 30개 기준 예시 맵 5개를 생성한다 — baseSeed 부터 성공 시드를 5개 채택해
        /// X 방향으로 나란히 배치하고 씬을 저장한다.
        /// </summary>
        [MenuItem("Tools/Map/절차 예시 맵 5개 생성")]
        public static void BuildFiveExampleMaps()
        {
            Log.D("[MapGenSceneBuilder] BuildFiveExampleMaps");
            if (EditorApplication.isPlaying)
            {
                Log.W("[MapGenSceneBuilder] 플레이 모드에서는 실행할 수 없다 — 정지 후 재실행.");
                return;
            }

            UnityEngine.SceneManagement.Scene activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(activeScene.path))
            {
                Log.W("[MapGenSceneBuilder] Untitled 씬은 저장 경로가 없어 조립 결과를 보존할 수 없다 — 씬을 먼저 저장한 뒤 재실행.");
                return;
            }

            // 비활성 루트·과거 중복 루트까지 전부 제거(멱등) — GameObject.Find 는 비활성을 못 찾고 타 씬까지 검색한다
            foreach (GameObject sceneRoot in activeScene.GetRootGameObjects())
            {
                if (sceneRoot.name == rootName)
                {
                    Object.DestroyImmediate(sceneRoot);
                }
            }

            var root = new GameObject(rootName);
            var generator = new MapGenerator();
            List<RoomTemplateDef> templates = PrefabRoomTemplates.Create();

            int built = 0;
            int seed = baseSeed;
            var summary = new System.Text.StringBuilder();
            while (built < mapCount && seed < baseSeed + 40)
            {
                var genParams = new MapGenParams
                {
                    Seed = seed,
                    RoomsTotalMin = roomsMin,
                    RoomsTotalMax = roomsMax,
                    EnabledZombieTypes = ZombieTypeMask.Walker | ZombieTypeMask.Listener | ZombieTypeMask.Watcher,
                };
                MapGenResult result = generator.Generate(genParams, templates);
                if (!result.Success)
                {
                    Log.D($"[MapGenSceneBuilder] 시드 {seed} 실패 — 다음 시드 시도: {string.Join(" / ", result.FailReasons)}");
                    seed++;
                    continue;
                }

                Vector3 offset = new Vector3(built * mapSpacing, 0f, mapZOffset);
                BuildMap(result.Blueprint, templates, root.transform, offset, built);
                summary.AppendLine($"맵 {built}: 시드 {seed} · 방 {result.Blueprint.Rooms.Count} · 리롤 {result.RerollCount} · 경고 {result.LastReport.Warnings.Count}");
                built++;
                seed++;
            }

            EditorSceneManager.MarkSceneDirty(root.scene);
            if (!EditorSceneManager.SaveScene(root.scene))
            {
                Log.E($"[MapGenSceneBuilder] 씬 저장 실패 — 수동 저장(Ctrl+S) 필요. path='{root.scene.path}'");
            }

            Log.D($"[MapGenSceneBuilder] 완료 — {built}/{mapCount}개 생성\n{summary}");
        }

        /// <summary>블루프린트 하나를 방 프리팹·문·스폰 표식으로 조립한다.</summary>
        /// <param name="blueprint">조립할 블루프린트.</param>
        /// <param name="templates">생성에 사용한 템플릿 목록.</param>
        /// <param name="parent">루트 부모.</param>
        /// <param name="offset">맵 원점 월드 오프셋.</param>
        /// <param name="index">맵 번호(이름용).</param>
        private static void BuildMap(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, Transform parent, Vector3 offset, int index)
        {
            Log.D($"[MapGenSceneBuilder] BuildMap {index} 시드={blueprint.Meta.Seed}");
            var mapRoot = new GameObject($"GeneratedMap_{index}_Seed{blueprint.Meta.Seed}");
            mapRoot.transform.SetParent(parent, false);
            mapRoot.transform.position = offset;

            // 셀 바운드 정규화 — 맵 로컬 (0,0) 이 최소 셀에 오게
            (int minX, int minY) = MinCellBounds(blueprint, templates);

            // 방 배치
            var roomInstances = new GameObject[blueprint.Rooms.Count];
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                roomInstances[r] = PlaceRoom(blueprint.Rooms[r], FindTemplate(templates, blueprint.Rooms[r].TemplateId), mapRoot.transform, minX, minY);
            }

            // 간선 처리 — 개구부 벽 비활성화 + 문 프리팹 + 복도 개구 봉인 벽
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
                        PlaceCorridorSealWall(blueprint, templates, edge, sealsRoot.transform, mapRoot.transform.position, minX, minY);
                    }

                    continue;
                }

                if (edge.State == EdgeState.BlockedWall)
                {
                    continue;
                }

                PlaceOpening(blueprint, templates, edge, roomInstances, doorsRoot.transform, mapRoot.transform.position, minX, minY);
            }

            // 코너 기둥 — 서로 다른 방·복도가 만나는 노출 코너의 벽 이음 갭을 가린다
            var columnsRoot = new GameObject("Columns");
            columnsRoot.transform.SetParent(mapRoot.transform, false);
            PlaceCornerColumns(blueprint, templates, columnsRoot.transform, mapRoot.transform.position, minX, minY);

            // 스폰 표식
            var spawnsRoot = new GameObject("Spawns");
            spawnsRoot.transform.SetParent(mapRoot.transform, false);
            for (int s = 0; s < blueprint.Spawns.Count; s++)
            {
                PlaceSpawnMarker(blueprint, templates, blueprint.Spawns[s], spawnsRoot.transform, mapRoot.transform.position, minX, minY);
            }
        }

        /// <summary>방 프리팹을 인스턴스화하고 바닥 타일(Hall_Floor) 실측 바운드로 셀 원점에 정렬한다.</summary>
        /// <param name="room">배치할 방.</param>
        /// <param name="template">방 템플릿.</param>
        /// <param name="mapRoot">맵 루트.</param>
        /// <param name="minX">맵 최소 셀 X(정규화 기준).</param>
        /// <param name="minY">맵 최소 셀 Y.</param>
        /// <returns>배치된 인스턴스.</returns>
        private static GameObject PlaceRoom(BlueprintRoom room, RoomTemplateDef template, Transform mapRoot, int minX, int minY)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabRoomTemplates.PrefabPaths[template.TemplateId]);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetParent(mapRoot, false);
            // 셀 회전은 시계방향(North→East) — 위에서 본 Unity Y+ 회전과 방향 일치
            instance.transform.localRotation = Quaternion.Euler(0f, 90f * (int)room.Rotation, 0f);
            instance.transform.localPosition = Vector3.zero;

            // 프리팹 내장 라이트 비활성화 — 맵 5개면 라이트 300개+ 로 URP Forward+ 한도를 넘어
            // 씬 뷰 렌더 에러(ZBinningJob)가 난다. 그레이박스 시연에 라이팅은 불필요(P5 소관)
            foreach (Light light in instance.GetComponentsInChildren<Light>(true))
            {
                light.enabled = false;
            }

            Bounds floor = FloorBounds(instance);
            float targetX = mapRoot.position.x + (room.Cell.X - minX) * PrefabRoomTemplates.CellMeters;
            float targetZ = mapRoot.position.z + (room.Cell.Y - minY) * PrefabRoomTemplates.CellMeters;
            instance.transform.position += new Vector3(targetX - floor.min.x, 0f, targetZ - floor.min.z);
            return instance;
        }

        /// <summary>
        /// 간선 개구부를 만든다 — 경계 게이트 박스와 교차하는 양쪽 방의 벽 모듈을 비활성화하고,
        /// 문 상태(열림/잠김)에 맞는 문 프리팹을 배치한다(잠긴 문 이름에 자물쇠 번호·용도 표기).
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="templates">템플릿 목록.</param>
        /// <param name="edge">처리할 연결 간선.</param>
        /// <param name="roomInstances">방 인스턴스 배열.</param>
        /// <param name="doorsRoot">문 부모.</param>
        /// <param name="mapOrigin">맵 원점 월드 좌표.</param>
        /// <param name="minX">맵 최소 셀 X.</param>
        /// <param name="minY">맵 최소 셀 Y.</param>
        private static void PlaceOpening(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, BlueprintEdge edge, GameObject[] roomInstances, Transform doorsRoot, Vector3 mapOrigin, int minX, int minY)
        {
            RoomTemplateDef templateA = FindTemplate(templates, blueprint.Rooms[edge.RoomA].TemplateId);
            SocketDef socketA = FindSocket(templateA, edge.SocketA);
            CellCoord worldCell = CellMath.WorldCell(blueprint.Rooms[edge.RoomA], templateA, socketA.LocalCell);
            SocketDirection dir = CellMath.RotateDirection(socketA.Direction, blueprint.Rooms[edge.RoomA].Rotation);

            float cell = PrefabRoomTemplates.CellMeters;
            Vector3 cellCenter = mapOrigin + new Vector3((worldCell.X - minX + 0.5f) * cell, 0f, (worldCell.Y - minY + 0.5f) * cell);
            Vector3 dirVec = DirectionVector(dir);
            Vector3 gateCenter = cellCenter + dirVec * (cell * 0.5f);

            // 경계선 방향 폭 3.9m 게이트 — 셀(4m) 안의 2m 벽 세그먼트 2장을 전부 자르되(문 조립체가 4m 전폭)
            // 0.05m 여유로 이웃 셀 세그먼트는 보호한다
            bool boundaryAlongX = dir == SocketDirection.North || dir == SocketDirection.South;
            var gate = new Bounds(gateCenter + Vector3.up * 3f, boundaryAlongX ? new Vector3(3.9f, 8f, 1.6f) : new Vector3(1.6f, 8f, 3.9f));
            var cutA = new List<Bounds>();
            var cutB = new List<Bounds>();
            DisableWallsIntersecting(roomInstances[edge.RoomA], gate, boundaryAlongX, cutA);
            DisableWallsIntersecting(roomInstances[edge.RoomB], gate, boundaryAlongX, cutB);

            // 문 기준점 = 절단 개구 실측 중심(셀 중심 ±0.4m 클램프) — 방 프리팹의 벽 세그먼트 그리드가
            // 바닥 그리드에서 프리팹별 0.2~0.8m 어긋나게 저작돼 있어 셀 중심 고정이면 문틀 옆에 슬릿이 남고,
            // 완전 추종이면 문이 소켓 셀에서 벗어난다. 잔여 슬릿은 아래 충진 슬래브가 봉합한다.
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

            if (edge.State == EdgeState.OpenPassage)
            {
                return; // 통로 — 문 없이 개구부만
            }

            string doorPath = edge.State == EdgeState.DoorLocked ? PrefabRoomTemplates.DoorClosedPath : PrefabRoomTemplates.DoorOpenedPath;
            var doorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(doorPath);
            var door = (GameObject)PrefabUtility.InstantiatePrefab(doorPrefab);
            door.transform.SetParent(doorsRoot, false);
            door.transform.position = gateCenter;
            door.transform.rotation = Quaternion.Euler(0f, YawFor(dir), 0f);

            // 문틀 기준 정렬 — 젖혀진 문짝(Hall_Door_L/R)이 바운드를 한쪽으로 0.5m 끌고 가므로
            // 문짝을 제외한 문틀 바운드 중심을 게이트 중심(셀 경계선)에 맞춘다
            Bounds doorBounds = FrameBounds(door);
            door.transform.position += new Vector3(gateCenter.x - doorBounds.center.x, 0f, gateCenter.z - doorBounds.center.z);
            door.name = edge.State == EdgeState.DoorLocked ? $"Door_Locked_{edge.LockNumber}_{edge.LockKind}" : "Door_Open";

            // 문틀보다 넓게 잘린 개구의 잔여 슬릿을 평면별로 봉합(프리팹 벽 그리드 오프셋 잔차)
            Bounds placedFrame = FrameBounds(door);
            FillDoorSideGaps(cutA, placedFrame, boundaryAlongX, doorsRoot, mapOrigin.y);
            FillDoorSideGaps(cutB, placedFrame, boundaryAlongX, doorsRoot, mapOrigin.y);
        }

        /// <summary>스폰 표식(색 구체)을 마커 전역 셀 중심에 놓는다 — 열쇠는 KeyNumber 를 이름에 표기.</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="templates">템플릿 목록.</param>
        /// <param name="spawn">표식할 스폰.</param>
        /// <param name="spawnsRoot">표식 부모.</param>
        /// <param name="mapOrigin">맵 원점 월드 좌표.</param>
        /// <param name="minX">맵 최소 셀 X.</param>
        /// <param name="minY">맵 최소 셀 Y.</param>
        private static void PlaceSpawnMarker(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, BlueprintSpawn spawn, Transform spawnsRoot, Vector3 mapOrigin, int minX, int minY)
        {
            RoomTemplateDef template = FindTemplate(templates, blueprint.Rooms[spawn.RoomIndex].TemplateId);
            MarkerDef marker = FindMarker(template, spawn.MarkerId);
            CellCoord worldCell = CellMath.WorldCell(blueprint.Rooms[spawn.RoomIndex], template, marker.LocalCell);

            float cell = PrefabRoomTemplates.CellMeters;
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(sphere.GetComponent<Collider>());
            sphere.transform.SetParent(spawnsRoot, false);
            sphere.transform.position = mapOrigin + new Vector3((worldCell.X - minX + 0.5f) * cell, 1.2f, (worldCell.Y - minY + 0.5f) * cell);
            sphere.transform.localScale = Vector3.one * (spawn.Kind == SpawnKind.HerdArea ? 2f : 1.1f);
            sphere.name = spawn.Kind == SpawnKind.Key ? $"Spawn_Key_{spawn.KeyNumber}" : $"Spawn_{spawn.Kind}";
            sphere.GetComponent<Renderer>().sharedMaterial = MarkerMaterial(spawn.Kind);
        }

        /// <summary>
        /// 게이트와 교차하며 통로를 실제로 막는 방향의 벽 모듈만 비활성화한다 —
        /// 통로 축과 평행한 옆벽은 보존하고, 경계 축 겹침이 0.5m 이하인 모서리 조각도 보존한다
        /// (프리팹 벽 그리드가 셀 그리드와 ±0.2m 어긋나 있어 교차 판정만 쓰면 세그먼트 3장이 잘려 6m 구멍이 난다).
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

        /// <summary>봉인된 복도 소켓의 개구(4m)를 벽 프리팹 2장으로 물리적으로 막는다 — 복도 단부에는 벽이 없다.</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="templates">템플릿 목록.</param>
        /// <param name="edge">봉인 간선(RoomB = -1, 복도 소켓).</param>
        /// <param name="sealsRoot">봉인 벽 부모.</param>
        /// <param name="mapOrigin">맵 원점 월드 좌표.</param>
        /// <param name="minX">맵 최소 셀 X.</param>
        /// <param name="minY">맵 최소 셀 Y.</param>
        private static void PlaceCorridorSealWall(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, BlueprintEdge edge, Transform sealsRoot, Vector3 mapOrigin, int minX, int minY)
        {
            RoomTemplateDef template = FindTemplate(templates, blueprint.Rooms[edge.RoomA].TemplateId);
            SocketDef socket = FindSocket(template, edge.SocketA);
            CellCoord worldCell = CellMath.WorldCell(blueprint.Rooms[edge.RoomA], template, socket.LocalCell);
            SocketDirection dir = CellMath.RotateDirection(socket.Direction, blueprint.Rooms[edge.RoomA].Rotation);

            float cell = PrefabRoomTemplates.CellMeters;
            Vector3 cellCenter = mapOrigin + new Vector3((worldCell.X - minX + 0.5f) * cell, 0f, (worldCell.Y - minY + 0.5f) * cell);
            Vector3 dirVec = DirectionVector(dir);
            Vector3 boundaryCenter = cellCenter + dirVec * (cell * 0.5f);

            bool boundaryAlongX = dir == SocketDirection.North || dir == SocketDirection.South;
            Vector3 along = boundaryAlongX ? Vector3.right : Vector3.forward;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabRoomTemplates.SealWallPath);
            for (int k = -1; k <= 1; k += 2)
            {
                var piece = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                piece.transform.SetParent(sealsRoot, false);
                piece.transform.rotation = Quaternion.Euler(0f, boundaryAlongX ? 0f : 90f, 0f); // 벽 기본 긴 축 = X
                Vector3 target = boundaryCenter + along * k;
                piece.transform.position = target;
                Bounds bounds = RendererBounds(piece);
                piece.transform.position += new Vector3(target.x - bounds.center.x, mapOrigin.y - bounds.min.y, target.z - bounds.center.z);
                piece.name = $"SealWall_{edge.RoomA}_{edge.SocketA}";
            }
        }

        /// <summary>
        /// 서로 다른 방·복도가 만나는 노출 코너(주변 4셀에 빈 셀 존재)마다 코너 기둥을 세워
        /// 프리팹 벽 이음의 세로 갭을 가린다. 단일 방 자체 코너는 프리팹이 이미 마감돼 있어 제외.
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="templates">템플릿 목록.</param>
        /// <param name="columnsRoot">기둥 부모.</param>
        /// <param name="mapOrigin">맵 원점 월드 좌표.</param>
        /// <param name="minX">맵 최소 셀 X.</param>
        /// <param name="minY">맵 최소 셀 Y.</param>
        private static void PlaceCornerColumns(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, Transform columnsRoot, Vector3 mapOrigin, int minX, int minY)
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

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabRoomTemplates.CornerColumnPath);
            float cell = PrefabRoomTemplates.CellMeters;
            var owners = new HashSet<int>();
            for (int px = 0; px <= maxX + 1; px++)
            {
                for (int py = 0; py <= maxY + 1; py++)
                {
                    owners.Clear();
                    int empty = 0;
                    for (int ox = -1; ox <= 0; ox++)
                    {
                        for (int oy = -1; oy <= 0; oy++)
                        {
                            if (owner.TryGetValue(CellKey(px + ox, py + oy), out int room))
                            {
                                owners.Add(room);
                            }
                            else
                            {
                                empty++;
                            }
                        }
                    }

                    if (owners.Count < 2 || empty == 0)
                    {
                        continue;
                    }

                    var column = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    column.transform.SetParent(columnsRoot, false);
                    column.transform.position = mapOrigin + new Vector3(px * cell, 0f, py * cell);
                    column.name = $"Column_{px}_{py}";
                }
            }
        }

        /// <summary>문 조립체에서 여닫이 문짝(Hall_Door_L/R)을 제외한 문틀 월드 바운드 — 정렬 기준.</summary>
        /// <param name="doorInstance">문 인스턴스.</param>
        /// <returns>문틀 월드 바운드.</returns>
        private static Bounds FrameBounds(GameObject doorInstance)
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

        /// <summary>정규화 셀 좌표 → 소유 맵 키.</summary>
        /// <param name="x">셀 X.</param>
        /// <param name="y">셀 Y.</param>
        /// <returns>64비트 키.</returns>
        private static long CellKey(int x, int y)
        {
            return ((long)x << 32) ^ (uint)y;
        }

        /// <summary>
        /// 한 벽 평면에서 문틀보다 넓게 잘린 개구의 잔여 슬릿을 회색 슬래브(콜라이더 포함)로 봉합한다 —
        /// 방 프리팹의 벽 서브 그리드가 셀 그리드와 0.2~0.8m 어긋나 있어 개구가 문틀 밖으로 삐져나올 수 있다.
        /// </summary>
        /// <param name="sideCuts">이 평면에서 비활성화한 벽 바운드 목록.</param>
        /// <param name="frame">배치 완료된 문틀 월드 바운드.</param>
        /// <param name="boundaryAlongX">경계선이 X 축 방향인지.</param>
        /// <param name="doorsRoot">슬래브 부모.</param>
        /// <param name="floorY">바닥 월드 Y.</param>
        private static void FillDoorSideGaps(List<Bounds> sideCuts, Bounds frame, bool boundaryAlongX, Transform doorsRoot, float floorY)
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
            float frameMin = boundaryAlongX ? frame.min.x : frame.min.z;
            float frameMax = boundaryAlongX ? frame.max.x : frame.max.z;
            float perpCenter = boundaryAlongX ? hole.center.z : hole.center.x;
            float thickness = Mathf.Max(0.3f, boundaryAlongX ? hole.size.z : hole.size.x);

            foreach ((float min, float max) in new[] { (holeMin, frameMin), (frameMax, holeMax) })
            {
                float width = max - min;
                if (width < 0.04f)
                {
                    continue;
                }

                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.transform.SetParent(doorsRoot, false);
                float axisCenter = (min + max) * 0.5f;
                slab.transform.position = boundaryAlongX
                    ? new Vector3(axisCenter, floorY + 3f, perpCenter)
                    : new Vector3(perpCenter, floorY + 3f, axisCenter);
                slab.transform.localScale = boundaryAlongX
                    ? new Vector3(width, 6f, thickness)
                    : new Vector3(thickness, 6f, width);
                slab.name = "GapFiller";
                slab.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("MG_Filler", new Color(0.42f, 0.43f, 0.45f));
            }
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
        /// <param name="templates">템플릿 목록.</param>
        /// <returns>(최소 X, 최소 Y).</returns>
        private static (int, int) MinCellBounds(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates)
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

        /// <summary>스폰 종류별 표식 머티리얼을 가져온다 — 없으면 폴더와 함께 생성해 에셋으로 저장한다.</summary>
        /// <param name="kind">스폰 종류.</param>
        /// <returns>공유 머티리얼.</returns>
        private static Material MarkerMaterial(SpawnKind kind)
        {
            (string name, Color color) = kind switch
            {
                SpawnKind.ZombieWalker => ("MG_Zombie", new Color(0.85f, 0.15f, 0.15f)),
                SpawnKind.ZombieListener => ("MG_Listener", new Color(1f, 0.45f, 0.1f)),
                SpawnKind.ZombieWatcher => ("MG_Watcher", new Color(0.7f, 0.1f, 0.55f)),
                SpawnKind.VaccineAntigen => ("MG_Vaccine", new Color(0.1f, 0.8f, 0.3f)),
                SpawnKind.VaccineSerum => ("MG_Vaccine", new Color(0.1f, 0.8f, 0.3f)),
                SpawnKind.VaccineStabilizer => ("MG_Vaccine", new Color(0.1f, 0.8f, 0.3f)),
                SpawnKind.Key => ("MG_Key", new Color(1f, 0.85f, 0.1f)),
                SpawnKind.Oil => ("MG_Oil", new Color(0.55f, 0.35f, 0.1f)),
                SpawnKind.Scrap => ("MG_Scrap", new Color(0.55f, 0.55f, 0.55f)),
                SpawnKind.Throwable => ("MG_Throwable", new Color(0.1f, 0.7f, 0.85f)),
                SpawnKind.CorpseStation => ("MG_Station", new Color(0.5f, 0.2f, 0.8f)),
                SpawnKind.Generator => ("MG_Generator", new Color(0.15f, 0.4f, 0.95f)),
                _ => ("MG_Herd", new Color(0.6f, 0.05f, 0.05f)),
            };

            return GetOrCreateMaterial(name, color);
        }

        /// <summary>이름·색으로 머티리얼 에셋을 가져오거나 없으면 폴더와 함께 생성한다.</summary>
        /// <param name="name">에셋 이름(확장자 제외).</param>
        /// <param name="color">기본 색.</param>
        /// <returns>공유 머티리얼.</returns>
        private static Material GetOrCreateMaterial(string name, Color color)
        {
            string path = $"{markerMaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            if (!AssetDatabase.IsValidFolder(markerMaterialFolder))
            {
                AssetDatabase.CreateFolder("Assets/02. Prefab/Map", "MapGenMarkers");
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            material = new Material(shader);
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
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

        /// <summary>문 프리팹의 Y 회전각 — 통로 축이 소켓 방향과 나란하도록.</summary>
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

        /// <summary>TemplateId 로 템플릿을 찾는다(없으면 데이터 결함 — NRE 표면화).</summary>
        /// <param name="templates">템플릿 목록.</param>
        /// <param name="templateId">찾을 ID.</param>
        /// <returns>일치 템플릿.</returns>
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
        /// <returns>소켓 정의.</returns>
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

        /// <summary>템플릿에서 마커 Id 로 마커를 찾는다.</summary>
        /// <param name="template">대상 템플릿.</param>
        /// <param name="markerId">마커 Id.</param>
        /// <returns>마커 정의.</returns>
        private static MarkerDef FindMarker(RoomTemplateDef template, int markerId)
        {
            for (int m = 0; m < template.Markers.Length; m++)
            {
                if (template.Markers[m].Id == markerId)
                {
                    return template.Markers[m];
                }
            }

            return null;
        }
    }
}
