using System.Collections.Generic;
using TMPro;
using Unity.AI.Navigation;
using Unity.AI.Navigation.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 튜토리얼 관련 씬·프리팹 배선 원샷 빌더 (튜토리얼.md 5장 씬 구성 + 핸드오프 §4-4).
/// Tools/Tutorial 메뉴로 실행한다. 재실행 가능 — 자기가 만든 루트를 지우고 다시 만든다.
/// 그레이박스 배치 기준: 통과 복도 길이 12m(잠정 12~15m), 철창↔복도 중심선 3.2m(≤5m, D31), 철창은 Default 레이어(시야 통과)·복도 벽은 Wall 레이어(시야 차단).
/// </summary>
public static class TutorialSceneBuilder
{
    private const string TutorialScenePath = "Assets/00. Scenes/Tutorial.unity";
    private const string SplashScenePath = "Assets/00. Scenes/Splash.unity";
    private const string MenuCanvasPrefabPath = "Assets/02. Prefab/UI/Lobby/Canvas-Menu.prefab";
    private const int WallLayer = 7; // 좀비 시야 차단 마스크(1152 = Wall|Door)에 포함되는 레이어

    // 배치 좌표 계획 — 플레이어 동선은 z=20 라인을 따라 서→동(+x)으로 진행한다.
    private static readonly Vector3 spawnPointPos = new Vector3(-10f, 0f, 20f);
    private static readonly Vector3 bottlePos = new Vector3(-2f, 0.05f, 20f);
    private static readonly Vector3 zombieBCageCenter = new Vector3(0f, 0f, 30f);   // S1_3 소음 시연 — 동선에서 10m+ 이격(투척 후 돌아본 좀비 B 의 시각 발각 방지, D31 즉발 5m 밖)
    private static readonly Vector3 corpsePos = new Vector3(8f, 0f, 20f);           // 통과 복도 진입 전(4-4-5)
    private static readonly Vector3 zombieACageCenter = new Vector3(18f, 0f, 23.5f); // 복도 북측 개구부에 접함
    private static readonly Vector3 respawnPos = new Vector3(11f, 0f, 20f);          // 복도 시작(4-4-4)
    private static readonly Vector3 passLinePos = new Vector3(25f, 1.2f, 20f);       // 복도 끝(성공 트리거)

    /// <summary>세 작업(튜토리얼 씬·스플래쉬 씬·타이틀 버튼)을 순서대로 전부 실행한다.</summary>
    [MenuItem("Tools/Tutorial/Build All")]
    public static void BuildAll()
    {
        BuildTutorialScene();
        BuildSplashScene();
        AddTutorialButtonToMenu();
        Debug.Log("[TutorialSceneBuilder] Build All 완료");
    }

    /// <summary>Tutorial.unity 에 매니저·네트워크·튜토리얼 시스템·좀비·환경·NavMesh 를 배선한다.</summary>
    [MenuItem("Tools/Tutorial/Build Tutorial Scene")]
    public static void BuildTutorialScene()
    {
        Scene scene = EditorSceneManager.OpenScene(TutorialScenePath, OpenSceneMode.Single);

        // 재실행 대비 — 빌더가 만드는 루트를 전부 제거 후 재생성한다.
        string[] ownedRoots =
        {
            "Game-MainCamera", "EventSystem", "NetworkManager", "TutorialHostBootstrap", "SessionExitHandler",
            "CameraManager", "NoiseRuntimeManager", "ClientGameManager", "LocalizeManager", "AudioManager",
            "SettingsSystem", "ZombieThreatAudioDirector", "GameScenePlayerSpawner", "DissonanceSetup",
            "Canvas-HUD", "Canvas-Gameplay", "Canvas-Tutorial", "TutorialDirector", "TutorialRespawnPoint",
            "TutorialPassLine", "====TUTORIAL_ENV====", "Zombie_A_Setting", "Zombie_B_Setting", "ZombieCorpse",
            "Bottle", "NavMesh", "Main Camera",
        };
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (System.Array.IndexOf(ownedRoots, root.name) >= 0) Object.DestroyImmediate(root);
        }

        // ---- 카메라 · 이벤트 시스템 ----
        InstantiatePrefab("Assets/02. Prefab/Camera/Game-MainCamera.prefab");
        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<InputSystemUIInputModule>();

        // ---- 상주 매니저 (ServerGameManager·MapGenManager 는 배치하지 않는다 — 스펙 1-2 GamePhase 밖) ----
        InstantiatePrefab("Assets/02. Prefab/Manager/CameraManager.prefab");
        InstantiatePrefab("Assets/02. Prefab/Manager/NoiseRuntimeManager.prefab");
        GameObject clientGm = InstantiatePrefab("Assets/02. Prefab/Manager/ClientGameManager.prefab");
        SetSerialized(clientGm.GetComponentInChildren<ClientGameManager>(), "initialState", p => p.enumValueIndex = (int)GameState.Game);
        InstantiatePrefab("Assets/02. Prefab/Manager/LocalizeManager.prefab");
        InstantiatePrefab("Assets/02. Prefab/Manager/AudioManager.prefab");
        InstantiatePrefab("Assets/02. Prefab/Manager/SettingsSystem.prefab");
        InstantiatePrefab("Assets/02. Prefab/Manager/ZombieThreatAudioDirector.prefab");
        InstantiatePrefab("Assets/Dissonance/Integrations/Unity_NFGO/DissonanceSetup.prefab");

        // ---- 네트워크: DevBootstrap 프리팹을 언팩해 NetworkManager 설정(직결 UTP·프리팹 목록)만 재사용 ----
        GameObject netGo = InstantiatePrefab("Assets/02. Prefab/Util/DevBootstrap.prefab");
        PrefabUtility.UnpackPrefabInstance(netGo, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        Object.DestroyImmediate(netGo.GetComponent<DevHostBootstrap>());
        netGo.name = "NetworkManager";

        // 부트스트랩은 별도 GO — 로비 경유(웜 패스)에서 씬 NetworkManager 가 중복 파괴돼도 살아남아야 한다.
        GameObject bootstrapGo = new GameObject("TutorialHostBootstrap");
        TutorialHostBootstrap bootstrap = bootstrapGo.AddComponent<TutorialHostBootstrap>();
        SetSerialized(bootstrap, "onMapAssembledServer", p => p.objectReferenceValue = FindAsset<ScriptableObject>("SO_Event_MapAssembledServer"));

        // ---- 중도 이탈 경로: 일시정지 '게임 나가기' 채널 구독자 (ServerGameManager 프리팹 없이 단독 배치) ----
        GameObject exitGo = new GameObject("SessionExitHandler");
        SessionExitHandler exitHandler = exitGo.AddComponent<SessionExitHandler>();
        SetSerialized(exitHandler, "sessionExitRequested", p => p.objectReferenceValue = FindAsset<ScriptableObject>("SO_Event_SessionExitRequest"));

        // ---- 플레이어 스포너 + 스폰 포인트(자식 Transform 이 스폰 지점) ----
        GameObject spawner = InstantiatePrefab("Assets/02. Prefab/Manager/GameScenePlayerSpawner.prefab");
        // 스포너는 자식 순번으로 스폰 지점을 고르고 프리팹이 기본 자식들을 이미 갖고 있다 —
        // 순번에 기대지 않고 기본 자식 전부를 튜토리얼 시작점으로 옮겨 어느 순번이 뽑혀도 같은 곳에 스폰되게 한다.
        foreach (Transform child in spawner.transform)
        {
            child.SetPositionAndRotation(spawnPointPos, Quaternion.Euler(0f, 90f, 0f));
        }

        // ---- HUD · 일시정지 캔버스 ----
        InstantiatePrefab("Assets/02. Prefab/UI/Gameplay/Canvas-HUD.prefab", "Assets/02. Prefab/UI/Canvas-HUD.prefab");
        InstantiatePrefab("Assets/02. Prefab/UI/Gameplay/Canvas-Gameplay.prefab", "Assets/02. Prefab/UI/Canvas-Gameplay.prefab");

        // ---- 튜토리얼 대화창 캔버스 ----
        TutorialDialogue dialogue = BuildDialogueCanvas();

        // ---- 환경(복도·철창) ----
        BuildEnvironment();

        // ---- 좀비 2 · 사체 · 투척물 ----
        GameObject zombieA = InstantiateZombieSetting("Zombie_A_Setting", zombieACageCenter, Quaternion.Euler(0f, 180f, 0f)); // 복도(남쪽)를 정면으로(4-4-2)
        GameObject zombieB = InstantiateZombieSetting("Zombie_B_Setting", zombieBCageCenter, Quaternion.identity);            // 플레이어 동선(남쪽)을 등짐(4-3)
        GameObject corpse = InstantiatePrefab("Assets/02. Prefab/Interaction/ZombieCorpse.prefab");
        corpse.transform.position = corpsePos;
        GameObject bottle = InstantiatePrefab("Assets/02. Prefab/Interaction/Bottle.prefab");
        bottle.transform.position = bottlePos;

        // ---- 리스폰 · 통과선 ----
        GameObject respawnGo = new GameObject("TutorialRespawnPoint");
        respawnGo.transform.SetPositionAndRotation(respawnPos, Quaternion.Euler(0f, 90f, 0f));
        TutorialRespawnPoint respawn = respawnGo.AddComponent<TutorialRespawnPoint>();

        GameObject passGo = new GameObject("TutorialPassLine");
        passGo.transform.position = passLinePos;
        BoxCollider passCollider = passGo.AddComponent<BoxCollider>();
        passCollider.isTrigger = true;
        passCollider.size = new Vector3(0.5f, 2.4f, 3f);
        TutorialPassLineTrigger passTrigger = passGo.AddComponent<TutorialPassLineTrigger>();

        // ---- 디렉터 ----
        GameObject directorGo = new GameObject("TutorialDirector");
        TutorialDirector director = directorGo.AddComponent<TutorialDirector>();
        SetSerialized(director, "disguiseStateChanged", p => p.objectReferenceValue = FindAsset<ScriptableObject>("SO_Event_DisguiseStateChanged"));
        SetSerialized(director, "disguiseRefillRequested", p => p.objectReferenceValue = FindAsset<ScriptableObject>("SO_Event_DisguiseRefillRequested"));
        SetSerialized(director, "zombieStateChanged", p => p.objectReferenceValue = FindAsset<ScriptableObject>("SO_Event_ZombieStateChanged"));
        SetSerialized(director, "noiseEmitted", p => p.objectReferenceValue = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/03. ScriptableObjects/NoiseSystem/SO_Event_NoiseEmitted.asset")); // 동명 자산 2종 — 반드시 NoiseSystem 쪽(EmptyHouse.NoiseSystem)
        SetSerialized(director, "playerCaught", p => p.objectReferenceValue = FindAsset<ScriptableObject>("SO_Event_PlayerCaught"));
        SetSerialized(director, "dialogue", p => p.objectReferenceValue = dialogue);
        SetSerialized(director, "respawnPoint", p => p.objectReferenceValue = respawn);
        SetSerialized(director, "zombieB", p => p.objectReferenceValue = zombieB.GetComponentInChildren<ZombieController>());
        SetSerialized(passTrigger, "director", p => p.objectReferenceValue = director);

        // ---- NavMesh (테스트 씬 관례: PhysicsColliders 수집 · 기본 Walkable · 에디터 베이크) ----
        GameObject navGo = new GameObject("NavMesh");
        NavMeshSurface surface = navGo.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
        surface.defaultArea = 0;
        NavMeshAssetManager.instance.StartBakingSurfaces(new Object[] { surface });

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[TutorialSceneBuilder] Tutorial 씬 빌드 완료 — zombieA={zombieA.name}");
    }

    /// <summary>Splash.unity 에 SplashRouter 를 배치하고 SO_SaveLoadSystem 을 배선한다.</summary>
    [MenuItem("Tools/Tutorial/Build Splash Scene")]
    public static void BuildSplashScene()
    {
        Scene scene = EditorSceneManager.OpenScene(SplashScenePath, OpenSceneMode.Single);
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "SplashRouter") Object.DestroyImmediate(root);
        }

        GameObject routerGo = new GameObject("SplashRouter");
        SplashRouter router = routerGo.AddComponent<SplashRouter>();
        SetSerialized(router, "saveLoadSystem", p => p.objectReferenceValue = FindAsset<ScriptableObject>("SO_SaveLoadSystem"));

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[TutorialSceneBuilder] Splash 씬 빌드 완료");
    }

    /// <summary>Canvas-Menu 프리팹의 ButtonContainer 에 튜토리얼 버튼을 추가하고 UIMenu.tutorialButton 에 배선한다.</summary>
    [MenuItem("Tools/Tutorial/Add Tutorial Button To Menu")]
    public static void AddTutorialButtonToMenu()
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(MenuCanvasPrefabPath);
        try
        {
            Transform container = FindDeepChild(contents.transform, "ButtonContainer");
            Transform existing = container.Find("TutorialButton");
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            GameObject buttonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/02. Prefab/UI/Common/UIGenericButton.prefab");
            GameObject button = (GameObject)PrefabUtility.InstantiatePrefab(buttonPrefab, container);
            button.name = "TutorialButton";
            button.transform.SetSiblingIndex(1); // Start 다음, Settings 앞

            // 라벨 키 — 시트 미등재 상태에서는 키 문자열이 그대로 표시된다(등재는 /localize-sheet).
            Component localizeText = null;
            foreach (Component comp in button.GetComponentsInChildren<Component>(true))
            {
                if (comp != null && comp.GetType().Name == "UILocalizeText") { localizeText = comp; break; }
            }
            SetSerialized(localizeText, "key", p => p.stringValue = "UI_TITLE_TUTORIAL");

            UIMenu menu = contents.GetComponent<UIMenu>();
            SetSerialized(menu, "tutorialButton", p => p.objectReferenceValue = button.GetComponent<UIGenericButton>());

            PrefabUtility.SaveAsPrefabAsset(contents, MenuCanvasPrefabPath);
            Debug.Log("[TutorialSceneBuilder] Canvas-Menu 튜토리얼 버튼 추가 완료");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    /// <summary>대화창 캔버스(패널+TMP 텍스트+TutorialDialogue)를 만들고 잠정 키 시퀀스(tut_*)를 채운다.</summary>
    /// <returns>배선 완료된 TutorialDialogue.</returns>
    private static TutorialDialogue BuildDialogueCanvas()
    {
        GameObject canvasGo = new GameObject("Canvas-Tutorial");
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasGo.AddComponent<GraphicRaycaster>();

        GameObject panel = new GameObject("DialoguePanel");
        panel.transform.SetParent(canvasGo.transform, false);
        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0.6f);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.2f, 0.05f);
        panelRect.anchorMax = new Vector2(0.8f, 0.2f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        GameObject textGo = new GameObject("LineText");
        textGo.transform.SetParent(panel.transform, false);
        TextMeshProUGUI text = textGo.AddComponent<TextMeshProUGUI>();
        text.fontSize = 30f;
        text.alignment = TextAlignmentOptions.Center;
        RectTransform textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(24f, 12f);
        textRect.offsetMax = new Vector2(-24f, -12f);

        panel.SetActive(false);

        TutorialDialogue dialogue = canvasGo.AddComponent<TutorialDialogue>();
        SetSerialized(dialogue, "inputReader", p => p.objectReferenceValue = FindAsset<ScriptableObject>("SO_InputReader"));
        SetSerialized(dialogue, "lineText", p => p.objectReferenceValue = text);
        SetSerialized(dialogue, "panelRoot", p => p.objectReferenceValue = panel);
        FillDialogueSequences(dialogue);

        return dialogue;
    }

    /// <summary>단계별 잠정 대사 키(tut_&lt;stage&gt;_&lt;nn&gt;)를 시퀀스 배열에 채운다. 원고·시트 등재는 기획 몫이라 키만 계약대로 둔다.</summary>
    /// <param name="dialogue">대상 대화창.</param>
    private static void FillDialogueSequences(TutorialDialogue dialogue)
    {
        var stages = new List<(TutorialStageKind stage, string[] lines, string[] fails)>
        {
            (TutorialStageKind.S0, new[] { "tut_s0_01", "tut_s0_02", "tut_s0_03", "tut_s0_04" }, new string[0]),
            (TutorialStageKind.S1_1, new[] { "tut_s1_1_01" }, new string[0]),
            (TutorialStageKind.S1_2, new[] { "tut_s1_2_01" }, new string[0]),
            (TutorialStageKind.S1_3, new[] { "tut_s1_3_01" }, new string[0]),
            (TutorialStageKind.S2_1, new[] { "tut_s2_1_01", "tut_s2_1_02" }, new string[0]),
            (TutorialStageKind.S2_2, new[] { "tut_s2_2_01" }, new string[0]),
            (TutorialStageKind.S2_3, new[] { "tut_s2_3_01" }, new[] { "tut_s2_3_fail_01" }),
            (TutorialStageKind.S2_4, new[] { "tut_s2_4_01" }, new string[0]),
            (TutorialStageKind.Completed, new[] { "tut_end_01", "tut_end_02" }, new string[0]),
        };

        SerializedObject so = new SerializedObject(dialogue);
        SerializedProperty seq = so.FindProperty("sequences");
        seq.arraySize = stages.Count;
        for (int i = 0; i < stages.Count; i++)
        {
            SerializedProperty entry = seq.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("Stage").enumValueIndex = (int)stages[i].stage;
            FillStringArray(entry.FindPropertyRelative("LineKeys"), stages[i].lines);
            FillStringArray(entry.FindPropertyRelative("FailLineKeys"), stages[i].fails);
        }

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>복도 벽(Wall 레이어 — 시야 차단)과 철창 2기(Default 레이어 — 시야 통과·물리 차단)를 세운다.</summary>
    private static void BuildEnvironment()
    {
        GameObject envRoot = new GameObject("====TUTORIAL_ENV====");

        // 통과 복도: x 12~24 · 폭 3m(z 18.5~21.5) · 길이 12m (게이지 지속 1/2 이하 잠정치, 소모율 확정 시 재계산 — 9장 7)
        MakeBox(envRoot, "Corridor_Wall_S", new Vector3(18f, 1.5f, 18.5f), new Vector3(12f, 3f, 0.3f), WallLayer);
        MakeBox(envRoot, "Corridor_Wall_N_West", new Vector3(13.5f, 1.5f, 21.5f), new Vector3(3f, 3f, 0.3f), WallLayer);
        MakeBox(envRoot, "Corridor_Wall_N_East", new Vector3(22.5f, 1.5f, 21.5f), new Vector3(3f, 3f, 0.3f), WallLayer);

        // 좀비 A 철창 — 복도 북측 개구부(x 15~21)에 접한다. 정면(남쪽) 창살이 곧 복도 벽 역할.
        MakeCage(envRoot, "Cage_A", new Vector3(18f, 0f, 23.5f), new Vector2(6f, 4f));

        // 좀비 B 철창 — S1 소음 시연용.
        MakeCage(envRoot, "Cage_B", new Vector3(0f, 0f, 26f), new Vector2(4f, 4f));
    }

    /// <summary>철창 1기 — 얇은 벽 4면. Default 레이어라 좀비 시야·청각은 통과하고 NavMesh·이동만 차단한다(9장 3).</summary>
    /// <param name="parent">부모 루트.</param>
    /// <param name="name">철창 이름.</param>
    /// <param name="center">바닥 중심.</param>
    /// <param name="size">가로(x)·세로(z) 크기.</param>
    private static void MakeCage(GameObject parent, string name, Vector3 center, Vector2 size)
    {
        GameObject cage = new GameObject(name);
        cage.transform.SetParent(parent.transform);
        cage.transform.position = center;
        float hx = size.x * 0.5f;
        float hz = size.y * 0.5f;
        MakeBox(cage, "Bars_S", center + new Vector3(0f, 1.25f, -hz), new Vector3(size.x, 2.5f, 0.15f), 0);
        MakeBox(cage, "Bars_N", center + new Vector3(0f, 1.25f, hz), new Vector3(size.x, 2.5f, 0.15f), 0);
        MakeBox(cage, "Bars_W", center + new Vector3(-hx, 1.25f, 0f), new Vector3(0.15f, 2.5f, size.y), 0);
        MakeBox(cage, "Bars_E", center + new Vector3(hx, 1.25f, 0f), new Vector3(0.15f, 2.5f, size.y), 0);
    }

    /// <summary>박스 지오메트리 하나를 만든다(그레이박스 — 아트 교체 대상).</summary>
    /// <param name="parent">부모.</param>
    /// <param name="name">이름.</param>
    /// <param name="center">월드 중심.</param>
    /// <param name="size">크기.</param>
    /// <param name="layer">레이어.</param>
    private static void MakeBox(GameObject parent, string name, Vector3 center, Vector3 size, int layer)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.layer = layer;
        box.transform.SetParent(parent.transform);
        box.transform.position = center;
        box.transform.localScale = size;
    }

    /// <summary>Zombie Setting 래퍼(인씬 NetworkObject + HomeAnchor)를 배치하고 배회를 끈다 — 등짐/정면 포즈 유지(스카웃 6절).</summary>
    /// <param name="name">래퍼 이름.</param>
    /// <param name="position">배치 위치.</param>
    /// <param name="rotation">바라볼 방향.</param>
    /// <returns>래퍼 루트.</returns>
    private static GameObject InstantiateZombieSetting(string name, Vector3 position, Quaternion rotation)
    {
        GameObject wrapper = InstantiatePrefab("Assets/02. Prefab/Zombie/Zombie Setting.prefab");
        wrapper.name = name;
        wrapper.transform.SetPositionAndRotation(position, rotation);
        ZombieController controller = wrapper.GetComponentInChildren<ZombieController>();
        SetSerialized(controller, "wanderEnabled", p => p.boolValue = false);
        return wrapper;
    }

    /// <summary>프리팹을 씬 루트로 인스턴스한다. 첫 경로가 없으면 폴백 경로, 그마저 없으면 이름 검색으로 찾는다.</summary>
    /// <param name="path">프리팹 경로.</param>
    /// <param name="fallbackPath">대체 경로(선택).</param>
    /// <returns>인스턴스 루트.</returns>
    private static GameObject InstantiatePrefab(string path, string fallbackPath = null)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null && fallbackPath != null) prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fallbackPath);
        if (prefab == null)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            foreach (string guid in AssetDatabase.FindAssets($"{name} t:Prefab"))
            {
                string candidate = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(candidate) == name)
                {
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(candidate);
                    break;
                }
            }
        }

        if (prefab == null)
        {
            Debug.LogError($"[TutorialSceneBuilder] 프리팹을 찾지 못함: {path}");
            return null;
        }

        return (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    }

    /// <summary>이름으로 에셋 하나를 찾는다(정확히 일치하는 파일명 우선).</summary>
    /// <typeparam name="T">에셋 타입.</typeparam>
    /// <param name="name">파일명(확장자 제외).</param>
    /// <returns>찾은 에셋. 없으면 null + 에러 로그.</returns>
    private static T FindAsset<T>(string name) where T : Object
    {
        foreach (string guid in AssetDatabase.FindAssets($"{name} t:{typeof(T).Name}"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == name) return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        Debug.LogError($"[TutorialSceneBuilder] 에셋을 찾지 못함: {name} ({typeof(T).Name})");
        return null;
    }

    /// <summary>직렬화 필드 하나를 SerializedObject 로 수정한다. 프로퍼티가 없으면 에러 로그를 남긴다.</summary>
    /// <param name="target">대상 컴포넌트.</param>
    /// <param name="propertyName">필드 이름.</param>
    /// <param name="setter">값 대입 동작.</param>
    private static void SetSerialized(Object target, string propertyName, System.Action<SerializedProperty> setter)
    {
        if (target == null)
        {
            Debug.LogError($"[TutorialSceneBuilder] SetSerialized 대상 없음: {propertyName}");
            return;
        }

        SerializedObject so = new SerializedObject(target);
        SerializedProperty prop = so.FindProperty(propertyName);
        if (prop == null)
        {
            Debug.LogError($"[TutorialSceneBuilder] 직렬화 필드 없음: {target.GetType().Name}.{propertyName}");
            return;
        }

        setter(prop);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>문자열 배열 프로퍼티를 채운다.</summary>
    /// <param name="arrayProp">대상 배열 프로퍼티.</param>
    /// <param name="values">채울 값.</param>
    private static void FillStringArray(SerializedProperty arrayProp, string[] values)
    {
        arrayProp.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            arrayProp.GetArrayElementAtIndex(i).stringValue = values[i];
        }
    }

    /// <summary>이름으로 자손 Transform 을 깊이 우선 탐색한다.</summary>
    /// <param name="root">탐색 루트.</param>
    /// <param name="name">찾을 이름.</param>
    /// <returns>찾은 Transform. 없으면 null.</returns>
    private static Transform FindDeepChild(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeepChild(root.GetChild(i), name);
            if (found != null) return found;
        }

        return null;
    }
}
