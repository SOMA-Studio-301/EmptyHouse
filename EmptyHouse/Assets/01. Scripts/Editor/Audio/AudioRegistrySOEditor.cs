using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// AudioRegistrySO 인스펙터를 AudioId 번호대(SFX/Ambient/BGM) 그룹 테이블로 그리는 커스텀 에디터.
/// 중복 ID·빈 참조 강조, ID 검색, 미등록 ID 추가, ID 정렬을 지원한다. 직렬화 구조는 건드리지 않는다.
/// </summary>
[CustomEditor(typeof(AudioRegistrySO))]
public class AudioRegistrySOEditor : Editor
{
    private const string foldKeyPrefix = "AudioRegistrySOEditor.Fold";                // 그룹 폴드아웃 SessionState 키 접두사
    private static readonly string[] groupNames = { "SFX (100~)", "Ambient (500~)", "BGM (1000~)", "기타 (None/범위 밖)" }; // 그룹 표시 이름. GroupOf 반환 인덱스와 짝
    private static readonly Color duplicateTint = new Color(1f, 0.30f, 0.30f, 0.16f); // 중복 ID 행 배경색
    private static readonly Color missingTint = new Color(1f, 0.80f, 0.25f, 0.12f);   // 빈 참조 행 배경색

    private string searchFilter = string.Empty; // ID 이름 검색 필터

    /// <summary>툴바·문제 요약·그룹 테이블 순으로 인스펙터를 그린다.</summary>
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        SerializedProperty entries = serializedObject.FindProperty("entries");

        HashSet<int> duplicateIds = FindDuplicateIds(entries);

        DrawToolbar(entries);
        DrawProblemSummary(entries, duplicateIds);

        List<int>[] buckets = BuildBuckets(entries);
        int deleteIndex = -1;
        for (int g = 0; g < groupNames.Length; g++)
        {
            if (g == 3 && buckets[g].Count == 0) continue; // 기타 그룹은 잘못된 데이터가 있을 때만 노출
            DrawGroup(g, buckets[g], entries, duplicateIds, ref deleteIndex);
        }

        if (deleteIndex >= 0)
        {
            entries.DeleteArrayElementAtIndex(deleteIndex);
        }

        serializedObject.ApplyModifiedProperties();
    }

    /// <summary>검색 필드와 ID 정렬 버튼이 있는 상단 툴바를 그린다.</summary>
    /// <param name="entries">엔트리 리스트 프로퍼티.</param>
    private void DrawToolbar(SerializedProperty entries)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            searchFilter = EditorGUILayout.TextField(searchFilter, EditorStyles.toolbarSearchField);
            if (GUILayout.Button("ID 정렬", EditorStyles.toolbarButton, GUILayout.Width(56f)))
            {
                SortEntriesById(entries);
            }
        }
        EditorGUILayout.Space(2f);
    }

    /// <summary>중복 ID·빈 참조·미등록 ID 를 상단 HelpBox 로 요약한다.</summary>
    /// <param name="entries">엔트리 리스트 프로퍼티.</param>
    /// <param name="duplicateIds">중복된 ID 값 집합.</param>
    private void DrawProblemSummary(SerializedProperty entries, HashSet<int> duplicateIds)
    {
        if (duplicateIds.Count > 0)
        {
            string names = string.Join(", ", duplicateIds.OrderBy(d => d).Select(d => ((AudioId)d).ToString()));
            EditorGUILayout.HelpBox($"중복 AudioId: {names}", MessageType.Error);
        }

        int missing = 0;
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty e = entries.GetArrayElementAtIndex(i);
            if (e.FindPropertyRelative("Cue").objectReferenceValue == null ||
                e.FindPropertyRelative("Configuration").objectReferenceValue == null)
            {
                missing++;
            }
        }
        if (missing > 0)
        {
            EditorGUILayout.HelpBox($"Cue/Configuration 이 비어 있는 엔트리 {missing}개", MessageType.Warning);
        }

        int unregistered = GetUnregisteredIds(entries).Count();
        if (unregistered > 0)
        {
            EditorGUILayout.HelpBox($"미등록 AudioId {unregistered}개 — 그룹 헤더의 + 버튼으로 추가", MessageType.Info);
        }
    }

    /// <summary>한 그룹을 헤더(폴드아웃·개수·+ 버튼)와 테이블로 그린다. 검색 중엔 매치 없는 그룹을 숨긴다.</summary>
    /// <param name="group">그룹 인덱스(GroupOf 기준).</param>
    /// <param name="indices">이 그룹에 속한 엔트리 인덱스 목록.</param>
    /// <param name="entries">엔트리 리스트 프로퍼티.</param>
    /// <param name="duplicateIds">중복된 ID 값 집합.</param>
    /// <param name="deleteIndex">삭제 요청된 엔트리 인덱스(없으면 -1) 참조.</param>
    private void DrawGroup(int group, List<int> indices, SerializedProperty entries, HashSet<int> duplicateIds, ref int deleteIndex)
    {
        bool searching = !string.IsNullOrEmpty(searchFilter);
        List<int> visible = searching
            ? indices.Where(i => MatchesFilter(entries, i)).ToList()
            : indices;
        if (searching && visible.Count == 0) return;

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            bool fold;
            using (new EditorGUILayout.HorizontalScope())
            {
                bool prev = SessionState.GetBool(foldKeyPrefix + group, true);
                fold = EditorGUILayout.Foldout(prev, $"{groupNames[group]} — {indices.Count}", true, EditorStyles.foldoutHeader);
                if (fold != prev) SessionState.SetBool(foldKeyPrefix + group, fold);

                if (GUILayout.Button("+", GUILayout.Width(24f)))
                {
                    ShowAddMenu(group, entries);
                }
            }

            if (!fold && !searching) return; // 검색 중엔 폴드 상태와 무관하게 매치를 보여준다

            if (visible.Count == 0)
            {
                EditorGUILayout.LabelField("엔트리 없음", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            DrawTableHeader();
            for (int k = 0; k < visible.Count; k++)
            {
                DrawRow(entries, visible[k], k, duplicateIds, ref deleteIndex);
            }
        }
    }

    /// <summary>테이블 열 제목(ID/Cue/Configuration)을 행과 같은 폭으로 그린다.</summary>
    private static void DrawTableHeader()
    {
        GetColumnWidths(out float idW, out float objW);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("ID", EditorStyles.miniBoldLabel, GUILayout.Width(idW));
            GUILayout.Label("Cue", EditorStyles.miniBoldLabel, GUILayout.Width(objW));
            GUILayout.Label("Configuration", EditorStyles.miniBoldLabel, GUILayout.Width(objW));
            GUILayout.Space(26f);
        }
    }

    /// <summary>엔트리 한 개를 한 줄(ID 드롭다운·Cue·Configuration·삭제 버튼)로 그린다.</summary>
    /// <param name="entries">엔트리 리스트 프로퍼티.</param>
    /// <param name="index">그릴 엔트리의 리스트 인덱스.</param>
    /// <param name="visualRow">그룹 내 표시 순번(줄무늬 배경용).</param>
    /// <param name="duplicateIds">중복된 ID 값 집합.</param>
    /// <param name="deleteIndex">삭제 요청된 엔트리 인덱스(없으면 -1) 참조.</param>
    private static void DrawRow(SerializedProperty entries, int index, int visualRow, HashSet<int> duplicateIds, ref int deleteIndex)
    {
        SerializedProperty entry = entries.GetArrayElementAtIndex(index);
        SerializedProperty id = entry.FindPropertyRelative("Id");
        SerializedProperty cue = entry.FindPropertyRelative("Cue");
        SerializedProperty cfg = entry.FindPropertyRelative("Configuration");

        Rect rowRect = EditorGUILayout.BeginHorizontal();
        Color? tint = RowTint(id, cue, cfg, visualRow, duplicateIds);
        if (tint.HasValue) EditorGUI.DrawRect(rowRect, tint.Value);

        GetColumnWidths(out float idW, out float objW);
        EditorGUILayout.PropertyField(id, GUIContent.none, GUILayout.Width(idW));
        EditorGUILayout.PropertyField(cue, GUIContent.none, GUILayout.Width(objW));
        EditorGUILayout.PropertyField(cfg, GUIContent.none, GUILayout.Width(objW));
        if (GUILayout.Button("−", GUILayout.Width(22f)))
        {
            deleteIndex = index;
        }
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>행 배경색을 정한다. 우선순위: 중복 ID(빨강) → 빈 참조(노랑) → 홀수 줄무늬 → 없음.</summary>
    /// <param name="id">Id 프로퍼티.</param>
    /// <param name="cue">Cue 프로퍼티.</param>
    /// <param name="cfg">Configuration 프로퍼티.</param>
    /// <param name="visualRow">그룹 내 표시 순번.</param>
    /// <param name="duplicateIds">중복된 ID 값 집합.</param>
    /// <returns>배경색. 칠하지 않으면 null.</returns>
    private static Color? RowTint(SerializedProperty id, SerializedProperty cue, SerializedProperty cfg, int visualRow, HashSet<int> duplicateIds)
    {
        if (duplicateIds.Contains(id.intValue)) return duplicateTint;
        if (cue.objectReferenceValue == null || cfg.objectReferenceValue == null) return missingTint;
        if (visualRow % 2 == 1)
        {
            return EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.03f) : new Color(0f, 0f, 0f, 0.04f);
        }
        return null;
    }

    /// <summary>해당 엔트리의 AudioId 이름이 검색 필터에 걸리는지 판단한다(대소문자 무시).</summary>
    /// <param name="entries">엔트리 리스트 프로퍼티.</param>
    /// <param name="index">검사할 엔트리 인덱스.</param>
    /// <returns>필터에 걸리면 true.</returns>
    private bool MatchesFilter(SerializedProperty entries, int index)
    {
        int id = entries.GetArrayElementAtIndex(index).FindPropertyRelative("Id").intValue;
        return ((AudioId)id).ToString().IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>인스펙터 폭에서 열 너비를 계산한다. 헤더와 행이 공유해 정렬을 맞춘다.</summary>
    /// <param name="idW">ID 열 너비.</param>
    /// <param name="objW">Cue/Configuration 열 너비.</param>
    private static void GetColumnWidths(out float idW, out float objW)
    {
        float w = Mathf.Max(EditorGUIUtility.currentViewWidth - 110f, 200f);
        idW = w * 0.40f;
        objW = w * 0.30f;
    }

    /// <summary>엔트리들을 번호대별 그룹 버킷(인덱스 목록)으로 나눈다.</summary>
    /// <param name="entries">엔트리 리스트 프로퍼티.</param>
    /// <returns>그룹 인덱스별 엔트리 인덱스 목록 배열.</returns>
    private static List<int>[] BuildBuckets(SerializedProperty entries)
    {
        List<int>[] buckets = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
        for (int i = 0; i < entries.arraySize; i++)
        {
            int id = entries.GetArrayElementAtIndex(i).FindPropertyRelative("Id").intValue;
            buckets[GroupOf(id)].Add(i);
        }
        return buckets;
    }

    /// <summary>AudioId 값의 번호대 그룹 인덱스를 돌려준다. SFX=0, Ambient=1, BGM=2, 기타=3.</summary>
    /// <param name="id">AudioId 의 정수 값.</param>
    /// <returns>그룹 인덱스.</returns>
    private static int GroupOf(int id)
    {
        return id >= 1000 ? 2 : id >= 500 ? 1 : id >= 100 ? 0 : 3;
    }

    /// <summary>두 번 이상 등장하는 AudioId 값의 집합을 만든다.</summary>
    /// <param name="entries">엔트리 리스트 프로퍼티.</param>
    /// <returns>중복된 ID 값 집합.</returns>
    private static HashSet<int> FindDuplicateIds(SerializedProperty entries)
    {
        HashSet<int> seen = new HashSet<int>();
        HashSet<int> duplicates = new HashSet<int>();
        for (int i = 0; i < entries.arraySize; i++)
        {
            int id = entries.GetArrayElementAtIndex(i).FindPropertyRelative("Id").intValue;
            if (!seen.Add(id)) duplicates.Add(id);
        }
        return duplicates;
    }

    /// <summary>enum 에는 있지만 레지스트리에 없는 AudioId 를 열거한다(None 제외).</summary>
    /// <param name="entries">엔트리 리스트 프로퍼티.</param>
    /// <returns>미등록 AudioId 열거.</returns>
    private static IEnumerable<AudioId> GetUnregisteredIds(SerializedProperty entries)
    {
        HashSet<int> registered = new HashSet<int>();
        for (int i = 0; i < entries.arraySize; i++)
        {
            registered.Add(entries.GetArrayElementAtIndex(i).FindPropertyRelative("Id").intValue);
        }
        foreach (AudioId id in Enum.GetValues(typeof(AudioId)))
        {
            if (id != AudioId.None && !registered.Contains((int)id)) yield return id;
        }
    }

    /// <summary>해당 그룹 번호대의 미등록 AudioId 를 컨텍스트 메뉴로 띄우고, 선택 시 엔트리를 추가한다.</summary>
    /// <param name="group">그룹 인덱스.</param>
    /// <param name="entries">엔트리 리스트 프로퍼티.</param>
    private void ShowAddMenu(int group, SerializedProperty entries)
    {
        GenericMenu menu = new GenericMenu();
        foreach (AudioId id in GetUnregisteredIds(entries).Where(v => GroupOf((int)v) == group).OrderBy(v => (int)v))
        {
            AudioId captured = id;
            menu.AddItem(new GUIContent(captured.ToString()), false, () => AddEntry(captured));
        }
        if (menu.GetItemCount() == 0)
        {
            menu.AddDisabledItem(new GUIContent("남은 미등록 ID 없음"));
        }
        menu.ShowAsContext();
    }

    /// <summary>지정 AudioId 로 빈 엔트리를 추가한다. 메뉴 콜백에서 호출되므로 자체 Update/Apply 를 수행한다.</summary>
    /// <param name="id">추가할 AudioId.</param>
    private void AddEntry(AudioId id)
    {
        serializedObject.Update();
        SerializedProperty entries = serializedObject.FindProperty("entries");
        int index = entries.arraySize;
        entries.InsertArrayElementAtIndex(index);
        SerializedProperty entry = entries.GetArrayElementAtIndex(index);
        entry.FindPropertyRelative("Id").intValue = (int)id;
        entry.FindPropertyRelative("Cue").objectReferenceValue = null;
        entry.FindPropertyRelative("Configuration").objectReferenceValue = null;
        serializedObject.ApplyModifiedProperties();
    }

    /// <summary>엔트리 전체를 ID 오름차순으로 정렬한다(안정 정렬 — 중복 ID 는 기존 순서 유지).</summary>
    /// <param name="entries">엔트리 리스트 프로퍼티.</param>
    private static void SortEntriesById(SerializedProperty entries)
    {
        int n = entries.arraySize;
        List<(int id, Object cue, Object cfg)> items = new List<(int, Object, Object)>(n);
        for (int i = 0; i < n; i++)
        {
            SerializedProperty e = entries.GetArrayElementAtIndex(i);
            items.Add((e.FindPropertyRelative("Id").intValue,
                       e.FindPropertyRelative("Cue").objectReferenceValue,
                       e.FindPropertyRelative("Configuration").objectReferenceValue));
        }

        items = items.OrderBy(t => t.id).ToList();

        for (int i = 0; i < n; i++)
        {
            SerializedProperty e = entries.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("Id").intValue = items[i].id;
            e.FindPropertyRelative("Cue").objectReferenceValue = items[i].cue;
            e.FindPropertyRelative("Configuration").objectReferenceValue = items[i].cfg;
        }
    }
}
