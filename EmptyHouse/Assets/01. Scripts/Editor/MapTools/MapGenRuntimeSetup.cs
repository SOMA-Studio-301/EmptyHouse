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
        private const string registryPath = mapGenFolder + "/SO_MapPrefabRegistry.asset"; // 프리팹 레지스트리
        private const string genParamsPath = mapGenFolder + "/SO_MapGenParams.asset"; // 생성 파라미터 단일 출처(런타임·에디터 프리뷰 공유)
        private const string managersPrefabPath = "Assets/02. Prefab/GameScene/=====MANAGERS=====.prefab"; // 상주 매니저 프리팹
        private const string managerChildName = "MapGenManager"; // MANAGERS 하위 자식 이름
        private const string playerSpawnerPrefabPath = "Assets/02. Prefab/Manager/GameScenePlayerSpawner.prefab"; // 플레이어 스포너(맵 조립 게이트 배선 대상)

        /// <summary>플레이어 프리팹 경로 — PlayerInteractor 참조 배선 대상.</summary>
        private static readonly string[] playerPrefabPaths =
        {
            "Assets/02. Prefab/Player/Player.prefab",
            "Assets/02. Prefab/Player/Player_UnityChan.prefab",
        };

        /// <summary>TemplateId → 기본(폴백) 방 프리팹 경로(MapGen.Editor asmdef 가 비참조라 PrefabRoomTemplates.PrefabPaths 를 복제 — 경로 변경 시 양쪽 갱신).</summary>
        private static readonly (string id, string path)[] roomPrefabPaths =
        {
            ("entrance_6x6", "Assets/02. Prefab/Map/DecoratedRooms/Entrance/Entrance-EmptyRoom-6x6.prefab"),
            ("room_3x3", "Assets/02. Prefab/Map/EmptyRooms/EmptyRoom-3x3.prefab"),
            ("room_6x6", "Assets/02. Prefab/Map/EmptyRooms/EmptyRoom-6x6.prefab"),
            ("room_6x9", "Assets/02. Prefab/Map/EmptyRooms/EmptyRoom-6x9.prefab"),
            ("hallway", "Assets/02. Prefab/Map/EmptyRooms/Hallway.prefab"),
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
        };

        private const string sealWallPath = "Assets/04. Arts/Environment/HorrorPack/!Prefabs/Architectural/Hall_Props/Hall_Wall_6M_1Side.prefab"; // 복도 봉인 벽
        private const string cornerColumnPath = "Assets/04. Arts/Environment/HorrorPack/!Prefabs/Architectural/Hall_Props/Hall_Clumn_Large_6M.prefab"; // 이음 기둥

        /// <summary>채널·레지스트리·MANAGERS·플레이어 프리팹을 순서대로 셋업한다(재실행 안전).</summary>
        [MenuItem("Tools/Map/런타임 어댑터 셋업")]
        public static void Setup()
        {
            Log.D("[MapGenRuntimeSetup] Setup");
            VoidEventChannelSO assembledChannel = EnsureChannel(assembledChannelPath);
            VoidEventChannelSO navReadyChannel = EnsureChannel(navReadyChannelPath);
            MapPrefabRegistrySO registry = EnsureRegistry();
            MapGenParamsSO genParams = EnsureGenParams();
            SetupManagersPrefab(registry, genParams, assembledChannel, navReadyChannel);
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

        /// <summary>생성 파라미터 에셋을 확보한다(없으면 코드 기본값으로 생성) — 기존 값은 절대 덮지 않는다(튜닝 결과 보존).</summary>
        /// <returns>파라미터 에셋.</returns>
        private static MapGenParamsSO EnsureGenParams()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenParamsSO>(genParamsPath);
            if (asset != null)
            {
                return asset;
            }

            EnsureFolder(mapGenFolder);
            asset = ScriptableObject.CreateInstance<MapGenParamsSO>();
            AssetDatabase.CreateAsset(asset, genParamsPath);
            Log.D($"[MapGenRuntimeSetup] 파라미터 에셋 생성 {genParamsPath} — 값은 코드 기본값. 인스펙터에서 조정하면 런타임·프리뷰가 함께 따라간다");
            return asset;
        }

        /// <summary>프리팹 레지스트리를 확보하고 방·봉인 벽·기둥을 채운다 — 문·스폰 프리팹 기존 값은 보존한다.</summary>
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

            var rooms = new RoomPrefabEntry[roomPrefabPaths.Length];
            for (int i = 0; i < roomPrefabPaths.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(roomPrefabPaths[i].path);
                if (prefab == null)
                {
                    Log.E($"[MapGenRuntimeSetup] 방 프리팹 없음: {roomPrefabPaths[i].path}");
                }

                rooms[i] = new RoomPrefabEntry { TemplateId = roomPrefabPaths[i].id, Prefab = prefab };
            }

            FillVariantPools(rooms);
            registry.RoomPrefabs = rooms;
            registry.SealWallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sealWallPath);
            registry.CornerColumnPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(cornerColumnPath);
            registry.CellMeters = MapTemplateCatalog.CellMeters;
            if (registry.SpawnPrefabs == null)
            {
                registry.SpawnPrefabs = new SpawnPrefabEntry[0];
            }

            if (registry.DoorPrefab == null)
            {
                Log.W("[MapGenRuntimeSetup] DoorPrefab 미등재 — 문 프리팹 제작 후 레지스트리에 연결해야 문이 스폰된다");
            }

            EditorUtility.SetDirty(registry);
            return registry;
        }

        /// <summary>
        /// DecoratedRooms 사이즈 폴더를 스캔해 변형 풀(Variants)을 채운다 — 경로 오름차순 정렬이 선택 순서라
        /// 결정론의 일부다(전 클라 같은 빌드 = 같은 풀). 부적합 프리팹은 린트로 걸러 풀에서 제외한다.
        /// </summary>
        /// <param name="rooms">채울 방 엔트리 배열.</param>
        private static void FillVariantPools(RoomPrefabEntry[] rooms)
        {
            List<RoomTemplateDef> templates = MapTemplateCatalog.Create();
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

            for (int i = 0; i < rooms.Length; i++)
            {
                rooms[i].Variants = pools.TryGetValue(rooms[i].TemplateId, out List<GameObject> matched) ? matched.ToArray() : new GameObject[0];
                Log.D($"[MapGenRuntimeSetup] 변형 풀 {rooms[i].TemplateId}: {rooms[i].Variants.Length}종{(rooms[i].Variants.Length == 0 ? " — 기본 프리팹 폴백" : string.Empty)}");
            }
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

        /// <summary>소켓 문 슬롯 게이트(3.9m 폭 × 5.8m 높이)와 교차하는 프롭(벽·바닥 제외)을 경고한다 — 문 자리를 가구가 막는 사고 예방.</summary>
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
                    if (renderer.name.Contains("Wall") || MapRuntimeAssembler.IsFloorRenderer(renderer.name))
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

        /// <summary>MANAGERS 프리팹에 MapGenManager 자식을 확보하고 컴포넌트·참조를 배선한다.</summary>
        /// <param name="registry">프리팹 레지스트리.</param>
        /// <param name="genParams">생성 파라미터 에셋.</param>
        /// <param name="assembledChannel">조립 완료 채널.</param>
        /// <param name="navReadyChannel">베이크 완료 채널.</param>
        private static void SetupManagersPrefab(MapPrefabRegistrySO registry, MapGenParamsSO genParams, VoidEventChannelSO assembledChannel, VoidEventChannelSO navReadyChannel)
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
                WireManagerComponents(sourcePath, registry, genParams, assembledChannel, navReadyChannel);
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
            driverSo.FindProperty("prefabRegistry").objectReferenceValue = registry;
            driverSo.FindProperty("genParamsAsset").objectReferenceValue = genParams;
            driverSo.FindProperty("onMapAssembledServer").objectReferenceValue = assembledChannel;
            driverSo.ApplyModifiedPropertiesWithoutUndo();

            var bakerSo = new SerializedObject(baker);
            bakerSo.FindProperty("driver").objectReferenceValue = driver;
            bakerSo.FindProperty("onMapAssembledServer").objectReferenceValue = assembledChannel;
            bakerSo.FindProperty("onMapNavMeshReadyServer").objectReferenceValue = navReadyChannel;
            bakerSo.ApplyModifiedPropertiesWithoutUndo();

            var spawnerSo = new SerializedObject(spawner);
            spawnerSo.FindProperty("driver").objectReferenceValue = driver;
            spawnerSo.FindProperty("prefabRegistry").objectReferenceValue = registry;
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
        /// <param name="registry">프리팹 레지스트리.</param>
        /// <param name="genParams">생성 파라미터 에셋.</param>
        /// <param name="assembledChannel">조립 완료 채널.</param>
        /// <param name="navReadyChannel">베이크 완료 채널.</param>
        private static void WireManagerComponents(string prefabPath, MapPrefabRegistrySO registry, MapGenParamsSO genParams, VoidEventChannelSO assembledChannel, VoidEventChannelSO navReadyChannel)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            EnsureComponent<NetworkObject>(root);
            MapGenNetworkDriver driver = EnsureComponent<MapGenNetworkDriver>(root);
            MapNavMeshRuntimeBaker baker = EnsureComponent<MapNavMeshRuntimeBaker>(root);
            MapStateObjectSpawner spawner = EnsureComponent<MapStateObjectSpawner>(root);

            var driverSo = new SerializedObject(driver);
            driverSo.FindProperty("prefabRegistry").objectReferenceValue = registry;
            driverSo.FindProperty("genParamsAsset").objectReferenceValue = genParams;
            driverSo.FindProperty("onMapAssembledServer").objectReferenceValue = assembledChannel;
            driverSo.ApplyModifiedPropertiesWithoutUndo();

            var bakerSo = new SerializedObject(baker);
            bakerSo.FindProperty("driver").objectReferenceValue = driver;
            bakerSo.FindProperty("onMapAssembledServer").objectReferenceValue = assembledChannel;
            bakerSo.FindProperty("onMapNavMeshReadyServer").objectReferenceValue = navReadyChannel;
            bakerSo.ApplyModifiedPropertiesWithoutUndo();

            var spawnerSo = new SerializedObject(spawner);
            spawnerSo.FindProperty("driver").objectReferenceValue = driver;
            spawnerSo.FindProperty("prefabRegistry").objectReferenceValue = registry;
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
