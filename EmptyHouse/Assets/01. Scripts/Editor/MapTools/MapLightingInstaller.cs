using System.Collections.Generic;
using Border.Core;
using EmptyHouse.Environment;
using UnityEditor;
using UnityEngine;

namespace EmptyHouse.EditorTools
{
    /// <summary>
    /// 방/복도 프리팹 안에 쿨톤 공포 조명(픽스처 + Point Light)을 설치하는 툴.
    /// 프리팹당 "Lighting_Auto" 루트 아래에 생성하므로 재실행 시 교체되고(멱등),
    /// 그 루트만 지우면 원복된다. 프리팹 1회 수정으로 모든 인스턴스에 반영된다.
    /// 색은 테마별(병원=병약한 청록, 일반=차가운 블루), 그림자는 끔(성능).
    /// </summary>
    public static class MapLightingInstaller
    {
        private const string lightRootName = "Lighting_Auto";
        private const string bulbPrefabPath = "Assets/Horror_Pack_1/!Prefabs/Lights/Light_Bulb.prefab";
        private const float fixtureDrop = 0.35f; // 천장면에서 픽스처 상단까지 내리는 간격

        private static readonly Color sickGreen = new Color(0.55f, 1.00f, 0.72f); // 병약한 청록
        private static readonly Color coldBlue = new Color(0.55f, 0.72f, 1.00f);  // 차가운 블루
        private static readonly Color hallBlue = new Color(0.42f, 0.58f, 0.95f);  // 복도용 어두운 블루

        /// <summary>프리팹 하나의 조명 설치 스펙.</summary>
        private struct LightSpec
        {
            public string prefabPath;   // 대상 프리팹
            public Color color;         // 라이트 색
            public float intensity;     // 강도
            public float range;         // 범위
            public int count;           // 픽스처 개수(실내 X축 균등 분할)
            public bool flicker;        // 깜빡임 여부
        }

        private const string dec = "Assets/02. Prefab/Map/DecoratedRooms/";

        /// <summary>설치 대상 프리팹과 테마별 파라미터.</summary>
        private static readonly LightSpec[] specs =
        {
            // 병원·화장실 계열: 병약한 청록 + 깜빡임
            new LightSpec { prefabPath = dec + "EmptyRoom-3x3__Hospital.prefab", color = sickGreen, intensity = 1.3f, range = 9f, count = 2, flicker = true },
            new LightSpec { prefabPath = dec + "EmptyRoom-5x5__Hospital.prefab", color = sickGreen, intensity = 1.3f, range = 10f, count = 2, flicker = true },
            new LightSpec { prefabPath = dec + "EmptyRoom-2x2__Toilet.prefab", color = sickGreen, intensity = 1.1f, range = 7f, count = 1, flicker = true },
            // 일반 방: 차가운 블루, 정적
            new LightSpec { prefabPath = dec + "EmptyRoom-3x3__Book.prefab", color = coldBlue, intensity = 1.1f, range = 9f, count = 2, flicker = false },
            new LightSpec { prefabPath = dec + "EmptyRoom-3x3__Book2.prefab", color = coldBlue, intensity = 1.1f, range = 9f, count = 2, flicker = false },
            new LightSpec { prefabPath = dec + "EmptyRoom-3x3__Warehouse.prefab", color = coldBlue, intensity = 1.0f, range = 9f, count = 2, flicker = false },
            new LightSpec { prefabPath = dec + "EmptyRoom-3x3__Bed.prefab", color = coldBlue, intensity = 1.0f, range = 9f, count = 2, flicker = false },
            new LightSpec { prefabPath = dec + "EmptyRoom-5x5__Book.prefab", color = coldBlue, intensity = 1.2f, range = 10f, count = 2, flicker = false },
            new LightSpec { prefabPath = dec + "EmptyRoom-6x8__A.prefab", color = coldBlue, intensity = 1.2f, range = 10f, count = 3, flicker = false },
            new LightSpec { prefabPath = dec + "Entrance-EmptyRoom-5x5.prefab", color = coldBlue, intensity = 1.2f, range = 10f, count = 2, flicker = false },
            // 복도: Hallway x2에만 드문드문, 어두운 블루 + 깜빡임
            new LightSpec { prefabPath = "Assets/02. Prefab/Map/Hallway x2.prefab", color = hallBlue, intensity = 0.85f, range = 7f, count = 1, flicker = true },
        };

        // ── 진입점 ─────────────────────────────────────────────────

        /// <summary>모든 대상 프리팹에 조명을 설치한다(기존 Lighting_Auto는 교체).</summary>
        public static void Install()
        {
            GameObject bulb = AssetDatabase.LoadAssetAtPath<GameObject>(bulbPrefabPath);
            if (bulb == null) { Log.D($"[Light] 픽스처 프리팹 없음: {bulbPrefabPath}"); return; }

            int ok = 0;
            foreach (LightSpec spec in specs)
                if (InstallOne(spec, bulb)) ok++;
            AssetDatabase.SaveAssets();
            Log.D($"[Light] 조명 설치 완료: {ok}/{specs.Length} 프리팹");
        }

        /// <summary>모든 대상 프리팹의 Lighting_Auto를 제거해 원복한다.</summary>
        public static void Uninstall()
        {
            int ok = 0;
            foreach (LightSpec spec in specs)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(spec.prefabPath);
                if (root == null) continue;
                Transform old = root.transform.Find(lightRootName);
                if (old != null)
                {
                    Object.DestroyImmediate(old.gameObject);
                    PrefabUtility.SaveAsPrefabAsset(root, spec.prefabPath);
                    ok++;
                }
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
            Log.D($"[Light] 조명 제거 완료: {ok}개 프리팹");
        }

        // ── 설치 본체 ──────────────────────────────────────────────

        /// <summary>
        /// 프리팹 하나에 조명을 설치한다: 내천장면을 계산해 픽스처를 부착하고
        /// Point Light(그림자 없음)를 구성한다. 실내 X축을 균등 분할해 배치한다.
        /// </summary>
        /// <param name="spec">설치 스펙.</param>
        /// <param name="bulb">픽스처(전구) 프리팹.</param>
        /// <returns>설치 성공 여부.</returns>
        private static bool InstallOne(LightSpec spec, GameObject bulb)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(spec.prefabPath);
            if (root == null) { Log.D($"[Light] 프리팹 로드 실패: {spec.prefabPath}"); return false; }

            try
            {
                Renderer[] rends = root.GetComponentsInChildren<Renderer>(true);
                if (rends.Length == 0) { Log.D($"[Light] 렌더러 없음: {spec.prefabPath}"); return false; }
                Bounds rb = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) rb.Encapsulate(rends[i].bounds);

                float ceilY = ComputeInnerCeilingY(rends, rb);

                Transform old = root.transform.Find(lightRootName);
                if (old != null) Object.DestroyImmediate(old.gameObject);
                Transform lightRoot = new GameObject(lightRootName).transform;
                lightRoot.SetParent(root.transform, false);

                for (int i = 0; i < spec.count; i++)
                {
                    // 실내 X축 균등 분할(벽에서 안쪽으로 여유), Z는 중앙
                    float t = (i + 1f) / (spec.count + 1f);
                    float x = Mathf.Lerp(rb.min.x + 1.2f, rb.max.x - 1.2f, t);
                    Vector3 pos = new Vector3(x, ceilY, rb.center.z);

                    GameObject fixture = (GameObject)PrefabUtility.InstantiatePrefab(bulb);
                    fixture.transform.SetParent(lightRoot, false);
                    fixture.transform.position = pos;
                    // 픽스처 상단을 천장면에 맞춘 뒤 살짝 내림
                    Renderer[] fr = fixture.GetComponentsInChildren<Renderer>(true);
                    if (fr.Length > 0)
                    {
                        Bounds fb = fr[0].bounds;
                        for (int k = 1; k < fr.Length; k++) fb.Encapsulate(fr[k].bounds);
                        fixture.transform.position += new Vector3(0f, (ceilY - fixtureDrop) - fb.max.y, 0f);
                    }

                    GameObject lightGo = new GameObject("PointLight");
                    lightGo.transform.SetParent(fixture.transform, false);
                    lightGo.transform.position = fixture.transform.position + new Vector3(0f, -0.25f, 0f);
                    Light li = lightGo.AddComponent<Light>();
                    li.type = LightType.Point;
                    li.color = spec.color;
                    li.intensity = spec.intensity;
                    li.range = spec.range;
                    li.shadows = LightShadows.None;
                    if (spec.flicker) lightGo.AddComponent<FlickeringLight>();
                }

                PrefabUtility.SaveAsPrefabAsset(root, spec.prefabPath);
                Log.D($"[Light] {System.IO.Path.GetFileNameWithoutExtension(spec.prefabPath)}: 조명 {spec.count}개 (천장 {ceilY:F1})");
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// 내천장면 높이(상부 얇은 슬래브 밑면들의 중앙값)를 구한다. 못 찾으면 상단-0.3.
        /// </summary>
        /// <param name="rends">프리팹 렌더러들.</param>
        /// <param name="rb">전체 바운즈.</param>
        /// <returns>내천장면 Y.</returns>
        private static float ComputeInnerCeilingY(Renderer[] rends, Bounds rb)
        {
            List<float> ys = new List<float>();
            foreach (Renderer r in rends)
            {
                Bounds b = r.bounds;
                if (b.size.y > 1.5f) continue;
                if (b.center.y > rb.min.y + rb.size.y * 0.7f) ys.Add(b.min.y);
            }
            if (ys.Count == 0) return rb.max.y - 0.3f;
            ys.Sort();
            return ys[ys.Count / 2];
        }
    }
}
