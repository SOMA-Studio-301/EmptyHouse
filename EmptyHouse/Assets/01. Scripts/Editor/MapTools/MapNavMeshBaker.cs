using System.Collections.Generic;
using Border.Core;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace EmptyHouse.EditorTools
{
    /// <summary>
    /// 바닥 전용 NavMesh 베이크 자동화 툴(v2 — 맵 오브젝트 씬 루트 체제).
    /// - 씬 루트의 "NavMesh" 오브젝트에 NavMeshSurface(전체 수집, 기본 Not Walkable)를 두고
    ///   바닥 슬래브만 Walkable 태깅해 "바닥 위만 보행"을 강제한다.
    /// - 문 조립체(틀·문짝·결합 메시)는 전부 정적 베이크에서 제외하고, 막는 역할은
    ///   NavMeshObstacle(Carve)이 담당한다 → 문이 열리면 실시간으로 길이 열린다.
    /// - 종이류 프랍과 동적 루트(좀비·인터랙터블)는 베이크 무영향 처리한다.
    /// </summary>
    public static class MapNavMeshBaker
    {
        private const string surfaceHostName = "NavMesh"; // 씬 루트의 서피스 호스트 오브젝트
        private const int walkableArea = 0;
        private const int notWalkableArea = 1;

        // 바닥 슬래브 판정: 두께/상면 높이/최소 크기
        private const float slabMaxThickness = 0.35f;
        private const float slabMaxTopY = 0.35f;
        private const float slabMinXZ = 0.9f;

        /// <summary>베이크에서 완전히 제외할 프랍 카테고리 프리팹 폴더.</summary>
        private static readonly string[] ignoreFolders =
        {
            "Assets/Horror_Pack_1/!Prefabs/Paper",
            "Assets/Horror_Pack_1/!Prefabs/Folders",
        };

        /// <summary>베이크 무영향 처리할 동적/시스템 씬 루트 이름.</summary>
        private static readonly string[] ignoreSceneRoots = { "Zombie", "=====INTERACTABLE=====" };

        /// <summary>
        /// 베이크 제외 + 옵스터클(Carve)을 붙일 문짝(leaf) 렌더러 접두사.
        /// 주의: "Hall_Door"처럼 짧은 접두사는 문틀("Hall_Doorway*")과 충돌하므로 언더스코어까지 포함해 정밀 매칭.
        /// </summary>
        private static readonly string[] doorCarvePrefixes = { "Hall_Door_L", "Hall_Door_R" };

        /// <summary>
        /// Walkable 태깅할 문 조립체(문틀·결합 메시) 접두사. 문턱(sill)이 이 메시에 포함되어
        /// 베이크 제외 시 문지방에 구멍이 나므로, 제외 대신 Walkable로 태깅한다
        /// (세로 패널·기둥은 경사 필터가 자동으로 걷기 불가 처리).
        /// </summary>
        private static readonly string[] doorWalkablePrefixes = { "Hall_Doorway", "Door-Opened", "Door-Closed" };

        /// <summary>문짝 옵스터클을 선반영할 문 조립 프리팹.</summary>
        private static readonly string[] doorPrefabs =
        {
            "Assets/02. Prefab/Map/Door-Closed.prefab",
            "Assets/02. Prefab/Map/Door-Opened.prefab",
        };

        /// <summary>
        /// 문 조립체 무시/옵스터클 처리(프리팹+씬) 후 바닥 전용 베이크를 수행한다.
        /// </summary>
        [MenuItem("Tools/Map/NavMesh 문 옵스터클 설치 + 리베이크")]
        public static void InstallDoorObstacles()
        {
            int prefabDone = 0;
            foreach (string path in doorPrefabs)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                if (root == null) { Log.D($"[Nav] 문 프리팹 없음: {path}"); continue; }
                int n = SetupDoors(root.transform);
                if (n > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
                prefabDone += n;
            }

            int sceneDone = 0;
            foreach (GameObject r in UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().GetRootGameObjects())
                sceneDone += SetupDoors(r.transform);

            AssetDatabase.SaveAssets();
            Log.D($"[Nav] 문 처리: 프리팹 {prefabDone} / 씬 {sceneDone} — 리베이크 시작");
            Bake();
        }

        /// <summary>
        /// 하위의 문 관련 렌더러를 처리한다: carve 대상은 무시+옵스터클, 틀/열린 조립체는 무시만.
        /// 이미 처리된 것은 건너뛴다(멱등).
        /// </summary>
        /// <param name="root">탐색 루트.</param>
        /// <returns>새로 처리한 렌더러 수.</returns>
        private static int SetupDoors(Transform root)
        {
            int done = 0;
            foreach (MeshRenderer mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool carve = MatchesAny(mr.name, doorCarvePrefixes);
                bool walk = !carve && MatchesAny(mr.name, doorWalkablePrefixes);
                if (!carve && !walk) continue;

                NavMeshModifier mod = mr.GetComponent<NavMeshModifier>();
                if (mod == null) mod = mr.gameObject.AddComponent<NavMeshModifier>();
                bool changed = false;
                if (carve && !mod.ignoreFromBuild) { mod.ignoreFromBuild = true; changed = true; }
                if (walk)
                {
                    if (mod.ignoreFromBuild || !mod.overrideArea || mod.area != walkableArea)
                    {
                        mod.ignoreFromBuild = false; // 이전 실행이 제외해 둔 것 원복(문턱 구멍 방지)
                        mod.overrideArea = true;
                        mod.area = walkableArea;
                        changed = true;
                    }
                    // 접두사 충돌로 문틀에 잘못 달렸던 거대 카빙 옵스터클 제거
                    NavMeshObstacle wrongOb = mr.GetComponent<NavMeshObstacle>();
                    if (wrongOb != null) { Object.DestroyImmediate(wrongOb, true); changed = true; }
                }

                if (carve && mr.GetComponent<NavMeshObstacle>() == null)
                {
                    MeshFilter mf = mr.GetComponent<MeshFilter>();
                    Bounds lb = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
                    NavMeshObstacle ob = mr.gameObject.AddComponent<NavMeshObstacle>();
                    ob.shape = NavMeshObstacleShape.Box;
                    ob.center = lb.center;
                    ob.size = lb.size;
                    ob.carving = true;
                    ob.carveOnlyStationary = true;
                    changed = true;
                }
                if (changed) done++;
            }
            return done;
        }

        /// <summary>이름이 접두사 목록 중 하나로 시작하는지 판정한다.</summary>
        /// <param name="name">이름.</param>
        /// <param name="prefixes">접두사 목록.</param>
        /// <returns>일치 여부.</returns>
        private static bool MatchesAny(string name, string[] prefixes)
        {
            foreach (string p in prefixes)
                if (name.StartsWith(p)) return true;
            return false;
        }

        /// <summary>
        /// 종이류·동적 루트 무영향 처리 → 바닥 태깅 → 씬 루트 "NavMesh" 서피스(전체 수집,
        /// 기본 Not Walkable)로 베이크한다.
        /// </summary>
        [MenuItem("Tools/Map/NavMesh 바닥 전용 베이크")]
        public static void Bake()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();

            // ① 무영향 카테고리 프리팹
            int ignored = 0;
            foreach (string folder in ignoreFolders)
            {
                if (!AssetDatabase.IsValidFolder(folder)) { Log.D($"[Nav] 폴더 없음: {folder}"); continue; }
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    GameObject root = PrefabUtility.LoadPrefabContents(path);
                    if (root == null) continue;
                    NavMeshModifier mod = root.GetComponent<NavMeshModifier>();
                    if (mod == null || !mod.ignoreFromBuild)
                    {
                        if (mod == null) mod = root.AddComponent<NavMeshModifier>();
                        mod.ignoreFromBuild = true;
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        ignored++;
                    }
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            // ② 동적/시스템 루트 무영향
            foreach (GameObject r in scene.GetRootGameObjects())
            {
                if (System.Array.IndexOf(ignoreSceneRoots, r.name) < 0) continue;
                NavMeshModifier mod = r.GetComponent<NavMeshModifier>();
                if (mod == null) mod = r.AddComponent<NavMeshModifier>();
                mod.ignoreFromBuild = true;
            }

            // ③ 씬 전체 바닥 슬래브 태깅
            int tagged = 0, already = 0;
            foreach (GameObject r in scene.GetRootGameObjects())
            {
                foreach (MeshRenderer mr in r.GetComponentsInChildren<MeshRenderer>(true))
                {
                    Bounds b = mr.bounds;
                    if (b.size.y > slabMaxThickness) continue;
                    if (b.max.y > slabMaxTopY) continue;
                    if (b.size.x < slabMinXZ || b.size.z < slabMinXZ) continue;

                    NavMeshModifier mod = mr.GetComponent<NavMeshModifier>();
                    if (mod != null && mod.ignoreFromBuild) continue;
                    if (mod != null && mod.overrideArea && mod.area == walkableArea) { already++; continue; }
                    if (mod == null) mod = mr.gameObject.AddComponent<NavMeshModifier>();
                    mod.overrideArea = true;
                    mod.area = walkableArea;
                    tagged++;
                }
            }

            // ④ 씬 루트 서피스로 베이크
            GameObject host = GameObject.Find(surfaceHostName);
            if (host == null) host = new GameObject(surfaceHostName);
            NavMeshSurface surf = host.GetComponent<NavMeshSurface>();
            if (surf == null) surf = host.AddComponent<NavMeshSurface>();
            surf.collectObjects = CollectObjects.All;
            surf.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            surf.defaultArea = notWalkableArea;
            surf.BuildNavMesh();

            // 데이터 에셋 영속화(없으면 생성)
            if (surf.navMeshData != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(surf.navMeshData)))
            {
                if (!AssetDatabase.IsValidFolder("Assets/00. Scenes/Game"))
                    AssetDatabase.CreateFolder("Assets/00. Scenes", "Game");
                AssetDatabase.CreateAsset(surf.navMeshData, "Assets/00. Scenes/Game/NavMesh-Environments.asset");
            }

            var tri = NavMesh.CalculateTriangulation();
            float areaSum = 0f;
            for (int i = 0; i + 2 < tri.indices.Length; i += 3)
            {
                Vector3 a = tri.vertices[tri.indices[i]];
                Vector3 c = tri.vertices[tri.indices[i + 1]];
                Vector3 d = tri.vertices[tri.indices[i + 2]];
                areaSum += Vector3.Cross(c - a, d - a).magnitude * 0.5f;
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Log.D($"[Nav] 완료: 무영향 프리팹 {ignored} / 바닥 태깅 {tagged}(기존 {already}) / 정점 {tri.vertices.Length}, 삼각형 {tri.indices.Length / 3}, 워커블 {areaSum:F0}㎡ / 반경 {NavMesh.GetSettingsByID(surf.agentTypeID).agentRadius:F2}");
        }
    }
}
