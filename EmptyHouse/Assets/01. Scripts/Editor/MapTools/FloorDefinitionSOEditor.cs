using System.Collections.Generic;
using EmptyHouse.MapGen.Core;
using EmptyHouse.MapGen.Runtime;
using UnityEditor;
using UnityEngine;

namespace EmptyHouse.EditorTools
{
    /// <summary>
    /// 층 정의(FloorDefinitionSO) 커스텀 인스펙터(M10-1) — 테마·규격 / 층 생성 파라미터 / 방 템플릿 /
    /// 계단 / 환경 프리팹 섹션으로 묶고, 결손(미배선)·규격 불일치를 그 자리에서 경고한다.
    /// 층 서수는 빈 집 정의(Floors 순서 + BasementCount)가 결정한다 — 이 에셋은 서수를 모른다(재사용 전제).
    /// </summary>
    [CustomEditor(typeof(FloorDefinitionSO))]
    public sealed class FloorDefinitionSOEditor : UnityEditor.Editor
    {
        private static readonly Color missingTint = new Color(1f, 0.55f, 0.55f); // 필수 슬롯 미할당 강조색

        /// <summary>층 파라미터 그룹 표 — GenParams 하위 필드명. FloorIndex·ThemeId 는 조립 시 스탬프라 제외.</summary>
        private static readonly (string title, string[] fields)[] paramGroups =
        {
            ("방 예산·레이아웃", new[] { "RoomsTotalMin", "RoomsTotalMax", "CycleRoomPercent", "CorridorLinkPercent", "CorridorChainMax", "DangerBias" }),
            ("탈출문·벽장", new[] { "ReturnExitCount", "WardrobeCount" }),
            ("좀비", new[] { "EnabledZombieTypes", "ZombieDensitySafeMin", "ZombieDensitySafeMax", "ZombieDensityMidMin", "ZombieDensityMidMax", "ZombieDensityDangerMin", "ZombieDensityDangerMax", "ListenerRatioPercent", "HerdZombieCountMin", "HerdZombieCountMax" }),
        };

        private readonly Dictionary<string, bool> foldouts = new Dictionary<string, bool>(); // 그룹 폴드아웃 상태(선택 유지)

        /// <summary>섹션별로 층 정의를 그린다 — 요약·경고 → 테마·규격 → 파라미터 → 템플릿 → 계단 → 환경 프리팹.</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var floor = (FloorDefinitionSO)target;

            EditorGUILayout.HelpBox("층 정의 = 테마·질감의 단위. 층 서수(B1/1F/2F)는 빈 집 정의의 Floors 순서 + BasementCount 가 결정한다 — 같은 층 에셋을 여러 빈 집이 재사용할 수 있다(한 빈 집 안 중복은 린트 에러).", MessageType.Info);
            DrawWarnings(floor);

            EditorGUILayout.LabelField("테마·규격", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ThemeId"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("CellMeters"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("FloorHeight"));

            EditorGUILayout.Space(6f);
            SerializedProperty genParams = serializedObject.FindProperty("GenParams");
            foreach ((string title, string[] fields) in paramGroups)
            {
                DrawGroup(genParams, title, fields);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField($"방 템플릿 — 배열 순서 = 코어 후보 순서(결정론) · {floor.Templates.Length}종", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Templates"), true);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("계단(다층 전용)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("StairTemplate"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("StairPrefab"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("StairVoidSlabPrefab"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("StairRailingPrefab"));

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("환경 프리팹(테마 종속)", EditorStyles.boldLabel);
            DrawRequiredObject("SealWallPrefab", floor.SealWallPrefab == null);
            DrawRequiredObject("CornerColumnPrefab", floor.CornerColumnPrefab == null);
            DrawRequiredObject("DoorPrefab", floor.DoorPrefab == null);
            DrawRequiredObject("ReturnExitPrefab", floor.ReturnExitPrefab == null);

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>결손·규격 불일치를 모아 상단 경고로 표시한다(린트와 같은 기준의 미리보기).</summary>
        /// <param name="floor">대상 층 정의.</param>
        private static void DrawWarnings(FloorDefinitionSO floor)
        {
            var problems = new List<string>();
            if (!Mathf.Approximately(floor.CellMeters, MapTemplateCatalog.CellMeters))
            {
                problems.Add($"CellMeters({floor.CellMeters}) ≠ 카탈로그({MapTemplateCatalog.CellMeters})");
            }

            if (floor.FloorHeight <= 0f)
            {
                problems.Add("FloorHeight ≤ 0");
            }

            int emptyTemplates = 0;
            for (int i = 0; i < floor.Templates.Length; i++)
            {
                if (floor.Templates[i] == null)
                {
                    emptyTemplates++;
                }
            }

            if (emptyTemplates > 0)
            {
                problems.Add($"빈 템플릿 슬롯 {emptyTemplates}개");
            }

            if (floor.SealWallPrefab == null || floor.DoorPrefab == null || floor.ReturnExitPrefab == null)
            {
                problems.Add("환경 프리팹 결손(린트가 조립 거부)");
            }

            FloorGenParams genParams = floor.GenParams;
            void CheckRange(int min, int max, string label)
            {
                if (min > max)
                {
                    problems.Add($"{label} 역전({min}>{max})");
                }
            }

            CheckRange(genParams.RoomsTotalMin, genParams.RoomsTotalMax, "방 예산");
            CheckRange(genParams.ZombieDensitySafeMin, genParams.ZombieDensitySafeMax, "안전 등급 좀비");
            CheckRange(genParams.ZombieDensityMidMin, genParams.ZombieDensityMidMax, "중간 등급 좀비");
            CheckRange(genParams.ZombieDensityDangerMin, genParams.ZombieDensityDangerMax, "위험 등급 좀비");
            CheckRange(genParams.HerdZombieCountMin, genParams.HerdZombieCountMax, "위장 무대 무리");

            if (problems.Count > 0)
            {
                EditorGUILayout.HelpBox("미해결: " + string.Join(" · ", problems), MessageType.Warning);
            }
        }

        /// <summary>GenParams 하위 필드 그룹을 폴드아웃으로 그린다(기본 펼침).</summary>
        /// <param name="genParams">GenParams 프로퍼티.</param>
        /// <param name="title">그룹 제목.</param>
        /// <param name="fields">필드명 목록.</param>
        private void DrawGroup(SerializedProperty genParams, string title, string[] fields)
        {
            if (!foldouts.TryGetValue(title, out bool open))
            {
                open = true;
            }

            open = EditorGUILayout.Foldout(open, title, true);
            foldouts[title] = open;
            if (!open)
            {
                return;
            }

            EditorGUI.indentLevel++;
            foreach (string field in fields)
            {
                SerializedProperty property = genParams.FindPropertyRelative(field);
                if (property != null)
                {
                    EditorGUILayout.PropertyField(property);
                }
            }

            EditorGUI.indentLevel--;
        }

        /// <summary>필수 프리팹 슬롯을 그린다 — 미배선이면 붉게 강조.</summary>
        /// <param name="propertyName">직렬화 필드명.</param>
        /// <param name="missing">미배선 여부.</param>
        private void DrawRequiredObject(string propertyName, bool missing)
        {
            Color prev = GUI.color;
            if (missing)
            {
                GUI.color = missingTint;
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty(propertyName));
            GUI.color = prev;
        }
    }
}
