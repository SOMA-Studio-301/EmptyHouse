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
        private static string MapDefinitionPath => EmptyHouse.MapGen.Editor.MapGenSceneBuilder.FindFirstMultiFloorDefinitionPath(); // 다층 검증 대상 — 프로젝트 첫 다층 정의 스캔(에셋 이름 하드코딩 회피)
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
        /// 실측(Hall_Stairs 런 5.44m·라이즈 3m): 12m 방, **스위치백(D2)** — 직선 2연속은 하단이 남벽에 붙어
        /// 진입 어프론이 없고 NavMesh 가 바닥과 이어지지 않았다(M9-9 실측 PathPartial).
        /// 기하: 플라이트1 동쪽 열(x=10) 남→북 base z5→top z10.44(y0→3) → 북동 참(y3) → 플라이트2 북쪽 행(z=10)
        /// 동→서 base x9.2→top x3.76(y3→6). 보이드 셀 (0,2)·(1,2) = 아래층 플라이트2 의 도착 개구 + 머리 공간.
        /// 도착자는 브리지로 남쪽 (0,1)·(1,1) 바닥에 내려선다. 남측 어프론 5m 가 진입 내비의 성립 조건.
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

            // 보이드 셀 (0,2)·(1,2) 바닥 타일 제거 — 아래층 계단(북행 서향 플라이트)의 도착 개구 + 상승 머리 공간.
            // NavMesh agentHeight 2.0 기준: 플라이트2(x 9.2→3.76 서향 상승)의 y+2 ≥ 6 구간(x ≤ 7.39)이 전부 보이드 아래다
            int removed = 0;
            var voidCenters = new[]
            {
                origin + new Vector3(0.5f * cellMeters, 0f, 2.5f * cellMeters),
                origin + new Vector3(1.5f * cellMeters, 0f, 2.5f * cellMeters),
            };
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !MapRuntimeAssembler.IsFloorRenderer(renderer.name))
                {
                    continue;
                }

                Bounds b = renderer.bounds;
                foreach (Vector3 voidCenter in voidCenters)
                {
                    if (b.size.x < cellMeters + 0.5f && voidCenter.x > b.min.x && voidCenter.x < b.max.x && voidCenter.z > b.min.z && voidCenter.z < b.max.z)
                    {
                        Object.DestroyImmediate(renderer.gameObject);
                        removed++;
                        break;
                    }
                }
            }

            // 계단 상부 천장 절개 — 셸에 천장(Hall_Ceiling_Deco·Screen, y≈6)이 있어 그대로면 계단 상부 통행 공간이 잘린다.
            // 북쪽 행(z = 셀 2) 전체 위 천장 조각을 걷어낸다(플라이트2 서향 상승 + 보이드 도착 대역)
            var ceilingCut = new Bounds(
                origin + new Vector3(1.5f * cellMeters, floorHeight, 2.5f * cellMeters),
                new Vector3(3f * cellMeters - 0.2f, 1.2f, cellMeters - 0.2f));
            int ceilingRemoved = 0;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer.name.Contains("Stair"))
                {
                    continue; // 앞선 파괴(바닥 타일 자식 등)로 죽은 항목 건너뜀
                }

                Bounds b = renderer.bounds;
                if (b.min.y > origin.y + 5.4f && b.Intersects(ceilingCut))
                {
                    Object.DestroyImmediate(renderer.gameObject);
                    ceilingRemoved++;
                }
            }

            // ── 스위치백(D2) — 남측 어프론 5m 확보가 핵심: 하단이 벽에 붙으면 진입 내비가 성립하지 않는다(M9-9 실측)
            // 플라이트1: 동쪽 열(x=10), 남→북, base z=5.0 → top z=10.44 (y 0→3)
            // 참(landing): 북동 구석 y=3 플랫폼(그레이박스 슬래브) — 플라이트1 top 과 플라이트2 base 를 잇는다
            // 플라이트2: 북쪽 행(z=10), 동→서, base x=8.0 → top x=2.56 (y 3→6, 도착 = 보이드 (0,2)·(1,2)).
            // base 는 보이드 경계(x=8, 참 서쪽 끝)에 둔다 — 9.2 로 물리면 위층 (2,2) 타일 아래 클리어런스 핀치로
            // 플라이트2 하단 내비가 끊긴다(M9-9 실측: y 3.6~4.4 밴드 단절)
            GameObject flight1 = PlaceFlight(flightPrefab, root.transform, "StairFlight_Lower",
                yaw: 0f, basePoint: origin + new Vector3(2.5f * cellMeters, 0f, 5.0f));
            GameObject flight2 = PlaceFlight(flightPrefab, root.transform, "StairFlight_Upper",
                yaw: 270f, basePoint: origin + new Vector3(8.0f, 3f, 2.5f * cellMeters));

            // 램프 플레이트 — Hall_Stairs 메시는 복셀화 시 계단면 내비가 조각나(M9-9 실측: 밴드형 단절)
            // 트레드 라인 위 0.05m 에 연속 경사판(29°)을 깔아 내비의 실체 면으로 쓴다(Stair 토큰 → Walkable 태깅)
            AddRampPlate(root.transform, flight1, "StairRamp_Lower");
            AddRampPlate(root.transform, flight2, "StairRamp_Upper");

            // 참 플랫폼 — 북동 구석(x 8~12, z 10.44~12) y=3 상면. 계단 전용 토큰(StairLanding)으로 베이커가 Walkable 태깅
            Bounds flight1Bounds = CombinedBounds(flight1);
            var landing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            landing.name = "StairLanding";
            landing.transform.SetParent(root.transform, false);
            float landingDepth = origin.z + 3f * cellMeters - flight1Bounds.max.z;
            landing.transform.localScale = new Vector3(cellMeters, 0.15f, landingDepth);
            landing.transform.position = new Vector3(origin.x + 2.5f * cellMeters, origin.y + 3f - 0.075f, flight1Bounds.max.z + landingDepth * 0.5f);

            // 상단 브리지 플레이트 — 플라이트2 꼭대기(보이드 위)와 남쪽 (0,1) 바닥 타일 사이를 잇는다
            Bounds flight2Bounds = CombinedBounds(flight2);
            var bridge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bridge.name = "StairTopBridge";
            bridge.transform.SetParent(root.transform, false);
            bridge.transform.localScale = new Vector3(2.2f, 0.1f, 1.2f);
            bridge.transform.position = new Vector3(flight2Bounds.min.x + 0.4f, origin.y + floorHeight - 0.05f, flight2Bounds.min.z - 0.55f);

            // 난간(N6)은 그레이박스 단계에서 미배치 — Hall_Railings_Stair 가 플라이트 동선과 겹쳐
            // NavMesh 를 끊는 것이 실측됐다(M9-9). 추락 방지 난간은 아트 패스에서 동선 밖 가장자리에 배치한다.
            _ = railingPrefab;

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
            // 소켓 = 남(1,0)·서(0,1) 2개만 — 동(2,1)은 플라이트1 옆구리(y 0~1.65)로, 북(1,2)은 보이드로 뚫려 개구 불가
            template.Sockets = new[]
            {
                new SocketAuthoring { Id = 0, CellX = 1, CellY = 0, Direction = SocketDirection.South },
                new SocketAuthoring { Id = 1, CellX = 0, CellY = 1, Direction = SocketDirection.West },
            };
            template.Markers = new MarkerAuthoring[0];
            template.Prefab = saved;
            template.Variants = new GameObject[0];
            EditorUtility.SetDirty(template);

            AssetDatabase.SaveAssets();

            // 3층 구성은 빈 집 정의(SO_Map_Hall3F — M10-1)가 원천이다. 구 층 스택 저작은 퇴역 —
            // 층 배선(계단실 템플릿·계단 프리팹·층고)은 FloorDefinitionSO 에셋에서 직접 편집한다
            string summary = $"[MapGenStairSetup] 프리팹 {stairPrefabPath}(보이드 타일 {removed}·천장 절개 {ceilingRemoved}) · 템플릿 {stairTemplatePath}";
            Log.D(summary);
            return summary;
        }

        /// <summary>플라이트 바운드를 따라 연속 경사 램프 플레이트(그레이박스 큐브)를 깐다 — 계단면 내비의 실체.</summary>
        /// <param name="parent">계단실 루트.</param>
        /// <param name="flight">대상 플라이트 인스턴스.</param>
        /// <param name="name">플레이트 이름(Stair 토큰 필수 — 베이커 Walkable 태깅·층 침범 제외 예외 판정).</param>
        private static void AddRampPlate(Transform parent, GameObject flight, string name)
        {
            Bounds b = CombinedBounds(flight);
            Vector3 fwd = -flight.transform.forward; // 메시 180° 보정(AlignFlight) 때문에 진행 방향 = -forward
            float runLength = Mathf.Abs(Vector3.Dot(b.size, fwd));
            Vector3 baseFoot = b.center - fwd * (runLength * 0.5f);
            baseFoot.y = b.min.y;
            Vector3 top = b.center + fwd * (runLength * 0.5f);
            top.y = b.max.y;

            Vector3 slope = top - baseFoot;
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = name;
            plate.transform.SetParent(parent, false);
            plate.transform.localScale = new Vector3(2.8f, 0.05f, slope.magnitude + 0.4f); // 양끝 0.2m 연장 — 바닥·참과 내비 접속 보장
            plate.transform.rotation = Quaternion.LookRotation(slope.normalized);
            plate.transform.position = (baseFoot + top) * 0.5f + Vector3.up * 0.05f;
        }

        /// <summary>플라이트를 인스턴스화해 진행 방향(yaw)으로 돌리고 실측 바운드 하단·시작점을 basePoint 에 맞춘다.</summary>
        private static GameObject PlaceFlight(GameObject prefab, Transform parent, string name, float yaw, Vector3 basePoint)
        {
            var flight = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            flight.name = name;
            AlignFlight(flight, basePoint, yaw);
            return flight;
        }

        /// <summary>플라이트 실측 정렬 — 회전 적용 후 바운드의 바닥·후단(진행 반대쪽)을 basePoint 에 맞춘다.
        /// Hall_Stairs 메시는 로컬 -Z 방향으로 상승한다(M9-9 실측: 베이스 쪽이 높게 놓여 램프와 V자 교차)
        /// — 진행 yaw 에 180° 를 보태 상승 방향을 진행 방향과 일치시킨다. 정렬 축은 yaw(진행 방향) 기준.</summary>
        private static void AlignFlight(GameObject flight, Vector3 basePoint, float yaw)
        {
            flight.transform.rotation = Quaternion.Euler(0f, yaw + 180f, 0f);
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
            var definition = AssetDatabase.LoadAssetAtPath<MapDefinitionSO>(MapDefinitionPath);
            MapGenPlan plan = MapPlanBuilder.Build(definition, new MapGenParams { Seed = seed, VaccineFloorPlan = new[] { 1, -1, -1 } }, out RoomTemplateSO[] flatAssets);
            if (plan == null)
            {
                return "린트 실패 — 콘솔 에러 참조(R4)";
            }

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
            GameObject mapRoot = MapRuntimeAssembler.Assemble(result.Blueprint, plan.FlatTemplates, definition, root.transform,
                (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent), flatAssets);

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
                    float planeY = mapRoot.transform.position.y + FloorGeometry.FloorPlaneY(definition, floorIndex);
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

        /// <summary>
        /// 3층 NavMesh 층간 연결 검증(M9-9) — 검증 조립 후 런타임 베이커와 같은 규칙(층 평면 슬래브 + 계단 Walkable)으로
        /// 베이크하고, 입구 층 → 2F / 입구 층 → B1 경로가 완주(PathComplete)되는지 계산한다. 씬은 저장하지 않는다.
        /// </summary>
        /// <param name="seed">확정 시드.</param>
        /// <returns>경로 검증 요약.</returns>
        public static string VerifyThreeFloorNavMesh(int seed)
        {
            string buildSummary = BuildThreeFloorVerification(seed);
            GameObject root = GameObject.Find("GeneratedMaps3F");
            if (root == null || root.transform.childCount == 0)
            {
                return "조립 실패 — " + buildSummary;
            }

            GameObject mapRoot = root.transform.GetChild(0).gameObject;
            var definition = AssetDatabase.LoadAssetAtPath<MapDefinitionSO>(MapDefinitionPath);
            float rootY = mapRoot.transform.position.y;
            var planes = new List<float> { rootY, rootY + FloorGeometry.FloorPlaneY(definition, 1), rootY + FloorGeometry.FloorPlaneY(definition, -1) };

            // 층 침범 콘텐츠 제외 — 런타임 베이커(M9-9)와 동일: 방 루트 Y 기준 5.5m 이상에서 시작하는 렌더러 제외
            foreach (Transform child in mapRoot.transform)
            {
                if (!child.name.StartsWith("Room_"))
                {
                    continue;
                }

                foreach (MeshRenderer renderer in child.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer.name.Contains("Stair") || renderer.bounds.min.y - child.position.y < 5.5f)
                    {
                        continue;
                    }

                    var intrude = renderer.GetComponent<Unity.AI.Navigation.NavMeshModifier>();
                    if (intrude == null)
                    {
                        intrude = renderer.gameObject.AddComponent<Unity.AI.Navigation.NavMeshModifier>();
                    }

                    intrude.ignoreFromBuild = true;
                }
            }

            // 런타임 베이커 규칙 복제 — 층 평면 슬래브 Walkable + 계단 Walkable, 기본 Not Walkable
            int tagged = 0;
            foreach (MeshRenderer renderer in mapRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                // 그림자 차폐 전용(ShadowsOnly, Screen 판) 베이크 제외 — 런타임 베이커와 동일 규칙
                if (renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
                {
                    var ignore = renderer.GetComponent<Unity.AI.Navigation.NavMeshModifier>();
                    if (ignore == null)
                    {
                        ignore = renderer.gameObject.AddComponent<Unity.AI.Navigation.NavMeshModifier>();
                    }

                    ignore.ignoreFromBuild = true;
                    continue;
                }

                // 판정 = "상면이 층 평면과 플러시"(런타임 베이커 M9-9 개정과 동일) — 아래층 벽·아치 상단 절단 방지
                Bounds b = renderer.bounds;
                bool onPlane = false;
                for (int p = 0; p < planes.Count && !onPlane; p++)
                {
                    onPlane = Mathf.Abs(b.max.y - planes[p]) <= 0.35f;
                }

                bool stair = renderer.name.Contains("Stair") && !renderer.name.Contains("Railing"); // 계단 토큰(난간 제외 — 난간은 차단물)
                if (!onPlane && !stair)
                {
                    continue;
                }

                var modifier = renderer.GetComponent<Unity.AI.Navigation.NavMeshModifier>();
                if (modifier != null && modifier.ignoreFromBuild)
                {
                    continue; // 층 침범 제외분은 태깅하지 않는다
                }

                if (modifier == null)
                {
                    modifier = renderer.gameObject.AddComponent<Unity.AI.Navigation.NavMeshModifier>();
                }

                modifier.overrideArea = true;
                modifier.area = 0;
                tagged++;
            }

            var surface = mapRoot.GetComponent<Unity.AI.Navigation.NavMeshSurface>();
            if (surface == null)
            {
                surface = mapRoot.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
            }

            surface.collectObjects = Unity.AI.Navigation.CollectObjects.Children;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.RenderMeshes;
            surface.defaultArea = 1;
            surface.BuildNavMesh();

            // 층별 대표 지점 — 그 층 계단실 방 중심을 NavMesh 에 스냅. 조립과 같은 시드·같은 정의로 재생성(재현 보장)
            MapGenPlan plan = MapPlanBuilder.Build(definition, new MapGenParams { Seed = seed, VaccineFloorPlan = new[] { 1, -1, -1 } }, out _);
            MapGenResult result = new MapGenerator().Generate(plan);
            var report = new System.Text.StringBuilder(buildSummary);
            Vector3? from = SamplePointOnFloor(mapRoot, result.Blueprint, definition, 0);
            foreach (int target in new[] { 1, -1 })
            {
                Vector3? to = SamplePointOnFloor(mapRoot, result.Blueprint, definition, target);
                if (from == null || to == null)
                {
                    report.AppendLine($"층 {target}: 샘플 지점 스냅 실패(from={from != null} to={to != null})");
                    continue;
                }

                var path = new UnityEngine.AI.NavMeshPath();
                bool found = UnityEngine.AI.NavMesh.CalculatePath(from.Value, to.Value, UnityEngine.AI.NavMesh.AllAreas, path);
                Vector3 lastCorner = found && path.corners.Length > 0 ? path.corners[path.corners.Length - 1] : from.Value;
                report.AppendLine($"층 0 → 층 {target}: {(found ? path.status.ToString() : "경로 계산 실패")} (지점 {from.Value} → {to.Value} · 도달 {lastCorner})");
            }

            // 계단면 내비 2-홉 진단 — 바닥 → 하부 플라이트 중앙 → 상부 플라이트 중앙 → 위층 지점.
            // 어느 홉에서 끊기는지로 단절 지점을 특정한다(회전 배치라 로컬 축 가정 프로브는 무효)
            Transform lower = null;
            Transform upper = null;
            foreach (Transform flight in mapRoot.GetComponentsInChildren<Transform>(true))
            {
                if (flight.name == "StairFlight_Lower" && lower == null)
                {
                    lower = flight;
                    upper = flight.parent.Find("StairFlight_Upper");
                }
            }

            if (lower != null && upper != null && from != null)
            {
                Bounds lb = lower.GetComponentInChildren<Renderer>().bounds;
                Bounds ub = upper.GetComponentInChildren<Renderer>().bounds;
                bool onLower = UnityEngine.AI.NavMesh.SamplePosition(new Vector3(lb.center.x, lb.center.y + 0.3f, lb.center.z), out UnityEngine.AI.NavMeshHit lowerHit, 1.5f, UnityEngine.AI.NavMesh.AllAreas);
                bool onUpper = UnityEngine.AI.NavMesh.SamplePosition(new Vector3(ub.center.x, ub.center.y + 0.3f, ub.center.z), out UnityEngine.AI.NavMeshHit upperHit, 1.5f, UnityEngine.AI.NavMesh.AllAreas);
                report.AppendLine($"  하부 중앙 스냅: {(onLower ? lowerHit.position.ToString() : "내비 없음")} · 상부 중앙 스냅: {(onUpper ? upperHit.position.ToString() : "내비 없음")}");
                if (onLower)
                {
                    var hop1 = new UnityEngine.AI.NavMeshPath();
                    UnityEngine.AI.NavMesh.CalculatePath(from.Value, lowerHit.position, UnityEngine.AI.NavMesh.AllAreas, hop1);
                    report.AppendLine($"  홉1 바닥→하부: {hop1.status}");
                }

                if (onLower && onUpper)
                {
                    var hop2 = new UnityEngine.AI.NavMeshPath();
                    UnityEngine.AI.NavMesh.CalculatePath(lowerHit.position, upperHit.position, UnityEngine.AI.NavMesh.AllAreas, hop2);
                    report.AppendLine($"  홉2 하부→상부: {hop2.status}");
                }

                // 단절 지점 정밀 스캔 — 어프론 진입(홉0) + 플라이트1 축 t 스캔(0.5m 간격)으로 첫 단절 위치를 특정
                Vector3 fwd = -lower.forward; // 메시 180° 보정(AlignFlight) 때문에 진행 방향 = -forward
                Bounds lb2 = CombinedBounds(lower.gameObject);
                Vector3 baseCenter = lb2.center - fwd * 2.72f; // 런 5.44m 절반 후퇴 = 하단 시작점
                baseCenter.y = lb2.min.y;
                Vector3 apron = baseCenter - fwd * 2.0f + Vector3.up * 0.3f;
                if (UnityEngine.AI.NavMesh.SamplePosition(apron, out UnityEngine.AI.NavMeshHit apronHit, 1.0f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    var hop0 = new UnityEngine.AI.NavMeshPath();
                    UnityEngine.AI.NavMesh.CalculatePath(from.Value, apronHit.position, UnityEngine.AI.NavMesh.AllAreas, hop0);
                    report.AppendLine($"  홉0 바닥→어프론: {hop0.status} (어프론 {apronHit.position})");
                    for (float t = 0f; t <= 6.0f; t += 0.5f)
                    {
                        Vector3 probe = baseCenter + fwd * t + Vector3.up * (Mathf.Clamp(3f * t / 5.44f, 0f, 3f) + 0.3f);
                        if (!UnityEngine.AI.NavMesh.SamplePosition(probe, out UnityEngine.AI.NavMeshHit h, 0.6f, UnityEngine.AI.NavMesh.AllAreas))
                        {
                            report.AppendLine($"    t={t:F1}: 내비 없음 (프로브 {probe})");
                            continue;
                        }

                        var p = new UnityEngine.AI.NavMeshPath();
                        UnityEngine.AI.NavMesh.CalculatePath(apronHit.position, h.position, UnityEngine.AI.NavMesh.AllAreas, p);
                        report.AppendLine($"    t={t:F1}: {p.status} {h.position}");
                    }
                }
                else
                {
                    report.AppendLine($"  어프론 스냅 실패 {apron}");
                }
            }

            // 층0 방별 도달 진단 — 지상층 수평 연결 자체가 끊겼는지 확인(홉0 단절의 원인 후보)
            if (from != null)
            {
                int reachable = 0;
                var unreachable = new List<string>();
                for (int r = 0; r < result.Blueprint.Rooms.Count; r++)
                {
                    if (result.Blueprint.Rooms[r].FloorIndex != 0)
                    {
                        continue;
                    }

                    Transform instance = mapRoot.transform.Find($"Room_{r}_{result.Blueprint.Rooms[r].TemplateId}");
                    if (instance == null)
                    {
                        continue;
                    }

                    Bounds rb = CombinedBounds(instance.gameObject);
                    var probe2 = new Vector3(rb.center.x, rootY + 0.5f, rb.center.z);
                    if (!UnityEngine.AI.NavMesh.SamplePosition(probe2, out UnityEngine.AI.NavMeshHit rh, 3f, UnityEngine.AI.NavMesh.AllAreas))
                    {
                        unreachable.Add($"방{r}({result.Blueprint.Rooms[r].TemplateId}) 스냅실패 {probe2}");
                        continue;
                    }

                    var rp = new UnityEngine.AI.NavMeshPath();
                    UnityEngine.AI.NavMesh.CalculatePath(from.Value, rh.position, UnityEngine.AI.NavMesh.AllAreas, rp);
                    if (rp.status == UnityEngine.AI.NavMeshPathStatus.PathComplete)
                    {
                        reachable++;
                    }
                    else
                    {
                        unreachable.Add($"방{r}({result.Blueprint.Rooms[r].TemplateId}) {rp.status} 중심 {rh.position}");
                    }
                }

                report.AppendLine($"  층0 도달: {reachable}·미도달 {unreachable.Count}");
                for (int i = 0; i < unreachable.Count && i < 8; i++)
                {
                    report.AppendLine($"    {unreachable[i]}");
                }
            }

            report.Append(DumpNavIslands());

            // 층0 간선별 통행 진단 — 간선 상태(통로/문)별로 개구 내비가 실제로 이어지는지 집계
            {
                var byState = new Dictionary<string, (int ok, int broken)>();
                var samplesBroken = new List<string>();
                for (int e = 0; e < result.Blueprint.Edges.Count; e++)
                {
                    BlueprintEdge edge = result.Blueprint.Edges[e];
                    if (edge.RoomB < 0 || result.Blueprint.IsVerticalEdge(edge))
                    {
                        continue; // 봉인 간선(RoomB<0)·수직 간선 제외
                    }

                    if (result.Blueprint.Rooms[edge.RoomA].FloorIndex != 0)
                    {
                        continue;
                    }

                    Vector3? pa = SnapRoomCenter(mapRoot, result.Blueprint, edge.RoomA, rootY);
                    Vector3? pb = SnapRoomCenter(mapRoot, result.Blueprint, edge.RoomB, rootY);
                    string state = edge.State.ToString();
                    if (!byState.ContainsKey(state))
                    {
                        byState[state] = (0, 0);
                    }

                    if (pa == null || pb == null)
                    {
                        continue;
                    }

                    var ep = new UnityEngine.AI.NavMeshPath();
                    UnityEngine.AI.NavMesh.CalculatePath(pa.Value, pb.Value, UnityEngine.AI.NavMesh.AllAreas, ep);
                    bool ok = ep.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
                    (int okC, int brokenC) = byState[state];
                    byState[state] = ok ? (okC + 1, brokenC) : (okC, brokenC + 1);
                    if (!ok && samplesBroken.Count < 6)
                    {
                        samplesBroken.Add($"간선{e} {state} 방{edge.RoomA}({result.Blueprint.Rooms[edge.RoomA].TemplateId})→방{edge.RoomB}({result.Blueprint.Rooms[edge.RoomB].TemplateId}) {pa.Value}→{pb.Value}");
                    }
                }

                foreach (KeyValuePair<string, (int ok, int broken)> kv in byState)
                {
                    report.AppendLine($"  층0 간선[{kv.Key}]: 통행 {kv.Value.ok}·단절 {kv.Value.broken}");
                }

                foreach (string s in samplesBroken)
                {
                    report.AppendLine($"    {s}");
                }

                // 첫 단절 OpenPassage 간선 하나를 골라 두 방 중심 사이를 정밀 스캔 — 끊긴 지점과 그 자리 지오메트리 실명
                for (int e = 0; e < result.Blueprint.Edges.Count; e++)
                {
                    BlueprintEdge edge = result.Blueprint.Edges[e];
                    if (edge.RoomB < 0 || result.Blueprint.IsVerticalEdge(edge) || edge.State != EdgeState.OpenPassage)
                    {
                        continue;
                    }

                    if (result.Blueprint.Rooms[edge.RoomA].FloorIndex != 0)
                    {
                        continue;
                    }

                    Vector3? pa = SnapRoomCenter(mapRoot, result.Blueprint, edge.RoomA, rootY);
                    Vector3? pb = SnapRoomCenter(mapRoot, result.Blueprint, edge.RoomB, rootY);
                    if (pa == null || pb == null)
                    {
                        continue;
                    }

                    var ep = new UnityEngine.AI.NavMeshPath();
                    UnityEngine.AI.NavMesh.CalculatePath(pa.Value, pb.Value, UnityEngine.AI.NavMesh.AllAreas, ep);
                    if (ep.status == UnityEngine.AI.NavMeshPathStatus.PathComplete)
                    {
                        continue;
                    }

                    MeshRenderer[] all = mapRoot.GetComponentsInChildren<MeshRenderer>(true);
                    report.AppendLine($"  단절 간선{e} 스캔 방{edge.RoomA}→방{edge.RoomB}:");
                    for (float s = 0f; s <= 1.001f; s += 0.04f)
                    {
                        Vector3 p = Vector3.Lerp(pa.Value, pb.Value, s);
                        bool on = UnityEngine.AI.NavMesh.SamplePosition(p + Vector3.up * 0.4f, out UnityEngine.AI.NavMeshHit h, 0.6f, UnityEngine.AI.NavMesh.AllAreas);
                        string reach = "내비없음";
                        if (on)
                        {
                            var lp = new UnityEngine.AI.NavMeshPath();
                            UnityEngine.AI.NavMesh.CalculatePath(pa.Value, h.position, UnityEngine.AI.NavMesh.AllAreas, lp);
                            reach = lp.status == UnityEngine.AI.NavMeshPathStatus.PathComplete ? "도달" : "섬분리";
                        }

                        var names = new List<string>();
                        if (reach != "도달")
                        {
                            foreach (MeshRenderer mr in all)
                            {
                                Bounds mb = mr.bounds;
                                if (p.x >= mb.min.x - 0.05f && p.x <= mb.max.x + 0.05f
                                    && p.z >= mb.min.z - 0.05f && p.z <= mb.max.z + 0.05f
                                    && mb.min.y < rootY + 1.5f && mb.max.y > rootY - 0.2f
                                    && mr.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
                                {
                                    names.Add($"{mr.name}(y {mb.min.y - rootY:F2}~{mb.max.y - rootY:F2})");
                                }
                            }
                        }

                        report.AppendLine($"    s={s:F2} ({p.x:F1},{p.z:F1}) {reach}{(on ? $" y={h.position.y - rootY:F2}" : "")}{(names.Count > 0 ? " — " + string.Join(" / ", names) : "")}");
                    }

                    break;
                }
            }


            Log.D(report.ToString());
            return report.ToString();
        }

        /// <summary>방 인스턴스 중심을 그 방 층 평면 NavMesh 에 스냅한다(없으면 null).</summary>
        private static Vector3? SnapRoomCenter(GameObject mapRoot, MapBlueprint blueprint, int roomIndex, float rootY)
        {
            Transform instance = mapRoot.transform.Find($"Room_{roomIndex}_{blueprint.Rooms[roomIndex].TemplateId}");
            if (instance == null)
            {
                return null;
            }

            Bounds rb = CombinedBounds(instance.gameObject);
            var probe = new Vector3(rb.center.x, rootY + 0.5f, rb.center.z);
            return UnityEngine.AI.NavMesh.SamplePosition(probe, out UnityEngine.AI.NavMeshHit hit, 3f, UnityEngine.AI.NavMesh.AllAreas)
                ? hit.position
                : (Vector3?)null;
        }

        /// <summary>현 NavMesh 삼각분할을 정점 용접(0.05m 격자)으로 섬 단위 클러스터링해 파편화 양상을 요약한다.</summary>
        /// <returns>섬 수 + 면적 상위 섬들의 바운드 요약.</returns>
        private static string DumpNavIslands()
        {
            UnityEngine.AI.NavMeshTriangulation tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
            int[] parent = new int[tri.vertices.Length];
            for (int i = 0; i < parent.Length; i++)
            {
                parent[i] = i;
            }

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }

                return x;
            }

            void Union(int a, int b)
            {
                int ra = Find(a);
                int rb = Find(b);
                if (ra != rb)
                {
                    parent[ra] = rb;
                }
            }

            // 근접 정점 용접 — 같은 0.05m 격자 키의 정점을 한 덩어리로
            var weld = new Dictionary<(int, int, int), int>();
            for (int i = 0; i < tri.vertices.Length; i++)
            {
                Vector3 v = tri.vertices[i];
                var key = (Mathf.RoundToInt(v.x * 20f), Mathf.RoundToInt(v.y * 20f), Mathf.RoundToInt(v.z * 20f));
                if (weld.TryGetValue(key, out int first))
                {
                    Union(i, first);
                }
                else
                {
                    weld[key] = i;
                }
            }

            for (int t = 0; t < tri.indices.Length; t += 3)
            {
                Union(tri.indices[t], tri.indices[t + 1]);
                Union(tri.indices[t], tri.indices[t + 2]);
            }

            var islands = new Dictionary<int, (Bounds bounds, int tris)>();
            for (int t = 0; t < tri.indices.Length; t += 3)
            {
                int root = Find(tri.indices[t]);
                Vector3 a = tri.vertices[tri.indices[t]];
                if (!islands.TryGetValue(root, out (Bounds bounds, int tris) entry))
                {
                    entry = (new Bounds(a, Vector3.zero), 0);
                }

                Bounds b = entry.bounds;
                b.Encapsulate(a);
                b.Encapsulate(tri.vertices[tri.indices[t + 1]]);
                b.Encapsulate(tri.vertices[tri.indices[t + 2]]);
                islands[root] = (b, entry.tris + 1);
            }

            var sorted = new List<(Bounds bounds, int tris)>(islands.Values);
            sorted.Sort((x, y) => (y.bounds.size.x * y.bounds.size.z).CompareTo(x.bounds.size.x * x.bounds.size.z));
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"  내비 섬 {sorted.Count}개 (상위 12):");
            for (int i = 0; i < sorted.Count && i < 12; i++)
            {
                Bounds b = sorted[i].bounds;
                sb.AppendLine($"    섬{i}: tris={sorted[i].tris} min=({b.min.x:F1},{b.min.y:F1},{b.min.z:F1}) max=({b.max.x:F1},{b.max.y:F1},{b.max.z:F1})");
            }

            return sb.ToString();
        }

        /// <summary>층의 아무 비복도 방 중심을 NavMesh 표면에 스냅한 지점을 구한다(없으면 null).</summary>
        private static Vector3? SamplePointOnFloor(GameObject mapRoot, MapBlueprint blueprint, MapDefinitionSO definition, int floorIndex)
        {
            float planeY = mapRoot.transform.position.y + FloorGeometry.FloorPlaneY(definition, floorIndex);
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                if (blueprint.Rooms[r].FloorIndex != floorIndex)
                {
                    continue;
                }

                Transform instance = mapRoot.transform.Find($"Room_{r}_{blueprint.Rooms[r].TemplateId}");
                if (instance == null)
                {
                    continue;
                }

                Bounds bounds = CombinedBounds(instance.gameObject);
                var probe = new Vector3(bounds.center.x, planeY + 0.5f, bounds.center.z);
                if (UnityEngine.AI.NavMesh.SamplePosition(probe, out UnityEngine.AI.NavMeshHit hit, 3f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    return hit.position;
                }
            }

            return null;
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
