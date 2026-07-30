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
    /// DecoratedRooms의 꾸며진 방 프리팹(Variant)으로 Map의 빈 방 인스턴스를 통째 교체하는 툴(v3).
    /// 슬롯의 문 위치는 추정이 아니라 프리팹 오버라이드 데이터(제거된 벽 + 추가된 문)에서 정확히 계산한다.
    /// - 셸 유지 변형(원본 벽 보존): 슬롯이 제거한 벽을 동일하게 제거해 문을 맞춘다.
    /// - 셸 교체 변형(벽/바닥/천장을 새로 지음, 예: Bed/Hospital): 제작된 벽을 그대로 쓰되,
    ///   슬롯 문 위치를 그 벽이 막으면 해당 슬롯에는 배정하지 않고 다른 변형을 시도한다.
    /// 문 존 주변의 낮은 가구만 정밀 컷하고(벽·슬래브 보호), 실행 전 Map을 백업한다.
    /// </summary>
    public static class MapRoomSwapper
    {
        private const string mapSceneName = "Game";
        private const string mapScenePath = "Assets/00. Scenes/Game.unity";
        private const string backupScenePath = "Assets/00. Scenes/Game_backup_swap.unity";
        private const string decoratedFolder = "Assets/02. Prefab/Map/DecoratedRooms";
        private const string generatedRootName = "GeneratedProps";
        private const string propSetName = "PropSet";
        private const float doorClearance = 1.2f;      // 문 존 사각형에서 이 거리 안의 낮은 가구 제거
        private const float lowPropMaxY = 2.0f;        // 컷 대상 가구 시작 높이 상한
        private const float wallLikeMinH = 1.8f;       // 이 이상 높이면 벽으로 간주(문 막힘 판정)
        private const float protectTallH = 2.2f;       // 이 이상 높이 유닛은 컷 보호(벽/기둥)
        private const float protectSlabT = 0.2f;       // 이 이하 두께의 넓은 판(바닥/천장)은 컷 보호
        private const int pickSeed = 20260730;         // 변형 분배 시드

        /// <summary>변형 하나: 프리팹, 베이스 경로, 사용 횟수.</summary>
        private class VariantInfo
        {
            public string name;
            public GameObject prefab;
            public string basePath;
            public int used;
        }

        /// <summary>슬롯 문 존 하나: 베이스 애셋의 제거된 벽(또는 추가된 문)의 월드 바운즈.</summary>
        private struct DoorZone
        {
            public GameObject assetWall; // 제거된 벽의 베이스 애셋 객체(추가 문이면 null)
            public Bounds bounds;        // 월드 바운즈
        }

        // ── 진입점 ─────────────────────────────────────────────────

        /// <summary>교체 없이 슬롯별 배정 계획과 문 막힘 판정만 로그로 보고한다.</summary>
        [MenuItem("Tools/Map/방 교체 미리보기 (매칭 리포트)")]
        public static void Preview() { Run(true); }

        /// <summary>모든 슬롯을 적합한 변형으로 교체하고 저장한다. 실행 전 Map을 백업한다.</summary>
        [MenuItem("Tools/Map/방 교체 실행 (DecoratedRooms → Map)")]
        public static void Swap() { Run(false); }

        /// <summary>
        /// 교체 본체: 변형 로드 → 슬롯별 문 존 계산 → 적합 변형 선택(셸 유지=벽 제거 적용,
        /// 셸 교체=문 막힘 검사) → 교체·프랍 컷 → 저장.
        /// </summary>
        /// <param name="dryRun">true면 보고만 하고 씬을 수정하지 않는다.</param>
        private static void Run(bool dryRun)
        {
            Scene map = EnsureMapScene();
            if (!map.IsValid()) { Log.D("[Swap] Map 씬을 열 수 없습니다."); return; }

            Dictionary<string, List<VariantInfo>> byBase = LoadVariants();
            if (byBase.Count == 0) { Log.D($"[Swap] {decoratedFolder} 에 '<Base>__<Variant>.prefab' 이 없습니다."); return; }

            if (!dryRun)
            {
                AssetDatabase.DeleteAsset(backupScenePath);
                if (!AssetDatabase.CopyAsset(mapScenePath, backupScenePath))
                { Log.D("[Swap] 백업 실패 — 중단합니다."); return; }
                Log.D($"[Swap] 백업 생성: {backupScenePath}");
            }

            System.Random rng = new System.Random(pickSeed);
            int done = 0, kept = 0;

            foreach (KeyValuePair<string, List<VariantInfo>> kv in byBase)
            {
                List<Transform> slots = FindRoomInstances(map, kv.Key);
                if (slots.Count == 0) { Log.D($"[Swap] '{kv.Key}' 인스턴스가 Map에 없음 — 스킵(수동 배치 대상)."); continue; }

                foreach (Transform slot in slots)
                {
                    string slotName = slot.name; // TryPlace가 슬롯을 파기하므로 이름을 미리 캡처
                    try
                    {
                        string slotBase = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(slot.gameObject);
                        List<VariantInfo> cands = kv.Value.Where(v => v.basePath == slotBase).ToList();
                        if (cands.Count == 0)
                        { Log.D($"[Swap]   '{slotName}': 베이스 일치 변형 없음 — 유지."); kept++; continue; }

                        var removed = PrefabUtility.GetRemovedGameObjects(slot.gameObject);
                        var added = PrefabUtility.GetAddedGameObjects(slot.gameObject);
                        List<DoorZone> zones = BuildDoorZones(map, slot, slotBase, removed, added);

                        // 사용 횟수 오름차순 + 동률 셔플로 후보 순서 결정
                        List<VariantInfo> ordered = cands
                            .OrderBy(v => v.used).ThenBy(_ => rng.Next()).ToList();

                        bool placed = false;
                        List<string> blockedBy = new List<string>();
                        foreach (VariantInfo v in ordered)
                        {
                            if (TryPlace(map, slot, v, removed, zones, dryRun, out string detail))
                            {
                                v.used++;
                                Log.D($"[Swap]   '{slotName}' ← '{v.name}' {detail}");
                                placed = true;
                                break;
                            }
                            blockedBy.Add(v.name.Substring(v.name.IndexOf("__") + 2));
                        }

                        if (placed) done++;
                        else { Log.D($"[Swap]   '{slotName}': 모든 변형이 문을 막음({string.Join(",", blockedBy)}) — 원본 유지."); kept++; }
                    }
                    catch (System.Exception e)
                    {
                        Log.D($"[Swap]   '{slotName}' 실패: {e.GetType().Name} - {e.Message}");
                        kept++;
                    }
                }
            }

            foreach (List<VariantInfo> list in byBase.Values)
                foreach (VariantInfo v in list) v.used = 0;

            if (!dryRun)
            {
                EditorSceneManager.MarkSceneDirty(map);
                EditorSceneManager.SaveScene(map);
            }
            Log.D($"[Swap] {(dryRun ? "미리보기" : "교체")} 완료: 배치 {done} / 유지 {kept}");
        }

        // ── 변형 로드 ──────────────────────────────────────────────

        /// <summary>DecoratedRooms의 변형 프리팹을 기본 방 이름별로 묶는다(Variant만).</summary>
        /// <returns>기본 방 이름 → 변형 목록.</returns>
        private static Dictionary<string, List<VariantInfo>> LoadVariants()
        {
            Dictionary<string, List<VariantInfo>> result = new Dictionary<string, List<VariantInfo>>();
            Regex nameRe = new Regex("^(.+)__(.+)$");
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { decoratedFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                Match m = nameRe.Match(file);
                if (!m.Success) continue;
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                GameObject baseAsset = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
                string basePath = baseAsset != null ? AssetDatabase.GetAssetPath(baseAsset) : null;
                if (string.IsNullOrEmpty(basePath))
                { Log.D($"[Swap] '{file}' 는 Variant가 아님 — 제외."); continue; }

                string baseType = m.Groups[1].Value;
                if (!result.TryGetValue(baseType, out List<VariantInfo> list)) result[baseType] = list = new List<VariantInfo>();
                list.Add(new VariantInfo { name = file, prefab = prefab, basePath = basePath });
            }
            return result;
        }

        // ── 문 존 계산 ─────────────────────────────────────────────

        /// <summary>
        /// 슬롯의 정확한 문 존을 계산한다: 베이스 프리팹을 슬롯 자세로 임시 정렬해
        /// "슬롯이 제거한 벽"의 월드 바운즈를 구하고, 슬롯이 추가한 문 오브젝트 바운즈를 더한다.
        /// </summary>
        /// <param name="map">맵 씬.</param>
        /// <param name="slot">슬롯 인스턴스.</param>
        /// <param name="basePath">베이스 프리팹 경로.</param>
        /// <param name="removed">슬롯의 제거 오버라이드.</param>
        /// <param name="added">슬롯의 추가 오버라이드.</param>
        /// <returns>문 존 목록(월드).</returns>
        private static List<DoorZone> BuildDoorZones(Scene map, Transform slot, string basePath,
            List<RemovedGameObject> removed, List<AddedGameObject> added)
        {
            List<DoorZone> zones = new List<DoorZone>();

            GameObject baseAsset = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
            GameObject temp = (GameObject)PrefabUtility.InstantiatePrefab(baseAsset, map);
            temp.transform.SetPositionAndRotation(slot.position, slot.rotation);
            temp.transform.localScale = slot.localScale;

            Dictionary<GameObject, GameObject> baseToTemp = new Dictionary<GameObject, GameObject>();
            foreach (Transform t in temp.GetComponentsInChildren<Transform>(true))
            {
                GameObject src = PrefabUtility.GetCorrespondingObjectFromSourceAtPath(t.gameObject, basePath);
                if (src != null && !baseToTemp.ContainsKey(src)) baseToTemp[src] = t.gameObject;
            }

            foreach (RemovedGameObject rm in removed)
            {
                if (rm.assetGameObject == null) continue;
                if (!baseToTemp.TryGetValue(rm.assetGameObject, out GameObject counterpart)) continue;
                if (TryGetBounds(counterpart, out Bounds b))
                    zones.Add(new DoorZone { assetWall = rm.assetGameObject, bounds = b });
                else
                    zones.Add(new DoorZone { assetWall = rm.assetGameObject, bounds = new Bounds(counterpart.transform.position, Vector3.one) });
            }
            Object.DestroyImmediate(temp);

            foreach (AddedGameObject ad in added)
            {
                if (ad.instanceGameObject == null) continue;
                if (TryGetBounds(ad.instanceGameObject, out Bounds b))
                    zones.Add(new DoorZone { assetWall = null, bounds = b });
            }
            return zones;
        }

        // ── 배치 시도 ──────────────────────────────────────────────

        /// <summary>
        /// 변형 하나를 슬롯에 배치 시도한다. 셸 유지 변형은 슬롯의 벽 제거를 적용하고,
        /// 셸 교체 변형은 제작된 벽이 문 존을 막는지 검사해 막히면 실패를 반환한다.
        /// 성공 시(실행 모드) 슬롯을 파기하고 문 존 주변 가구를 정밀 컷한다.
        /// </summary>
        /// <param name="map">맵 씬.</param>
        /// <param name="slot">슬롯.</param>
        /// <param name="v">변형.</param>
        /// <param name="removed">슬롯의 제거 오버라이드.</param>
        /// <param name="zones">슬롯 문 존.</param>
        /// <param name="dryRun">미리보기 여부.</param>
        /// <param name="detail">로그용 상세 문자열.</param>
        /// <returns>배치 성공 여부.</returns>
        private static bool TryPlace(Scene map, Transform slot, VariantInfo v,
            List<RemovedGameObject> removed, List<DoorZone> zones, bool dryRun, out string detail)
        {
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(v.prefab, map);
            go.transform.SetPositionAndRotation(slot.position, slot.rotation);
            go.transform.localScale = slot.localScale;

            Dictionary<GameObject, GameObject> baseToInst = new Dictionary<GameObject, GameObject>();
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                GameObject src = PrefabUtility.GetCorrespondingObjectFromSourceAtPath(t.gameObject, v.basePath);
                if (src != null && !baseToInst.ContainsKey(src)) baseToInst[src] = t.gameObject;
            }

            // 1) 슬롯의 벽 제거를 대응/막힘으로 분류
            List<GameObject> toDelete = new List<GameObject>();
            int absent = 0;
            foreach (RemovedGameObject rm in removed)
            {
                if (rm.assetGameObject == null) continue;
                if (baseToInst.TryGetValue(rm.assetGameObject, out GameObject target) && target != null)
                { toDelete.Add(target); continue; }

                // 변형에 그 벽이 없음(셸 교체) → 제작된 벽이 이 문 존을 막는지 검사
                absent++;
                Bounds zone = FindZone(zones, rm.assetGameObject);
                zone.Expand(new Vector3(-0.3f, 0f, -0.3f));
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                {
                    Bounds b = r.bounds;
                    if (b.size.y < wallLikeMinH) continue;
                    bool xz = b.min.x < zone.max.x && b.max.x > zone.min.x && b.min.z < zone.max.z && b.max.z > zone.min.z;
                    if (xz)
                    {
                        Object.DestroyImmediate(go);
                        detail = null;
                        return false; // 문 막힘 → 이 변형은 이 슬롯에 부적합
                    }
                }
            }

            if (dryRun)
            {
                Object.DestroyImmediate(go);
                detail = $"(셸 {(absent > 0 ? "교체" : "유지")}, 벽제거 {toDelete.Count}, 자체개구 {absent}, 문존 {zones.Count})";
                return true;
            }

            // 2) 실제 적용
            int sibling = slot.GetSiblingIndex();
            go.transform.SetParent(slot.parent, true);
            go.transform.SetSiblingIndex(sibling);
            go.name = v.name;

            foreach (GameObject d in toDelete) Object.DestroyImmediate(d);

            // 슬롯의 추가 오브젝트(문 등)를 월드 기준으로 복사
            HashSet<Transform> copied = new HashSet<Transform>();
            int addCopied = 0;
            foreach (AddedGameObject ad in PrefabUtility.GetAddedGameObjects(slot.gameObject))
            {
                GameObject src = ad.instanceGameObject;
                if (src == null) continue;
                Transform targetParent = go.transform;
                Transform srcParent = src.transform.parent;
                if (srcParent != null)
                {
                    GameObject pBase = PrefabUtility.GetCorrespondingObjectFromSourceAtPath(srcParent.gameObject, v.basePath);
                    if (pBase != null && baseToInst.TryGetValue(pBase, out GameObject mapped) && mapped != null)
                        targetParent = mapped.transform;
                }
                GameObject clone = Object.Instantiate(src);
                clone.name = src.name;
                clone.transform.SetParent(targetParent, true);
                clone.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
                copied.Add(clone.transform);
                addCopied++;
            }

            Object.DestroyImmediate(slot.gameObject);

            int propsCut = CullPropsNearZones(go, v.basePath, zones, copied);
            detail = $"(셸 {(absent > 0 ? "교체" : "유지")}, 벽제거 {toDelete.Count}, 문복사 {addCopied}, 프랍컷 {propsCut})";
            return true;
        }

        /// <summary>제거된 벽 애셋에 해당하는 문 존 바운즈를 찾는다(없으면 크기 0).</summary>
        /// <param name="zones">문 존 목록.</param>
        /// <param name="assetWall">제거된 벽 애셋.</param>
        /// <returns>해당 존 바운즈.</returns>
        private static Bounds FindZone(List<DoorZone> zones, GameObject assetWall)
        {
            foreach (DoorZone z in zones)
                if (z.assetWall == assetWall) return z.bounds;
            return new Bounds(Vector3.one * 99999f, Vector3.zero);
        }

        /// <summary>
        /// 문 존 주변의 낮은 가구(변형이 추가한 프랍)만 정밀 컷한다.
        /// 벽(높이 2.2 이상)과 넓은 슬래브(바닥/천장), 복사된 문은 보호한다.
        /// </summary>
        /// <param name="room">교체된 방 루트.</param>
        /// <param name="basePath">베이스 프리팹 경로(프랍 판별용).</param>
        /// <param name="zones">문 존 목록.</param>
        /// <param name="protectedRoots">보호 대상(복사된 문 등).</param>
        /// <returns>제거한 프랍 수.</returns>
        private static int CullPropsNearZones(GameObject room, string basePath, List<DoorZone> zones, HashSet<Transform> protectedRoots)
        {
            if (zones.Count == 0) return 0;
            if (!TryGetBounds(room, out Bounds rb)) return 0;

            List<Transform> units = new List<Transform>();
            void Collect(Transform t)
            {
                foreach (Transform c in t)
                {
                    if (protectedRoots.Contains(c)) continue;
                    GameObject src = PrefabUtility.GetCorrespondingObjectFromSourceAtPath(c.gameObject, basePath);
                    if (src != null) { Collect(c); continue; }
                    if (c.name == propSetName) { Collect(c); continue; }
                    units.Add(c);
                }
            }
            Collect(room.transform);

            int cut = 0;
            foreach (Transform u in units)
            {
                if (u == null) continue;
                if (!TryGetBounds(u.gameObject, out Bounds b)) continue;
                if (b.min.y >= rb.min.y + lowPropMaxY) continue;                       // 높은 것(조명 등)
                if (b.size.y > protectTallH) continue;                                  // 벽/기둥 보호
                if (b.size.y < protectSlabT && (b.size.x > 1.5f || b.size.z > 1.5f)) continue; // 바닥/천장 슬래브 보호

                foreach (DoorZone z in zones)
                {
                    float dx = Mathf.Max(z.bounds.min.x - b.max.x, 0f, b.min.x - z.bounds.max.x);
                    float dz = Mathf.Max(z.bounds.min.z - b.max.z, 0f, b.min.z - z.bounds.max.z);
                    if (dx * dx + dz * dz < doorClearance * doorClearance)
                    { Object.DestroyImmediate(u.gameObject); cut++; break; }
                }
            }
            return cut;
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
    }
}
