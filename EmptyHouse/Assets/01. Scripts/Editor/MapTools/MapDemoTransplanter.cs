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
    /// Horror Pack 데모 씬의 사람 손 배치를 Map의 Room에 테마별로 이식하는 자동 배치기(v3).
    /// 규칙: Hallway는 통로이므로 비운다. 데모의 벽/바닥/천장/문(Architectural)은 가져오지 않고
    /// 내부 구조물만 가져온다. 가구 배치는 방 크기에 맞게 스케일하고 벽 인접 가구는 실제 벽에
    /// 밀착시키며, 천장 조명은 방 천장에 부착한다. 방마다 개구부를 감지해 통로를 확보한다.
    /// 생성물은 전부 GeneratedProps 아래에 두어 통째 삭제로 원복한다.
    /// </summary>
    public static class MapDemoTransplanter
    {
        private const string mapSceneName = "Map";
        private const string mapScenePath = "Assets/00. Scenes/Map.unity";
        private const string generatedRootName = "GeneratedProps";
        private const string demoOffices = "Assets/Horror_Pack_1/Scenes/Demo_Offices.unity";
        private const string demoHallOffice = "Assets/Horror_Pack_1/Scenes/Demo_Hall_And_Office.unity";

        private const float openingClearance = 2.2f;   // 개구부 중심 반경 내 낮은 프랍 제거
        private const float lowPropMaxY = 2.0f;        // 통로 컷 대상이 되는 프랍 시작 높이 상한
        private const float roomBoundsInflate = 0.4f;  // 방 경계 허용 여유(벽 밀착 허용)
        private const float ceilingSnapMinBottom = 2.2f; // 이보다 높이 시작하는 조명은 천장 부착 대상
        private const float wallSnapMaxDist = 2.5f;    // 벽성 가구를 최근접 내벽에 스냅하는 최대 거리
        private const float wallMountedMinY = 0.5f;    // 바닥에서 이보다 떠서 시작하면 벽걸이로 판정
        private const float tallFurnitureMinH = 1.5f;  // 이보다 큰 키의 가구는 벽에 세우는 가구로 판정
        private const float floorTileThickness = 0.05f; // 맵 바닥 타일 두께(프랍 앵커 테마 침하 보정)
        private const float minPosScale = 0.7f;        // 위치 축소 하한(과도한 압축 방지)
        private const float maxPosScale = 1.25f;       // 위치 확대 상한(과도한 벌어짐 방지)
        private const float isolatedDiagMax = 1.2f;    // 이보다 작은 프랍이
        private const float isolatedDistMin = 2.5f;    // 이 거리 안에 이웃이 없으면 고립 파편으로 제거

        private static readonly Regex archNameFallback =
            new Regex("^(hall|office)_(wall|floor|ceiling|colum|door|arch)", RegexOptions.IgnoreCase);

        /// <summary>테마 하나: 어느 데모 씬의 어떤 그룹을 어떤 영역 기준으로 가져올지.</summary>
        private struct ThemeConfig
        {
            public string name;          // 로그용 테마 이름
            public string scenePath;     // 소스 데모 씬
            public string anchorGroup;   // 바닥/천장/기본 영역 기준 그룹
            public string[] propGroups;  // 복사할 프랍 그룹들
            public bool useCustomRegion; // true면 아래 커스텀 영역을 소스로 사용(영역 잘라내기)
            public Vector2 customCenter; // 데모 좌표계 XZ 중심
            public Vector2 customSize;   // 데모 좌표계 XZ 크기
            public bool anchorIsProps;   // 앵커가 구조물이 아닌 프랍 그룹이면 true(바닥 두께 침하 보정)
        }

        /// <summary>씬+그룹 조합당 1개 만들어지는 공유 템플릿(평탄화된 유닛 목록 포함).</summary>
        private class TemplateData
        {
            public GameObject go;          // 비활성 템플릿 루트(자식 = 유닛, 데모 좌표 유지)
            public bool[] isLight;         // 유닛별: Lights 카테고리 여부
            public bool[] hasRenderer;     // 유닛별: 렌더러 보유 여부
        }

        /// <summary>테마별 런타임: 공유 템플릿 + 영역 메타.</summary>
        private class ThemeRuntime
        {
            public string name;
            public TemplateData tpl;
            public Vector2 srcCenter;
            public Vector2 srcSize;
            public float srcFloorY;
            public float srcHeight;
            public bool anchorIsProps;
        }

        // ── 테마 정의 ──────────────────────────────────────────────

        private static readonly ThemeConfig themeOffice = new ThemeConfig
        {
            name = "사무실", scenePath = demoOffices, anchorGroup = "Architect",
            propGroups = new[] { "Furniture", "Small_Props", "Ceiling_Light" },
        };

        private static readonly ThemeConfig themeOfficeSmall = new ThemeConfig
        {
            name = "사무실(소형)", scenePath = demoOffices, anchorGroup = "Architect",
            propGroups = new[] { "Furniture", "Small_Props", "Ceiling_Light" },
            useCustomRegion = true, customCenter = new Vector2(-4f, 6f), customSize = new Vector2(12f, 13f),
        };

        private static readonly ThemeConfig themeMedical = new ThemeConfig
        {
            name = "병동", scenePath = demoHallOffice, anchorGroup = "Medical",
            propGroups = new[] { "Medical" }, anchorIsProps = true,
        };

        private static readonly ThemeConfig themeWardSmall = new ThemeConfig
        {
            name = "병실(소형)", scenePath = demoHallOffice, anchorGroup = "Medical",
            propGroups = new[] { "Medical" }, anchorIsProps = true,
            useCustomRegion = true, customCenter = new Vector2(16f, -50f), customSize = new Vector2(12f, 13f),
        };

        private static readonly ThemeConfig themeToilet = new ThemeConfig
        {
            name = "화장실", scenePath = demoOffices, anchorGroup = "Toilet",
            propGroups = new[] { "Toilet" }, anchorIsProps = true,
        };

        private static readonly ThemeConfig themeArchive = new ThemeConfig
        {
            name = "서고", scenePath = demoHallOffice, anchorGroup = "Wood_Room",
            propGroups = new[] { "Wood_Room" }, anchorIsProps = true,
        };

        /// <summary>방 종류(기본 이름) → 순환 적용할 테마 목록.</summary>
        private static readonly (string roomType, ThemeConfig[] themes)[] roomPlan =
        {
            ("EmptyRoom-5x5", new[] { themeOffice }),
            ("Entrance-EmptyRoom-5x5", new[] { themeOffice }),
            ("EmptyRoom-6x8", new[] { themeMedical }),
            ("EmptyRoom-3x3", new[] { themeOfficeSmall, themeWardSmall }),
            ("A-EmptyRoom-3x3", new[] { themeOfficeSmall }),
            ("SmallRoom", new[] { themeToilet }),
            ("BookRoom", new[] { themeArchive }),
        };

        // ── 진입점 ─────────────────────────────────────────────────

        /// <summary>
        /// Room 전체를 테마별로 자동 배치한다(v3). Hallway는 비워 둔다.
        /// 구조물 필터/벽 스냅/천장 부착/통로 확보/고립 정리를 적용하고 씬을 저장한다.
        /// </summary>
        public static void DecorateAllRooms()
        {
            Scene map = EnsureMapScene();
            if (!map.IsValid()) { Log.D("[AutoDecor] Map 씬을 열 수 없습니다."); return; }

            ClearGenerated(map);

            List<ThemeRuntime> runtimes = BuildThemeRuntimes(map);
            if (runtimes.Count == 0) { Log.D("[AutoDecor] 템플릿 빌드 실패."); return; }
            Dictionary<string, ThemeRuntime> byName = runtimes.ToDictionary(r => r.name);

            foreach ((string roomType, ThemeConfig[] themes) in roomPlan)
                DecorateRoomType(map, roomType, themes, byName);

            foreach (TemplateData tpl in runtimes.Select(r => r.tpl).Distinct())
                Object.DestroyImmediate(tpl.go);

            EditorSceneManager.MarkSceneDirty(map);
            EditorSceneManager.SaveScene(map);
            Log.D("[AutoDecor] 전체 배치 완료 후 저장.");
        }

        // ── 템플릿/테마 빌드 ───────────────────────────────────────

        /// <summary>
        /// 데모 씬을 씬 단위로 한 번씩 열어, 씬+그룹 조합당 공유 템플릿을 만들고
        /// 테마별 영역 메타와 벽 인접 마스크를 계산한다.
        /// </summary>
        /// <param name="map">맵 씬.</param>
        /// <returns>테마 런타임 목록.</returns>
        private static List<ThemeRuntime> BuildThemeRuntimes(Scene map)
        {
            List<ThemeRuntime> result = new List<ThemeRuntime>();
            ThemeConfig[] allThemes = roomPlan.SelectMany(p => p.themes).GroupBy(t => t.name).Select(g => g.First()).ToArray();
            Dictionary<string, TemplateData> tplCache = new Dictionary<string, TemplateData>();

            foreach (IGrouping<string, ThemeConfig> byScene in allThemes.GroupBy(t => t.scenePath))
            {
                Scene demo = EditorSceneManager.OpenScene(byScene.Key, OpenSceneMode.Additive);
                foreach (ThemeConfig cfg in byScene)
                {
                    string tplKey = cfg.scenePath + "|" + string.Join(",", cfg.propGroups);
                    if (!tplCache.TryGetValue(tplKey, out TemplateData tpl))
                    {
                        tpl = BuildTemplate(map, demo, cfg.propGroups, cfg.name);
                        if (tpl == null) continue;
                        tplCache[tplKey] = tpl;
                    }

                    GameObject anchor = FindInScene(demo, cfg.anchorGroup);
                    if (anchor == null || !TryGetBounds(anchor.gameObject, out Bounds ab))
                    { Log.D($"[AutoDecor] 테마 '{cfg.name}' 앵커 실패 — 건너뜀."); continue; }

                    result.Add(new ThemeRuntime
                    {
                        name = cfg.name,
                        tpl = tpl,
                        srcCenter = cfg.useCustomRegion ? cfg.customCenter : new Vector2(ab.center.x, ab.center.z),
                        srcSize = cfg.useCustomRegion ? cfg.customSize : new Vector2(ab.size.x, ab.size.z),
                        srcFloorY = ab.min.y,
                        srcHeight = ab.size.y,
                        anchorIsProps = cfg.anchorIsProps,
                    });
                }
                EditorSceneManager.CloseScene(demo, true);
            }
            return result;
        }

        /// <summary>
        /// 데모 그룹들을 복사해 구조물(Architectural)·파티션 유리·깨진 프리팹을 제거하고,
        /// 남은 유닛(프리팹 인스턴스 루트 단위)을 평탄화한 비활성 템플릿을 만든다.
        /// </summary>
        /// <param name="map">맵 씬(템플릿을 둘 곳).</param>
        /// <param name="demo">열린 데모 씬.</param>
        /// <param name="propGroups">복사할 그룹 이름들.</param>
        /// <param name="themeName">로그용 테마 이름.</param>
        /// <returns>템플릿 데이터(그룹을 하나도 못 찾으면 null).</returns>
        private static TemplateData BuildTemplate(Scene map, Scene demo, string[] propGroups, string themeName)
        {
            // 주의: Object.Instantiate 복사본은 프리팹 연결이 끊긴다(실측 확인).
            // 따라서 분류(카테고리/조명)는 반드시 "원본" 씬 오브젝트에서 끝내고, 생존 유닛만 개별 복사한다.
            List<Transform> units = new List<Transform>();
            HashSet<Transform> seen = new HashSet<Transform>();
            int found = 0;
            foreach (string grp in propGroups)
            {
                GameObject src = FindInScene(demo, grp);
                if (src == null) { Log.D($"[AutoDecor] 그룹 '{grp}' 없음(테마 {themeName})."); continue; }
                found++;
                foreach (Renderer r in src.GetComponentsInChildren<Renderer>(true)) CollectUnit(r.transform, seen, units);
                foreach (Light l in src.GetComponentsInChildren<Light>(true)) CollectUnit(l.transform, seen, units);
            }
            if (found == 0) return null;

            // 중첩 유닛 제거: 다른 유닛의 하위에 있는 유닛은 부모 복사에 포함되므로 제외(중복 방지)
            HashSet<Transform> unitSet = new HashSet<Transform>(units);
            units.RemoveAll(u =>
            {
                for (Transform p = u.parent; p != null; p = p.parent)
                    if (unitSet.Contains(p)) return true;
                return false;
            });

            // 바닥 높이(유리 파티션 판정용) — 유닛 바운즈 최저값
            float floorY = float.MaxValue;
            foreach (Transform u in units)
                if (TryGetBounds(u.gameObject, out Bounds b)) floorY = Mathf.Min(floorY, b.min.y);

            // 원본에서 분류 → 생존 유닛만 개별 복사해 평탄화 템플릿 구성
            GameObject tplRoot = new GameObject("TPL_" + themeName);
            SceneManager.MoveGameObjectToScene(tplRoot, map);
            List<bool> isLight = new List<bool>();
            List<bool> hasRend = new List<bool>();
            int cutArch = 0, cutGlass = 0, cutMissing = 0;

            foreach (Transform u in units)
            {
                if (u.name.StartsWith("Missing Prefab")) { cutMissing++; continue; }

                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(u.gameObject);
                string cat = CategoryOf(path);
                if (cat == "Architectural" || archNameFallback.IsMatch(u.name)) { cutArch++; continue; }

                bool rend = TryGetBounds(u.gameObject, out Bounds ub);
                if (cat == "Glass" && rend && (ub.min.y > floorY + 0.3f || ub.size.y > 1.2f))
                { cutGlass++; continue; } // 파티션 창유리(벽 제거 후 공중 부유 방지)

                Transform copy = Object.Instantiate(u.gameObject).transform;
                copy.name = u.name;
                copy.SetParent(tplRoot.transform, true); // 맵 씬(tplRoot)으로 이동
                copy.SetPositionAndRotation(u.position, u.rotation);
                copy.localScale = u.lossyScale;
                // 조명 판정: Lights 카테고리 프리팹 + 비프리팹 씬 Point light(렌더러 없이 Light만)도 포함
                bool lightOnly = !rend && u.GetComponentInChildren<Light>(true) != null;
                isLight.Add(cat == "Lights" || lightOnly);
                hasRend.Add(rend);
            }
            tplRoot.SetActive(false);

            Log.D($"[AutoDecor] 템플릿 '{themeName}': 유닛 {tplRoot.transform.childCount} (구조물컷 {cutArch}, 유리컷 {cutGlass}, 깨짐컷 {cutMissing})");
            return new TemplateData { go = tplRoot, isLight = isLight.ToArray(), hasRenderer = hasRend.ToArray() };
        }

        /// <summary>
        /// 컴포넌트가 속한 유닛 루트(최근접 프리팹 인스턴스 루트, 비프리팹은 자신)를 중복 없이 수집한다.
        /// </summary>
        /// <param name="t">컴포넌트 트랜스폼.</param>
        /// <param name="seen">중복 방지 집합.</param>
        /// <param name="units">수집 목록.</param>
        private static void CollectUnit(Transform t, HashSet<Transform> seen, List<Transform> units)
        {
            GameObject root = PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject);
            Transform key = root != null ? root.transform : t;
            if (seen.Add(key)) units.Add(key);
        }

        /// <summary>프리팹 에셋 경로에서 Horror Pack 카테고리 폴더명을 추출한다.</summary>
        /// <param name="assetPath">프리팹 경로(null 허용).</param>
        /// <returns>카테고리명(모르면 빈 문자열).</returns>
        private static string CategoryOf(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return "";
            int i = assetPath.IndexOf("!Prefabs/");
            if (i < 0) return "";
            string rest = assetPath.Substring(i + 9);
            int slash = rest.IndexOf('/');
            return slash > 0 ? rest.Substring(0, slash) : rest;
        }

        // ── 방 종류 단위 배치 ──────────────────────────────────────

        /// <summary>
        /// 한 방 종류의 모든 인스턴스에 테마를 순환 적용해 배치한다.
        /// </summary>
        /// <param name="map">맵 씬.</param>
        /// <param name="roomType">방 기본 이름.</param>
        /// <param name="themes">순환 적용할 테마들.</param>
        /// <param name="runtimes">테마 이름 → 런타임.</param>
        private static void DecorateRoomType(Scene map, string roomType, ThemeConfig[] themes, Dictionary<string, ThemeRuntime> runtimes)
        {
            List<Transform> instances = FindRoomInstances(map, roomType);
            if (instances.Count == 0) { Log.D($"[AutoDecor] '{roomType}' 인스턴스 없음."); return; }

            Transform typeGroup = GetOrCreateGroup(map, roomType);
            int done = 0, passCut = 0, boundsCut = 0, isoCut = 0;

            for (int i = 0; i < instances.Count; i++)
            {
                Transform room = instances[i];
                if (!TryGetBounds(room.gameObject, out Bounds rb)) continue;
                if (!runtimes.TryGetValue(themes[i % themes.Length].name, out ThemeRuntime rt)) continue;

                DecorateOneRoom(room, rb, rt, typeGroup, ref passCut, ref boundsCut, ref isoCut);
                done++;
            }
            Log.D($"[AutoDecor] '{roomType}' {done}개 배치 (테마 {string.Join("/", themes.Select(t => t.name))}, 통로컷 {passCut}, 경계컷 {boundsCut}, 고립컷 {isoCut})");
        }

        /// <summary>
        /// 방 하나에 테마 템플릿을 복제 배치한다:
        /// 위치 스케일 → 천장 조명 부착(내천장면 기준) → 벽성 가구 최근접 내벽 스냅 →
        /// 통로/경계(내벽면) 컷 → 고립 파편 정리.
        /// </summary>
        /// <param name="room">대상 방 루트.</param>
        /// <param name="rb">방 월드 바운즈.</param>
        /// <param name="rt">테마 런타임.</param>
        /// <param name="typeGroup">GeneratedProps 하위 방 종류 그룹.</param>
        /// <param name="passCut">통로 컷 카운터.</param>
        /// <param name="boundsCut">경계 컷 카운터.</param>
        /// <param name="isoCut">고립 컷 카운터.</param>
        private static void DecorateOneRoom(Transform room, Bounds rb, ThemeRuntime rt, Transform typeGroup,
            ref int passCut, ref int boundsCut, ref int isoCut)
        {
            GameObject clone = Object.Instantiate(rt.tpl.go);
            clone.name = room.name;
            clone.SetActive(true);

            float fx = Mathf.Clamp(rb.size.x / rt.srcSize.x, minPosScale, maxPosScale);
            float fz = Mathf.Clamp(rb.size.z / rt.srcSize.y, minPosScale, maxPosScale);
            // 프랍 앵커 테마는 srcFloorY가 "바닥 표면 위 프랍 최저점"이라 바닥 타일 두께만큼 보정
            float floorDelta = rb.min.y - rt.srcFloorY + (rt.anchorIsProps ? floorTileThickness : 0f);

            int n = clone.transform.childCount;
            List<Transform> units = new List<Transform>(n);
            for (int i = 0; i < n; i++) units.Add(clone.transform.GetChild(i));

            float[] faces = ComputeInnerWallFaces(room.gameObject, rb);
            float ceilY = ComputeInnerCeilingY(room.gameObject, rb);
            // 내벽면 기준 유효 실내 사각형(면이 없으면 외곽 바운즈로 대체)
            float inXMin = float.IsNaN(faces[1]) ? rb.min.x + roomBoundsInflate : faces[1];
            float inXMax = float.IsNaN(faces[0]) ? rb.max.x - roomBoundsInflate : faces[0];
            float inZMin = float.IsNaN(faces[3]) ? rb.min.z + roomBoundsInflate : faces[3];
            float inZMax = float.IsNaN(faces[2]) ? rb.max.z - roomBoundsInflate : faces[2];

            bool[] dead = new bool[n];

            // 1) 위치 스케일 + 천장 부착 + 벽성 가구 스냅
            for (int i = 0; i < n; i++)
            {
                Transform u = units[i];
                Vector3 p = u.position;
                u.position = new Vector3(
                    rb.center.x + (p.x - rt.srcCenter.x) * fx,
                    p.y + floorDelta,
                    rb.center.z + (p.z - rt.srcCenter.y) * fz);

                bool rend = rt.tpl.hasRenderer[i];

                if (rt.tpl.isLight[i])
                {
                    if (rend && TryGetBounds(u.gameObject, out Bounds lb))
                    {
                        if (lb.min.y > rb.min.y + ceilingSnapMinBottom)
                            u.position += new Vector3(0f, (ceilY - 0.02f) - lb.max.y, 0f); // 내천장면 부착
                    }
                    else if (u.position.y > rb.min.y + ceilingSnapMinBottom)
                    {
                        u.position = new Vector3(u.position.x, ceilY - 0.4f, u.position.z); // 광원은 천장 살짝 아래
                    }
                    continue;
                }

                // 벽성 가구(벽걸이 또는 큰 키): 최근접 내벽에 밀착, 벽걸이인데 붙일 벽이 없으면 제거
                if (!rend || !TryGetBounds(u.gameObject, out Bounds ub)) continue;
                bool wallMounted = ub.min.y > rb.min.y + wallMountedMinY;
                bool tall = !wallMounted && ub.size.y > tallFurnitureMinH;
                if (!wallMounted && !tall) continue;

                float best = float.MaxValue; int side = -1; float shift = 0f;
                if (!float.IsNaN(faces[0])) { float d = (faces[0] - 0.03f) - ub.max.x; if (Mathf.Abs(d) < best) { best = Mathf.Abs(d); side = 0; shift = d; } }
                if (!float.IsNaN(faces[1])) { float d = (faces[1] + 0.03f) - ub.min.x; if (Mathf.Abs(d) < best) { best = Mathf.Abs(d); side = 1; shift = d; } }
                if (!float.IsNaN(faces[2])) { float d = (faces[2] - 0.03f) - ub.max.z; if (Mathf.Abs(d) < best) { best = Mathf.Abs(d); side = 2; shift = d; } }
                if (!float.IsNaN(faces[3])) { float d = (faces[3] + 0.03f) - ub.min.z; if (Mathf.Abs(d) < best) { best = Mathf.Abs(d); side = 3; shift = d; } }

                if (side >= 0 && best <= wallSnapMaxDist)
                    u.position += side <= 1 ? new Vector3(shift, 0f, 0f) : new Vector3(0f, 0f, shift);
                else if (wallMounted)
                { dead[i] = true; boundsCut++; Object.DestroyImmediate(u.gameObject); } // 허공 벽걸이 방지
            }

            // 2) 통로/경계(내벽면) 컷
            List<Vector2> openings = FindOpenings(room.gameObject, rb);
            List<Transform> alive = new List<Transform>();
            for (int i = 0; i < n; i++)
            {
                if (dead[i]) continue;
                Transform u = units[i];
                bool rend = TryGetBounds(u.gameObject, out Bounds b);
                Vector3 lo = rend ? b.min : u.position;
                Vector3 hi = rend ? b.max : u.position;

                bool outside =
                    lo.x < inXMin - 0.25f || hi.x > inXMax + 0.25f ||
                    lo.z < inZMin - 0.25f || hi.z > inZMax + 0.25f;

                bool nearOpening = false;
                if (!outside && lo.y < rb.min.y + lowPropMaxY)
                    foreach (Vector2 o in openings)
                    {
                        float dx = Mathf.Max(lo.x - o.x, 0f, o.x - hi.x);
                        float dz = Mathf.Max(lo.z - o.y, 0f, o.y - hi.z);
                        if (dx * dx + dz * dz < openingClearance * openingClearance) { nearOpening = true; break; }
                    }

                if (outside) { boundsCut++; Object.DestroyImmediate(u.gameObject); }
                else if (nearOpening) { passCut++; Object.DestroyImmediate(u.gameObject); }
                else alive.Add(u);
            }

            // 3) 받침 검사: 바닥에서 뜬 유닛은 받침(다른 유닛 상판·벽 밀착·천장대)이 있어야 생존
            List<(Transform t, Bounds b)> aliveB = new List<(Transform, Bounds)>();
            foreach (Transform u in alive)
                if (u != null && TryGetBounds(u.gameObject, out Bounds ab)) aliveB.Add((u, ab));
            foreach ((Transform t, Bounds b) in aliveB)
            {
                if (b.min.y <= rb.min.y + 0.35f) continue;                 // 바닥 위 → 통과
                if (b.min.y > rb.min.y + ceilingSnapMinBottom) continue;   // 천장대(조명) → 통과
                bool wallFlush =
                    (!float.IsNaN(faces[0]) && Mathf.Abs(faces[0] - b.max.x) < 0.15f) ||
                    (!float.IsNaN(faces[1]) && Mathf.Abs(b.min.x - faces[1]) < 0.15f) ||
                    (!float.IsNaN(faces[2]) && Mathf.Abs(faces[2] - b.max.z) < 0.15f) ||
                    (!float.IsNaN(faces[3]) && Mathf.Abs(b.min.z - faces[3]) < 0.15f);
                if (wallFlush) continue;
                bool hasSupport = false;
                foreach ((Transform t2, Bounds b2) in aliveB)
                {
                    if (t2 == t) continue;
                    bool xz = b.min.x < b2.max.x && b.max.x > b2.min.x && b.min.z < b2.max.z && b.max.z > b2.min.z;
                    if (xz && Mathf.Abs(b2.max.y - b.min.y) < 0.25f) { hasSupport = true; break; }
                }
                if (!hasSupport) { isoCut++; alive.Remove(t); Object.DestroyImmediate(t.gameObject); }
            }

            // 4) 고립 파편 정리(조명 제외): 작은 유닛인데 주변에 아무것도 없으면 제거
            List<(Transform t, Vector2 c, float diag, bool light)> meta = new List<(Transform, Vector2, float, bool)>();
            foreach (Transform u in alive)
            {
                if (u == null) continue;
                int idx = units.IndexOf(u);
                bool light = idx >= 0 && rt.tpl.isLight[idx];
                if (!TryGetBounds(u.gameObject, out Bounds b)) continue;
                meta.Add((u, new Vector2(b.center.x, b.center.z), new Vector2(b.size.x, b.size.z).magnitude, light));
            }
            foreach ((Transform t, Vector2 c, float diag, bool light) in meta)
            {
                if (light || diag > isolatedDiagMax) continue;
                bool hasNeighbor = false;
                foreach ((Transform t2, Vector2 c2, float _, bool __) in meta)
                    if (t2 != t && (c2 - c).sqrMagnitude < isolatedDistMin * isolatedDistMin) { hasNeighbor = true; break; }
                if (!hasNeighbor) { isoCut++; Object.DestroyImmediate(t.gameObject); }
            }

            clone.transform.SetParent(typeGroup, true);
        }

        /// <summary>
        /// 방의 내천장면 높이(천장 슬래브 밑면들의 중앙값)를 구한다. 못 찾으면 외곽 상단-0.1을 반환한다.
        /// </summary>
        /// <param name="room">방 루트.</param>
        /// <param name="rb">방 바운즈.</param>
        /// <returns>내천장면 Y.</returns>
        private static float ComputeInnerCeilingY(GameObject room, Bounds rb)
        {
            List<float> ys = new List<float>();
            foreach (Renderer r in room.GetComponentsInChildren<Renderer>())
            {
                Bounds b = r.bounds;
                if (b.size.y > 1.5f) continue; // 벽 제외, 얇은 슬래브만
                if (b.center.y > rb.min.y + rb.size.y * 0.75f) ys.Add(b.min.y);
            }
            float m = Median(ys);
            return float.IsNaN(m) ? rb.max.y - 0.1f : m;
        }

        /// <summary>
        /// 방의 4방향 내벽 면 좌표(중앙값)를 구한다. 벽이 없는 방향은 NaN.
        /// 인덱스: 0=+X(내면 min.x), 1=-X(내면 max.x), 2=+Z(내면 min.z), 3=-Z(내면 max.z).
        /// </summary>
        /// <param name="room">방 루트.</param>
        /// <param name="rb">방 바운즈.</param>
        /// <returns>4방향 내벽 면 좌표.</returns>
        private static float[] ComputeInnerWallFaces(GameObject room, Bounds rb)
        {
            List<float> xp = new List<float>(), xn = new List<float>(), zp = new List<float>(), zn = new List<float>();
            foreach (Renderer r in room.GetComponentsInChildren<Renderer>())
            {
                Bounds b = r.bounds;
                if (b.size.y < 2.5f) continue; // 벽/기둥만(가구·바닥 제외)
                if (b.size.x < 2.5f)
                {
                    if (b.center.x > rb.center.x + rb.size.x * 0.25f) xp.Add(b.min.x);
                    else if (b.center.x < rb.center.x - rb.size.x * 0.25f) xn.Add(b.max.x);
                }
                if (b.size.z < 2.5f)
                {
                    if (b.center.z > rb.center.z + rb.size.z * 0.25f) zp.Add(b.min.z);
                    else if (b.center.z < rb.center.z - rb.size.z * 0.25f) zn.Add(b.max.z);
                }
            }
            return new[] { Median(xp), Median(xn), Median(zp), Median(zn) };
        }

        /// <summary>리스트의 중앙값을 구한다(비어있으면 NaN).</summary>
        /// <param name="v">값 목록.</param>
        /// <returns>중앙값.</returns>
        private static float Median(List<float> v)
        {
            if (v.Count == 0) return float.NaN;
            v.Sort();
            return v[v.Count / 2];
        }

        // ── 개구부 감지 ────────────────────────────────────────────

        /// <summary>
        /// 방의 개구부(통로) 중심 XZ 목록을 감지한다.
        /// ① 이름 기반: door/doorway/archway/entrance 오브젝트 위치.
        /// ② 틈 기반: 방 둘레 중간 높이를 샘플링해 벽 렌더러가 없는 구간을 클러스터링.
        /// </summary>
        /// <param name="room">방 루트.</param>
        /// <param name="rb">방 월드 바운즈.</param>
        /// <returns>개구부 중심 XZ 목록(1.5m 내 중복 병합).</returns>
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

        /// <summary>개구부 후보 지점을 기존 클러스터(1.5m 내)에 병합하거나 새로 추가한다.</summary>
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

        /// <summary>씬에서 기본 이름(+" (n)" 접미사) 방 인스턴스 루트를 찾는다. 생성물/템플릿 하위는 제외.</summary>
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
                if (t.name == generatedRootName || t.name.StartsWith("TPL_")) return;
                if (pattern.IsMatch(t.name)) { result.Add(t); return; }
                foreach (Transform c in t) rec(c);
            };
            foreach (GameObject root in scene.GetRootGameObjects()) rec(root.transform);
            return result;
        }

        /// <summary>지정 씬에서 이름 일치 오브젝트를 재귀 탐색한다.</summary>
        /// <param name="scene">대상 씬.</param>
        /// <param name="name">찾을 이름.</param>
        /// <returns>찾은 오브젝트(없으면 null).</returns>
        private static GameObject FindInScene(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                GameObject hit = SearchRecursive(root.transform, name);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>트랜스폼 하위에서 이름 일치 노드를 재귀 탐색한다.</summary>
        /// <param name="t">현재 노드.</param>
        /// <param name="name">찾을 이름.</param>
        /// <returns>찾은 오브젝트(없으면 null).</returns>
        private static GameObject SearchRecursive(Transform t, string name)
        {
            if (t.name == name) return t.gameObject;
            foreach (Transform c in t)
            {
                GameObject hit = SearchRecursive(c, name);
                if (hit != null) return hit;
            }
            return null;
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
