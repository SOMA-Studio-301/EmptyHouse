using System.Collections.Generic;
using Border.Core;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace EmptyHouse.EditorTools
{
    /// <summary>
    /// 바닥 전용 NavMesh 베이크 자동화 툴.
    /// ① 종이류 카테고리 프리팹 원본에 NavMeshModifier(Ignore From Build)를 달아 베이크 무영향 처리
    /// ② 씬의 바닥 슬래브(얇고 낮고 넓은 수평면)를 자동 탐지해 NavMeshModifier(Walkable) 태깅
    /// ③ NavMeshSurface의 기본 영역을 Not Walkable로 바꿔 "바닥만 걷기 가능, 나머지는 장애물"을 강제
    /// ④ 리베이크. 재실행 시 기존 태깅을 재사용한다(멱등).
    /// </summary>
    public static class MapNavMeshBaker
    {
        private const string envRootName = "=====ENVIRONMENTS=====";
        private const int walkableArea = 0;     // Built-in Walkable
        private const int notWalkableArea = 1;  // Built-in Not Walkable

        // 바닥 슬래브 판정: 두께/상면 높이/최소 크기
        private const float slabMaxThickness = 0.35f;
        private const float slabMaxTopY = 0.35f;
        private const float slabMinXZ = 0.9f;

        /// <summary>베이크에서 완전히 제외(무영향)할 프랍 카테고리 프리팹 폴더들.</summary>
        private static readonly string[] ignoreFolders =
        {
            "Assets/Horror_Pack_1/!Prefabs/Paper",
            "Assets/Horror_Pack_1/!Prefabs/Folders",
        };

        /// <summary>문짝(leaf) 메시 이름 접두사. 이 이름의 렌더러가 옵스터클 대상.</summary>
        private static readonly string[] doorLeafPrefixes = { "Hall_Door_L", "Hall_Door_R", "Hall_Door" };

        /// <summary>문짝 옵스터클을 달 문 조립 프리팹들.</summary>
        private static readonly string[] doorPrefabs =
        {
            "Assets/02. Prefab/Map/Door-Closed.prefab",
            "Assets/02. Prefab/Map/Door-Opened.prefab",
        };

        /// <summary>
        /// 문짝을 정적 베이크에서 제외하고 NavMeshObstacle(Carve)을 부착한다.
        /// ① 문 조립 프리팹의 문짝에 부착(인스턴스 전체 반영) ② 씬의 비프리팹 문 클론에도 부착
        /// 후 바닥 전용 베이크를 다시 수행한다.
        /// </summary>
        [MenuItem("Tools/Map/NavMesh 문 옵스터클 설치 + 리베이크")]
        public static void InstallDoorObstacles()
        {
            // ① 프리팹 문짝
            int prefabLeafs = 0;
            foreach (string path in doorPrefabs)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                if (root == null) { Log.D($"[Nav] 문 프리팹 없음: {path}"); continue; }
                int n = SetupDoorLeafs(root.transform);
                if (n > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
                prefabLeafs += n;
            }

            // ② 씬의 문짝(비프리팹 클론 포함; 프리팹에서 이미 받은 것은 멱등 스킵)
            GameObject env = GameObject.Find(envRootName);
            int sceneLeafs = env != null ? SetupDoorLeafs(env.transform) : 0;

            AssetDatabase.SaveAssets();
            Log.D($"[Nav] 문 옵스터클: 프리팹 문짝 {prefabLeafs}개 / 씬 문짝 {sceneLeafs}개 처리 — 리베이크 시작");
            Bake();
        }

        /// <summary>
        /// 하위에서 문짝 렌더러를 찾아 NavMeshModifier(베이크 제외)와 NavMeshObstacle(Carve)을 부착한다.
        /// 이미 옵스터클이 있으면 건너뛴다(멱등).
        /// </summary>
        /// <param name="root">탐색 루트.</param>
        /// <returns>새로 처리한 문짝 수.</returns>
        private static int SetupDoorLeafs(Transform root)
        {
            int done = 0;
            foreach (MeshRenderer mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool isLeaf = false;
                foreach (string p in doorLeafPrefixes)
                    if (mr.name.StartsWith(p)) { isLeaf = true; break; }
                if (!isLeaf) continue;
                if (mr.GetComponent<UnityEngine.AI.NavMeshObstacle>() != null) continue;

                NavMeshModifier mod = mr.GetComponent<NavMeshModifier>();
                if (mod == null) mod = mr.gameObject.AddComponent<NavMeshModifier>();
                mod.ignoreFromBuild = true;

                MeshFilter mf = mr.GetComponent<MeshFilter>();
                Bounds lb = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
                UnityEngine.AI.NavMeshObstacle ob = mr.gameObject.AddComponent<UnityEngine.AI.NavMeshObstacle>();
                ob.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
                ob.center = lb.center;
                ob.size = lb.size;
                ob.carving = true;
                ob.carveOnlyStationary = true;
                done++;
            }
            return done;
        }

        /// <summary>
        /// 종이류 무영향 처리 → 바닥 태깅 → 기본 영역 Not Walkable → 리베이크를 일괄 수행한다.
        /// </summary>
        [MenuItem("Tools/Map/NavMesh 바닥 전용 베이크")]
        public static void Bake()
        {
            // ① 무영향 카테고리: 프리팹 원본에 Ignore From Build
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

            // ② 씬 바닥 슬래브 탐지·태깅
            GameObject env = GameObject.Find(envRootName);
            if (env == null) { Log.D($"[Nav] '{envRootName}' 를 찾을 수 없습니다."); return; }

            int tagged = 0, already = 0;
            foreach (MeshRenderer mr in env.GetComponentsInChildren<MeshRenderer>(true))
            {
                Bounds b = mr.bounds;
                if (b.size.y > slabMaxThickness) continue;                    // 두꺼움 → 바닥 아님
                if (b.max.y > slabMaxTopY) continue;                          // 상면이 높음(가구 상판/천장) → 제외
                if (b.size.x < slabMinXZ || b.size.z < slabMinXZ) continue;   // 소품 크기 → 제외

                NavMeshModifier mod = mr.GetComponent<NavMeshModifier>();
                if (mod != null && mod.ignoreFromBuild) continue;             // 무영향 지정물은 건너뜀
                if (mod != null && mod.overrideArea && mod.area == walkableArea) { already++; continue; }
                if (mod == null) mod = mr.gameObject.AddComponent<NavMeshModifier>();
                mod.overrideArea = true;
                mod.area = walkableArea;
                tagged++;
            }

            // ③ 기본 영역 Not Walkable + ④ 리베이크
            NavMeshSurface surf = env.GetComponent<NavMeshSurface>();
            if (surf == null) surf = env.AddComponent<NavMeshSurface>();
            surf.collectObjects = CollectObjects.Children;
            surf.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            surf.defaultArea = notWalkableArea;
            surf.BuildNavMesh();

            var tri = NavMesh.CalculateTriangulation();
            float areaSum = 0f;
            for (int i = 0; i + 2 < tri.indices.Length; i += 3)
            {
                Vector3 a = tri.vertices[tri.indices[i]];
                Vector3 c = tri.vertices[tri.indices[i + 1]];
                Vector3 d = tri.vertices[tri.indices[i + 2]];
                areaSum += Vector3.Cross(c - a, d - a).magnitude * 0.5f;
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(env.scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(env.scene);
            AssetDatabase.SaveAssets();
            Log.D($"[Nav] 완료: 무영향 프리팹 {ignored}개 / 바닥 태깅 {tagged}개(기존 {already}) / 정점 {tri.vertices.Length}, 삼각형 {tri.indices.Length / 3}, 워커블 면적 {areaSum:F0}㎡ / 에이전트 반경 {NavMesh.GetSettingsByID(surf.agentTypeID).agentRadius:F2}");
        }
    }
}
