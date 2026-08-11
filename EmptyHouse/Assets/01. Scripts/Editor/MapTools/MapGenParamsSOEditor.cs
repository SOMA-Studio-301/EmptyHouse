using System.Collections.Generic;
using EmptyHouse.MapGen.Runtime;
using UnityEditor;
using UnityEngine;

namespace EmptyHouse.EditorTools
{
    /// <summary>
    /// 생성 파라미터 에셋(SO_MapGenParams) 커스텀 인스펙터 — 24개 수치를 목적별로 묶어 보여주고
    /// 최소/최대 역전 같은 즉시 판정 가능한 결함을 그 자리에서 경고한다.
    /// 이 에셋이 런타임과 에디터 프리뷰의 단일 출처라, 여기서 바꾸면 양쪽이 함께 바뀐다 —
    /// 상단 안내와 "프리뷰 5맵 생성" 버튼으로 수정→확인 왕복을 짧게 만든다.
    /// </summary>
    [CustomEditor(typeof(MapGenParamsSO))]
    public sealed class MapGenParamsSOEditor : UnityEditor.Editor
    {
        private const string previewMenuPath = "Tools/Map/절차 예시 맵 5개 생성"; // 프리뷰 빌더 메뉴(에디터 어셈블리 경계상 메뉴로 호출)

        /// <summary>그룹 제목 → 그 그룹에 그릴 필드명 목록. 여기 없는 필드는 "기타"로 모아 그린다(필드 추가 누락 방지).</summary>
        private static readonly (string title, string[] fields)[] groups =
        {
            ("시드", new[] { "Seed" }),
            ("레이아웃", new[] { "RoomsTotalMin", "RoomsTotalMax", "CorridorLinkPercent", "CorridorChainMax", "CycleRoomPercent" }),
            ("지름길·검증", new[] { "ShortcutValueMin", "ListenerCounterDist", "RerollMax" }),
            ("자물쇠", new[] { "ShortcutLockCountMin", "ShortcutLockCountMax", "ItemDoorLockCount" }),
            ("좀비", new[] { "EnabledZombieTypes", "ZombieDensitySafeMin", "ZombieDensitySafeMax", "ZombieDensityMidMin", "ZombieDensityMidMax", "ZombieDensityDangerMin", "ZombieDensityDangerMax", "ListenerRatioPercent", "HerdZombieCountMin", "HerdZombieCountMax" }),
            ("아이템", new[] { "ThrowableBudget", "OilCount", "ScrapCount" }),
        };

        /// <summary>최소/최대 쌍 — 역전 시 경고한다.</summary>
        private static readonly (string min, string max, string label)[] rangePairs =
        {
            ("RoomsTotalMin", "RoomsTotalMax", "총 방 수"),
            ("ShortcutLockCountMin", "ShortcutLockCountMax", "지름길 자물쇠"),
            ("ZombieDensitySafeMin", "ZombieDensitySafeMax", "안전 등급 좀비"),
            ("ZombieDensityMidMin", "ZombieDensityMidMax", "중간 등급 좀비"),
            ("ZombieDensityDangerMin", "ZombieDensityDangerMax", "위험 등급 좀비"),
            ("HerdZombieCountMin", "HerdZombieCountMax", "위장 무대 무리"),
        };

        private readonly Dictionary<string, bool> foldouts = new Dictionary<string, bool>();

        /// <summary>그룹 폴드아웃·요약·검증 경고·프리뷰 버튼으로 인스펙터를 그린다.</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty root = serializedObject.FindProperty("Params");

            EditorGUILayout.HelpBox("런타임 드라이버와 에디터 프리뷰가 이 에셋 하나를 공유한다. 여기서 바꾸면 양쪽이 함께 바뀐다.", MessageType.Info);
            DrawSummary(root);
            DrawWarnings(root);
            EditorGUILayout.Space();

            var drawn = new HashSet<string>();
            foreach ((string title, string[] fields) in groups)
            {
                DrawGroup(root, title, fields, drawn);
            }

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

            open = EditorGUILayout.BeginFoldoutHeaderGroup(open, title);
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

                    EditorGUILayout.PropertyField(property);
                    drawn.Add(field);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
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

        /// <summary>현재 값으로 예상되는 규모를 한 줄로 요약한다.</summary>
        /// <param name="root">Params 프로퍼티.</param>
        private static void DrawSummary(SerializedProperty root)
        {
            int roomsMin = IntOf(root, "RoomsTotalMin");
            int roomsMax = IntOf(root, "RoomsTotalMax");
            int locks = IntOf(root, "ShortcutLockCountMax") + IntOf(root, "ItemDoorLockCount");
            int seed = IntOf(root, "Seed");
            EditorGUILayout.LabelField($"방 {roomsMin}~{roomsMax} · 자물쇠 최대 {locks} · 시드 {(seed == 0 ? "랜덤(서버 확정)" : seed.ToString())}", EditorStyles.miniBoldLabel);
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
