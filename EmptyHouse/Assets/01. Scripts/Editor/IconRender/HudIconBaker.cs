using System.Collections.Generic;
using System.IO;
using Border.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EmptyHouse.EditorTools
{
    /// <summary>
    /// HUD 슬롯 아이콘 일괄 베이크 툴(에디터 전용 프리렌더 — 런타임 렌더 금지 결정의 구현체).
    /// ItemDataSO 전수 스캔 + 설정 SO 의 ExtraTargets 를 대상으로 리그 프리팹을 임시 인스턴스화해
    /// 각 WorldPrefab 을 투명 배경 PNG 로 굽고, Sprite 로 임포트해 Icon 필드에 자동 할당한다.
    /// 결정론 보장: 앰비언트/포그/스카이박스 강제, 씬 라이트 임시 소등, 인스턴스 프로브 차단.
    /// </summary>
    public static class HudIconBaker
    {
        private const string layerName = "IconRender"; // 리그와 공유하는 격리 레이어 이름

        /// <summary>
        /// 전체 대상을 한 번에 굽는다. 메뉴 진입점 — 어떤 씬이 열려 있어도 결과가 동일하다.
        /// </summary>
        [MenuItem("Tools/Icon Render/Bake All")]
        private static void BakeAll()
        {
            IconRenderSettingsSO settings = LoadSettings();
            if (settings == null) return;

            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0)
            {
                Log.D($"[IconBake] '{layerName}' 레이어가 없습니다. Tools/Icon Render/Create Rig & Scene 를 먼저 실행하세요.");
                return;
            }

            Dictionary<GameObject, List<ItemDataSO>> targets = CollectTargets(settings);
            if (targets.Count == 0)
            {
                Log.D("[IconBake] 베이크 대상이 없습니다.");
                return;
            }

            Directory.CreateDirectory(settings.OutputFolder);

            // 결정론: 열린 씬의 조명 환경을 통째로 밀어두고 시작한다. finally 에서 원복
            AmbientMode prevAmbientMode = RenderSettings.ambientMode;
            Color prevAmbientLight = RenderSettings.ambientLight;
            bool prevFog = RenderSettings.fog;
            Material prevSkybox = RenderSettings.skybox;
            DefaultReflectionMode prevReflectionMode = RenderSettings.defaultReflectionMode;
            float prevReflectionIntensity = RenderSettings.reflectionIntensity;

            // 미리보기 잔여물 차단: 씬에 남은 IconRender 레이어 오브젝트(지우지 않은 미리보기 인스턴스,
            // 리그 씬의 리그 포함)를 임시 비활성 — 안 하면 원점에 겹쳐 모든 아이콘에 찍혀 들어간다
            var sweptObjects = new List<GameObject>();
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t.gameObject.layer != layer || !t.gameObject.activeSelf) continue;
                t.gameObject.SetActive(false);
                sweptObjects.Add(t.gameObject);
            }

            var disabledLights = new List<Light>();
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (!light.enabled) continue;
                light.enabled = false;
                disabledLights.Add(light);
            }

            GameObject rig = null;
            var baked = new List<(string file, List<ItemDataSO> owners)>();
            try
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = settings.AmbientColor;
                RenderSettings.fog = false;
                RenderSettings.skybox = null;
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.reflectionIntensity = 0f;

                rig = (GameObject)PrefabUtility.InstantiatePrefab(settings.RigPrefab);
                Camera cam = rig.GetComponentInChildren<Camera>();

                foreach (KeyValuePair<GameObject, List<ItemDataSO>> target in targets)
                {
                    string file = BakeOne(cam, target.Key, settings, layer);
                    if (file != null) baked.Add((file, target.Value));
                }
            }
            finally
            {
                if (rig != null) Object.DestroyImmediate(rig);
                foreach (Light light in disabledLights) light.enabled = true;
                foreach (GameObject swept in sweptObjects) swept.SetActive(true);

                RenderSettings.ambientMode = prevAmbientMode;
                RenderSettings.ambientLight = prevAmbientLight;
                RenderSettings.fog = prevFog;
                RenderSettings.skybox = prevSkybox;
                RenderSettings.defaultReflectionMode = prevReflectionMode;
                RenderSettings.reflectionIntensity = prevReflectionIntensity;
            }

            foreach ((string file, List<ItemDataSO> owners) in baked)
                ImportAndAssign(file, owners, settings);

            AssetDatabase.SaveAssets();
            Log.D($"[IconBake] 완료 — {baked.Count}개 아이콘 → {settings.OutputFolder}");
        }

        /// <summary>
        /// 설정 SO 를 프로젝트에서 찾아 검증한다. 없거나 리그 미지정이면 안내 로그 후 null.
        /// </summary>
        /// <returns>유효한 설정 SO. 실패 시 null.</returns>
        private static IconRenderSettingsSO LoadSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:IconRenderSettingsSO");
            if (guids.Length == 0)
            {
                Log.D("[IconBake] 설정 SO 가 없습니다. Tools/Icon Render/Create Rig & Scene 를 먼저 실행하세요.");
                return null;
            }

            var settings = AssetDatabase.LoadAssetAtPath<IconRenderSettingsSO>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (settings.RigPrefab == null)
            {
                Log.D("[IconBake] 설정 SO 에 리그 프리팹이 비어 있습니다.");
                return null;
            }
            return settings;
        }

        /// <summary>
        /// 베이크 대상을 수집한다: ItemDataSO 전수 스캔(WorldPrefab 기준, 중복 프리팹은 1회만 렌더해
        /// 소유 SO 전부에 할당) + ExtraTargets. WorldPrefab 이 빈 SO 는 로그를 남기고 건너뛴다.
        /// </summary>
        /// <returns>프리팹 → 그 프리팹을 쓰는 ItemDataSO 목록(ExtraTargets 는 빈 목록).</returns>
        private static Dictionary<GameObject, List<ItemDataSO>> CollectTargets(IconRenderSettingsSO settings)
        {
            var targets = new Dictionary<GameObject, List<ItemDataSO>>();

            foreach (string guid in AssetDatabase.FindAssets("t:ItemDataSO"))
            {
                var so = AssetDatabase.LoadAssetAtPath<ItemDataSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (so.WorldPrefab == null)
                {
                    Log.D($"[IconBake] 스킵(WorldPrefab 없음): {so.name}");
                    continue;
                }

                if (!targets.TryGetValue(so.WorldPrefab, out List<ItemDataSO> owners))
                    targets[so.WorldPrefab] = owners = new List<ItemDataSO>();
                owners.Add(so);
            }

            foreach (GameObject extra in settings.ExtraTargets)
            {
                if (extra != null && !targets.ContainsKey(extra))
                    targets[extra] = new List<ItemDataSO>();
            }

            return targets;
        }

        /// <summary>
        /// 프리팹 하나를 렌더해 PNG 로 저장한다: 인스턴스화 → 레이어 통일·프로브 차단 → Bounds 프레이밍
        /// → 슈퍼샘플 렌더 → 절반씩 다운스케일 → ReadPixels → PNG. Renderer 가 없으면 스킵.
        /// </summary>
        /// <param name="cam">리그 카메라.</param>
        /// <param name="prefab">렌더할 픽업 프리팹.</param>
        /// <param name="settings">베이크 파라미터.</param>
        /// <param name="layer">IconRender 레이어 인덱스.</param>
        /// <returns>저장된 에셋 경로. 스킵 시 null.</returns>
        private static string BakeOne(Camera cam, GameObject prefab, IconRenderSettingsSO settings, int layer)
        {
            float aspect = ResolveAspect(settings, prefab);
            int renderW = Mathf.RoundToInt(settings.RenderSize * aspect);
            int renderH = settings.RenderSize;
            int finalW = Mathf.RoundToInt(settings.FinalSize * aspect);
            int finalH = settings.FinalSize;

            GameObject go = Object.Instantiate(prefab, Vector3.zero, ResolveRotation(settings, prefab));
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;

            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Log.D($"[IconBake] 스킵(Renderer 없음): {prefab.name}");
                Object.DestroyImmediate(go);
                return null;
            }

            foreach (Renderer r in renderers)
            {
                r.lightProbeUsage = LightProbeUsage.Off; // 열린 씬의 프로브가 섞이면 결정론이 깨진다
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }

            FrameCamera(cam, CalculateBounds(renderers), settings.Padding);

            RenderTexture rt = RenderTexture.GetTemporary(renderW, renderH, 24,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderCamera(cam, rt);

            RenderTexture final = Downscale(rt, finalW, finalH);
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = final;
            var tex = new Texture2D(finalW, finalH, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0f, 0f, finalW, finalH), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;

            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            if (final != rt) RenderTexture.ReleaseTemporary(final);
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(go);

            string file = $"{settings.OutputFolder}/{settings.FileNamePrefix}{prefab.name.ToLowerInvariant()}.png";
            File.WriteAllBytes(file, png);
            return file;
        }

        /// <summary>
        /// 씬에서 선택한 오브젝트를 아이콘 미리보기 상태로 만든다: 자식 포함 IconRender 레이어 전환
        /// + 열린 씬의 리그 카메라를 베이크와 동일한 수학으로 배치. Game 뷰가 곧 아이콘 미리보기가 된다.
        /// 각도가 정해지면 Transform Rotation 값을 설정 SO 의 RotationOverrides 에 옮겨 적고 인스턴스를 지운다.
        /// </summary>
        [MenuItem("Tools/Icon Render/Frame Selected (Preview)")]
        private static void FrameSelected()
        {
            IconRenderSettingsSO settings = LoadSettings();
            if (settings == null) return;

            int layer = LayerMask.NameToLayer(layerName);
            GameObject target = Selection.activeGameObject;
            if (target == null || !target.scene.IsValid())
            {
                Log.D("[IconBake] 씬에 놓인 프리팹 인스턴스를 선택한 뒤 실행하세요.");
                return;
            }

            GameObject rig = GameObject.Find("IconRenderRig");
            Camera cam = rig != null ? rig.GetComponentInChildren<Camera>() : null;
            if (cam == null)
            {
                Log.D("[IconBake] 열린 씬에서 리그 카메라를 찾지 못했습니다. IconRender 씬을 여세요.");
                return;
            }

            foreach (Transform t in target.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Log.D($"[IconBake] 선택한 오브젝트에 Renderer 가 없습니다: {target.name}");
                return;
            }

            FrameCamera(cam, CalculateBounds(renderers), settings.Padding);
            Log.D($"[IconBake] 미리보기 프레이밍 완료 — {target.name} 현재 회전: {target.transform.rotation.eulerAngles}");
        }

        /// <summary>
        /// Renderer 집합의 합산 Bounds 를 구한다.
        /// </summary>
        /// <returns>전체를 감싸는 월드 Bounds.</returns>
        private static Bounds CalculateBounds(Renderer[] renderers)
        {
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer r in renderers)
                bounds.Encapsulate(r.bounds);
            return bounds;
        }

        /// <summary>
        /// 외접구가 세로 FOV 를 padding 비율로 채우도록 카메라 위치·클립 평면을 잡는다.
        /// 회전은 건드리지 않는다 — 시점 각도는 리그 고정(프랍별 카메라 분기 금지). 베이크와 미리보기가 공유한다.
        /// </summary>
        private static void FrameCamera(Camera cam, Bounds bounds, float padding)
        {
            float radius = Mathf.Max(bounds.extents.magnitude, 0.001f);
            float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float distance = radius / (Mathf.Tan(halfFov) * padding);
            cam.transform.position = bounds.center - cam.transform.forward * distance;
            cam.nearClipPlane = Mathf.Max(0.01f, distance - radius * 2f);
            cam.farClipPlane = distance + radius * 2f;
        }

        /// <summary>
        /// 프리팹의 아이콘 방향 회전을 설정 SO 의 오버라이드 목록에서 찾는다. 없으면 무회전.
        /// 회전은 Bounds 프레이밍 계산 전에 적용되므로 어떤 값이든 점유율은 유지된다.
        /// </summary>
        /// <returns>인스턴스에 적용할 월드 회전.</returns>
        private static Quaternion ResolveRotation(IconRenderSettingsSO settings, GameObject prefab)
        {
            foreach (IconRenderSettingsSO.RotationOverride o in settings.RotationOverrides)
            {
                if (o.Prefab == prefab) return Quaternion.Euler(o.Euler);
            }
            return Quaternion.identity;
        }

        /// <summary>
        /// 프리팹의 렌더 종횡비를 설정 SO 의 오버라이드 목록에서 찾는다. 없으면 1(정사각).
        /// </summary>
        /// <returns>가로/세로 비.</returns>
        private static float ResolveAspect(IconRenderSettingsSO settings, GameObject prefab)
        {
            foreach (IconRenderSettingsSO.AspectOverride o in settings.AspectOverrides)
            {
                if (o.Prefab == prefab) return Mathf.Max(0.1f, o.Aspect);
            }
            return 1f;
        }

        /// <summary>
        /// URP 단일 카메라 렌더 요청으로 destination 에 렌더한다. 미지원 환경이면 Camera.Render() 폴백.
        /// </summary>
        private static void RenderCamera(Camera cam, RenderTexture destination)
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = destination };
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                RenderPipeline.SubmitRenderRequest(cam, request);
                return;
            }

            RenderTexture prev = cam.targetTexture;
            cam.targetTexture = destination;
            cam.Render();
            cam.targetTexture = prev;
        }

        /// <summary>
        /// src 를 목표 해상도까지 절반씩 단계적으로 블릿해 다운스케일한다. 한 번에 줄이면 샘플이
        /// 픽셀을 건너뛰어 계단이 남는다. 이미 목표 크기면 src 그대로 반환.
        /// </summary>
        /// <returns>목표 해상도의 임시 RT(src 와 다르면 호출부가 해제).</returns>
        private static RenderTexture Downscale(RenderTexture src, int targetW, int targetH)
        {
            RenderTexture current = src;
            while (current.width > targetW)
            {
                int w = Mathf.Max(targetW, current.width / 2);
                int h = Mathf.Max(targetH, current.height / 2);
                RenderTexture next = RenderTexture.GetTemporary(w, h, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(current, next);
                if (current != src) RenderTexture.ReleaseTemporary(current);
                current = next;
            }
            return current;
        }

        /// <summary>
        /// PNG 를 Sprite 로 임포트(Alpha Is Transparency 로 알파 프린지 억제, 밉맵 off)하고,
        /// 설정이 켜져 있으면 소유 ItemDataSO 전부의 Icon 에 할당한다.
        /// </summary>
        /// <param name="file">임포트할 에셋 경로.</param>
        /// <param name="owners">이 아이콘을 쓰는 SO 목록(ExtraTargets 는 빈 목록).</param>
        /// <param name="settings">베이크 파라미터.</param>
        private static void ImportAndAssign(string file, List<ItemDataSO> owners, IconRenderSettingsSO settings)
        {
            AssetDatabase.ImportAsset(file);

            var importer = (TextureImporter)AssetImporter.GetAtPath(file);
            if (importer.textureType != TextureImporterType.Sprite ||
                importer.spriteImportMode != SpriteImportMode.Single || // 프로젝트 기본 프리셋이 Multiple 이라 명시 필수 — Multiple 은 슬라이스가 없으면 Sprite 서브에셋이 안 생긴다
                !importer.alphaIsTransparency || importer.mipmapEnabled)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            if (!settings.AssignIcons) return;

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(file);
            foreach (ItemDataSO so in owners)
            {
                so.Icon = sprite;
                EditorUtility.SetDirty(so);
            }
        }
    }
}
