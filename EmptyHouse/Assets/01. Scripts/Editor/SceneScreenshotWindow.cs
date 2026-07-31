using System;
using System.IO;
using Border.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EmptyHouse.EditorTools
{
    /// <summary>
    /// 씬/게임 캡처 통합 툴.
    /// - 카메라 렌더 캡처: Game View와 무관하게 임의 해상도로 카메라를 렌더해 저장(Steam 그래픽 자산용). 슈퍼샘플 지원.
    /// - Game View 캡처: 화면에 보이는 그대로(UI 포함) 저장(인게임 스크린샷용).
    /// 결과는 프로젝트 루트의 Screenshots/ 폴더에 PNG로 저장된다.
    /// </summary>
    public class SceneScreenshotWindow : EditorWindow
    {
        private static readonly string[] presetNames =
        {
            "직접 입력",
            "아이콘∕로고/아이콘 (120x120)",
            "스토어/Main Capsule (1232x706)",
            "스토어/Small Capsule (464x174)",
            "스토어/Header Capsule (920x430)",
            "스토어/Header Capsule Bundle (1414x464)",
            "스토어/Vertical Capsule (748x896)",
            "스토어/배경화면 (1438x810)",
            "스토어/Broadcast 좌·우 (199x433)",
            "이벤트/Event Cover (800x450)",
            "이벤트/Event Header (1920x622)",
            "라이브러리/Library Capsule (600x900)",
            "라이브러리/Library Header (920x430)",
            "라이브러리/Library Logo (1280x720)",
            "라이브러리/Library Hero (3840x1240)",
            "스크린샷/FHD (1920x1080)",
            "스크린샷/4K (3840x2160)",
        };
        private static readonly Vector2Int[] presetSizes =
        {
            Vector2Int.zero,
            new Vector2Int(120, 120),
            new Vector2Int(1232, 706),
            new Vector2Int(464, 174),
            new Vector2Int(920, 430),
            new Vector2Int(1414, 464),
            new Vector2Int(748, 896),
            new Vector2Int(1438, 810),
            new Vector2Int(199, 433),
            new Vector2Int(800, 450),
            new Vector2Int(1920, 622),
            new Vector2Int(600, 900),
            new Vector2Int(920, 430),
            new Vector2Int(1280, 720),
            new Vector2Int(3840, 1240),
            new Vector2Int(1920, 1080),
            new Vector2Int(3840, 2160),
        };
        private static readonly int[] supersampleValues = { 1, 2, 4 };
        private static readonly string[] supersampleNames = { "1x", "2x", "4x" };

        [SerializeField] private Camera targetCamera;
        [SerializeField] private int presetIndex = 15;
        [SerializeField] private int width = 1920;
        [SerializeField] private int height = 1080;
        [SerializeField] private int supersampleIndex = 1;
        [SerializeField] private int gameViewSuperSize = 1;
        [SerializeField] private string lastSavedPath;

        [SerializeField] private bool dofEnabled;
        [SerializeField] private Transform focusTarget;
        [SerializeField] private float aperture = 4f;
        [SerializeField] private float focalLength = 50f;
        [SerializeField] private bool dofPreview;

        private const string previewGoName = "__ScreenshotDoFPreview";

        private GameObject previewVolumeGo;
        private VolumeProfile dofProfile;
        private DepthOfField dofOverride;

        /// <summary>메뉴에서 툴 창을 연다.</summary>
        [MenuItem("Tools/스크린샷 툴")]
        public static void Open()
        {
            GetWindow<SceneScreenshotWindow>("스크린샷 툴");
        }

        /// <summary>도메인 리로드로 남은 미리보기 볼륨을 정리하고, 미리보기 상태를 복원하며 업데이트 훅을 등록한다.</summary>
        private void OnEnable()
        {
            DestroyStalePreviewObjects();
            if (dofEnabled && dofPreview) EnsureDofVolume();
            EditorApplication.update += OnEditorUpdate;
        }

        /// <summary>업데이트 훅을 해제하고 미리보기 볼륨을 정리한다.</summary>
        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            CleanupDofVolume();
        }

        /// <summary>미리보기 중 초점 대상/카메라 이동을 따라 DoF 값을 갱신한다.</summary>
        private void OnEditorUpdate()
        {
            if (previewVolumeGo != null) UpdateDofValues(ResolveCamera());
        }

        /// <summary>툴 GUI를 그린다. 두 캡처 모드와 공통 저장 폴더 UI로 구성.</summary>
        private void OnGUI()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("카메라 렌더 캡처 (그래픽 자산용)", EditorStyles.boldLabel);
            targetCamera = (Camera)EditorGUILayout.ObjectField(
                new GUIContent("대상 카메라", "비우면 Main Camera를 사용한다."), targetCamera, typeof(Camera), true);

            int newPreset = EditorGUILayout.Popup("해상도 프리셋", presetIndex, presetNames);
            if (newPreset != presetIndex)
            {
                presetIndex = newPreset;
                if (presetIndex > 0)
                {
                    width = presetSizes[presetIndex].x;
                    height = presetSizes[presetIndex].y;
                }
            }

            EditorGUI.BeginChangeCheck();
            width = Mathf.Clamp(EditorGUILayout.IntField("가로", width), 16, 8192);
            height = Mathf.Clamp(EditorGUILayout.IntField("세로", height), 16, 8192);
            if (EditorGUI.EndChangeCheck()) presetIndex = 0;

            supersampleIndex = EditorGUILayout.Popup(
                new GUIContent("슈퍼샘플", "배율만큼 고해상도로 렌더한 뒤 다운스케일해 품질을 높인다."),
                supersampleIndex, supersampleNames);

            if (GUILayout.Button($"카메라 캡처 ({width}x{height})", GUILayout.Height(28f)))
                CaptureCamera();

            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("초점 흐림 (Depth of Field)", EditorStyles.boldLabel);
            dofEnabled = EditorGUILayout.Toggle(
                new GUIContent("DoF 적용", "캡처 시 초점 대상까지 거리에 초점을 맞추고 배경을 흐린다."), dofEnabled);
            if (dofEnabled)
            {
                focusTarget = (Transform)EditorGUILayout.ObjectField(
                    new GUIContent("초점 대상", "이 오브젝트에 초점을 맞춘다(예: 좀비)."), focusTarget, typeof(Transform), true);
                aperture = EditorGUILayout.Slider(
                    new GUIContent("조리개 (f값)", "낮을수록 배경이 많이 흐려진다."), aperture, 1f, 22f);
                focalLength = EditorGUILayout.Slider(
                    new GUIContent("초점거리 (mm)", "높을수록 흐림이 강해진다."), focalLength, 10f, 300f);

                bool newPreview = EditorGUILayout.Toggle(
                    new GUIContent("미리보기", "전역 볼륨을 임시 생성해 Game View에서 실시간 확인한다. Game View 캡처에도 반영됨."), dofPreview);
                if (newPreview != dofPreview)
                {
                    dofPreview = newPreview;
                    if (dofPreview) EnsureDofVolume();
                    else CleanupDofVolume();
                }

                if (focusTarget == null)
                    EditorGUILayout.HelpBox("초점 대상을 지정해야 DoF가 적용된다.", MessageType.Warning);
                else
                {
                    Camera previewCam = ResolveCamera();
                    if (dofPreview && previewCam != null && !previewCam.GetUniversalAdditionalCameraData().renderPostProcessing)
                        EditorGUILayout.HelpBox("카메라의 Post Processing이 꺼져 있어 미리보기가 표시되지 않는다. (카메라 캡처 시에는 자동 활성화됨)", MessageType.Warning);
                }
            }
            else if (dofPreview)
            {
                dofPreview = false;
                CleanupDofVolume();
            }

            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("Game View 캡처 (인게임 스크린샷용)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("화면에 보이는 그대로(오버레이 UI 포함) 저장한다. 해상도는 Game View 크기 × 배율. 플레이 모드에서 사용 권장.", MessageType.Info);
            gameViewSuperSize = EditorGUILayout.IntSlider("배율 (superSize)", gameViewSuperSize, 1, 4);

            if (GUILayout.Button("Game View 캡처", GUILayout.Height(28f)))
                CaptureGameView();

            EditorGUILayout.Space(12f);
            if (!string.IsNullOrEmpty(lastSavedPath))
                EditorGUILayout.LabelField("마지막 저장", lastSavedPath, EditorStyles.miniLabel);
            if (GUILayout.Button("저장 폴더 열기"))
                OpenSaveFolder();
        }

        /// <summary>현재 설정으로 카메라 렌더 캡처를 실행한다. DoF 설정 시 임시 볼륨을 적용한다.</summary>
        private void CaptureCamera()
        {
            Camera cam = ResolveCamera();
            if (cam == null)
            {
                Log.D("[Screenshot] 대상 카메라가 없습니다. 카메라를 지정하거나 씬에 Main Camera를 두세요.");
                return;
            }

            bool applyDof = dofEnabled && focusTarget != null;
            if (dofEnabled && focusTarget == null)
                Log.D("[Screenshot] 초점 대상이 없어 DoF 없이 캡처합니다.");
            bool tempVolume = applyDof && previewVolumeGo == null;
            if (applyDof)
            {
                if (tempVolume) EnsureDofVolume();
                UpdateDofValues(cam);
            }

            int ss = supersampleValues[Mathf.Clamp(supersampleIndex, 0, supersampleValues.Length - 1)];
            lastSavedPath = Capture(cam, width, height, ss, PresetLabel());

            if (applyDof && tempVolume) CleanupDofVolume();
        }

        /// <summary>
        /// 카메라를 지정 해상도로 렌더해 Screenshots 폴더에 PNG로 저장한다.
        /// 오버레이 카메라가 지정되면 베이스로 자동 전환하고, 베이스 렌더(PP 강제 활성화 후 원복) 뒤
        /// 스택의 활성 오버레이를 같은 RT에 덧그린다. 배치 캡처용으로 외부 호출 가능.
        /// </summary>
        /// <param name="cam">렌더할 카메라.</param>
        /// <param name="width">저장 가로 해상도.</param>
        /// <param name="height">저장 세로 해상도.</param>
        /// <param name="supersample">슈퍼샘플 배율(1 이상).</param>
        /// <param name="label">파일명 앞에 붙는 프리셋 라벨.</param>
        /// <returns>저장된 파일의 절대 경로.</returns>
        public static string Capture(Camera cam, int width, int height, int supersample, string label)
        {
            cam = ResolveBaseCamera(cam);

            UniversalAdditionalCameraData baseData = cam.GetUniversalAdditionalCameraData();
            bool prevPostProcessing = baseData.renderPostProcessing;
            baseData.renderPostProcessing = true;

            // URP는 targetTexture 포맷을 내부 버퍼로 상속하므로 HDR 포맷이어야 블룸·톤매핑이 유지된다
            RenderTexture full = RenderTexture.GetTemporary(width * supersample, height * supersample, 24, RenderTextureFormat.DefaultHDR);
            RenderCamera(cam, full);
            baseData.renderPostProcessing = prevPostProcessing;
            CompositeOverlays(baseData, full);

            RenderTexture final = Downscale(full, width, height);
            RenderTexture ldr = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32); // PNG 인코딩용 sRGB 변환 버퍼
            Graphics.Blit(final, ldr);
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = ldr;
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;

            byte[] png = tex.EncodeToPNG();
            DestroyImmediate(tex);
            RenderTexture.ReleaseTemporary(ldr);
            if (final != full) RenderTexture.ReleaseTemporary(final);
            RenderTexture.ReleaseTemporary(full);

            string path = Path.Combine(GetSaveFolder(), $"{label}_{width}x{height}_{DateTime.Now:yyMMdd_HHmmss}.png");
            File.WriteAllBytes(path, png);
            Log.D($"[Screenshot] 저장 완료: {path}");
            return path;
        }

        /// <summary>현재 프리셋에서 파일명용 라벨을 만든다(카테고리·해상도 표기·공백 제거). 직접 입력이면 "Asset".</summary>
        /// <returns>파일명 라벨.</returns>
        private string PresetLabel()
        {
            if (presetIndex <= 0 || presetIndex >= presetNames.Length) return "Asset";
            string name = presetNames[presetIndex];
            int slash = name.LastIndexOf('/');
            if (slash >= 0) name = name.Substring(slash + 1);
            int paren = name.IndexOf(" (", StringComparison.Ordinal);
            if (paren >= 0) name = name.Substring(0, paren);
            return name.Replace(" ", "");
        }

        /// <summary>대상 카메라를 반환한다. 지정이 없으면 Main Camera.</summary>
        /// <returns>사용할 카메라. 없으면 null.</returns>
        private Camera ResolveCamera()
        {
            return targetCamera != null ? targetCamera : Camera.main;
        }

        /// <summary>오버레이 카메라면 그 카메라를 스택에 포함한 베이스 카메라를 찾아 반환한다. 못 찾으면 입력 그대로.</summary>
        /// <param name="cam">기준 카메라.</param>
        /// <returns>캡처에 사용할 베이스 카메라.</returns>
        private static Camera ResolveBaseCamera(Camera cam)
        {
            if (cam.GetUniversalAdditionalCameraData().renderType == CameraRenderType.Base) return cam;
            foreach (Camera candidate in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                UniversalAdditionalCameraData data = candidate.GetUniversalAdditionalCameraData();
                if (data.renderType == CameraRenderType.Base && data.cameraStack != null && data.cameraStack.Contains(cam))
                    return candidate;
            }
            Log.D($"[Screenshot] {cam.name}은(는) 오버레이지만 베이스 카메라를 찾지 못해 그대로 캡처합니다.");
            return cam;
        }

        /// <summary>DoF용 임시 전역 볼륨(HideAndDontSave)을 생성한다. 이미 있으면 무시.</summary>
        private void EnsureDofVolume()
        {
            if (previewVolumeGo != null) return;
            previewVolumeGo = new GameObject(previewGoName) { hideFlags = HideFlags.HideAndDontSave };
            Volume volume = previewVolumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1000f;
            dofProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            dofProfile.hideFlags = HideFlags.HideAndDontSave;
            dofOverride = dofProfile.Add<DepthOfField>(true);
            dofOverride.mode.value = DepthOfFieldMode.Bokeh;
            volume.sharedProfile = dofProfile;
            UpdateDofValues(ResolveCamera());
        }

        /// <summary>카메라~초점 대상 거리와 조리개/초점거리 슬라이더 값을 DoF 오버라이드에 반영한다.</summary>
        /// <param name="cam">거리 계산 기준 카메라.</param>
        private void UpdateDofValues(Camera cam)
        {
            if (dofOverride == null || cam == null || focusTarget == null) return;
            dofOverride.focusDistance.value = Vector3.Distance(cam.transform.position, focusTarget.position);
            dofOverride.aperture.value = aperture;
            dofOverride.focalLength.value = focalLength;
        }

        /// <summary>임시 볼륨과 프로파일을 파괴하고 참조를 비운다.</summary>
        private void CleanupDofVolume()
        {
            if (previewVolumeGo != null) DestroyImmediate(previewVolumeGo);
            if (dofProfile != null) DestroyImmediate(dofProfile);
            previewVolumeGo = null;
            dofProfile = null;
            dofOverride = null;
        }

        /// <summary>도메인 리로드 등으로 참조를 잃고 남은 미리보기 볼륨 오브젝트를 이름으로 찾아 제거한다.</summary>
        private static void DestroyStalePreviewObjects()
        {
            Volume[] volumes = Resources.FindObjectsOfTypeAll<Volume>();
            foreach (Volume v in volumes)
            {
                if (v != null && v.gameObject != null && v.gameObject.name == previewGoName)
                    DestroyImmediate(v.gameObject);
            }
        }

        /// <summary>
        /// 렌더 요청(StandardRequest, 포스트프로세싱 포함 풀 스택)으로 카메라를 destination에 렌더한다.
        /// 미지원 환경이면 Camera.Render() 폴백을 사용한다.
        /// </summary>
        /// <param name="cam">렌더할 카메라.</param>
        /// <param name="destination">출력 RenderTexture.</param>
        private static void RenderCamera(Camera cam, RenderTexture destination)
        {
            RenderPipeline.StandardRequest request = new RenderPipeline.StandardRequest
            {
                destination = destination,
            };
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
        /// 렌더 요청 경로는 카메라 스택 합성이 RT에 반영되지 않으므로, 스택의 활성 오버레이 카메라를
        /// 같은 RT에 순서대로 덧그린다. PP 이중 적용을 막기 위해 오버레이의 PP는 끈 채 렌더한다.
        /// </summary>
        /// <param name="baseData">베이스 카메라의 URP 카메라 데이터.</param>
        /// <param name="destination">베이스 렌더 결과가 담긴 RenderTexture.</param>
        private static void CompositeOverlays(UniversalAdditionalCameraData baseData, RenderTexture destination)
        {
            if (baseData.renderType != CameraRenderType.Base) return;
            foreach (Camera overlay in baseData.cameraStack)
            {
                if (overlay == null || !overlay.isActiveAndEnabled) continue;
                UniversalAdditionalCameraData overlayData = overlay.GetUniversalAdditionalCameraData();
                bool prevPostProcessing = overlayData.renderPostProcessing;
                CameraRenderType prevRenderType = overlayData.renderType;
                overlayData.renderPostProcessing = false;
                overlayData.renderType = CameraRenderType.Base; // 렌더 요청 경로가 오버레이를 스킵하므로 렌더 동안만 Base로 위장
                UniversalRenderPipeline.SingleCameraRequest request = new UniversalRenderPipeline.SingleCameraRequest
                {
                    destination = destination,
                };
                if (RenderPipeline.SupportsRenderRequest(overlay, request))
                    RenderPipeline.SubmitRenderRequest(overlay, request);
                overlayData.renderType = prevRenderType;
                overlayData.renderPostProcessing = prevPostProcessing;
            }
        }

        /// <summary>src를 목표 해상도까지 절반씩 단계적으로 블릿해 다운스케일한다. 이미 목표 크기면 src를 그대로 반환.</summary>
        /// <param name="src">원본(슈퍼샘플) 텍스처.</param>
        /// <param name="targetW">목표 가로.</param>
        /// <param name="targetH">목표 세로.</param>
        /// <returns>목표 해상도의 임시 RenderTexture(src와 다르면 호출부가 해제).</returns>
        private static RenderTexture Downscale(RenderTexture src, int targetW, int targetH)
        {
            RenderTexture current = src;
            while (current.width > targetW)
            {
                int w = Mathf.Max(targetW, current.width / 2);
                int h = Mathf.Max(targetH, current.height / 2);
                RenderTexture next = RenderTexture.GetTemporary(w, h, 0, src.format); // 다운스케일 중 HDR 정밀도 유지
                Graphics.Blit(current, next);
                if (current != src) RenderTexture.ReleaseTemporary(current);
                current = next;
            }
            return current;
        }

        /// <summary>Game View 화면을 그대로(UI 포함) PNG로 저장한다. 파일은 다음 프레임 렌더 후 기록된다.</summary>
        private void CaptureGameView()
        {
            string path = Path.Combine(GetSaveFolder(), $"GameView_x{gameViewSuperSize}_{DateTime.Now:yyMMdd_HHmmss}.png");
            ScreenCapture.CaptureScreenshot(path, gameViewSuperSize);
            lastSavedPath = path;
            if (!EditorApplication.isPlaying) RepaintGameView();
            Log.D($"[Screenshot] Game View 캡처 요청: {path} (다음 프레임에 저장됨)");
        }

        /// <summary>에디트 모드에서 캡처가 즉시 기록되도록 Game View를 찾아 리페인트한다.</summary>
        private static void RepaintGameView()
        {
            Type gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType == null) return;
            EditorWindow gameView = GetWindow(gameViewType);
            if (gameView != null) gameView.Repaint();
        }

        /// <summary>프로젝트 루트의 Screenshots 폴더 경로를 반환한다. 없으면 생성.</summary>
        /// <returns>저장 폴더 절대 경로.</returns>
        private static string GetSaveFolder()
        {
            string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots");
            Directory.CreateDirectory(folder);
            return folder;
        }

        /// <summary>마지막 저장 파일을 선택한 상태로 탐색기를 연다. 저장 이력이 없으면 폴더만 연다.</summary>
        private void OpenSaveFolder()
        {
            if (!string.IsNullOrEmpty(lastSavedPath) && File.Exists(lastSavedPath))
            {
                EditorUtility.RevealInFinder(lastSavedPath);
                return;
            }
            EditorUtility.RevealInFinder(GetSaveFolder());
        }
    }
}
