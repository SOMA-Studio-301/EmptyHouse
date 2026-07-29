using System.Collections.Generic;
using System.Linq;
using Border.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EmptyHouse.EditorTools
{
    /// <summary>
    /// Map 씬의 빈방을 Horror Pack 프랍으로 절차적 자동 배치하는 에디터 툴.
    /// 방은 이름(EmptyRoom/Hallway/SmallRoom/BookRoom)으로 탐지하고,
    /// 생성물은 전부 GeneratedProps 루트 아래에 두어 통째 삭제로 원복한다.
    /// </summary>
    public static class MapPropPlacer
    {
        private const string mapScenePath = "Assets/00. Scenes/Map.unity";
        private const string mapSceneName = "Map";
        private const string prefabRoot = "Assets/Horror_Pack_1/!Prefabs";
        private const string generatedRootName = "GeneratedProps";
        private const int randomSeed = 20260728;

        /// <summary>방 컨셉 분류. 컨셉에 따라 배치할 프랍셋이 달라진다.</summary>
        private enum RoomConcept { Hallway, BookRoom, SmallRoom, GenericRoom }

        /// <summary>탐지된 방 하나의 정보(오브젝트/컨셉/바운즈).</summary>
        private struct RoomInfo
        {
            public GameObject go;
            public RoomConcept concept;
            public Bounds bounds;
            public bool hasBounds;
        }

        /// <summary>바닥에 흩뿌릴 프랍 카테고리와 개수 스펙.</summary>
        private struct ScatterSpec
        {
            public string category;
            public int count;
            public float minScale;
            public float maxScale;

            public ScatterSpec(string category, int count, float minScale = 1f, float maxScale = 1f)
            {
                this.category = category;
                this.count = count;
                this.minScale = minScale;
                this.maxScale = maxScale;
            }
        }

        // ── 메뉴 진입점 ─────────────────────────────────────────────

        /// <summary>
        /// Map 씬의 방을 탐지만 하고 배치는 하지 않는다(검증용). 방 이름/컨셉/바운즈를 로그로 출력한다.
        /// </summary>
        [MenuItem("Tools/Map/1. 방 스캔 (배치 안함)")]
        public static void ScanRooms()
        {
            if (!EnsureMapScene(out Scene scene)) return;

            List<RoomInfo> rooms = FindRooms(scene);
            Log.D($"[MapPropPlacer] 방 {rooms.Count}개 탐지");

            Dictionary<RoomConcept, int> byConcept = new Dictionary<RoomConcept, int>();
            foreach (RoomInfo r in rooms)
            {
                byConcept.TryGetValue(r.concept, out int c);
                byConcept[r.concept] = c + 1;
            }
            foreach (KeyValuePair<RoomConcept, int> kv in byConcept)
                Log.D($"[MapPropPlacer]   {kv.Key}: {kv.Value}개");

            int shown = 0;
            foreach (RoomInfo r in rooms)
            {
                if (shown++ >= 20) { Log.D("[MapPropPlacer]   ... (이하 생략)"); break; }
                string b = r.hasBounds ? $"size={r.bounds.size:F1} center={r.bounds.center:F1}" : "렌더러 없음";
                Log.D($"[MapPropPlacer]   '{r.go.name}' [{r.concept}] {b}");
            }
        }

        /// <summary>
        /// Map 씬의 방을 탐지해 컨셉별 프랍셋을 절차적으로 배치한다.
        /// 재실행 시 기존 GeneratedProps를 먼저 제거하므로 멱등하다. 완료 후 씬을 저장한다.
        /// </summary>
        [MenuItem("Tools/Map/2. 프랍 자동 배치")]
        public static void PlaceProps()
        {
            if (!EnsureMapScene(out Scene scene)) return;

            ClearGeneratedInternal(scene);

            List<RoomInfo> rooms = FindRooms(scene);
            if (rooms.Count == 0) { Log.D("[MapPropPlacer] 배치할 방이 없습니다."); return; }

            Transform generatedRoot = new GameObject(generatedRootName).transform;
            SceneManager.MoveGameObjectToScene(generatedRoot.gameObject, scene);

            System.Random rng = new System.Random(randomSeed);
            int total = 0;
            foreach (RoomInfo room in rooms)
            {
                if (!room.hasBounds) continue;
                total += PlaceForRoom(room, generatedRoot, rng);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Log.D($"[MapPropPlacer] 배치 완료: 방 {rooms.Count}개에 프랍 {total}개 생성 후 저장");
        }

        /// <summary>
        /// GeneratedProps 루트를 통째로 삭제해 배치를 원복한다. 완료 후 씬을 저장한다.
        /// </summary>
        [MenuItem("Tools/Map/3. GeneratedProps 삭제 (원복)")]
        public static void ClearGenerated()
        {
            if (!EnsureMapScene(out Scene scene)) return;
            int n = ClearGeneratedInternal(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Log.D($"[MapPropPlacer] GeneratedProps 삭제 완료(루트 {n}개) 후 저장");
        }

        // ── 방 탐지 ─────────────────────────────────────────────────

        /// <summary>
        /// 활성 씬을 Map으로 보장한다. 활성 씬이 Map이 아니면 Map.scene을 Single로 연다.
        /// </summary>
        /// <param name="scene">보장된 Map 씬.</param>
        /// <returns>Map 씬 확보 성공 여부.</returns>
        private static bool EnsureMapScene(out Scene scene)
        {
            scene = EditorSceneManager.GetActiveScene();
            if (scene.name == mapSceneName) return true;
            scene = EditorSceneManager.OpenScene(mapScenePath, OpenSceneMode.Single);
            return scene.IsValid();
        }

        /// <summary>
        /// 씬 루트부터 재귀 순회하며 이름으로 방을 탐지한다. 방으로 판정되면 그 하위는 더 파고들지 않는다.
        /// </summary>
        /// <param name="scene">대상 씬.</param>
        /// <returns>탐지된 방 목록.</returns>
        private static List<RoomInfo> FindRooms(Scene scene)
        {
            List<RoomInfo> result = new List<RoomInfo>();
            foreach (GameObject root in scene.GetRootGameObjects())
                CollectRooms(root.transform, result);
            return result;
        }

        /// <summary>
        /// 트랜스폼을 재귀 순회하며 방 이름 규칙에 맞는 노드를 수집한다.
        /// </summary>
        /// <param name="t">현재 노드.</param>
        /// <param name="acc">수집 결과 누적 리스트.</param>
        private static void CollectRooms(Transform t, List<RoomInfo> acc)
        {
            if (TryClassify(t.name, out RoomConcept concept))
            {
                bool has = TryGetFloorBounds(t.gameObject, out Bounds bounds);
                acc.Add(new RoomInfo { go = t.gameObject, concept = concept, bounds = bounds, hasBounds = has });
                return;
            }
            foreach (Transform child in t)
                CollectRooms(child, acc);
        }

        /// <summary>
        /// 오브젝트 이름으로 방 컨셉을 분류한다.
        /// </summary>
        /// <param name="name">오브젝트 이름.</param>
        /// <param name="concept">분류된 컨셉.</param>
        /// <returns>방으로 분류되었는지 여부.</returns>
        private static bool TryClassify(string name, out RoomConcept concept)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("hallway")) { concept = RoomConcept.Hallway; return true; }
            if (n.Contains("bookroom")) { concept = RoomConcept.BookRoom; return true; }
            if (n.Contains("smallroom")) { concept = RoomConcept.SmallRoom; return true; }
            if (n.Contains("emptyroom")) { concept = RoomConcept.GenericRoom; return true; }
            concept = default;
            return false;
        }

        /// <summary>
        /// 자식 렌더러들을 합쳐 방의 월드 바운즈를 구한다.
        /// </summary>
        /// <param name="room">방 오브젝트.</param>
        /// <param name="bounds">합산된 바운즈.</param>
        /// <returns>렌더러가 하나라도 있어 바운즈를 구했는지 여부.</returns>
        private static bool TryGetFloorBounds(GameObject room, out Bounds bounds)
        {
            Renderer[] rends = room.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) { bounds = default; return false; }
            bounds = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) bounds.Encapsulate(rends[i].bounds);
            return true;
        }

        // ── 배치 ───────────────────────────────────────────────────

        /// <summary>
        /// 방 컨셉에 맞는 프랍셋을 바닥에 흩뿌리고, 조명을 천장에 배치한다.
        /// </summary>
        /// <param name="room">대상 방.</param>
        /// <param name="generatedRoot">GeneratedProps 루트.</param>
        /// <param name="rng">시드 고정 난수기.</param>
        /// <returns>생성한 프랍 개수.</returns>
        private static int PlaceForRoom(RoomInfo room, Transform generatedRoot, System.Random rng)
        {
            Transform roomGroup = new GameObject(room.go.name).transform;
            roomGroup.SetParent(generatedRoot, false);

            ScatterSpec[] specs = GetSpecs(room.concept);
            List<Vector2> placed = new List<Vector2>();
            int made = 0;

            foreach (ScatterSpec spec in specs)
            {
                GameObject[] pool = LoadCategory(spec.category);
                if (pool.Length == 0) continue;
                for (int i = 0; i < spec.count; i++)
                {
                    if (!TryFindSpot(room.bounds, placed, rng, out Vector3 pos)) continue;
                    GameObject prefab = pool[rng.Next(pool.Length)];
                    if (Spawn(prefab, pos, RandomYaw(rng), RandomScale(spec, rng), roomGroup)) made++;
                }
            }

            made += PlaceLights(room, roomGroup, rng);
            return made;
        }

        /// <summary>
        /// 방 컨셉별 바닥 흩뿌리기 프랍셋 스펙을 반환한다. 방 바닥 넓이에 비례해 개수를 보정한다.
        /// </summary>
        /// <param name="concept">방 컨셉.</param>
        /// <returns>스캐터 스펙 배열.</returns>
        private static ScatterSpec[] GetSpecs(RoomConcept concept)
        {
            switch (concept)
            {
                case RoomConcept.Hallway:
                    return new[]
                    {
                        new ScatterSpec("Paper", 6),
                        new ScatterSpec("Basement_Props", 2),
                        new ScatterSpec("Cardboard_Boxes", 1),
                    };
                case RoomConcept.BookRoom:
                    return new[]
                    {
                        new ScatterSpec("Wood", 3),
                        new ScatterSpec("Books", 8),
                        new ScatterSpec("Folders", 4),
                        new ScatterSpec("Office_Furniture", 2),
                        new ScatterSpec("Paper", 5),
                    };
                case RoomConcept.SmallRoom:
                    return new[]
                    {
                        new ScatterSpec("Toilet_Props", 4),
                        new ScatterSpec("Glass", 3),
                        new ScatterSpec("Paper", 3),
                    };
                default: // GenericRoom
                    return new[]
                    {
                        new ScatterSpec("Office_Furniture", 3),
                        new ScatterSpec("Office_Props", 5),
                        new ScatterSpec("Basement_Props", 3),
                        new ScatterSpec("Paper", 5),
                    };
            }
        }

        private static readonly Dictionary<string, GameObject[]> categoryCache = new Dictionary<string, GameObject[]>();

        /// <summary>
        /// 카테고리 폴더의 프리팹을 로드해 캐시한다.
        /// </summary>
        /// <param name="category">Horror Pack 카테고리 폴더명.</param>
        /// <returns>해당 카테고리 프리팹 배열(없으면 빈 배열).</returns>
        private static GameObject[] LoadCategory(string category)
        {
            if (categoryCache.TryGetValue(category, out GameObject[] cached)) return cached;
            string folder = $"{prefabRoot}/{category}";
            GameObject[] items = AssetDatabase.FindAssets("t:Prefab", new[] { folder })
                .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(x => x != null)
                .ToArray();
            categoryCache[category] = items;
            return items;
        }

        /// <summary>
        /// 방 바운즈 안쪽(벽에서 안으로 들인 영역)에서 다른 프랍과 최소 간격을 지키는 배치 지점을 찾는다.
        /// </summary>
        /// <param name="bounds">방 바운즈.</param>
        /// <param name="placed">이미 배치된 XZ 좌표들.</param>
        /// <param name="rng">난수기.</param>
        /// <param name="pos">찾은 배치 지점(바닥 높이).</param>
        /// <returns>지점을 찾았는지 여부.</returns>
        private static bool TryFindSpot(Bounds bounds, List<Vector2> placed, System.Random rng, out Vector3 pos)
        {
            const float inset = 0.6f;
            const float minSpacing = 0.9f;
            float minX = bounds.min.x + inset, maxX = bounds.max.x - inset;
            float minZ = bounds.min.z + inset, maxZ = bounds.max.z - inset;
            float floorY = bounds.min.y;

            if (maxX <= minX) { minX = maxX = bounds.center.x; }
            if (maxZ <= minZ) { minZ = maxZ = bounds.center.z; }

            for (int attempt = 0; attempt < 12; attempt++)
            {
                float x = Lerp(minX, maxX, rng);
                float z = Lerp(minZ, maxZ, rng);
                Vector2 p = new Vector2(x, z);
                bool tooClose = placed.Any(q => (q - p).sqrMagnitude < minSpacing * minSpacing);
                if (tooClose) continue;
                placed.Add(p);
                pos = new Vector3(x, floorY, z);
                return true;
            }
            pos = default;
            return false;
        }

        /// <summary>
        /// 방 천장 근처에 조명 프랍을 최대 2개 배치한다.
        /// </summary>
        /// <param name="room">대상 방.</param>
        /// <param name="roomGroup">방 그룹 트랜스폼.</param>
        /// <param name="rng">난수기.</param>
        /// <returns>배치한 조명 개수.</returns>
        private static int PlaceLights(RoomInfo room, Transform roomGroup, System.Random rng)
        {
            GameObject[] lights = LoadCategory("Lights");
            if (lights.Length == 0) return 0;
            float ceilingY = room.bounds.max.y - 0.1f;
            Vector3 c = room.bounds.center;
            Vector3[] spots =
            {
                new Vector3(c.x, ceilingY, c.z),
                new Vector3(Lerp(room.bounds.min.x + 0.5f, room.bounds.max.x - 0.5f, rng), ceilingY, c.z),
            };
            int made = 0;
            foreach (Vector3 s in spots)
            {
                GameObject prefab = lights[rng.Next(lights.Length)];
                if (Spawn(prefab, s, Quaternion.identity, Vector3.one, roomGroup)) made++;
            }
            return made;
        }

        /// <summary>
        /// 프리팹을 프리팹 연결을 유지한 채 인스턴스화해 지정 위치/회전/스케일로 배치한다.
        /// </summary>
        /// <param name="prefab">원본 프리팹.</param>
        /// <param name="pos">월드 위치.</param>
        /// <param name="rot">월드 회전.</param>
        /// <param name="scale">로컬 스케일.</param>
        /// <param name="parent">부모 트랜스폼.</param>
        /// <returns>생성 성공 여부.</returns>
        private static bool Spawn(GameObject prefab, Vector3 pos, Quaternion rot, Vector3 scale, Transform parent)
        {
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            if (inst == null) return false;
            inst.transform.SetPositionAndRotation(pos, rot);
            inst.transform.localScale = Vector3.Scale(inst.transform.localScale, scale);
            return true;
        }

        /// <summary>
        /// GeneratedProps 루트를 찾아 모두 제거한다.
        /// </summary>
        /// <param name="scene">대상 씬.</param>
        /// <returns>제거한 루트 개수.</returns>
        private static int ClearGeneratedInternal(Scene scene)
        {
            int n = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != generatedRootName) continue;
                Object.DestroyImmediate(root);
                n++;
            }
            return n;
        }

        // ── 난수 헬퍼 ───────────────────────────────────────────────

        /// <summary>[min,max] 구간을 난수로 보간한다.</summary>
        /// <param name="min">하한.</param>
        /// <param name="max">상한.</param>
        /// <param name="rng">난수기.</param>
        /// <returns>보간값.</returns>
        private static float Lerp(float min, float max, System.Random rng) => min + (float)rng.NextDouble() * (max - min);

        /// <summary>Y축 랜덤 회전을 만든다.</summary>
        /// <param name="rng">난수기.</param>
        /// <returns>Y축 회전 쿼터니언.</returns>
        private static Quaternion RandomYaw(System.Random rng) => Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

        /// <summary>스펙 범위 내 균등 스케일을 만든다.</summary>
        /// <param name="spec">스캐터 스펙.</param>
        /// <param name="rng">난수기.</param>
        /// <returns>균등 스케일 벡터.</returns>
        private static Vector3 RandomScale(ScatterSpec spec, System.Random rng)
        {
            float s = Lerp(spec.minScale, spec.maxScale, rng);
            return new Vector3(s, s, s);
        }
    }
}
