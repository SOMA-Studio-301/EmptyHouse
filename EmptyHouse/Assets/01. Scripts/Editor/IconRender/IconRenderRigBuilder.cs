using System.IO;
using Border.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace EmptyHouse.EditorTools
{
    /// <summary>
    /// 아이콘 렌더 리그(카메라 + 키/필/림 라이트) 프리팹, 튜닝용 씬, 설정 SO 를 일괄 생성하는 셋업 툴.
    /// 리그 프리팹과 설정 SO 는 이미 있으면 건너뛴다(수동 튜닝 보존). 씬은 항상 리그 인스턴스 +
    /// 고정 앰비언트 구성으로 재생성한다. 조명은 카메라의 자식 — 프레이밍 거리와 무관하게 빛 각도 고정.
    /// </summary>
    public static class IconRenderRigBuilder
    {
        private const string layerName = "IconRender"; // 격리 레이어. 카메라 컬링·라이트 컬링을 이 레이어로 제한
        private const string rigPrefabPath = "Assets/02. Prefab/Util/IconRenderRig.prefab"; // 리그 프리팹 경로
        private const string scenePath = "Assets/00. Scenes/IconRender.unity"; // 리그 튜닝·검증용 씬 경로
        private const string settingsFolder = "Assets/03. ScriptableObjects/IconRender"; // 설정 SO 폴더
        private const string settingsPath = settingsFolder + "/SO_IconRenderSettings.asset"; // 설정 SO 경로
        private const string radioPrefabPath = "Assets/02. Prefab/Interaction/Radio.prefab"; // ExtraTargets 기본 등록 대상(ItemDataSO 없음)

        /// <summary>
        /// 리그 프리팹 → 설정 SO → 튜닝용 씬 순으로 생성한다. 메뉴 진입점.
        /// </summary>
        [MenuItem("Tools/Icon Render/Create Rig & Scene")]
        private static void CreateAll()
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0)
            {
                Log.D($"[IconRig] '{layerName}' 레이어가 없습니다. Tags and Layers 에 먼저 추가하세요.");
                return;
            }

            GameObject rigPrefab = EnsureRigPrefab(layer);
            IconRenderSettingsSO settings = EnsureSettings(rigPrefab);
            RebuildScene(rigPrefab, settings.AmbientColor);

            AssetDatabase.SaveAssets();
            Log.D($"[IconRig] 셋업 완료 — 리그: {rigPrefabPath}, 씬: {scenePath}, 설정: {settingsPath}");
        }

        /// <summary>
        /// 리그 프리팹이 없으면 새로 만들어 저장하고, 있으면 그대로 반환한다(튜닝 보존).
        /// </summary>
        /// <returns>리그 프리팹 에셋.</returns>
        private static GameObject EnsureRigPrefab(int layer)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(rigPrefabPath);
            if (existing != null)
            {
                Log.D("[IconRig] 리그 프리팹이 이미 있어 생성을 건너뜁니다.");
                return existing;
            }

            GameObject root = BuildRig(layer);
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(root, rigPrefabPath);
            Object.DestroyImmediate(root);
            return asset;
        }

        /// <summary>
        /// 리그 계층을 구성한다: 루트 → 카메라(투명 배경·IconRender 컬링·FOV 25·포스트 off)
        /// → 자식 디렉셔널 3등(키/필/림, 그림자 off, 라이트 컬링도 IconRender 로 제한).
        /// </summary>
        /// <returns>씬에 임시 생성된 리그 루트.</returns>
        private static GameObject BuildRig(int layer)
        {
            var root = new GameObject("IconRenderRig") { layer = layer };

            var camGo = new GameObject("RenderCamera") { layer = layer };
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.localRotation = Quaternion.Euler(20f, -30f, 0f); // 3/4 시점. 아이템은 원점에 무회전 배치되므로 각도는 카메라가 만든다

            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.clear; // 배경 알파 0 — 아이콘 투명 배경의 원천
            cam.cullingMask = 1 << layer;
            cam.fieldOfView = 25f; // 직교 대신 저왜곡 원근(문서 4-2). 바꾸면 전 아이콘 재베이크
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;

            UniversalAdditionalCameraData camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = false; // URP 포스트가 알파 채널을 파괴한다(문서 함정 ②)
            camData.renderShadows = false;
            camData.antialiasing = AntialiasingMode.None; // 품질은 슈퍼샘플 다운스케일이 담당

            CreateLight(camGo.transform, "KeyLight", new Vector3(20f, -25f, 0f), 1.2f, layer);
            CreateLight(camGo.transform, "FillLight", new Vector3(10f, 40f, 0f), 0.4f, layer);
            CreateLight(camGo.transform, "RimLight", new Vector3(-20f, 180f, 0f), 1.5f, layer); // 뒤에서 윤곽을 잡는다 — 어두운 HUD 가독성 요구사항(함정 ③)

            return root;
        }

        /// <summary>
        /// 디렉셔널 라이트 하나를 카메라 자식으로 만든다. 그림자 off(알파 실루엣 오염 방지),
        /// 라이트 컬링을 IconRender 로 제한해 베이크 중 열린 씬을 비추지 않게 한다.
        /// </summary>
        private static void CreateLight(Transform parent, string name, Vector3 localEuler, float intensity, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(localEuler);

            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.None;
            light.intensity = intensity;
            light.cullingMask = 1 << layer;
        }

        /// <summary>
        /// 설정 SO 가 없으면 만들고(무전기 프리팹을 ExtraTargets 에 기본 등록), 있으면 리그 참조만 보정한다.
        /// </summary>
        /// <returns>설정 SO 에셋.</returns>
        private static IconRenderSettingsSO EnsureSettings(GameObject rigPrefab)
        {
            var existing = AssetDatabase.LoadAssetAtPath<IconRenderSettingsSO>(settingsPath);
            if (existing != null)
            {
                if (existing.RigPrefab == null)
                {
                    existing.RigPrefab = rigPrefab;
                    EditorUtility.SetDirty(existing);
                }
                return existing;
            }

            Directory.CreateDirectory(settingsFolder);
            AssetDatabase.Refresh();

            var settings = ScriptableObject.CreateInstance<IconRenderSettingsSO>();
            settings.RigPrefab = rigPrefab;

            var radio = AssetDatabase.LoadAssetAtPath<GameObject>(radioPrefabPath);
            if (radio != null) settings.ExtraTargets.Add(radio);
            else Log.D($"[IconRig] 무전기 프리팹을 찾지 못해 ExtraTargets 에 등록하지 못했습니다: {radioPrefabPath}");

            AssetDatabase.CreateAsset(settings, settingsPath);
            return settings;
        }

        /// <summary>
        /// 튜닝용 씬을 리그 인스턴스 + 고정 앰비언트(Flat)·포그 off·스카이박스 없음 구성으로 재생성한다.
        /// 씬이 에디터에 열려 있으면 그 자리에서 재구성하고, 아니면 임시로 열어 저장 후 닫는다.
        /// </summary>
        private static void RebuildScene(GameObject rigPrefab, Color ambientColor)
        {
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool wasOpen = scene.IsValid() && scene.isLoaded;
            if (!wasOpen) scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            foreach (GameObject go in scene.GetRootGameObjects())
                Object.DestroyImmediate(go);

            Scene prevActive = SceneManager.GetActiveScene();
            bool sameAsActive = prevActive == scene;
            SceneManager.SetActiveScene(scene);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambientColor;
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 0f;

            PrefabUtility.InstantiatePrefab(rigPrefab, scene);

            if (!sameAsActive) SceneManager.SetActiveScene(prevActive);
            EditorSceneManager.SaveScene(scene, scenePath);
            if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
        }
    }
}
