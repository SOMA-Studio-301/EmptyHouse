using Border.Core;
using Border.Events;
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
        private const string managersPrefabPath = "Assets/02. Prefab/GameScene/=====MANAGERS=====.prefab"; // 상주 매니저 프리팹
        private const string managerChildName = "MapGenManager"; // MANAGERS 하위 자식 이름
        private const string playerSpawnerPrefabPath = "Assets/02. Prefab/Manager/GameScenePlayerSpawner.prefab"; // 플레이어 스포너(맵 조립 게이트 배선 대상)

        /// <summary>플레이어 프리팹 경로 — PlayerInteractor 참조 배선 대상.</summary>
        private static readonly string[] playerPrefabPaths =
        {
            "Assets/02. Prefab/Player/Player.prefab",
            "Assets/02. Prefab/Player/Player_UnityChan.prefab",
        };

        /// <summary>TemplateId → 방 프리팹 경로(MapGen.Editor asmdef 가 비참조라 PrefabRoomTemplates.PrefabPaths 를 복제 — 경로 변경 시 양쪽 갱신).</summary>
        private static readonly (string id, string path)[] roomPrefabPaths =
        {
            ("entrance_6x6", "Assets/02. Prefab/Map/Rooms/Entrance-EmptyRoom-6x6.prefab"),
            ("room_3x3", "Assets/02. Prefab/Map/Rooms/EmptyRoom-3x3.prefab"),
            ("room_6x6", "Assets/02. Prefab/Map/Rooms/EmptyRoom-6x6.prefab"),
            ("room_6x9", "Assets/02. Prefab/Map/Rooms/EmptyRoom-6x9.prefab"),
            ("hallway", "Assets/02. Prefab/Map/Rooms/Hallway.prefab"),
            ("hallway_x2", "Assets/02. Prefab/Map/Rooms/Hallway x2.prefab"),
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
            SetupManagersPrefab(registry, assembledChannel, navReadyChannel);
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

        /// <summary>MANAGERS 프리팹에 MapGenManager 자식을 확보하고 컴포넌트·참조를 배선한다.</summary>
        /// <param name="registry">프리팹 레지스트리.</param>
        /// <param name="assembledChannel">조립 완료 채널.</param>
        /// <param name="navReadyChannel">베이크 완료 채널.</param>
        private static void SetupManagersPrefab(MapPrefabRegistrySO registry, VoidEventChannelSO assembledChannel, VoidEventChannelSO navReadyChannel)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(managersPrefabPath);
            Transform child = root.transform.Find(managerChildName);
            if (child == null)
            {
                var go = new GameObject(managerChildName);
                go.transform.SetParent(root.transform, false);
                child = go.transform;
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
