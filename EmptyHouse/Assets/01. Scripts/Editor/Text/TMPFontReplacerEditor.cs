using System.Collections.Generic;
using Border.Core;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// TMPFontReplacerSO 인스펙터에 검사(Dry Run)/일괄 교체 버튼을 붙이고, 실제 폰트 교체를 수행하는 커스텀 인스펙터이다.
/// 프리팹 인스턴스의 TMP는 소스 체인(변형/중첩 포함)의 원본 프리팹 에셋을 직접 수정하고, 씬 인스턴스의 폰트 오버라이드는 되돌려 상속시킨다.
/// </summary>
[CustomEditor(typeof(TMPFontReplacerSO))]
public class TMPFontReplacerEditor : Editor
{
    /// <summary>
    /// 기본 인스펙터 아래에 검사/교체 버튼을 그린다.
    /// </summary>
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        TMPFontReplacerSO settings = (TMPFontReplacerSO)target;

        EditorGUILayout.Space(8f);
        using (new EditorGUI.DisabledScope(settings.TargetFont == null))
        {
            if (GUILayout.Button("검사 (Dry Run)")) Run(settings, dryRun: true);
            EditorGUILayout.Space(2f);
            if (GUILayout.Button("폰트 일괄 교체")) Run(settings, dryRun: false);
        }

        if (settings.TargetFont == null)
            EditorGUILayout.HelpBox("Target Font를 지정해야 실행할 수 있다.", MessageType.Info);
    }

    /// <summary>
    /// 설정 SO를 찾아(없으면 안내) 선택해 연다.
    /// </summary>
    [MenuItem("Tools/TMP/폰트 교체 설정 열기")]
    internal static void OpenSettings()
    {
        TMPFontReplacerSO so = FindSettings();
        if (so == null)
        {
            Log.D("[TMPFontReplacer] 설정 에셋이 없다. Create > Tools > TMP Font Replacer로 생성한다.");
            return;
        }
        Selection.activeObject = so;
        EditorGUIUtility.PingObject(so);
    }

    /// <summary>
    /// 프로젝트에서 첫 번째 TMPFontReplacerSO 에셋을 찾아 반환한다. 없으면 null.
    /// </summary>
    /// <returns>찾은 설정 SO, 없으면 null.</returns>
    internal static TMPFontReplacerSO FindSettings()
    {
        string[] guids = AssetDatabase.FindAssets("t:TMPFontReplacerSO");
        if (guids.Length == 0) return null;
        return AssetDatabase.LoadAssetAtPath<TMPFontReplacerSO>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    /// <summary>
    /// 설정에 따라 열린 씬들의 TMP 폰트를 검사하거나 교체한다.
    /// </summary>
    /// <param name="settings">교체 설정 SO.</param>
    /// <param name="dryRun">true면 실제 수정 없이 대상 수만 집계/로그한다.</param>
    internal static void Run(TMPFontReplacerSO settings, bool dryRun)
    {
        if (settings.TargetFont == null) return;

        Material desiredMat = settings.FontMaterial != null ? settings.FontMaterial : settings.TargetFont.material;

        List<TMP_Text> targets = CollectSceneTexts(settings);

        // 프리팹 소스 편집 대상: 씬 인스턴스에서 역추적한 소스 체인의 프리팹 에셋 경로 모음
        HashSet<string> prefabPaths = new HashSet<string>();
        int sceneEditCount = 0;   // 씬 오브젝트에 직접 반영(순수 씬 or 오버라이드)
        int revertCount = 0;      // 프리팹 상속을 위해 인스턴스 오버라이드를 되돌린 수
        int skipped = 0;          // 필터 제외 or 이미 목표 폰트

        HashSet<Scene> dirtyScenes = new HashSet<Scene>();

        foreach (TMP_Text tmp in targets)
        {
            if (!PassesFilter(tmp, settings)) { skipped++; continue; }

            bool isInstance = PrefabUtility.IsPartOfPrefabInstance(tmp);

            if (isInstance && settings.EditPrefabSource)
            {
                CollectSourcePaths(tmp, prefabPaths);
                if (!dryRun && RevertFontOverride(tmp))
                {
                    revertCount++;
                    dirtyScenes.Add(tmp.gameObject.scene);
                }
                continue;
            }

            // 순수 씬 오브젝트, 또는 프리팹 소스 편집을 끈 경우: 인스턴스에 직접 반영
            if (AlreadyTarget(tmp, settings.TargetFont, desiredMat)) { skipped++; continue; }

            if (!dryRun)
            {
                Undo.RecordObject(tmp, "Replace TMP Font");
                ApplyFont(tmp, settings.TargetFont, settings.FontMaterial);
                EditorUtility.SetDirty(tmp);
                dirtyScenes.Add(tmp.gameObject.scene);
            }
            sceneEditCount++;
        }

        int prefabTextCount = 0;
        if (!dryRun)
        {
            foreach (string path in prefabPaths)
                prefabTextCount += EditPrefabAsset(path, settings, desiredMat);

            foreach (Scene s in dirtyScenes)
                EditorSceneManager.MarkSceneDirty(s);

            if (prefabPaths.Count > 0) AssetDatabase.SaveAssets();
        }

        if (dryRun)
        {
            Log.D($"[TMPFontReplacer] (Dry Run) 씬 직접 대상 {sceneEditCount}개, 프리팹 에셋 {prefabPaths.Count}개, 스킵 {skipped}개. 실제 변경 없음.");
        }
        else
        {
            Log.D($"[TMPFontReplacer] 완료 — 씬 직접 {sceneEditCount}개, 프리팹 에셋 {prefabPaths.Count}개(내부 TMP {prefabTextCount}개) 수정, 인스턴스 오버라이드 복원 {revertCount}개, 스킵 {skipped}개.");
        }
    }

    /// <summary>
    /// 대상 씬(설정에 따라 열린 전체/활성)의 TMP_Text를 비활성 포함 여부에 맞춰 수집한다.
    /// </summary>
    /// <param name="settings">교체 설정 SO.</param>
    /// <returns>수집된 TMP_Text 목록.</returns>
    private static List<TMP_Text> CollectSceneTexts(TMPFontReplacerSO settings)
    {
        List<TMP_Text> result = new List<TMP_Text>();
        List<Scene> scenes = new List<Scene>();

        if (settings.AllOpenScenes)
        {
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                Scene s = EditorSceneManager.GetSceneAt(i);
                if (s.isLoaded) scenes.Add(s);
            }
        }
        else
        {
            scenes.Add(SceneManager.GetActiveScene());
        }

        foreach (Scene s in scenes)
            foreach (GameObject root in s.GetRootGameObjects())
                result.AddRange(root.GetComponentsInChildren<TMP_Text>(settings.IncludeInactive));

        return result;
    }

    /// <summary>
    /// 필터 목록이 비어 있으면 통과, 아니면 현재 폰트가 목록에 있을 때만 통과한다.
    /// </summary>
    /// <param name="tmp">검사할 TMP_Text.</param>
    /// <param name="settings">교체 설정 SO.</param>
    /// <returns>교체 대상이면 true.</returns>
    private static bool PassesFilter(TMP_Text tmp, TMPFontReplacerSO settings)
    {
        if (settings.OnlyReplaceFonts.Count == 0) return true;
        for (int i = 0; i < settings.OnlyReplaceFonts.Count; i++)
            if (settings.OnlyReplaceFonts[i] == tmp.font) return true;
        return false;
    }

    /// <summary>
    /// 이미 목표 폰트/머티리얼이면 true(스킵 대상).
    /// </summary>
    /// <param name="tmp">검사할 TMP_Text.</param>
    /// <param name="font">목표 폰트.</param>
    /// <param name="desiredMat">목표 머티리얼.</param>
    /// <returns>변경 불필요면 true.</returns>
    private static bool AlreadyTarget(TMP_Text tmp, TMP_FontAsset font, Material desiredMat)
    {
        return tmp.font == font && tmp.fontSharedMaterial == desiredMat;
    }

    /// <summary>
    /// TMP에 목표 폰트를 적용한다. 커스텀 머티리얼이 있으면 그것을, 없으면 폰트 기본 머티리얼을 쓴다.
    /// </summary>
    /// <param name="tmp">적용 대상 TMP_Text.</param>
    /// <param name="font">목표 폰트.</param>
    /// <param name="customMat">커스텀 머티리얼(없으면 null).</param>
    private static void ApplyFont(TMP_Text tmp, TMP_FontAsset font, Material customMat)
    {
        tmp.font = font; // 세터가 fontSharedMaterial을 폰트 기본 머티리얼로 함께 갱신한다.
        if (customMat != null) tmp.fontSharedMaterial = customMat;
    }

    /// <summary>
    /// 프리팹 인스턴스 컴포넌트의 소스 체인(변형→베이스)에 있는 모든 프리팹 에셋 경로를 모은다.
    /// </summary>
    /// <param name="instance">씬 프리팹 인스턴스의 TMP_Text.</param>
    /// <param name="paths">경로를 누적할 집합.</param>
    private static void CollectSourcePaths(TMP_Text instance, HashSet<string> paths)
    {
        Object next = PrefabUtility.GetCorrespondingObjectFromSource(instance);
        while (next != null)
        {
            string p = AssetDatabase.GetAssetPath(next);
            if (!string.IsNullOrEmpty(p)) paths.Add(p);
            next = PrefabUtility.GetCorrespondingObjectFromSource(next);
        }
    }

    /// <summary>
    /// 인스턴스의 폰트/머티리얼 프로퍼티 오버라이드를 되돌려 프리팹 소스 값을 상속하게 한다.
    /// </summary>
    /// <param name="tmp">씬 프리팹 인스턴스의 TMP_Text.</param>
    /// <returns>오버라이드가 하나라도 있어 되돌렸으면 true.</returns>
    private static bool RevertFontOverride(TMP_Text tmp)
    {
        bool reverted = false;
        SerializedObject so = new SerializedObject(tmp);
        reverted |= RevertIfOverridden(so, "m_fontAsset");
        reverted |= RevertIfOverridden(so, "m_sharedMaterial");
        return reverted;
    }

    /// <summary>
    /// 지정 프로퍼티가 인스턴스 오버라이드면 되돌린다.
    /// </summary>
    /// <param name="so">인스턴스 컴포넌트의 SerializedObject.</param>
    /// <param name="propName">프로퍼티 이름.</param>
    /// <returns>되돌렸으면 true.</returns>
    private static bool RevertIfOverridden(SerializedObject so, string propName)
    {
        SerializedProperty prop = so.FindProperty(propName);
        if (prop == null || !prop.prefabOverride) return false;
        PrefabUtility.RevertPropertyOverride(prop, InteractionMode.AutomatedAction);
        return true;
    }

    /// <summary>
    /// 프리팹 에셋 파일을 열어 내부의 모든 TMP 폰트를 목표로 교체하고 저장한다.
    /// </summary>
    /// <param name="path">프리팹 에셋 경로.</param>
    /// <param name="settings">교체 설정 SO.</param>
    /// <param name="desiredMat">목표 머티리얼.</param>
    /// <returns>실제 교체한 TMP 개수.</returns>
    private static int EditPrefabAsset(string path, TMPFontReplacerSO settings, Material desiredMat)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        int count = 0;
        try
        {
            foreach (TMP_Text tmp in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (!PassesFilter(tmp, settings)) continue;
                if (AlreadyTarget(tmp, settings.TargetFont, desiredMat)) continue;
                ApplyFont(tmp, settings.TargetFont, settings.FontMaterial);
                count++;
            }
            if (count > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return count;
    }
}
