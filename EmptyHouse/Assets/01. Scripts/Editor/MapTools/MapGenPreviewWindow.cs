using System;
using System.Collections.Generic;
using Border.Core;
using EmptyHouse.MapGen.Core;
using EmptyHouse.MapGen.Runtime;
using UnityEditor;
using UnityEngine;

namespace EmptyHouse.MapGen.Editor
{
    /// <summary>
    /// 기획자 검증 툴(10절) — 알고리즘이 의도대로 동작하는지 코드를 읽지 않고 화면으로 확인한다.
    /// 게임과 동일한 생성 라이브러리(MapGenerator.Generate)를 직접 호출한다 — 별도 생성 코드 경로 금지(AC-21).
    /// 게임 로그의 시드를 입력하면 동일 블루프린트가 재현된다(AC-22).
    /// 편집 범위는 재료(파라미터·마커 기본값)뿐 — 생성된 맵의 개별 수정 기능은 시드 재현을 깨므로 금지(10절).
    /// </summary>
    public sealed class MapGenPreviewWindow : EditorWindow
    {
        [SerializeField] private int seedInput; // 입력 시드 — 0 = 랜덤(생성 시 실제 값 확정·표기, X8)
        [SerializeField] private int lastConfirmedSeed; // 마지막 생성에 실제 사용한 확정 시드(AC-17·AC-22 재현 표기)
        [SerializeField] private string selectedMapPath; // 선택된 빈 집 정의 에셋 경로 — 시드 재현은 같은 정의에서만 성립(도메인 리로드 보존)
        [SerializeField] private int floorFilterIndex; // 층 필터(0=전체, 1..N=Blueprint.Floors 순번) — 다층 결과 전용
        [SerializeField] private MapGenParams workingParams = new MapGenParams(); // 파라미터 오버라이드 작업본(9절)
        [SerializeField] private bool showDangerOverlay = true; // 위험 등급 그라데이션 표시(10절)
        [SerializeField] private bool showSpawnOverlay = true; // 스폰 오버레이 표시(10절)
        [SerializeField] private bool showWanderRadii; // 좀비 배회 반경 원 표시(10절)
        [SerializeField] private bool showHearingRadii; // 좀비 청각 반경 근사 표시(10절 — 시각화 전용, 편집 불가)
        [SerializeField] private int spawnKindFilterMask = ~0; // SpawnKind 비트 필터(~0 = 전체 표시)
        [SerializeField] private float assumedCellMeters = 4f; // 셀 실측 m 가정치 ⚪ — 방 프리팹 바닥 타일(Hall_Floor_4M) 실측 4m 근거, 청각 근사 전용
        [SerializeField] private float hearingRefDb = 50f; // 청각 근사 기준 소음 dB ⚪(10절 예시 = 투척물 50dB)
        [SerializeField] private List<WanderOverride> wanderOverrides = new List<WanderOverride>(); // 배회 반경 마커 기본값 오버라이드(10절 편집 범위)
        [SerializeField] private BlueprintCanvasDrawer canvasDrawer = new BlueprintCanvasDrawer(); // 캔버스 렌더러 — 팬·줌 상태를 도메인 리로드 너머로 보존
        [SerializeField] private bool paramsFoldout = true; // 파라미터 오버라이드 폴드아웃 상태
        [SerializeField] private bool floorParamsFoldout = true; // 층별 파라미터 폴드아웃 상태(층 스택 세트 전용)
        [SerializeField] private List<FloorGenParams> floorParamOverrides = new List<FloorGenParams>(); // 층별 파라미터 세션 작업본 — 생성 시 스택 SO 값을 덮는다(SO 는 수정하지 않음)
        [SerializeField] private Vector2 inputScroll; // 입력 패널 스크롤 위치
        [SerializeField] private Vector2 validationScroll; // 검증 패널 스크롤 위치

        private string[] cachedMapPaths; // 프로젝트 내 전체 MapDefinitionSO 경로 캐시(도메인 리로드 시 재스캔)
        private string[] cachedMapNames; // 드롭다운 표시명 — "MapId (에셋명)"

        private readonly MapGenerator generator = new MapGenerator(); // 게임과 동일 생성 라이브러리(AC-21)
        private MapGenResult lastResult; // 마지막 생성 결과 — 도메인 리로드 시 소실, 재생성으로 복원
        private List<RoomTemplateDef> lastTemplates; // 마지막 생성에 쓴 템플릿 — 오버레이가 풋프린트·마커를 조회
        private MapBlueprint displayBlueprint; // 캔버스 표시용 블루프린트 — 다층이면 층별 X 오프셋 펼침 사본(원본 좌표는 층간 겹침)

        /// <summary>배회 반경 오버라이드 1건 — (템플릿, ZombieSpawn 마커) 기본값을 세션 단위로 대체한다(10절 — 재료 편집).</summary>
        [Serializable]
        private sealed class WanderOverride
        {
            public string TemplateId; // 대상 템플릿 ID
            public int MarkerId; // 대상 ZombieSpawn 마커 Id
            public float RadiusCells; // 대체 배회 반경(셀)
        }

        /// <summary>미리보기 윈도우를 연다.</summary>
        [MenuItem("Tools/Map/절차 생성 미리보기")]
        public static void ShowWindow()
        {
            Log.D("[MapGenPreviewWindow] ShowWindow");
            var window = GetWindow<MapGenPreviewWindow>("절차 생성 미리보기");
            window.minSize = new Vector2(980f, 520f);
            window.wantsMouseMove = true; // 캔버스 호버 툴팁 갱신용
            window.Show();
        }

        /// <summary>좌측 입력 패널 · 중앙 캔버스 · 우측 검증 결과 패널을 배치해 그린다.</summary>
        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(300f)))
                {
                    DrawInputPanel();
                }

                Rect canvasRect = GUILayoutUtility.GetRect(200f, float.MaxValue, 200f, float.MaxValue, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                var options = new BlueprintCanvasDrawer.OverlayOptions
                {
                    ShowDanger = showDangerOverlay,
                    ShowSpawns = showSpawnOverlay,
                    ShowWanderRadii = showWanderRadii,
                    ShowHearingRadii = showHearingRadii,
                    SpawnKindFilterMask = spawnKindFilterMask,
                    AssumedCellMeters = assumedCellMeters,
                    HearingRefDb = hearingRefDb,
                };
                // 층 필터·라벨(M9 다층) — 다층 결과에서만 활성. FloorFilter 기본은 반드시 int.MinValue(전체) — 0 이면 1F 필터가 된다
                options.FloorFilter = int.MinValue;
                if (lastResult != null && lastResult.Success && lastResult.Blueprint.Floors.Count > 1)
                {
                    options.ShowFloorLabels = true;
                    if (floorFilterIndex > 0 && floorFilterIndex <= lastResult.Blueprint.Floors.Count)
                    {
                        options.FloorFilter = lastResult.Blueprint.Floors[floorFilterIndex - 1].FloorIndex;
                    }
                }

                MapBlueprint blueprint = lastResult != null && lastResult.Success ? (displayBlueprint ?? lastResult.Blueprint) : null;
                canvasDrawer.Draw(canvasRect, blueprint, lastTemplates, options);

                using (new EditorGUILayout.VerticalScope(GUILayout.Width(300f)))
                {
                    DrawValidationPanel();
                }
            }

            if (Event.current.type == EventType.MouseMove)
            {
                Repaint(); // 호버 툴팁 추적
            }
        }

        /// <summary>
        /// 빈 집 선택 드롭다운 — 프로젝트 내 전체 MapDefinitionSO 를 스캔해 기획자가 자유롭게 갈아끼우며 본다.
        /// ↻ 버튼으로 재스캔(에셋 추가·삭제 반영). 시드 재현은 같은 정의에서만 성립한다.
        /// </summary>
        private void DrawMapSelector()
        {
            if (cachedMapPaths == null)
            {
                RefreshMapCache();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (cachedMapPaths.Length == 0)
                {
                    EditorGUILayout.HelpBox("MapDefinitionSO 에셋이 없다 — 런타임 어댑터 셋업 실행 또는 Create > EmptyHouse > MapGen > Map Definition", MessageType.Warning);
                }
                else
                {
                    int index = System.Array.IndexOf(cachedMapPaths, selectedMapPath);
                    if (index < 0)
                    {
                        index = 0; // 삭제·미선택 — 첫 정의로 폴백
                        selectedMapPath = cachedMapPaths[0];
                    }

                    int next = EditorGUILayout.Popup("빈 집 정의", index, cachedMapNames);
                    if (next != index)
                    {
                        selectedMapPath = cachedMapPaths[next];
                        floorParamOverrides.Clear(); // 맵이 바뀌면 층 구성도 바뀐다 — 작업본은 새 정의 값으로 재구축
                    }
                }

                if (GUILayout.Button("↻", GUILayout.Width(26f)))
                {
                    RefreshMapCache();
                }
            }
        }

        /// <summary>프로젝트의 MapDefinitionSO 전체를 재스캔해 드롭다운 캐시를 갱신한다.</summary>
        private void RefreshMapCache()
        {
            Log.D("[MapGenPreviewWindow] RefreshMapCache");
            string[] guids = AssetDatabase.FindAssets("t:MapDefinitionSO");
            cachedMapPaths = new string[guids.Length];
            cachedMapNames = new string[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                cachedMapPaths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
                var definition = AssetDatabase.LoadAssetAtPath<MapDefinitionSO>(cachedMapPaths[i]);
                cachedMapNames[i] = $"{(string.IsNullOrEmpty(definition.MapId) ? "(MapId 없음)" : definition.MapId)} — {definition.name}";
            }
        }

        /// <summary>현재 선택된 빈 집 정의를 로드한다 — 없으면 null(호출부가 안내).</summary>
        /// <returns>선택 정의 — 미선택·삭제면 null.</returns>
        private MapDefinitionSO SelectedDefinition()
        {
            return string.IsNullOrEmpty(selectedMapPath) ? null : AssetDatabase.LoadAssetAtPath<MapDefinitionSO>(selectedMapPath);
        }

        /// <summary>입력 패널 — 시드(0=랜덤)·빈 집 선택·파라미터 오버라이드(9절)·오버레이 토글·배회 반경 오버라이드 편집·생성 버튼.</summary>
        private void DrawInputPanel()
        {
            inputScroll = EditorGUILayout.BeginScrollView(inputScroll);
            EditorGUILayout.LabelField("생성", EditorStyles.boldLabel);
            seedInput = EditorGUILayout.IntField(new GUIContent("시드 (0 = 랜덤)"), seedInput);
            DrawMapSelector();
            if (lastConfirmedSeed != 0)
            {
                EditorGUILayout.LabelField($"마지막 확정 시드: {lastConfirmedSeed}", EditorStyles.miniLabel);
            }

            if (GUILayout.Button("생성", GUILayout.Height(28f)))
            {
                GenerateNow();
            }

            EditorGUILayout.Space(6f);
            paramsFoldout = EditorGUILayout.Foldout(paramsFoldout, "파라미터 오버라이드(9절)", true);
            if (paramsFoldout)
            {
                if (GUILayout.Button("기본값 리셋"))
                {
                    ResetParamsToDefaults();
                }

                MapGenParams p = workingParams;
                p.RoomsTotalMin = EditorGUILayout.IntField("총 방 수 Min", p.RoomsTotalMin);
                p.RoomsTotalMax = EditorGUILayout.IntField("총 방 수 Max", p.RoomsTotalMax);
                p.CycleRoomPercent = EditorGUILayout.IntSlider("사이클 소속 방 목표 %", p.CycleRoomPercent, 0, 100);
                p.CorridorLinkPercent = EditorGUILayout.IntSlider("복도 경유 확률 %", p.CorridorLinkPercent, 0, 100);
                p.CorridorChainMax = EditorGUILayout.IntField("복도 연쇄 Max", p.CorridorChainMax);
                p.ReturnExitCount = EditorGUILayout.IntField("탈출문 수", p.ReturnExitCount);
                p.WardrobeCount = EditorGUILayout.IntField("벽장 수", p.WardrobeCount);
                p.ShortcutValueMin = EditorGUILayout.IntField("지름길 최소 가치", p.ShortcutValueMin);
                p.ListenerCounterDist = EditorGUILayout.IntField("Listener 보장 거리", p.ListenerCounterDist);
                p.RerollMax = EditorGUILayout.IntField("리롤 상한", p.RerollMax);
                p.ShortcutLockCountMin = EditorGUILayout.IntField("지름길 자물쇠 Min", p.ShortcutLockCountMin);
                p.ShortcutLockCountMax = EditorGUILayout.IntField("지름길 자물쇠 Max", p.ShortcutLockCountMax);
                p.ItemDoorLockCount = EditorGUILayout.IntField("중요 물품 문 수", p.ItemDoorLockCount);
                p.ZombieDensitySafeMin = EditorGUILayout.IntField("좀비 안전 Min", p.ZombieDensitySafeMin);
                p.ZombieDensitySafeMax = EditorGUILayout.IntField("좀비 안전 Max", p.ZombieDensitySafeMax);
                p.ZombieDensityMidMin = EditorGUILayout.IntField("좀비 중간 Min", p.ZombieDensityMidMin);
                p.ZombieDensityMidMax = EditorGUILayout.IntField("좀비 중간 Max", p.ZombieDensityMidMax);
                p.ZombieDensityDangerMin = EditorGUILayout.IntField("좀비 위험 Min", p.ZombieDensityDangerMin);
                p.ZombieDensityDangerMax = EditorGUILayout.IntField("좀비 위험 Max", p.ZombieDensityDangerMax);
                p.ListenerRatioPercent = EditorGUILayout.IntField("Listener 비율 %", p.ListenerRatioPercent);
                p.ThrowableBudget = EditorGUILayout.IntField("투척물 예산", p.ThrowableBudget);
                p.OilCount = EditorGUILayout.IntField("기름 수", p.OilCount);
                p.ScrapCount = EditorGUILayout.IntField("스크랩 수", p.ScrapCount);
                p.HerdZombieCountMin = EditorGUILayout.IntField("무리 좀비 Min", p.HerdZombieCountMin);
                p.HerdZombieCountMax = EditorGUILayout.IntField("무리 좀비 Max", p.HerdZombieCountMax);
                p.EnabledZombieTypes = (ZombieTypeMask)EditorGUILayout.EnumFlagsField("활성 좀비 타입", p.EnabledZombieTypes);
            }

            DrawFloorParamOverrides(); // 단층 정의도 층 1개의 층별 파라미터를 갖는다(M10-1)

            if (lastResult != null && lastResult.Success && lastResult.Blueprint.Floors.Count > 1)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("층 필터", EditorStyles.boldLabel);
                var floorNames = new string[lastResult.Blueprint.Floors.Count + 1];
                floorNames[0] = "전체";
                for (int f = 0; f < lastResult.Blueprint.Floors.Count; f++)
                {
                    floorNames[f + 1] = BlueprintCanvasDrawer.FloorName(lastResult.Blueprint.Floors[f].FloorIndex);
                }

                floorFilterIndex = GUILayout.Toolbar(Mathf.Clamp(floorFilterIndex, 0, floorNames.Length - 1), floorNames);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("오버레이", EditorStyles.boldLabel);
            showDangerOverlay = EditorGUILayout.ToggleLeft("위험 등급 그라데이션", showDangerOverlay);
            showSpawnOverlay = EditorGUILayout.ToggleLeft("스폰 오버레이", showSpawnOverlay);
            spawnKindFilterMask = EditorGUILayout.MaskField("스폰 필터", spawnKindFilterMask, System.Enum.GetNames(typeof(SpawnKind)));
            showWanderRadii = EditorGUILayout.ToggleLeft("배회 반경", showWanderRadii);
            showHearingRadii = EditorGUILayout.ToggleLeft("청각 반경(근사)", showHearingRadii);
            using (new EditorGUI.IndentLevelScope())
            {
                assumedCellMeters = EditorGUILayout.FloatField("셀 실측 m(가정)", assumedCellMeters);
                hearingRefDb = EditorGUILayout.FloatField("기준 소음 dB", hearingRefDb);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("배회 반경 오버라이드(재료 편집)", EditorStyles.boldLabel);
            for (int i = wanderOverrides.Count - 1; i >= 0; i--)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    WanderOverride entry = wanderOverrides[i];
                    entry.TemplateId = EditorGUILayout.TextField(entry.TemplateId, GUILayout.Width(110f));
                    entry.MarkerId = EditorGUILayout.IntField(entry.MarkerId, GUILayout.Width(30f));
                    entry.RadiusCells = EditorGUILayout.FloatField(entry.RadiusCells, GUILayout.Width(40f));
                    if (GUILayout.Button("×", GUILayout.Width(22f)))
                    {
                        wanderOverrides.RemoveAt(i);
                    }
                }
            }

            if (GUILayout.Button("+ 오버라이드 추가"))
            {
                wanderOverrides.Add(new WanderOverride { TemplateId = "room_small", MarkerId = 0, RadiusCells = 2f });
            }

            EditorGUILayout.HelpBox("팬: 우클릭/휠클릭 드래그 · 줌: 휠\n생성 결과의 개별 수정은 지원하지 않는다(시드 재현 보존 — 10절).", MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 층별 파라미터 오버라이드 섹션 — 선택된 빈 집 정의의 층 목록을 세션 작업본으로 복제해
        /// 층마다 방 예산·사이클·복도·위험 가중·좀비 노브를 편집한다. SO 는 수정하지 않는다(10절 — 재료 편집은 세션 한정).
        /// </summary>
        private void DrawFloorParamOverrides()
        {
            MapDefinitionSO definition = SelectedDefinition();
            if (definition == null || definition.Floors == null || definition.Floors.Length == 0)
            {
                return;
            }

            SyncFloorParamOverrides(definition);
            EditorGUILayout.Space(6f);
            floorParamsFoldout = EditorGUILayout.Foldout(floorParamsFoldout, "층별 파라미터(정의 오버라이드)", true);
            if (!floorParamsFoldout)
            {
                return;
            }

            if (GUILayout.Button("정의 값 리셋"))
            {
                floorParamOverrides.Clear();
                SyncFloorParamOverrides(definition);
            }

            for (int i = 0; i < floorParamOverrides.Count; i++)
            {
                FloorGenParams fp = floorParamOverrides[i];
                EditorGUILayout.LabelField(BlueprintCanvasDrawer.FloorName(fp.FloorIndex), EditorStyles.boldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    fp.RoomsTotalMin = EditorGUILayout.IntField("방 예산 Min", fp.RoomsTotalMin);
                    fp.RoomsTotalMax = EditorGUILayout.IntField("방 예산 Max", fp.RoomsTotalMax);
                    fp.CycleRoomPercent = EditorGUILayout.IntSlider("사이클 소속 방 목표 %", fp.CycleRoomPercent, 0, 100);
                    fp.CorridorLinkPercent = EditorGUILayout.IntSlider("복도 경유 확률 %", fp.CorridorLinkPercent, 0, 100);
                    fp.CorridorChainMax = EditorGUILayout.IntField("복도 연쇄 Max", fp.CorridorChainMax);
                    fp.DangerBias = EditorGUILayout.IntField("위험 층 가중", fp.DangerBias);
                    fp.ReturnExitCount = EditorGUILayout.IntField("탈출문 수", fp.ReturnExitCount);
                    fp.WardrobeCount = EditorGUILayout.IntField("벽장 수", fp.WardrobeCount);
                    fp.ZombieDensitySafeMin = EditorGUILayout.IntField("좀비 안전 Min", fp.ZombieDensitySafeMin);
                    fp.ZombieDensitySafeMax = EditorGUILayout.IntField("좀비 안전 Max", fp.ZombieDensitySafeMax);
                    fp.ZombieDensityMidMin = EditorGUILayout.IntField("좀비 중간 Min", fp.ZombieDensityMidMin);
                    fp.ZombieDensityMidMax = EditorGUILayout.IntField("좀비 중간 Max", fp.ZombieDensityMidMax);
                    fp.ZombieDensityDangerMin = EditorGUILayout.IntField("좀비 위험 Min", fp.ZombieDensityDangerMin);
                    fp.ZombieDensityDangerMax = EditorGUILayout.IntField("좀비 위험 Max", fp.ZombieDensityDangerMax);
                    fp.ListenerRatioPercent = EditorGUILayout.IntField("Listener 비율 %", fp.ListenerRatioPercent);
                    fp.HerdZombieCountMin = EditorGUILayout.IntField("무리 좀비 Min", fp.HerdZombieCountMin);
                    fp.HerdZombieCountMax = EditorGUILayout.IntField("무리 좀비 Max", fp.HerdZombieCountMax);
                    fp.EnabledZombieTypes = (ZombieTypeMask)EditorGUILayout.EnumFlagsField("활성 좀비 타입", fp.EnabledZombieTypes);
                }
            }
        }

        /// <summary>
        /// 층별 파라미터 작업본을 빈 집 정의와 동기화한다 — 층 구성(수·서수)이 다르면 정의 값으로 재구축한다.
        /// 매 프레임 호출되는 GUI 경로라 로그 트레이스를 두지 않는다.
        /// </summary>
        /// <param name="definition">기준 빈 집 정의.</param>
        private void SyncFloorParamOverrides(MapDefinitionSO definition)
        {
            bool rebuild = floorParamOverrides.Count != definition.Floors.Length;
            for (int i = 0; !rebuild && i < definition.Floors.Length; i++)
            {
                rebuild = floorParamOverrides[i].FloorIndex != definition.FloorIndexOf(i);
            }

            if (!rebuild)
            {
                return;
            }

            floorParamOverrides.Clear();
            for (int i = 0; i < definition.Floors.Length; i++)
            {
                FloorDefinitionSO entry = definition.Floors[i];
                FloorGenParams source = entry.GenParams ?? new FloorGenParams();
                FloorGenParams clone = JsonUtility.FromJson<FloorGenParams>(JsonUtility.ToJson(source));
                clone.FloorIndex = definition.FloorIndexOf(i); // 서수·테마는 정의가 원천(MapPlanBuilder 조립과 같은 강제 — 드리프트 차단)
                clone.ThemeId = entry.ThemeId;
                floorParamOverrides.Add(clone);
            }
        }

        /// <summary>
        /// 층별 파라미터 세션 오버라이드를 조립된 Plan 에 적용한다 — FloorIndex 매칭으로 튜닝 노브만 덮어쓰고
        /// 서수·테마는 Plan(스택) 값을 유지한다. Plan 의 FloorGenParams 는 Build 가 만든 새 인스턴스라 직접 수정해도 SO 는 무사하다.
        /// </summary>
        /// <param name="plan">적용 대상 Plan.</param>
        private void ApplyFloorParamOverrides(MapGenPlan plan)
        {
            Log.D("[MapGenPreviewWindow] ApplyFloorParamOverrides");
            for (int i = 0; i < plan.FloorParams.Length; i++)
            {
                FloorGenParams target = plan.FloorParams[i];
                for (int o = 0; o < floorParamOverrides.Count; o++)
                {
                    FloorGenParams source = floorParamOverrides[o];
                    if (source.FloorIndex != target.FloorIndex)
                    {
                        continue;
                    }

                    target.RoomsTotalMin = source.RoomsTotalMin;
                    target.RoomsTotalMax = source.RoomsTotalMax;
                    target.CycleRoomPercent = source.CycleRoomPercent;
                    target.CorridorLinkPercent = source.CorridorLinkPercent;
                    target.CorridorChainMax = source.CorridorChainMax;
                    target.DangerBias = source.DangerBias;
                    target.ZombieDensitySafeMin = source.ZombieDensitySafeMin;
                    target.ZombieDensitySafeMax = source.ZombieDensitySafeMax;
                    target.ZombieDensityMidMin = source.ZombieDensityMidMin;
                    target.ZombieDensityMidMax = source.ZombieDensityMidMax;
                    target.ZombieDensityDangerMin = source.ZombieDensityDangerMin;
                    target.ZombieDensityDangerMax = source.ZombieDensityDangerMax;
                    target.ListenerRatioPercent = source.ListenerRatioPercent;
                    target.HerdZombieCountMin = source.HerdZombieCountMin;
                    target.HerdZombieCountMax = source.HerdZombieCountMax;
                    target.EnabledZombieTypes = source.EnabledZombieTypes;
                    target.ReturnExitCount = source.ReturnExitCount;
                    target.WardrobeCount = source.WardrobeCount;
                    break;
                }
            }
        }

        /// <summary>검증 결과 패널 — 4종 패스 각각의 통과/실패·실패 사유·경고(X6)·리롤 횟수·확정 시드를 표시한다(AC-23·AC-17).</summary>
        private void DrawValidationPanel()
        {
            validationScroll = EditorGUILayout.BeginScrollView(validationScroll);
            EditorGUILayout.LabelField("검증 결과(7절)", EditorStyles.boldLabel);
            if (lastResult == null)
            {
                EditorGUILayout.HelpBox("아직 생성 결과가 없습니다.\n도메인 리로드로 소실됐다면 같은 시드로 재생성하세요(AC-22).", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.LabelField($"확정 시드: {lastConfirmedSeed}");
            EditorGUILayout.LabelField($"성공: {(lastResult.Success ? "예" : "아니오")} · 리롤 {lastResult.RerollCount}회");
            DrawCompositionStats();
            ValidationReport report = lastResult.LastReport;
            if (report != null)
            {
                DrawPassRow("1. 필수 아이템 도달", report.EssentialsReachable);
                DrawPassRow("2. 열쇠 R 불변식", report.KeyInvariantHolds);
                DrawPassRow("3. 파훼 쌍 A등급", report.HardlockPairsHold);
                DrawPassRow("4. 지름길 가치", report.ShortcutValuesHold);
            }

            if (lastResult.FailReasons.Count > 0)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField($"실패 사유({lastResult.FailReasons.Count})", EditorStyles.boldLabel);
                for (int i = 0; i < lastResult.FailReasons.Count; i++)
                {
                    EditorGUILayout.LabelField($"· {lastResult.FailReasons[i]}", EditorStyles.wordWrappedMiniLabel);
                }
            }

            if (report != null && report.Warnings.Count > 0)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField($"경고 X6({report.Warnings.Count})", EditorStyles.boldLabel);
                for (int i = 0; i < report.Warnings.Count; i++)
                {
                    EditorGUILayout.LabelField($"· {report.Warnings[i]}", EditorStyles.wordWrappedMiniLabel);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>구성 통계 — 방/복도 수와 직결·복도 경유 간선 수를 표시한다(직결/경유 혼합 비율 확인용).</summary>
        private void DrawCompositionStats()
        {
            if (!lastResult.Success || lastResult.Blueprint == null || lastTemplates == null)
            {
                return;
            }

            MapBlueprint blueprint = lastResult.Blueprint;
            int roomCount = 0;
            int corridorCount = 0;
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                RoomTemplateDef template = FindTemplate(blueprint.Rooms[r].TemplateId);
                if (template == null || template.IsEntranceAnchor)
                {
                    continue;
                }

                if (template.IsCorridor)
                {
                    corridorCount++;
                }
                else
                {
                    roomCount++;
                }
            }

            int directEdges = 0;
            int roomCorridorEdges = 0;
            int corridorCorridorEdges = 0;
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                BlueprintEdge edge = blueprint.Edges[e];
                if (edge.RoomB < 0 || edge.State == EdgeState.BlockedWall)
                {
                    continue;
                }

                bool corridorA = FindTemplate(blueprint.Rooms[edge.RoomA].TemplateId)?.IsCorridor ?? false;
                bool corridorB = FindTemplate(blueprint.Rooms[edge.RoomB].TemplateId)?.IsCorridor ?? false;
                if (corridorA && corridorB)
                {
                    corridorCorridorEdges++;
                }
                else if (corridorA || corridorB)
                {
                    roomCorridorEdges++;
                }
                else
                {
                    directEdges++;
                }
            }

            // 사이클 지표 — 잎 방(연결 1개) 비율과 사이클 소속 방 비율(외딴길↔순환 튜닝 눈금)
            var degrees = new int[blueprint.Rooms.Count];
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                BlueprintEdge edge = blueprint.Edges[e];
                if (edge.RoomB < 0 || edge.State == EdgeState.BlockedWall)
                {
                    continue;
                }

                degrees[edge.RoomA]++;
                degrees[edge.RoomB]++;
            }

            int leafRooms = 0;
            int nonCorridorRooms = 0;
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                RoomTemplateDef template = FindTemplate(blueprint.Rooms[r].TemplateId);
                if (template == null || template.IsCorridor)
                {
                    continue;
                }

                nonCorridorRooms++;
                if (degrees[r] == 1)
                {
                    leafRooms++;
                }
            }

            float leafPct = nonCorridorRooms == 0 ? 0f : 100f * leafRooms / nonCorridorRooms;

            EditorGUILayout.LabelField($"방 {roomCount} + 입구 1 · 복도 {corridorCount}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"방↔방 직결 {directEdges} · 방↔복도 {roomCorridorEdges} · 복도↔복도 {corridorCorridorEdges}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"사이클 소속 방 {blueprint.Meta.CycleRoomPercentAchieved:F1}% (목표 {workingParams.CycleRoomPercent}%) · 잎 방 {leafPct:F1}%", EditorStyles.miniLabel);
        }

        /// <summary>마지막 생성 템플릿 목록에서 ID 로 템플릿을 찾는다.</summary>
        /// <param name="templateId">찾을 템플릿 ID.</param>
        /// <returns>일치 템플릿 — 없으면 null.</returns>
        private RoomTemplateDef FindTemplate(string templateId)
        {
            for (int i = 0; i < lastTemplates.Count; i++)
            {
                if (lastTemplates[i].TemplateId == templateId)
                {
                    return lastTemplates[i];
                }
            }

            return null;
        }

        /// <summary>검증 패스 한 줄을 통과/실패 색으로 표시한다.</summary>
        /// <param name="label">패스 이름.</param>
        /// <param name="passed">통과 여부.</param>
        private static void DrawPassRow(string label, bool passed)
        {
            Color prev = GUI.contentColor;
            GUI.contentColor = passed ? new Color(0.5f, 1f, 0.55f) : new Color(1f, 0.45f, 0.4f);
            EditorGUILayout.LabelField($"{(passed ? "✔" : "✘")} {label}");
            GUI.contentColor = prev;
        }

        /// <summary>
        /// 현재 입력으로 생성을 실행한다 — 시드 확정(X8) 후 선택한 빈 집 정의를 MapPlanBuilder 로 조립하고
        /// 세션 오버라이드(전역·층별·배회)를 적용해 MapGenerator.Generate 를 직접 호출한다(AC-21 — 런타임 단일 경로).
        /// 실패 결과도 그대로 패널에 보고한다(X2 — 폴백 없음).
        /// </summary>
        private void GenerateNow()
        {
            Log.D("[MapGenPreviewWindow] GenerateNow");
            lastConfirmedSeed = ResolveSeed();
            displayBlueprint = null;

            // 작업본 오염 방지 — 스냅샷은 JSON 복제본으로 넘긴다([Serializable])
            MapGenParams snapshot = JsonUtility.FromJson<MapGenParams>(JsonUtility.ToJson(workingParams));
            snapshot.Seed = lastConfirmedSeed;

            MapDefinitionSO definition = SelectedDefinition();
            if (definition == null)
            {
                Log.E("[MapGenPreviewWindow] 선택된 빈 집 정의가 없다 — 드롭다운에서 선택(↻ 로 재스캔) 후 생성");
                lastResult = null;
                Repaint();
                return;
            }

            MapGenPlan plan = MapPlanBuilder.Build(definition, snapshot, out _);
            if (plan == null)
            {
                Log.E("[MapGenPreviewWindow] 정의 린트 실패 — 조립 거부(R4), 콘솔 에러 참조");
                lastResult = null;
                Repaint();
                return;
            }

            SyncFloorParamOverrides(definition); // 패널이 아직 안 그려진 세션(단축 실행)에서도 작업본을 보장한다
            ApplyFloorParamOverrides(plan);
            ApplyWanderOverrides(plan.FlatTemplates);
            lastResult = generator.Generate(plan);
            lastTemplates = new List<RoomTemplateDef>(plan.FlatTemplates);
            if (lastResult.Success && lastResult.Blueprint.Floors.Count > 1)
            {
                displayBlueprint = FloorSpread(lastResult.Blueprint, plan);
            }

            floorFilterIndex = 0;
            canvasDrawer.RequestFit();
            Repaint();
        }

        /// <summary>
        /// 다층 블루프린트를 층별 X 오프셋으로 펼친 표시용 사본을 만든다 — 원본 좌표는 층간 XZ 가 겹친다.
        /// 간선·스폰은 인덱스 참조라 그대로 두고 방 좌표만 이동한다(수직 간선은 층 섬 사이의 긴 선으로 보인다 — 의도).
        /// </summary>
        /// <param name="blueprint">원본 블루프린트(수정하지 않는다).</param>
        /// <param name="plan">층 슬롯 순서 조회용 계획.</param>
        /// <returns>표시용 사본.</returns>
        private static MapBlueprint FloorSpread(MapBlueprint blueprint, MapGenPlan plan)
        {
            // 층 서수 → 펼침 순번(슬롯 순서) — 좌→우 = 슬롯 0..N
            var slotOfFloor = new Dictionary<int, int>();
            for (int i = 0; i < plan.Floors.Length; i++)
            {
                slotOfFloor[plan.Floors[i].FloorIndex] = i;
            }

            // 층 폭 실측 — 오프셋 간격 = 최대 폭 + 6셀
            int minX = int.MaxValue;
            int maxX = int.MinValue;
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                minX = Mathf.Min(minX, blueprint.Rooms[r].Cell.X);
                maxX = Mathf.Max(maxX, blueprint.Rooms[r].Cell.X + 9);
            }

            int stride = maxX - minX + 6;
            var copy = new MapBlueprint { Meta = blueprint.Meta };
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                BlueprintRoom room = blueprint.Rooms[r];
                copy.Rooms.Add(new BlueprintRoom
                {
                    TemplateId = room.TemplateId,
                    Cell = new CellCoord(room.Cell.X + slotOfFloor[room.FloorIndex] * stride, room.Cell.Y),
                    Rotation = room.Rotation,
                    FloorIndex = room.FloorIndex,
                    TemplateIndex = room.TemplateIndex,
                });
            }

            copy.Edges.AddRange(blueprint.Edges);
            copy.Spawns.AddRange(blueprint.Spawns);
            copy.Floors.AddRange(blueprint.Floors);
            copy.Shafts.AddRange(blueprint.Shafts);
            return copy;
        }

        /// <summary>입력 시드를 확정 시드로 변환한다 — 0 이면 0 아닌 랜덤 값을 굴려 기록한다(X8 — 랜덤이 재현 불가로 이어지지 않게).</summary>
        /// <returns>생성에 실제 사용할 확정 시드(0 아님).</returns>
        private int ResolveSeed()
        {
            Log.D("[MapGenPreviewWindow] ResolveSeed");
            if (seedInput != 0)
            {
                return seedInput;
            }

            int rolled;
            do
            {
                rolled = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            }
            while (rolled == 0);

            return rolled;
        }

        /// <summary>파라미터 작업본을 게임 기본값(new MapGenParams)으로 되돌린다 — 로그 시드 재현(AC-22)은 기본값 상태에서 검증한다.</summary>
        private void ResetParamsToDefaults()
        {
            Log.D("[MapGenPreviewWindow] ResetParamsToDefaults");
            workingParams = new MapGenParams();
        }

        /// <summary>배회 반경 오버라이드를 템플릿 세트의 ZombieSpawn 마커 기본값에 적용한다 — 재료 편집이지 생성 결과 수정이 아니다(10절 편집 범위 원칙).</summary>
        /// <param name="templates">적용 대상 템플릿 목록(DevTemplateSet 새 인스턴스).</param>
        private void ApplyWanderOverrides(IReadOnlyList<RoomTemplateDef> templates)
        {
            Log.D("[MapGenPreviewWindow] ApplyWanderOverrides");
            for (int i = 0; i < wanderOverrides.Count; i++)
            {
                WanderOverride entry = wanderOverrides[i];
                for (int t = 0; t < templates.Count; t++)
                {
                    if (templates[t].TemplateId != entry.TemplateId)
                    {
                        continue;
                    }

                    MarkerDef[] markers = templates[t].Markers;
                    for (int m = 0; m < markers.Length; m++)
                    {
                        if (markers[m].Kind == MarkerKind.ZombieSpawn && markers[m].Id == entry.MarkerId)
                        {
                            markers[m].WanderRadiusCells = entry.RadiusCells;
                        }
                    }
                }
            }
        }
    }
}
