using System.Collections.Generic;
using EmptyHouse.MapGen.Runtime;
using UnityEditor;
using UnityEngine;

namespace EmptyHouse.EditorTools
{
    /// <summary>
    /// 빈집들 카탈로그(MapCatalogSO) 커스텀 인스펙터(M10-1) — 항목을 "정의 · MapId · 필요 기름" 행으로 그리고
    /// 빈 슬롯·MapId 중복/공백을 즉시 경고한다. "프로젝트 정의 전부 추가"로 미등재 정의를 일괄 등재한다.
    /// 서버 선택·mapId 복제 배선은 M10-2 소관 — 여기는 목록 데이터만.
    /// </summary>
    [CustomEditor(typeof(MapCatalogSO))]
    public sealed class MapCatalogSOEditor : UnityEditor.Editor
    {
        private static readonly Color missingTint = new Color(1f, 0.55f, 0.55f); // 결손 행 강조색

        /// <summary>카탈로그 행 목록·검증 경고·일괄 등재 버튼을 그린다.</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var catalog = (MapCatalogSO)target;
            SerializedProperty entries = serializedObject.FindProperty("Entries");

            EditorGUILayout.HelpBox("사용 가능한 빈 집 목록의 전역 단일 출처 — 식별은 MapId(순서는 표시용). FuelCost 는 경제상점 E7 확정 대기 자리.", MessageType.Info);
            DrawWarnings(catalog);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("빈 집", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField("MapId", EditorStyles.miniBoldLabel, GUILayout.Width(110f));
                EditorGUILayout.LabelField("기름", EditorStyles.miniBoldLabel, GUILayout.Width(50f));
                GUILayout.Space(24f);
            }

            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                SerializedProperty definition = entry.FindPropertyRelative("Definition");
                SerializedProperty fuelCost = entry.FindPropertyRelative("FuelCost");
                var definitionAsset = (MapDefinitionSO)definition.objectReferenceValue;

                Color prev = GUI.color;
                if (definitionAsset == null)
                {
                    GUI.color = missingTint;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(definition, GUIContent.none);
                    EditorGUILayout.LabelField(definitionAsset == null ? "—" : definitionAsset.MapId, GUILayout.Width(110f));
                    EditorGUILayout.PropertyField(fuelCost, GUIContent.none, GUILayout.Width(50f));
                    if (GUILayout.Button("×", GUILayout.Width(22f)))
                    {
                        entries.DeleteArrayElementAtIndex(i);
                        GUI.color = prev;
                        break; // 인덱스 무효 — 다음 리페인트에서 이어 그린다
                    }
                }

                GUI.color = prev;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ 항목 추가"))
                {
                    entries.InsertArrayElementAtIndex(entries.arraySize);
                }

                if (GUILayout.Button("프로젝트 정의 전부 추가(미등재만)"))
                {
                    serializedObject.ApplyModifiedProperties();
                    AddAllDefinitions(catalog);
                    serializedObject.Update();
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>빈 슬롯·MapId 공백·중복을 모아 경고한다.</summary>
        /// <param name="catalog">대상 카탈로그.</param>
        private static void DrawWarnings(MapCatalogSO catalog)
        {
            var problems = new List<string>();
            var seenIds = new HashSet<string>();
            for (int i = 0; i < catalog.Entries.Length; i++)
            {
                MapDefinitionSO definition = catalog.Entries[i].Definition;
                if (definition == null)
                {
                    problems.Add($"슬롯 {i} 정의 미배선");
                    continue;
                }

                if (string.IsNullOrEmpty(definition.MapId))
                {
                    problems.Add($"{definition.name} MapId 공백");
                }
                else if (!seenIds.Add(definition.MapId))
                {
                    problems.Add($"MapId 중복 '{definition.MapId}'");
                }
            }

            if (problems.Count > 0)
            {
                EditorGUILayout.HelpBox("미해결: " + string.Join(" · ", problems), MessageType.Warning);
            }
        }

        /// <summary>프로젝트의 모든 MapDefinitionSO 를 스캔해 미등재 정의만 항목으로 추가한다(FuelCost 0).</summary>
        /// <param name="catalog">대상 카탈로그.</param>
        private static void AddAllDefinitions(MapCatalogSO catalog)
        {
            var existing = new HashSet<MapDefinitionSO>();
            foreach (MapCatalogEntry entry in catalog.Entries)
            {
                if (entry.Definition != null)
                {
                    existing.Add(entry.Definition);
                }
            }

            var merged = new List<MapCatalogEntry>(catalog.Entries);
            int added = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:MapDefinitionSO"))
            {
                var definition = AssetDatabase.LoadAssetAtPath<MapDefinitionSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (existing.Contains(definition))
                {
                    continue;
                }

                merged.Add(new MapCatalogEntry { Definition = definition, FuelCost = 0f });
                added++;
            }

            if (added > 0)
            {
                Undo.RecordObject(catalog, "카탈로그 정의 일괄 추가");
                catalog.Entries = merged.ToArray();
                EditorUtility.SetDirty(catalog);
            }
        }
    }
}
