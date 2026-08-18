using System.Collections.Generic;
using Border.Core;
using Border.Events;
using EmptyHouse.MapGen.Core;
using EmptyHouse.MapGen.Runtime;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace EmptyHouse.EditorTools
{
    /// <summary>
    /// 절차 맵 런타임 어댑터 원클릭 셋업(멱등) — 이벤트 채널 2종·프리팹 레지스트리 생성,
    /// MANAGERS 프리팹에 MapGenManager 자식(NetworkObject + 드라이버·베이커·스포너) 추가·배선,
    /// 플레이어 프리팹의 PlayerInteractor 신규 참조(deathHandler·playerReturn) 연결까지 처리한다.
    /// 문 프리팹·스폰 프리팹은 별도 제작 후 레지스트리에 수동 등재(재실행해도 기존 값 보존).
    /// </summary>
    public static class MapGenRuntimeSetup
    {
        private const string eventFolder = "Assets/03. ScriptableObjects/Events"; // 채널 SO 표준 위치
        private const string mapGenFolder = "Assets/03. ScriptableObjects/MapGen"; // 맵 생성 SO 위치
        private const string assembledChannelPath = eventFolder + "/SO_Event_MapAssembledServer.asset"; // 조립 완료(X7) 채널
        private const string navReadyChannelPath = eventFolder + "/SO_Event_MapNavMeshReadyServer.asset"; // 베이크 완료 채널
        private const string registryPath = mapGenFolder + "/SO_MapPrefabRegistry.asset"; // 프리팹 레지스트리(테마 무관 상호작용·아이템 — M10-1 이후 CommonRegistry 역할)
        private const string templatesFolder = mapGenFolder + "/Templates"; // 방 템플릿 SO 위치(M9-3)
        private const string mapDefinitionPath = mapGenFolder + "/SO_Map_Hall.asset"; // 빈 집 정의 단일 출처(M10-1 — 런타임·에디터 프리뷰 공유)
        private const string floorDefinitionPath = mapGenFolder + "/SO_Floor_Hall_Main.asset"; // 단층 층 정의(환경 프리팹·템플릿 소유)
        private const string managersPrefabPath = "Assets/02. Prefab/GameScene/=====MANAGERS=====.prefab"; // 상주 매니저 프리팹
        private const string managerChildName = "MapGenManager"; // MANAGERS 하위 자식 이름
        private const string playerSpawnerPrefabPath = "Assets/02. Prefab/Manager/GameScenePlayerSpawner.prefab"; // 플레이어 스포너(맵 조립 게이트 배선 대상)

        /// <summary>플레이어 프리팹 경로 — PlayerInteractor 참조 배선 대상.</summary>
        private static readonly string[] playerPrefabPaths =
        {
            "Assets/02. Prefab/Player/Player.prefab",
            "Assets/02. Prefab/Player/Player_UnityChan.prefab",
        };

        /// <summary>TemplateId → 기본(폴백) 방 프리팹 경로 — **템플릿 SO 최초 생성 시드 전용**(생성 후엔 RoomTemplateSO.Prefab 이 원천이라 재실행해도 덮지 않는다).</summary>
        private static readonly (string id, string path)[] roomPrefabPaths =
        {
            ("entrance_6x6", "Assets/02. Prefab/Map/DecoratedRooms/Entrance/Entrance-EmptyRoom-6x6.prefab"),
            ("room_3x3", "Assets/02. Prefab/Map/EmptyRooms/EmptyRoom-3x3.prefab"),
            ("room_6x6", "Assets/02. Prefab/Map/EmptyRooms/EmptyRoom-6x6.prefab"),
            ("room_6x9", "Assets/02. Prefab/Map/EmptyRooms/EmptyRoom-6x9.prefab"),
            ("hallway", "Assets/02. Prefab/Map/EmptyRooms/Hallway.prefab"),
            ("safezone_3x3", "Assets/02. Prefab/Map/EmptyRooms/EmptyRoom-3x3.prefab"),
            ("hallway_x2", "Assets/02. Prefab/Map/EmptyRooms/Hallway x2.prefab"),
        };

        /// <summary>사이즈 폴더 → 매칭 템플릿 후보(DecoratedRooms 변형 풀 스캔 대상 — 후보가 여럿이면 바닥 실측 셀 크기로 판별).</summary>
        private static readonly (string folder, string[] templateIds)[] variantFolders =
        {
            ("Assets/02. Prefab/Map/DecoratedRooms/3x3", new[] { "room_3x3" }),
            ("Assets/02. Prefab/Map/DecoratedRooms/6x6", new[] { "room_6x6" }),
            ("Assets/02. Prefab/Map/DecoratedRooms/6x9", new[] { "room_6x9" }),
            ("Assets/02. Prefab/Map/DecoratedRooms/Entrance", new[] { "entrance_6x6" }),
            ("Assets/02. Prefab/Map/DecoratedRooms/Hallway", new[] { "hallway", "hallway_x2" }),
            ("Assets/02. Prefab/Map/DecoratedRooms/SafeZone", new[] { "safezone_3x3" }),
        };

        private const string sealWallPath = "Assets/04. Arts/Environment/HorrorPack/!Prefabs/Architectural/Hall_Props/Hall_Wall_6M_1Side.prefab"; // 복도 봉인 벽
        private const string cornerColumnPath = "Assets/04. Arts/Environment/HorrorPack/!Prefabs/Architectural/Hall_Props/Hall_Clumn_Large_6M.prefab"; // 이음 기둥

        /// <summary>채널·레지스트리·MANAGERS·플레이어 프리팹을 순서대로 셋업한다(재실행 안전).</summary>
        public static void Setup()
        {
            Log.D("[MapGenRuntimeSetup] Setup");
            VoidEventChannelSO assembledChannel = EnsureChannel(assembledChannelPath);
            VoidEventChannelSO navReadyChannel = EnsureChannel(navReadyChannelPath);
            MapPrefabRegistrySO registry = EnsureRegistry();
            RoomTemplateSO[] templates = EnsureTemplates(); // 템플릿 SO·변형 풀은 매 실행 재스캔(기존 코어 데이터는 보존)
            MapDefinitionSO mapDefinition = EnsureMapDefinition(registry, templates);
            SetupManagersPrefab(mapDefinition, assembledChannel, navReadyChannel);
            SetupPlayerSpawnerPrefab(assembledChannel);
            SetupPlayerPrefabs();
            AssetDatabase.SaveAssets();
            Log.D("[MapGenRuntimeSetup] 완료 — 남은 수동 작업: 문 프리팹 제작·레지스트리 DoorPrefab/SpawnPrefabs 등재·NetworkPrefabs 등록");
        }

        /// <summary>Void 이벤트 채널 에셋을 확보한다(없으면 생성).</summary>
        /// <param name="path">에셋 경로.</param>
        /// <returns>채널 에셋.</returns>
        private static VoidEventChannelSO EnsureChannel(string path)
        {
            var channel = AssetDatabase.LoadAssetAtPath<VoidEventChannelSO>(path);
            if (channel != null)
            {
                return channel;
            }

            EnsureFolder(eventFolder);
            channel = ScriptableObject.CreateInstance<VoidEventChannelSO>();
            AssetDatabase.CreateAsset(channel, path);
            Log.D($"[MapGenRuntimeSetup] 채널 생성 {path}");
            return channel;
        }

        /// <summary>
        /// 빈 집 정의(단층 hall)를 확보한다(M10-1) — 없으면 층 정의(템플릿·봉인 벽·기둥·미터 규격)와 함께 생성한다.
        /// 기존 에셋은 절대 덮지 않는다(튜닝·배선 결과 보존).
        /// </summary>
        /// <param name="registry">테마 무관 상호작용·아이템 레지스트리(CommonRegistry 로 배선).</param>
        /// <param name="templates">확보된 방 템플릿 SO 배열(층 정의 최초 생성 시드).</param>
        /// <returns>빈 집 정의 에셋.</returns>
        private static MapDefinitionSO EnsureMapDefinition(MapPrefabRegistrySO registry, RoomTemplateSO[] templates)
        {
            var definition = AssetDatabase.LoadAssetAtPath<MapDefinitionSO>(mapDefinitionPath);
            if (definition != null)
            {
                return definition;
            }

            EnsureFolder(mapGenFolder);
            var floor = AssetDatabase.LoadAssetAtPath<FloorDefinitionSO>(floorDefinitionPath);
            if (floor == null)
            {
                floor = ScriptableObject.CreateInstance<FloorDefinitionSO>();
                AssetDatabase.CreateAsset(floor, floorDefinitionPath);
                floor.ThemeId = "hall";
                floor.Templates = templates;
                floor.SealWallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sealWallPath);
                floor.CornerColumnPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(cornerColumnPath);
                floor.CellMeters = MapTemplateCatalog.CellMeters;
                floor.FloorHeight = 9f;
                EditorUtility.SetDirty(floor);
                Log.W($"[MapGenRuntimeSetup] 층 정의 생성 {floorDefinitionPath} — DoorPrefab·ReturnExitPrefab 은 제작 후 층 정의에 연결해야 스폰된다");
            }

            definition = ScriptableObject.CreateInstance<MapDefinitionSO>();
            AssetDatabase.CreateAsset(definition, mapDefinitionPath);
            definition.MapId = "hall";
            definition.Floors = new[] { floor };
            definition.BasementCount = 0;
            definition.CommonRegistry = registry;
            EditorUtility.SetDirty(definition);
            Log.D($"[MapGenRuntimeSetup] 빈 집 정의 생성 {mapDefinitionPath} — 생성 파라미터는 인스펙터에서 조정하면 런타임·프리뷰가 함께 따라간다");
            return definition;
        }

        /// <summary>공용 레지스트리(테마 무관 상호작용·아이템)를 확보한다 — 스폰·페어 프리팹 기존 값은 보존한다.</summary>
        /// <returns>레지스트리 에셋.</returns>
        private static MapPrefabRegistrySO EnsureRegistry()
        {
            var registry = AssetDatabase.LoadAssetAtPath<MapPrefabRegistrySO>(registryPath);
            if (registry == null)
            {
                EnsureFolder(mapGenFolder);
                registry = ScriptableObject.CreateInstance<MapPrefabRegistrySO>();
                AssetDatabase.CreateAsset(registry, registryPath);
                Log.D($"[MapGenRuntimeSetup] 레지스트리 생성 {registryPath}");
            }

            if (registry.SpawnPrefabs == null)
            {
                registry.SpawnPrefabs = new SpawnPrefabEntry[0];
            }

            EditorUtility.SetDirty(registry);
            return registry;
        }

        /// <summary>
        /// 방 템플릿 SO 를 확보한다(M9-3) — 카탈로그 픽스처 순서대로 SO_Template_{id}.asset 을 만들고(최초 1회 시드),
        /// 기존 에셋의 **코어 데이터·기본 프리팹은 보존**한다(SO 가 진실 — 재실행이 튜닝을 덮지 않는다).
        /// 변형 풀(Variants)만 매 실행 스캔 산출물로 갱신하고, 카탈로그와의 코어 데이터 드리프트는 경고로 보고한다
        /// (골든 픽스처와 실생성이 갈렸다는 신호 — 의도된 변경이면 카탈로그 픽스처·골든도 갱신할 것).
        /// </summary>
        /// <returns>카탈로그 순서의 템플릿 SO 배열(= 코어 후보 순서).</returns>
        private static RoomTemplateSO[] EnsureTemplates()
        {
            EnsureFolder(templatesFolder);
            List<RoomTemplateDef> defs = MapTemplateCatalog.Create();
            var result = new RoomTemplateSO[defs.Count];
            for (int i = 0; i < defs.Count; i++)
            {
                string path = $"{templatesFolder}/SO_Template_{defs[i].TemplateId}.asset";
                var so = AssetDatabase.LoadAssetAtPath<RoomTemplateSO>(path);
                if (so == null)
                {
                    so = ScriptableObject.CreateInstance<RoomTemplateSO>();
                    so.CopyFrom(defs[i]);
                    so.ExcludeFromNavMesh = defs[i].TemplateId == "safezone_3x3"; // 안전지대 어댑터 플래그 시드(최초 1회 — 이후 진실은 SO)
                    AssetDatabase.CreateAsset(so, path);
                    Log.D($"[MapGenRuntimeSetup] 템플릿 SO 생성 {path}");
                }
                else if (!RoomTemplateSO.DefEquals(so.ToDef(), defs[i]))
                {
                    Log.W($"[MapGenRuntimeSetup] 템플릿 드리프트 — {defs[i].TemplateId} 의 SO 코어 데이터가 카탈로그 픽스처와 다르다. " +
                          "SO 가 실생성 진실이고 골든은 픽스처 기준이라 회귀 게이트가 실생성을 못 지킨다 — 의도된 변경이면 카탈로그·골든 갱신 필요");
                }

                if (so.Prefab == null)
                {
                    foreach ((string id, string prefabPath) in roomPrefabPaths)
                    {
                        if (id == defs[i].TemplateId)
                        {
                            so.Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                        }
                    }

                    if (so.Prefab == null)
                    {
                        Log.E($"[MapGenRuntimeSetup] 기본 프리팹 시드 실패: {defs[i].TemplateId}");
                    }
                }

                result[i] = so;
            }

            FillVariantPools(result, defs);
            return result;
        }

        /// <summary>
        /// DecoratedRooms 사이즈 폴더를 스캔해 템플릿 SO 의 변형 풀(Variants)을 채운다 — 경로 오름차순 정렬이
        /// 선택 순서라 결정론의 일부다(전 클라 같은 에셋 = 같은 풀). 부적합 프리팹은 린트로 걸러 풀에서 제외한다.
        /// </summary>
        /// <param name="templateAssets">채울 템플릿 SO 배열.</param>
        /// <param name="templates">코어 템플릿 목록(린트 실측 대조용).</param>
        private static void FillVariantPools(RoomTemplateSO[] templateAssets, List<RoomTemplateDef> templates)
        {
            var pools = new Dictionary<string, List<GameObject>>();
            foreach ((string folder, string[] templateIds) in variantFolders)
            {
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    Log.W($"[MapGenRuntimeSetup] 변형 폴더 없음 — 건너뜀: {folder}");
                    continue;
                }

                var paths = new List<string>();
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                {
                    paths.Add(AssetDatabase.GUIDToAssetPath(guid));
                }

                paths.Sort(System.StringComparer.Ordinal);
                foreach (string path in paths)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    string templateId = LintVariant(prefab, path, templateIds, templates);
                    if (templateId == null)
                    {
                        continue;
                    }

                    if (!pools.TryGetValue(templateId, out List<GameObject> pool))
                    {
                        pool = new List<GameObject>();
                        pools[templateId] = pool;
                    }

                    pool.Add(prefab);
                }
            }

            for (int i = 0; i < templateAssets.Length; i++)
            {
                templateAssets[i].Variants = pools.TryGetValue(templateAssets[i].TemplateId, out List<GameObject> matched) ? matched.ToArray() : new GameObject[0];
                EditorUtility.SetDirty(templateAssets[i]);
                Log.D($"[MapGenRuntimeSetup] 변형 풀 {templateAssets[i].TemplateId}: {templateAssets[i].Variants.Length}종{(templateAssets[i].Variants.Length == 0 ? " — 기본 프리팹 폴백" : string.Empty)}");
            }
        }

        /// <summary>
        /// 템플릿 SO 추출본과 카탈로그 픽스처의 동기 상태를 검사한다 — 골든(픽스처 기준)이 실생성(SO 기준)을
        /// 지키려면 둘이 같아야 한다. unity-cli exec 로도 호출 가능한 진단 진입점.
        /// </summary>
        /// <returns>검사 결과 요약 문자열.</returns>
        public static string ValidateTemplateSync()
        {
            var floor = AssetDatabase.LoadAssetAtPath<FloorDefinitionSO>(floorDefinitionPath);
            if (floor == null || floor.Templates == null)
            {
                return "[MapGenRuntimeSetup] 층 정의/템플릿 미생성 — 셋업을 먼저 실행";
            }

            List<RoomTemplateDef> fromSo = floor.CreateTemplates();
            List<RoomTemplateDef> fixture = MapTemplateCatalog.Create();
            if (fromSo.Count != fixture.Count)
            {
                return $"[MapGenRuntimeSetup] 동기 실패 — 템플릿 수 SO {fromSo.Count} vs 픽스처 {fixture.Count}";
            }

            for (int i = 0; i < fixture.Count; i++)
            {
                if (!RoomTemplateSO.DefEquals(fromSo[i], fixture[i]))
                {
                    return $"[MapGenRuntimeSetup] 동기 실패 — 인덱스 {i}({fixture[i].TemplateId}) 코어 데이터 불일치";
                }
            }

            return $"[MapGenRuntimeSetup] 동기 확인 — 템플릿 {fixture.Count}종 SO == 카탈로그 픽스처(순서 포함)";
        }

        /// <summary>
        /// 변형 프리팹 린트 — NetworkObject 포함 거부(방은 로컬 조립이라 세션에 못 들어간다),
        /// 테마 바닥 실측(IsFloorRenderer — 셀 정렬 계약), 풋프린트 셀 크기로 후보 템플릿 판별(Hallway 1x2/2x2 판별 겸용).
        /// 소켓 문 슬롯을 막는 프롭은 경고만 하고 풀에는 넣는다(저작 중 상태 허용).
        /// </summary>
        /// <param name="prefab">검사할 프리팹.</param>
        /// <param name="path">프리팹 경로(로그용).</param>
        /// <param name="candidateIds">폴더의 후보 템플릿 ID 목록.</param>
        /// <param name="templates">템플릿 집합.</param>
        /// <returns>매칭 템플릿 ID — 부적합이면 null(풀 제외).</returns>
        private static string LintVariant(GameObject prefab, string path, string[] candidateIds, List<RoomTemplateDef> templates)
        {
            if (prefab.GetComponentInChildren<NetworkObject>(true) != null)
            {
                Log.E($"[MapGenRuntimeSetup] 변형 부적합(NetworkObject 포함) — 풀 제외: {path}");
                return null;
            }

            Bounds floor = default;
            bool found = false;
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (!MapRuntimeAssembler.IsFloorRenderer(renderer.name))
                {
                    continue;
                }

                if (!found)
                {
                    floor = renderer.bounds;
                    found = true;
                }
                else
                {
                    floor.Encapsulate(renderer.bounds);
                }
            }

            if (!found)
            {
                Log.E($"[MapGenRuntimeSetup] 변형 부적합(테마 바닥 타일 없음 — 셀 정렬 불가, Hall_Floor/Wards_Floor 계열 필요) — 풀 제외: {path}");
                return null;
            }

            float cell = MapTemplateCatalog.CellMeters;
            int cellsX = Mathf.RoundToInt(floor.size.x / cell);
            int cellsY = Mathf.RoundToInt(floor.size.z / cell);
            if (Mathf.Abs(floor.size.x - cellsX * cell) > 0.5f || Mathf.Abs(floor.size.z - cellsY * cell) > 0.5f)
            {
                Log.W($"[MapGenRuntimeSetup] 변형 바닥 크기 드리프트({floor.size.x:F2}x{floor.size.z:F2}m — 셀 격자 오정렬 의심): {path}");
            }

            foreach (string id in candidateIds)
            {
                foreach (RoomTemplateDef template in templates)
                {
                    if (template.TemplateId != id || template.WidthCells != cellsX || template.HeightCells != cellsY)
                    {
                        continue;
                    }

                    WarnSocketBlockers(prefab, template, floor, path);
                    return id;
                }
            }

            Log.E($"[MapGenRuntimeSetup] 변형 부적합(실측 {cellsX}x{cellsY}셀이 폴더 템플릿과 불일치) — 풀 제외: {path}");
            return null;
        }

        /// <summary>
        /// 소켓 문 슬롯 게이트(3.9m 폭 × 5.8m 높이)와 교차하는 프롭(벽·바닥 제외)을 경고한다 — 문 자리를 가구가 막는 사고 예방.
        /// 벽 오브젝트(이름에 "Wall")의 자식 프롭은 제외 — 조립기가 개구를 뚫을 때 벽을 통째로 비활성화하므로 함께 사라진다.
        /// **벽에 붙는 소품은 그 벽의 자식으로 넣는 것이 규약이다**(액자·선반·스위치). 방 루트에 두면 벽이 뚫린 뒤 공중에 뜨거나 문을 막는다.
        /// </summary>
        /// <param name="prefab">검사할 프리팹.</param>
        /// <param name="template">매칭 템플릿.</param>
        /// <param name="floor">바닥 실측 바운드.</param>
        /// <param name="path">프리팹 경로(로그용).</param>
        private static void WarnSocketBlockers(GameObject prefab, RoomTemplateDef template, Bounds floor, string path)
        {
            float cell = MapTemplateCatalog.CellMeters;
            foreach (SocketDef socket in template.Sockets)
            {
                Vector3 dirVec;
                switch (socket.Direction)
                {
                    case SocketDirection.North: dirVec = Vector3.forward; break;
                    case SocketDirection.East: dirVec = Vector3.right; break;
                    case SocketDirection.South: dirVec = Vector3.back; break;
                    default: dirVec = Vector3.left; break;
                }

                var cellCenter = new Vector3(floor.min.x + (socket.LocalCell.X + 0.5f) * cell, floor.max.y, floor.min.z + (socket.LocalCell.Y + 0.5f) * cell);
                Vector3 gateCenter = cellCenter + dirVec * (cell * 0.5f);
                bool alongX = socket.Direction == SocketDirection.North || socket.Direction == SocketDirection.South;
                var gate = new Bounds(gateCenter + Vector3.up * 2.9f, alongX ? new Vector3(3.9f, 5.8f, 1.6f) : new Vector3(1.6f, 5.8f, 3.9f));
                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.name.Contains("Wall") || MapRuntimeAssembler.IsFloorRenderer(renderer.name) || IsUnderWall(renderer.transform, prefab.transform))
                    {
                        continue;
                    }

                    if (renderer.bounds.Intersects(gate))
                    {
                        Log.W($"[MapGenRuntimeSetup] 소켓 {socket.Id} 문 슬롯을 프롭이 막는다({renderer.name}) — 문이 열리면 통행 불가 소지: {path}");
                        break; // 소켓당 1회만 경고
                    }
                }
            }
        }

        /// <summary>이 트랜스폼이 벽 오브젝트의 자식인지 — 벽이 개구로 잘리면 함께 사라지므로 소켓 막힘 검사에서 제외한다.</summary>
        /// <param name="target">검사할 트랜스폼.</param>
        /// <param name="root">프리팹 루트(여기까지만 거슬러 올라간다).</param>
        /// <returns>조상 중 이름에 "Wall" 이 있으면 true.</returns>
        private static bool IsUnderWall(Transform target, Transform root)
        {
            for (Transform parent = target.parent; parent != null && parent != root; parent = parent.parent)
            {
                if (parent.name.Contains("Wall"))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>MANAGERS 프리팹에 MapGenManager 자식을 확보하고 컴포넌트·참조를 배선한다.</summary>
        /// <param name="mapDefinition">빈 집 정의(M10-1 — 구 레지스트리·파라미터 직참조를 대체).</param>
        /// <param name="assembledChannel">조립 완료 채널.</param>
        /// <param name="navReadyChannel">베이크 완료 채널.</param>
        private static void SetupManagersPrefab(MapDefinitionSO mapDefinition, VoidEventChannelSO assembledChannel, VoidEventChannelSO navReadyChannel)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(managersPrefabPath);
            Transform child = root.transform.Find(managerChildName);
            if (child == null)
            {
                var go = new GameObject(managerChildName);
                go.transform.SetParent(root.transform, false);
                child = go.transform;
            }

            // MapGenManager 가 별도 프리팹으로 분리돼 있으면 그 원본에 배선한다 —
            // 여기(중첩 인스턴스)에 쓰면 오버라이드로 박혀 원본과 값이 갈린다(설정 출처 이원화 방지)
            if (PrefabUtility.IsPartOfPrefabInstance(child.gameObject))
            {
                string sourcePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject);
                PrefabUtility.UnloadPrefabContents(root);
                WireManagerComponents(sourcePath, mapDefinition, assembledChannel, navReadyChannel);
                return;
            }

            // 조립 앵커 고정 — 입구 앵커 방이 이 transform 위치에 오므로(MapRuntimeAssembler 계약)
            // MANAGERS(씬 원점 배치) 기준 로컬 0 = 입구 월드 (0,0,0)·무회전을 보장한다
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;

            EnsureComponent<NetworkObject>(child.gameObject); // 드라이버(NetworkBehaviour)의 in-scene 스폰 전제
            MapGenNetworkDriver driver = EnsureComponent<MapGenNetworkDriver>(child.gameObject);
            MapNavMeshRuntimeBaker baker = EnsureComponent<MapNavMeshRuntimeBaker>(child.gameObject);
            MapStateObjectSpawner spawner = EnsureComponent<MapStateObjectSpawner>(child.gameObject);

            // private [SerializeField] 배선 — SerializedObject 경유(프리팹 에셋 직접 수정)
            var driverSo = new SerializedObject(driver);
            driverSo.FindProperty("mapDefinition").objectReferenceValue = mapDefinition;
            driverSo.FindProperty("onMapAssembledServer").objectReferenceValue = assembledChannel;
            driverSo.ApplyModifiedPropertiesWithoutUndo();

            var bakerSo = new SerializedObject(baker);
            bakerSo.FindProperty("driver").objectReferenceValue = driver;
            bakerSo.FindProperty("onMapAssembledServer").objectReferenceValue = assembledChannel;
            bakerSo.FindProperty("onMapNavMeshReadyServer").objectReferenceValue = navReadyChannel;
            bakerSo.ApplyModifiedPropertiesWithoutUndo();

            var spawnerSo = new SerializedObject(spawner);
            spawnerSo.FindProperty("driver").objectReferenceValue = driver;
            spawnerSo.FindProperty("onMapNavMeshReadyServer").objectReferenceValue = navReadyChannel;
            spawnerSo.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, managersPrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            Log.D($"[MapGenRuntimeSetup] MANAGERS 프리팹 배선 완료 — {managerChildName}");
        }

        /// <summary>
        /// 분리된 MapGenManager 프리팹 원본에 컴포넌트·참조를 배선한다 — 중첩 인스턴스 오버라이드가 아니라
        /// 원본을 고쳐야 어디에 배치하든 같은 값이 나온다(설정 단일 출처).
        /// </summary>
        /// <param name="prefabPath">MapGenManager 프리팹 경로.</param>
        /// <param name="mapDefinition">빈 집 정의(M10-1).</param>
        /// <param name="assembledChannel">조립 완료 채널.</param>
        /// <param name="navReadyChannel">베이크 완료 채널.</param>
        private static void WireManagerComponents(string prefabPath, MapDefinitionSO mapDefinition, VoidEventChannelSO assembledChannel, VoidEventChannelSO navReadyChannel)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            EnsureComponent<NetworkObject>(root);
            MapGenNetworkDriver driver = EnsureComponent<MapGenNetworkDriver>(root);
            MapNavMeshRuntimeBaker baker = EnsureComponent<MapNavMeshRuntimeBaker>(root);
            MapStateObjectSpawner spawner = EnsureComponent<MapStateObjectSpawner>(root);

            var driverSo = new SerializedObject(driver);
            driverSo.FindProperty("mapDefinition").objectReferenceValue = mapDefinition;
            driverSo.FindProperty("onMapAssembledServer").objectReferenceValue = assembledChannel;
            driverSo.ApplyModifiedPropertiesWithoutUndo();

            var bakerSo = new SerializedObject(baker);
            bakerSo.FindProperty("driver").objectReferenceValue = driver;
            bakerSo.FindProperty("onMapAssembledServer").objectReferenceValue = assembledChannel;
            bakerSo.FindProperty("onMapNavMeshReadyServer").objectReferenceValue = navReadyChannel;
            bakerSo.ApplyModifiedPropertiesWithoutUndo();

            var spawnerSo = new SerializedObject(spawner);
            spawnerSo.FindProperty("driver").objectReferenceValue = driver;
            spawnerSo.FindProperty("onMapNavMeshReadyServer").objectReferenceValue = navReadyChannel;
            spawnerSo.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            Log.D($"[MapGenRuntimeSetup] MapGenManager 원본 배선 완료 — {prefabPath}");
        }

        /// <summary>플레이어 스포너 프리팹에 맵 조립 완료(X7) 채널을 배선한다 — 조립 전 스폰 게이트용.</summary>
        /// <param name="assembledChannel">조립 완료 채널.</param>
        private static void SetupPlayerSpawnerPrefab(VoidEventChannelSO assembledChannel)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(playerSpawnerPrefabPath);
            var spawner = root.GetComponent<GameScenePlayerSpawner>();
            var so = new SerializedObject(spawner);
            so.FindProperty("onMapAssembledServer").objectReferenceValue = assembledChannel;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, playerSpawnerPrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            Log.D($"[MapGenRuntimeSetup] 플레이어 스포너 배선 완료: {playerSpawnerPrefabPath}");
        }

        /// <summary>플레이어 프리팹의 PlayerInteractor 에 deathHandler·playerReturn 참조를 연결한다.</summary>
        private static void SetupPlayerPrefabs()
        {
            foreach (string path in playerPrefabPaths)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                var interactor = root.GetComponentInChildren<PlayerInteractor>(true);
                if (interactor == null)
                {
                    Log.W($"[MapGenRuntimeSetup] PlayerInteractor 없음 — 건너뜀: {path}");
                    PrefabUtility.UnloadPrefabContents(root);
                    continue;
                }

                var so = new SerializedObject(interactor);
                so.FindProperty("deathHandler").objectReferenceValue = root.GetComponentInChildren<PlayerDeathHandler>(true);
                so.FindProperty("playerReturn").objectReferenceValue = root.GetComponentInChildren<PlayerReturn>(true);
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
                Log.D($"[MapGenRuntimeSetup] 플레이어 참조 배선 완료: {path}");
            }
        }

        /// <summary>컴포넌트를 확보한다(없으면 추가).</summary>
        /// <param name="target">대상 게임오브젝트.</param>
        /// <returns>확보한 컴포넌트.</returns>
        private static T EnsureComponent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        /// <summary>폴더를 확보한다(없으면 생성 — 상위는 존재 전제).</summary>
        /// <param name="folder">에셋 폴더 경로.</param>
        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            int slash = folder.LastIndexOf('/');
            AssetDatabase.CreateFolder(folder.Substring(0, slash), folder.Substring(slash + 1));
        }
    }
}
