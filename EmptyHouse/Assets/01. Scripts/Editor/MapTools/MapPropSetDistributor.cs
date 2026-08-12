using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Border.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EmptyHouse.EditorTools
{
    /// <summary>
    /// 사용자가 PropSets.unity에서 직접 꾸민 방(PropSet)을 Map의 같은 프리팹 인스턴스들에
    /// 회전 포함 정확히 이식·분배하는 툴.
    /// 규약: PropSets 씬의 "&lt;방프리팹명&gt;__&lt;변형명&gt;" 루트의 자식 "PropSet" 아래 프랍을 배치한다.
    /// 변형이 여러 개면 시드 고정 랜덤으로 섞어 분배하고, 각 인스턴스의 개구부(문)를 막는
    /// 프랍만 자동 제거한다. 생성물은 GeneratedProps 아래에 두어 통째 삭제로 원복한다.
    /// </summary>
    public static class MapPropSetDistributor
    {
        private const string mapSceneName = "Game";
        private const string mapScenePath = "Assets/00. Scenes/Game.unity";
        private const string propSetsScenePath = "Assets/00. Scenes/PropSets.unity";
        private const string propSetChildName = "PropSet";
        private const string generatedRootName = "GeneratedProps";
        private const int shuffleSeed = 20260729;      // 변형 분배 셔플 시드(재실행 시 동일 결과)
        private const float openingClearance = 2.2f;   // 개구부 중심 반경 내 낮은 프랍 제거
        private const float lowPropMaxY = 2.0f;        // 통로 컷 대상 프랍 시작 높이 상한

        /// <summary>변형 하나: 소스 샘플 방 트랜스폼 값과 프랍셋 복제 원본.</summary>
        private class Variant
        {
            public string name;            // 변형명(로그용)
            public GameObject setClone;    // 맵 씬에 옮겨 둔 PropSet 사본(비활성, 분배 후 파기)
            public Matrix4x4 sampleW2L;    // 샘플 방 world→local
        }

        // ── 진입점 ─────────────────────────────────────────────────

        /// <summary>
        /// PropSets 씬의 사용자 제작 프랍셋을 Map의 같은 프리팹 방 인스턴스들에 분배 배치하고 저장한다.
        /// 기존 GeneratedProps는 먼저 제거한다(멱등).
        /// </summary>
        public static void Distribute()
        {
            Scene map = EnsureMapScene();
            if (!map.IsValid()) { Log.D("[PropSet] Map 씬을 열 수 없습니다."); return; }

            ClearGenerated(map);

            // 1) PropSets 씬에서 변형 수집(방종류 → 변형 목록)
            Dictionary<string, List<Variant>> byType = CollectVariants(map);
            if (byType.Count == 0)
            {
                Log.D("[PropSet] 사용 가능한 프랍셋이 없습니다. PropSets.unity의 '<방이름>__<변형>' 안 PropSet에 프랍을 배치하세요.");
                return;
            }

            // 2) 방종류별 분배
            System.Random rng = new System.Random(shuffleSeed);
            foreach (KeyValuePair<string, List<Variant>> kv in byType)
                DistributeType(map, kv.Key, kv.Value, rng);

            // 3) 템플릿 정리 + 저장
            foreach (Variant v in byType.Values.SelectMany(x => x))
                Object.DestroyImmediate(v.setClone);
            EditorSceneManager.MarkSceneDirty(map);
            EditorSceneManager.SaveScene(map);
            Log.D("[PropSet] 분배 완료 후 저장.");
        }

        /// <summary>
        /// GeneratedProps를 통째로 삭제해 원복하고 저장한다.
        /// </summary>
        public static void Revert()
        {
            Scene map = EnsureMapScene();
            if (!map.IsValid()) return;
            ClearGenerated(map);
            EditorSceneManager.MarkSceneDirty(map);
            EditorSceneManager.SaveScene(map);
            Log.D("[PropSet] 원복 완료 후 저장.");
        }

        // ── 수집 ───────────────────────────────────────────────────

        /// <summary>
        /// PropSets 씬을 추가로 열어 "&lt;방이름&gt;__&lt;변형&gt;" 루트의 PropSet을 맵 씬으로 복사해 수집한다.
        /// 프랍이 하나도 없는 PropSet은 건너뛴다.
        /// </summary>
        /// <param name="map">맵 씬.</param>
        /// <returns>방종류 → 변형 목록.</returns>
        private static Dictionary<string, List<Variant>> CollectVariants(Scene map)
        {
            Dictionary<string, List<Variant>> result = new Dictionary<string, List<Variant>>();
            Regex nameRe = new Regex("^(.+)__([A-Za-z0-9가-힣_-]+)$");

            Scene sets = EditorSceneManager.OpenScene(propSetsScenePath, OpenSceneMode.Additive);
            foreach (GameObject root in sets.GetRootGameObjects())
            {
                Match m = nameRe.Match(root.name);
                if (!m.Success) continue;
                Transform set = root.transform.Find(propSetChildName);
                if (set == null) { Log.D($"[PropSet] '{root.name}'에 {propSetChildName} 자식이 없음 — 건너뜀."); continue; }
                if (set.childCount == 0) { Log.D($"[PropSet] '{root.name}' 프랍셋이 비어 있음 — 건너뜀."); continue; }

                GameObject clone = Object.Instantiate(set.gameObject);
                clone.name = root.name;
                SceneManager.MoveGameObjectToScene(clone, map);
                clone.transform.SetPositionAndRotation(set.position, set.rotation);
                clone.SetActive(false);

                string type = m.Groups[1].Value;
                if (!result.TryGetValue(type, out List<Variant> list)) result[type] = list = new List<Variant>();
                list.Add(new Variant
                {
                    name = m.Groups[2].Value,
                    setClone = clone,
                    sampleW2L = root.transform.worldToLocalMatrix,
                });
            }
            EditorSceneManager.CloseScene(sets, true);
            return result;
        }

        // ── 분배 ───────────────────────────────────────────────────

        /// <summary>
        /// 한 방 종류의 모든 Map 인스턴스에 변형을 섞어 이식한다.
        /// 같은 프리팹이므로 샘플→인스턴스 행렬로 정확히 정합되고, 인스턴스별 개구부만 컷한다.
        /// </summary>
        /// <param name="map">맵 씬.</param>
        /// <param name="roomType">방 프리팹명(= 인스턴스 기본 이름).</param>
        /// <param name="variants">변형 목록.</param>
        /// <param name="rng">분배 셔플 난수기.</param>
        private static void DistributeType(Scene map, string roomType, List<Variant> variants, System.Random rng)
        {
            List<Transform> instances = FindRoomInstances(map, roomType);
            if (instances.Count == 0) { Log.D($"[PropSet] '{roomType}' 인스턴스가 Map에 없음 — 건너뜀."); return; }

            // 변형을 균등 반복 + 셔플해 "적당히 섞인" 배정 시퀀스 생성
            List<int> order = new List<int>(instances.Count);
            for (int i = 0; i < instances.Count; i++) order.Add(i % variants.Count);
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            Transform typeGroup = GetOrCreateGroup(map, roomType);
            int done = 0, passCut = 0;
            int[] usage = new int[variants.Count];

            for (int i = 0; i < instances.Count; i++)
            {
                Transform room = instances[i];
                Variant v = variants[order[i]];
                usage[order[i]]++;

                // 샘플 방 로컬 → 이 인스턴스 월드 변환(같은 프리팹이라 정확)
                Matrix4x4 t = room.localToWorldMatrix * v.sampleW2L;
                GameObject clone = Object.Instantiate(v.setClone);
                clone.name = $"{room.name} [{v.name}]";
                clone.SetActive(true);
                clone.transform.SetParent(typeGroup, false);
                clone.transform.SetPositionAndRotation(
                    t.MultiplyPoint3x4(v.setClone.transform.position),
                    t.rotation * v.setClone.transform.rotation);

                // 이 인스턴스의 개구부를 막는 낮은 프랍만 제거
                if (TryGetBounds(room.gameObject, out Bounds rb))
                    passCut += CullOpenings(clone.transform, room.gameObject, rb);
                done++;
            }

            string usageStr = string.Join(", ", variants.Select((v, k) => $"{v.name}×{usage[k]}"));
            Log.D($"[PropSet] '{roomType}' {done}개 방 배치 ({usageStr}) / 통로컷 {passCut}");
        }

        /// <summary>
        /// 프랍셋 사본에서 이 방의 개구부 반경 안 낮은 프랍(직속 자식 단위)을 제거한다.
        /// </summary>
        /// <param name="setRoot">프랍셋 사본 루트.</param>
        /// <param name="room">방 루트.</param>
        /// <param name="rb">방 바운즈.</param>
        /// <returns>제거한 프랍 수.</returns>
        private static int CullOpenings(Transform setRoot, GameObject room, Bounds rb)
        {
            List<Vector2> openings = FindOpenings(room, rb);
            if (openings.Count == 0) return 0;

            List<Transform> children = new List<Transform>();
            foreach (Transform c in setRoot) children.Add(c);

            int cut = 0;
            foreach (Transform u in children)
            {
                bool rend = TryGetBounds(u.gameObject, out Bounds b);
                Vector3 lo = rend ? b.min : u.position;
                Vector3 hi = rend ? b.max : u.position;
                if (lo.y >= rb.min.y + lowPropMaxY) continue; // 높은 프랍(천장 조명 등)은 통로와 무관

                foreach (Vector2 o in openings)
                {
                    float dx = Mathf.Max(lo.x - o.x, 0f, o.x - hi.x);
                    float dz = Mathf.Max(lo.z - o.y, 0f, o.y - hi.z);
                    if (dx * dx + dz * dz < openingClearance * openingClearance)
                    {
                        Object.DestroyImmediate(u.gameObject);
                        cut++;
                        break;
                    }
                }
            }
            return cut;
        }

        // ── 개구부 감지(검증된 로직) ───────────────────────────────

        /// <summary>
        /// 방의 개구부(통로) 중심 XZ 목록을 감지한다.
        /// ① 이름 기반(door/entrance/archway) ② 둘레 중간높이 벽 틈 샘플링.
        /// </summary>
        /// <param name="room">방 루트.</param>
        /// <param name="rb">방 바운즈.</param>
        /// <returns>개구부 중심 XZ 목록.</returns>
        private static List<Vector2> FindOpenings(GameObject room, Bounds rb)
        {
            List<Vector2> zones = new List<Vector2>();

            foreach (Transform t in room.GetComponentsInChildren<Transform>())
            {
                string n = t.name.ToLowerInvariant();
                if (!(n.Contains("door") || n.Contains("entrance") || n.Contains("archway"))) continue;
                Vector3 p = t.position;
                Renderer[] rr = t.GetComponentsInChildren<Renderer>();
                if (rr.Length > 0)
                {
                    Bounds b = rr[0].bounds;
                    for (int i = 1; i < rr.Length; i++) b.Encapsulate(rr[i].bounds);
                    p = b.center;
                }
                AddZone(zones, new Vector2(p.x, p.z));
            }

            Renderer[] rends = room.GetComponentsInChildren<Renderer>();
            float midY = rb.min.y + rb.size.y * 0.5f;
            const float inset = 0.7f, step = 0.6f, wallR = 0.9f;

            bool HasWall(Vector3 p)
            {
                foreach (Renderer r in rends)
                {
                    Bounds b = r.bounds;
                    if (b.max.y < midY - 0.5f || b.min.y > midY + 0.5f) continue;
                    if (p.x >= b.min.x - wallR && p.x <= b.max.x + wallR &&
                        p.z >= b.min.z - wallR && p.z <= b.max.z + wallR) return true;
                }
                return false;
            }

            for (float x = rb.min.x + inset; x <= rb.max.x - inset; x += step)
            {
                if (!HasWall(new Vector3(x, midY, rb.min.z + inset))) AddZone(zones, new Vector2(x, rb.min.z + inset));
                if (!HasWall(new Vector3(x, midY, rb.max.z - inset))) AddZone(zones, new Vector2(x, rb.max.z - inset));
            }
            for (float z = rb.min.z + inset; z <= rb.max.z - inset; z += step)
            {
                if (!HasWall(new Vector3(rb.min.x + inset, midY, z))) AddZone(zones, new Vector2(rb.min.x + inset, z));
                if (!HasWall(new Vector3(rb.max.x - inset, midY, z))) AddZone(zones, new Vector2(rb.max.x - inset, z));
            }
            return zones;
        }

        /// <summary>개구부 후보를 1.5m 내 클러스터에 병합하거나 새로 추가한다.</summary>
        /// <param name="zones">클러스터 목록.</param>
        /// <param name="p">후보 XZ.</param>
        private static void AddZone(List<Vector2> zones, Vector2 p)
        {
            for (int i = 0; i < zones.Count; i++)
                if ((zones[i] - p).sqrMagnitude < 1.5f * 1.5f)
                {
                    zones[i] = (zones[i] + p) * 0.5f;
                    return;
                }
            zones.Add(p);
        }

        // ── 공용 헬퍼 ──────────────────────────────────────────────

        /// <summary>활성 씬을 Map으로 보장한다.</summary>
        /// <returns>Map 씬.</returns>
        private static Scene EnsureMapScene()
        {
            Scene active = EditorSceneManager.GetActiveScene();
            if (active.name == mapSceneName) return active;
            return EditorSceneManager.OpenScene(mapScenePath, OpenSceneMode.Single);
        }

        /// <summary>씬에서 기본 이름(+" (n)") 방 인스턴스 루트를 찾는다. GeneratedProps 하위는 제외.</summary>
        /// <param name="scene">대상 씬.</param>
        /// <param name="baseName">방 기본 이름.</param>
        /// <returns>인스턴스 목록.</returns>
        private static List<Transform> FindRoomInstances(Scene scene, string baseName)
        {
            Regex pattern = new Regex($"^{Regex.Escape(baseName)}( \\(\\d+\\))?$");
            List<Transform> result = new List<Transform>();
            System.Action<Transform> rec = null;
            rec = t =>
            {
                if (t.name == generatedRootName) return;
                if (pattern.IsMatch(t.name)) { result.Add(t); return; }
                foreach (Transform c in t) rec(c);
            };
            foreach (GameObject root in scene.GetRootGameObjects()) rec(root.transform);
            return result;
        }

        /// <summary>자식 렌더러들을 합쳐 월드 바운즈를 구한다.</summary>
        /// <param name="go">대상 오브젝트.</param>
        /// <param name="bounds">합산 바운즈.</param>
        /// <returns>렌더러가 있어 바운즈를 구했는지 여부.</returns>
        private static bool TryGetBounds(GameObject go, out Bounds bounds)
        {
            Renderer[] rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) { bounds = default; return false; }
            bounds = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) bounds.Encapsulate(rends[i].bounds);
            return true;
        }

        /// <summary>GeneratedProps 루트를 통째로 제거한다.</summary>
        /// <param name="scene">대상 씬.</param>
        private static void ClearGenerated(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == generatedRootName) Object.DestroyImmediate(root);
        }

        /// <summary>GeneratedProps/&lt;방종류&gt; 그룹을 찾거나 만든다.</summary>
        /// <param name="scene">대상 씬.</param>
        /// <param name="roomType">방 종류 이름.</param>
        /// <returns>그룹 트랜스폼.</returns>
        private static Transform GetOrCreateGroup(Scene scene, string roomType)
        {
            Transform root = null;
            foreach (GameObject r in scene.GetRootGameObjects())
                if (r.name == generatedRootName) { root = r.transform; break; }
            if (root == null)
            {
                GameObject go = new GameObject(generatedRootName);
                SceneManager.MoveGameObjectToScene(go, scene);
                root = go.transform;
            }
            Transform group = new GameObject(roomType).transform;
            group.SetParent(root, false);
            return group;
        }
    }
}
