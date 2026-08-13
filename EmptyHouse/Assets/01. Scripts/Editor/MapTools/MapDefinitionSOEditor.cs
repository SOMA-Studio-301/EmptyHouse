using System.Collections.Generic;
using EmptyHouse.MapGen.Runtime;
using UnityEditor;
using UnityEngine;

namespace EmptyHouse.EditorTools
{
    /// <summary>
    /// 빈 집 정의(MapDefinitionSO) 커스텀 인스펙터(M10-1 — 구 SO_MapGenParams 인스펙터 승계) —
    /// 정의 필드(맵 키·층·레지스트리·조명)를 위에, 맵 전역 생성 파라미터를 목적별 그룹으로 아래에 그리고
    /// 최소/최대 역전 같은 즉시 판정 가능한 결함을 그 자리에서 경고한다.
    /// 이 에셋이 런타임과 에디터 프리뷰의 단일 출처라, 여기서 바꾸면 양쪽이 함께 바뀐다 —
    /// 상단 안내와 "프리뷰 5맵 생성" 버튼으로 수정→확인 왕복을 짧게 만든다.
    /// </summary>
    [CustomEditor(typeof(MapDefinitionSO))]
    public sealed class MapDefinitionSOEditor : UnityEditor.Editor
    {
        private static readonly string[] definitionFields = { "MapId", "CommonRegistry", "LightingProfile" }; // 정의 고유 필드(기본 드로어) — 층 목록은 DrawFloorList 전용

        private const string previewMenuPath = "Tools/Map/절차 예시 맵 5개 생성"; // 프리뷰 빌더 메뉴(에디터 어셈블리 경계상 메뉴로 호출)

        /// <summary>그룹 제목 → 그 그룹에 그릴 필드명 목록 — **맵 전역으로 실제 소비되는 노브만**. 여기·레거시 목록에 없는 필드는 "기타"로 모아 그린다(필드 추가 누락 방지).</summary>
        private static readonly (string title, string[] fields)[] groups =
        {
            ("시드", new[] { "Seed" }),
            ("지름길·검증", new[] { "ShortcutValueMin", "ListenerCounterDist", "RerollMax" }),
            ("자물쇠·열쇠(R 불변식 — 전역 그래프)", new[] { "ShortcutLockCountMin", "ShortcutLockCountMax", "ItemDoorLockCount", "KeyDistanceMin", "KeyDistanceMax" }),
            ("아이템", new[] { "ThrowableBudget", "OilCount", "ScrapCount" }),
            ("계단 샤프트(다층)", new[] { "ShaftCountMin", "ShaftCountMax", "ShaftDepthPercentMin", "ShaftDepthPercentMax", "ShaftMinSeparationCells", "FloorRetryMax" }),
            ("층 배정", new[] { "VaccineFloorPlan", "CorpseStationFloorPlan" }),
        };

        /// <summary>층 이관 완료 스칼라(M9~M10-1) — 정의 기반 경로는 층 정의 GenParams 가 원천이라 여기 값은 죽어 있다. FromLegacy(테스트·레거시 툴) 전용으로만 남아 접힌 비활성 그룹으로 그린다.</summary>
        private static readonly string[] legacyFields =
        {
            "RoomsTotalMin", "RoomsTotalMax", "CycleRoomPercent", "CorridorLinkPercent", "CorridorChainMax",
            "ReturnExitCount", "WardrobeCount", "EnabledZombieTypes",
            "ZombieDensitySafeMin", "ZombieDensitySafeMax", "ZombieDensityMidMin", "ZombieDensityMidMax",
            "ZombieDensityDangerMin", "ZombieDensityDangerMax", "ListenerRatioPercent", "HerdZombieCountMin", "HerdZombieCountMax",
        };

        /// <summary>최소/최대 쌍 — 역전 시 경고한다(라이브 노브만 — 층 이관분은 층 정의 인스펙터 소관).</summary>
        private static readonly (string min, string max, string label)[] rangePairs =
        {
            ("ShortcutLockCountMin", "ShortcutLockCountMax", "지름길 자물쇠"),
            ("KeyDistanceMin", "KeyDistanceMax", "열쇠 거리"),
            ("ShaftCountMin", "ShaftCountMax", "계단 샤프트"),
        };

        private readonly Dictionary<string, bool> foldouts = new Dictionary<string, bool>();

        /// <summary>그룹 폴드아웃·요약·검증 경고·프리뷰 버튼으로 인스펙터를 그린다.</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty root = serializedObject.FindProperty("GenParams");

            EditorGUILayout.HelpBox("런타임 드라이버와 에디터 프리뷰가 이 에셋 하나를 공유한다. 여기서 바꾸면 양쪽이 함께 바뀐다.\n층별 노브(방 예산·사이클·복도·좀비·탈출문·벽장)는 각 층 정의(FloorDefinitionSO) 에셋에서 편집한다(M10-1).", MessageType.Info);
            foreach (string field in definitionFields)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(field), true);
            }

            DrawFloorList();

            EditorGUILayout.Space();
            DrawSummary(serializedObject, root);
            DrawWarnings(root);
            EditorGUILayout.Space();

            var drawn = new HashSet<string>();
            foreach ((string title, string[] fields) in groups)
            {
                DrawGroup(root, title, fields, drawn);
            }

            DrawLegacyGroup(root, drawn);
            DrawLeftovers(root, drawn);

            EditorGUILayout.Space();
            if (GUILayout.Button("프리뷰 5맵 생성(현재 값으로)", GUILayout.Height(28f)))
            {
                serializedObject.ApplyModifiedProperties();
                AssetDatabase.SaveAssets(); // 프리뷰 빌더는 디스크의 에셋을 읽는다 — 저장 후 실행해야 방금 바꾼 값이 반영된다
                EditorApplication.ExecuteMenuItem(previewMenuPath);
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>한 그룹을 폴드아웃으로 그린다(기본 펼침).</summary>
        /// <param name="root">Params 프로퍼티.</param>
        /// <param name="title">그룹 제목.</param>
        /// <param name="fields">그릴 필드명 목록.</param>
        /// <param name="drawn">이미 그린 필드 집합(중복·누락 판정용).</param>
        private void DrawGroup(SerializedProperty root, string title, string[] fields, HashSet<string> drawn)
        {
            if (!foldouts.TryGetValue(title, out bool open))
            {
                open = true;
            }

            // 배열 필드(층 배정 등)는 자체 폴드아웃 헤더를 그린다 — FoldoutHeaderGroup 은 중첩 금지라 일반 Foldout 사용
            open = EditorGUILayout.Foldout(open, title, true);
            foldouts[title] = open;
            if (open)
            {
                EditorGUI.indentLevel++;
                foreach (string field in fields)
                {
                    SerializedProperty property = root.FindPropertyRelative(field);
                    if (property == null)
                    {
                        continue; // 코드에서 제거된 필드 — 그룹 표만 남은 경우
                    }

                    EditorGUILayout.PropertyField(property, true);
                    drawn.Add(field);
                }

                EditorGUI.indentLevel--;
            }
        }

        /// <summary>
        /// 층 목록 섹션 — 아래→위 순서 행마다 유도 서수(B1/1F/2F)를 라벨로 붙이고 ▲▼(재정렬)·×(제거)·추가를 제공한다.
        /// 같은 층 에셋 중복·서수 0(시드 층) 부재는 즉시 경고한다(린트와 같은 기준의 미리보기).
        /// </summary>
        private void DrawFloorList()
        {
            EditorGUILayout.Space();
            var definition = (MapDefinitionSO)target;
            SerializedProperty floors = serializedObject.FindProperty("Floors");
            SerializedProperty basementCount = serializedObject.FindProperty("BasementCount");

            EditorGUILayout.LabelField("층 목록(아래→위) — 서수는 위치 + 지하 층 수로 유도", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(basementCount, new GUIContent("지하 층 수(BasementCount)"));

            if (basementCount.intValue < 0 || basementCount.intValue >= Mathf.Max(1, floors.arraySize))
            {
                EditorGUILayout.HelpBox($"BasementCount({basementCount.intValue}) 범위 밖 — 서수 0(시드 층)이 존재하려면 0 ≤ 값 ≤ 층 수-1", MessageType.Error);
            }

            var seen = new HashSet<Object>();
            bool duplicate = false;
            for (int i = 0; i < floors.arraySize; i++)
            {
                SerializedProperty slot = floors.GetArrayElementAtIndex(i);
                int floorIndex = i - basementCount.intValue;
                string ordinal = floorIndex < 0 ? $"B{-floorIndex}" : $"{floorIndex + 1}F";
                if (slot.objectReferenceValue != null && !seen.Add(slot.objectReferenceValue))
                {
                    duplicate = true;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(floorIndex == 0 ? $"{ordinal}·시드" : ordinal, GUILayout.Width(56f));
                    EditorGUILayout.PropertyField(slot, GUIContent.none);
                    using (new EditorGUI.DisabledScope(i == 0))
                    {
                        if (GUILayout.Button("▼", GUILayout.Width(24f)))
                        {
                            floors.MoveArrayElement(i, i - 1); // 아래→위 목록이라 ▼ = 슬롯 앞으로
                        }
                    }

                    using (new EditorGUI.DisabledScope(i == floors.arraySize - 1))
                    {
                        if (GUILayout.Button("▲", GUILayout.Width(24f)))
                        {
                            floors.MoveArrayElement(i, i + 1);
                        }
                    }

                    if (GUILayout.Button("×", GUILayout.Width(22f)))
                    {
                        if (slot.objectReferenceValue != null)
                        {
                            slot.objectReferenceValue = null; // 참조 슬롯은 1차 삭제가 값 비우기 — 한 번에 지우면 배열이 안 줄어든다
                        }

                        floors.DeleteArrayElementAtIndex(i);
                        break;
                    }
                }
            }

            if (GUILayout.Button("+ 층 추가(맨 위)"))
            {
                floors.InsertArrayElementAtIndex(floors.arraySize);
                floors.GetArrayElementAtIndex(floors.arraySize - 1).objectReferenceValue = null;
            }

            if (duplicate)
            {
                EditorGUILayout.HelpBox("같은 층 정의 에셋이 두 슬롯에 배선됨 — 린트가 조립을 거부한다(층마다 별도 에셋)", MessageType.Error);
            }
        }

        /// <summary>층 이관 완료 스칼라를 접힌 **비활성** 그룹으로 그린다 — 실수 편집 차단 + "여기 값은 안 쓰인다"를 UI 로 못박는다.</summary>
        /// <param name="root">GenParams 프로퍼티.</param>
        /// <param name="drawn">이미 그린 필드 집합.</param>
        private void DrawLegacyGroup(SerializedProperty root, HashSet<string> drawn)
        {
            const string title = "레거시 v1(층 이관 — 정의 경로 미사용)";
            if (!foldouts.TryGetValue(title, out bool open))
            {
                open = false; // 기본 접힘 — 죽은 값이라 펼칠 일이 드물다
            }

            open = EditorGUILayout.Foldout(open, title, true); // FoldoutHeaderGroup 중첩 금지 — DrawGroup 과 동일 규약
            foldouts[title] = open;
            if (open)
            {
                EditorGUILayout.HelpBox("FromLegacy(테스트·레거시 툴) 전용 — 실제 생성은 층 정의(FloorDefinitionSO)의 GenParams 를 쓴다.", MessageType.None);
                EditorGUI.indentLevel++;
                EditorGUI.BeginDisabledGroup(true);
                foreach (string field in legacyFields)
                {
                    SerializedProperty property = root.FindPropertyRelative(field);
                    if (property != null)
                    {
                        EditorGUILayout.PropertyField(property, true);
                    }
                }

                EditorGUI.EndDisabledGroup();
                EditorGUI.indentLevel--;
            }

            foreach (string field in legacyFields)
            {
                drawn.Add(field); // 접혀 있어도 '기타' 안전망에 다시 나타나지 않게 그린 것으로 계산한다
            }
        }

        /// <summary>그룹 표에 없는 필드를 모아 그린다 — 필드가 추가돼도 인스펙터에서 사라지지 않게 하는 안전망.</summary>
        /// <param name="root">Params 프로퍼티.</param>
        /// <param name="drawn">이미 그린 필드 집합.</param>
        private void DrawLeftovers(SerializedProperty root, HashSet<string> drawn)
        {
            var leftovers = new List<SerializedProperty>();
            SerializedProperty iterator = root.Copy();
            SerializedProperty end = root.GetEndProperty();
            if (!iterator.NextVisible(true))
            {
                return;
            }

            while (!SerializedProperty.EqualContents(iterator, end))
            {
                if (!drawn.Contains(iterator.name))
                {
                    leftovers.Add(iterator.Copy());
                }

                if (!iterator.NextVisible(false))
                {
                    break;
                }
            }

            if (leftovers.Count == 0)
            {
                return;
            }

            EditorGUILayout.LabelField("기타(그룹 미분류)", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            foreach (SerializedProperty property in leftovers)
            {
                EditorGUILayout.PropertyField(property);
            }

            EditorGUI.indentLevel--;
        }

        /// <summary>현재 값으로 예상되는 규모를 한 줄로 요약한다 — 방 예산은 층 정의 합산(실소비 값).</summary>
        /// <param name="serialized">정의 SerializedObject(층 목록 조회).</param>
        /// <param name="root">GenParams 프로퍼티.</param>
        private static void DrawSummary(SerializedObject serialized, SerializedProperty root)
        {
            int roomsMin = 0;
            int roomsMax = 0;
            var definition = (MapDefinitionSO)serialized.targetObject;
            for (int i = 0; i < definition.Floors.Length; i++)
            {
                if (definition.Floors[i] != null && definition.Floors[i].GenParams != null)
                {
                    roomsMin += definition.Floors[i].GenParams.RoomsTotalMin;
                    roomsMax += definition.Floors[i].GenParams.RoomsTotalMax;
                }
            }

            int locks = IntOf(root, "ShortcutLockCountMax") + IntOf(root, "ItemDoorLockCount");
            int seed = IntOf(root, "Seed");
            EditorGUILayout.LabelField($"층 {definition.Floors.Length} · 방 {roomsMin}~{roomsMax}(층 합) · 자물쇠 최대 {locks} · 시드 {(seed == 0 ? "랜덤(서버 확정)" : seed.ToString())}", EditorStyles.miniBoldLabel);
        }

        /// <summary>최소/최대 역전과 자물쇠 변종 재고 부족을 경고한다.</summary>
        /// <param name="root">Params 프로퍼티.</param>
        private static void DrawWarnings(SerializedProperty root)
        {
            foreach ((string min, string max, string label) in rangePairs)
            {
                SerializedProperty minProperty = root.FindPropertyRelative(min);
                SerializedProperty maxProperty = root.FindPropertyRelative(max);
                if (minProperty == null || maxProperty == null)
                {
                    continue;
                }

                if (minProperty.intValue > maxProperty.intValue)
                {
                    EditorGUILayout.HelpBox($"{label}: 최소({minProperty.intValue})가 최대({maxProperty.intValue})보다 크다 — 생성이 실패하거나 리롤만 반복한다.", MessageType.Error);
                }
            }

            int lockTotal = IntOf(root, "ShortcutLockCountMax") + IntOf(root, "ItemDoorLockCount");
            var registry = AssetDatabase.LoadAssetAtPath<MapPrefabRegistrySO>("Assets/03. ScriptableObjects/MapGen/SO_MapPrefabRegistry.asset");
            if (registry != null && registry.PairPrefabs != null)
            {
                int usable = 0;
                for (int i = 0; i < registry.PairPrefabs.Length; i++)
                {
                    if (registry.PairPrefabs[i].Key != null && registry.PairPrefabs[i].Lock != null)
                    {
                        usable++;
                    }
                }

                if (lockTotal > usable)
                {
                    EditorGUILayout.HelpBox($"자물쇠 최대 {lockTotal}개인데 열쇠·자물쇠가 모두 등재된 페어는 {usable}쌍 — 초과분은 자물쇠 없이 잠긴 문(해정 불가)이 된다.", MessageType.Warning);
                }
            }
        }

        /// <summary>정수 필드 값을 읽는다(없으면 0).</summary>
        /// <param name="root">Params 프로퍼티.</param>
        /// <param name="field">필드명.</param>
        /// <returns>정수 값.</returns>
        private static int IntOf(SerializedProperty root, string field)
        {
            SerializedProperty property = root.FindPropertyRelative(field);
            return property == null ? 0 : property.intValue;
        }
    }
}
