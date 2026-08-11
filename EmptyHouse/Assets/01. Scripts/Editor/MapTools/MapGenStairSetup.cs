using System.Collections.Generic;
using Border.Core;
using EmptyHouse.MapGen.Core;
using EmptyHouse.MapGen.Runtime;
using UnityEditor;
using UnityEngine;

namespace EmptyHouse.EditorTools
{
    /// <summary>
    /// 계단실 셋업(M9-8) — ① 기성 부품(Hall_Stairs 3m × 2단 스위치백 + 참 슬래브)으로 계단실 그레이박스
    /// 프리팹을 저작하고 ② 계단실 템플릿 SO ③ 3층 층 스택 SO 를 만든 뒤 ④ 활성 씬에 3층 검증 조립을 수행한다.
    /// 프리팹 저작은 EmptyRoom-3x3 셸(벽·바닥)을 기반으로 보이드 셀 바닥 타일 1장을 걷어내고 계단을 심는다(D1·D2).
    /// </summary>
    public static class MapGenStairSetup
    {
        private const string shellPath = "Assets/02. Prefab/Map/EmptyRooms/EmptyRoom-3x3.prefab"; // 3×3 셸(벽 6m·바닥 타일)
        private const string stairFlightPath = "Assets/04. Arts/Environment/HorrorPack/!Prefabs/Architectural/Hall_Props/Hall_Stairs.prefab"; // 기성 플라이트(라이즈 3m)
        private const string landingSlabPath = "Assets/04. Arts/Environment/HorrorPack/!Prefabs/Architectural/Hall_Props/Hall_Floor_2nd_Floor_Stair_Half.prefab"; // 참 슬래브
        private const string railingPath = "Assets/04. Arts/Environment/HorrorPack/!Prefabs/Architectural/Hall_Props/Hall_Railings_Stair.prefab"; // 보이드 난간(N6)
        private const string stairsFolder = "Assets/02. Prefab/Map/DecoratedRooms/Stairs"; // 계단실 프리팹 폴더
        private const string stairPrefabPath = stairsFolder + "/StairRoom-3x3.prefab"; // 계단실 그레이박스 프리팹
        private const string stairTemplatePath = "Assets/03. ScriptableObjects/MapGen/Templates/SO_Template_stair_3x3.asset"; // 계단실 템플릿 SO
        private const string floorStackPath = "Assets/03. ScriptableObjects/MapGen/SO_MapFloorStack.asset"; // 3층 스택 SO
        private const string registryPath = "Assets/03. ScriptableObjects/MapGen/SO_MapPrefabRegistry.asset"; // 테마 레지스트리(전 층 공유)
        private const float floorHeight = 6f; // 층고(실측 확정 — Hall_Wall_6M)
        private const float cellMeters = 4f; // 셀 실측

        /// <summary>기성 부품 실측을 로그로 남긴다(저작 좌표 판단 근거).</summary>
        /// <returns>실측 요약.</returns>
        public static string MeasureKit()
        {
            var sb = new System.Text.StringBuilder();
            foreach (string path in new[] { shellPath, stairFlightPath, landingSlabPath, railingPath })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    sb.AppendLine($"{path}: 없음");
                    continue;
                }

                Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }

                sb.AppendLine($"{System.IO.Path.GetFileNameWithoutExtension(path)}: center={bounds.center} size={bounds.size} renderers={renderers.Length}");
            }

            Log.D(sb.ToString());
            return sb.ToString();
        }

        /// <summary>
        /// 계단실 그레이박스 프리팹·템플릿 SO·3층 스택 SO 를 생성한다(멱등 — 기존 프리팹은 다시 만든다).
        /// 실측(Hall_Stairs 런 5.44m·라이즈 3m): 12m 방에 **직선 2연속 플라이트**(0→3→6m)가 들어맞아 스위치백이 불필요하다.
        /// 배치 기하: 동쪽 열(셀 x=2) 남→북 직선 상승, 보이드 셀 = 상단 도착 셀 NE(2,2) — 그 바닥 타일을 걷어내
        /// 아래층 계단의 도착 개구로 쓴다. 도착자는 꼭대기 계단에서 서쪽 셀 (1,2) 바닥으로 내려선다(그레이박스 동선).
        /// </summary>
        /// <returns>생성 결과 요약.</returns>
        public static string Build()
        {
            var shell = AssetDatabase.LoadAssetAtPath<GameObject>(shellPath);
            var flightPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(stairFlightPath);
            var railingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(railingPath);

            if (!AssetDatabase.IsValidFolder(stairsFolder))
            {
                AssetDatabase.CreateFolder("Assets/02. Prefab/Map/DecoratedRooms", "Stairs");
            }

            // ── 셸 인스턴스 — 바닥 실측 원점(민 코너)을 로컬 기준으로 좌표를 만든다 ─────────────
            var root = (GameObject)PrefabUtility.InstantiatePrefab(shell);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            root.name = "StairRoom-3x3";

            Bounds floor = FloorBounds(root);
            Vector3 origin = new Vector3(floor.min.x, floor.max.y, floor.min.z); // 셀 (0,0) 민 코너·바닥 상면

            // 보이드 셀 (2,2) 바닥 타일 제거 — 아래층에서 올라오는 도착 개구
            Vector3 voidCenter = origin + new Vector3(2.5f * cellMeters, 0f, 2.5f * cellMeters);
            int removed = 0;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!MapRuntimeAssembler.IsFloorRenderer(renderer.name))
                {
                    continue;
                }

                Bounds b = renderer.bounds;
                if (b.size.x < cellMeters + 0.5f && voidCenter.x > b.min.x && voidCenter.x < b.max.x && voidCenter.z > b.min.z && voidCenter.z < b.max.z)
                {
                    Object.DestroyImmediate(renderer.gameObject);
                    removed++;
                }
            }

            // ── 직선 2연속 플라이트 — 동쪽 열(x = 셀 2.5 = 10m), 남→북 진행 ────────────────────
            // 실측 런 5.44m: 플라이트1 z 0.2→5.64(y 0→3) · 플라이트2 z 5.64→11.08(y 3→6, 상단 = 보이드 셀 (2,2))
            GameObject flight1 = PlaceFlight(flightPrefab, root.transform, "StairFlight_Lower",
                yaw: 0f, basePoint: origin + new Vector3(2.5f * cellMeters, 0f, 0.2f));
            Bounds flight1Bounds = CombinedBounds(flight1);
            GameObject flight2 = PlaceFlight(flightPrefab, root.transform, "StairFlight_Upper",
                yaw: 0f, basePoint: new Vector3(origin.x + 2.5f * cellMeters, origin.y + 3f, flight1Bounds.max.z));

            // 난간(N6) — 보이드 셀 서쪽 가장자리(추락 방지, z∈[8,12] 경계 x=8)
            if (railingPrefab != null)
            {
                var railing = (GameObject)PrefabUtility.InstantiatePrefab(railingPrefab, root.transform);
                railing.name = "StairRailing_Void";
                railing.transform.position = origin + new Vector3(2f * cellMeters, 0f, 2.5f * cellMeters);
            }

            var saved = PrefabUtility.SaveAsPrefabAsset(root, stairPrefabPath);
            Object.DestroyImmediate(root);

            // ── 템플릿 SO — room_3x3 소켓 위상 재사용(D1). 보이드 셀 (2,2)는 소켓 셀이 아니다 ─────
            var template = AssetDatabase.LoadAssetAtPath<RoomTemplateSO>(stairTemplatePath);
            if (template == null)
            {
                template = ScriptableObject.CreateInstance<RoomTemplateSO>();
                AssetDatabase.CreateAsset(template, stairTemplatePath);
            }

            template.TemplateId = "stair_3x3";
            template.WidthCells = 3;
            template.HeightCells = 3;
            template.AllowedFloors = FloorMask.F1;
            template.Tags = RoomTagMask.None;
            template.MinCount = 0;
            template.MaxCount = 3; // ShaftCountMax 상한
            template.IsCorridor = false;
            template.IsEntranceAnchor = false;
            template.IsStairAnchor = true;
            template.Sockets = new[]
            {
                new SocketAuthoring { Id = 0, CellX = 1, CellY = 0, Direction = SocketDirection.South },
                new SocketAuthoring { Id = 1, CellX = 1, CellY = 2, Direction = SocketDirection.North },
                new SocketAuthoring { Id = 2, CellX = 0, CellY = 1, Direction = SocketDirection.West },
                new SocketAuthoring { Id = 3, CellX = 2, CellY = 1, Direction = SocketDirection.East },
            };
            template.Markers = new MarkerAuthoring[0];
            template.Prefab = saved;
            template.Variants = new GameObject[0];
            EditorUtility.SetDirty(template);

            // ── 3층 스택 SO — 전 층 Hall 테마 공유·층고 6m·DangerBias B1>2F>1F ─────────────────
            var registry = AssetDatabase.LoadAssetAtPath<MapPrefabRegistrySO>(registryPath);
            var stack = AssetDatabase.LoadAssetAtPath<MapFloorStackSO>(floorStackPath);
            if (stack == null)
            {
                stack = ScriptableObject.CreateInstance<MapFloorStackSO>();
                AssetDatabase.CreateAsset(stack, floorStackPath);
            }

            stack.Floors = new[]
            {
                FloorEntry(0, registry, template, 0),
                FloorEntry(1, registry, template, 1),
                FloorEntry(-1, registry, template, 2),
            };
            EditorUtility.SetDirty(stack);
            AssetDatabase.SaveAssets();

            string summary = $"[MapGenStairSetup] 프리팹 {stairPrefabPath}(보이드 타일 제거 {removed}) · 템플릿 {stairTemplatePath} · 스택 {floorStackPath}";
            Log.D(summary);
            return summary;
        }

        /// <summary>층 스택 항목 하나를 만든다.</summary>
        private static FloorPrefabSet FloorEntry(int floorIndex, MapPrefabRegistrySO registry, RoomTemplateSO stairTemplate, int dangerBias)
        {
            return new FloorPrefabSet
            {
                FloorIndex = floorIndex,
                ThemeId = "hall",
                Registry = registry,
                StairTemplate = stairTemplate,
                CellMeters = cellMeters,
                FloorHeight = floorHeight,
                GenParams = new FloorGenParams { FloorIndex = floorIndex, ThemeId = "hall", DangerBias = dangerBias },
                StairFlightPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(stairFlightPath),
                StairVoidSlabPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(landingSlabPath),
                StairRailingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(railingPath),
            };
        }

        /// <summary>플라이트를 인스턴스화해 진행 방향(yaw)으로 돌리고 실측 바운드 하단·시작점을 basePoint 에 맞춘다.</summary>
        private static GameObject PlaceFlight(GameObject prefab, Transform parent, string name, float yaw, Vector3 basePoint)
        {
            var flight = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            flight.name = name;
            AlignFlight(flight, basePoint, yaw);
            return flight;
        }

        /// <summary>플라이트 실측 정렬 — 회전 적용 후 바운드의 바닥·후단(진행 반대쪽)을 basePoint 에 맞춘다.</summary>
        private static void AlignFlight(GameObject flight, Vector3 basePoint, float yaw)
        {
            flight.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            flight.transform.position = basePoint;
            Bounds bounds = CombinedBounds(flight);

            // 진행 축(yaw 0 = +Z, 90 = +X)의 후단과 바닥을 시작점에 정렬
            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            float backOffset = Mathf.Abs(forward.z) > 0.5f
                ? (forward.z > 0f ? basePoint.z - bounds.min.z : bounds.max.z - basePoint.z)
                : (forward.x > 0f ? basePoint.x - bounds.min.x : bounds.max.x - basePoint.x);
            flight.transform.position += forward * backOffset + Vector3.up * (basePoint.y - bounds.min.y);

            // 진행 수직축은 중앙 정렬
            Bounds after = CombinedBounds(flight);
            if (Mathf.Abs(forward.z) > 0.5f)
            {
                flight.transform.position += Vector3.right * (basePoint.x - after.center.x);
            }
            else
            {
                flight.transform.position += Vector3.forward * (basePoint.z - after.center.z);
            }
        }

        /// <summary>인스턴스 렌더러 합성 바운드.</summary>
        private static Bounds CombinedBounds(GameObject instance)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        /// <summary>인스턴스의 바닥 타일 합성 바운드.</summary>
        private static Bounds FloorBounds(GameObject instance)
        {
            Bounds bounds = default;
            bool found = false;
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (!MapRuntimeAssembler.IsFloorRenderer(renderer.name))
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return bounds;
        }

        /// <summary>
        /// 활성 씬에 3층 검증 조립(M9-8 수용) — 층 스택 플랜으로 생성·조립하고 계단 접지·정렬을 감사한다.
        /// 씬을 저장하지 않는다(검증 후 수동 원복 전제). 루트 GeneratedMaps3F 는 재실행 시 교체.
        /// </summary>
        /// <param name="seed">확정 시드.</param>
        /// <returns>감사 요약.</returns>
        public static string BuildThreeFloorVerification(int seed)
        {
            var stack = AssetDatabase.LoadAssetAtPath<MapFloorStackSO>(floorStackPath);
            var registry = AssetDatabase.LoadAssetAtPath<MapPrefabRegistrySO>(registryPath);
            var lintErrors = new List<string>();
            if (!MapFloorPlanAssembler.Lint(stack, lintErrors))
            {
                return "린트 실패: " + string.Join(" / ", lintErrors);
            }

            MapGenPlan plan = MapFloorPlanAssembler.Build(stack, new MapGenParams { Seed = seed, VaccineFloorPlan = new[] { 1, -1, -1 } }, out RoomTemplateSO[] flatAssets);
            MapGenResult result = new MapGenerator().Generate(plan);
            if (!result.Success)
            {
                return $"생성 실패 시드 {seed}: {string.Join(" / ", result.FailReasons)}";
            }

            GameObject old = GameObject.Find("GeneratedMaps3F");
            if (old != null)
            {
                Object.DestroyImmediate(old);
            }

            var root = new GameObject("GeneratedMaps3F");
            root.transform.position = new Vector3(0f, 0f, -900f); // 기존 그레이박스와 겹치지 않는 남쪽
            GameObject mapRoot = MapRuntimeAssembler.Assemble(result.Blueprint, plan.FlatTemplates, registry, root.transform, null,
                (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent), stack, flatAssets);

            var culler = mapRoot.GetComponent<EmptyHouse.Environment.MapLightCuller>();
            if (culler != null)
            {
                Object.DestroyImmediate(culler);
            }

            foreach (Light light in mapRoot.GetComponentsInChildren<Light>(true))
            {
                light.enabled = false;
            }

            // 감사 — 샤프트마다 층별 계단실 인스턴스가 층 평면에 접지했는지(공중에 뜨지 않음)
            var report = new System.Text.StringBuilder();
            int issues = 0;
            foreach (StairShaft shaft in result.Blueprint.Shafts)
            {
                for (int f = 0; f < result.Blueprint.Floors.Count; f++)
                {
                    int floorIndex = result.Blueprint.Floors[f].FloorIndex;
                    float planeY = mapRoot.transform.position.y + FloorGeometry.FloorPlaneY(stack, floorIndex);
                    int roomIndex = FindStairRoom(result.Blueprint, shaft, floorIndex);
                    Transform roomInstance = mapRoot.transform.Find($"Room_{roomIndex}_{result.Blueprint.Rooms[roomIndex].TemplateId}");
                    if (roomInstance == null)
                    {
                        report.AppendLine($"샤프트 {shaft.ShaftId} 층 {floorIndex}: 인스턴스 없음");
                        issues++;
                        continue;
                    }

                    Bounds bounds = CombinedBounds(roomInstance.gameObject);
                    float gap = Mathf.Abs(bounds.min.y - planeY);
                    if (gap > 0.6f)
                    {
                        report.AppendLine($"샤프트 {shaft.ShaftId} 층 {floorIndex}: 접지 이탈 {gap:F2}m (bounds.min.y={bounds.min.y:F2}, plane={planeY:F2})");
                        issues++;
                    }
                }
            }

            string summary = $"[MapGenStairSetup] 3층 검증 조립 시드 {seed} — 방 {result.Blueprint.Rooms.Count}·샤프트 {result.Blueprint.Shafts.Count}·접지 문제 {issues}건\n{report}";
            Log.D(summary);
            return summary;
        }

        /// <summary>샤프트 좌표·층으로 계단실 방 인덱스를 찾는다.</summary>
        private static int FindStairRoom(MapBlueprint blueprint, StairShaft shaft, int floorIndex)
        {
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                BlueprintRoom room = blueprint.Rooms[r];
                if (room.FloorIndex == floorIndex && room.Cell.X == shaft.Cell.X && room.Cell.Y == shaft.Cell.Y)
                {
                    return r;
                }
            }

            return -1;
        }
    }
}
