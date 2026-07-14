using UnityEditor;
using UnityEngine;

/// <summary>
/// LocalizationSheetImporterSO 인스펙터에 Secret 상태와 '로컬라이즈 업데이트' 버튼을 추가하는 커스텀 인스펙터이다.
/// </summary>
[CustomEditor(typeof(LocalizationSheetImporterSO))]
public class LocalizationSheetImporterEditor : Editor
{
    /// <summary>
    /// 기본 인스펙터에 Secret 상태 표시와 임포트 버튼을 덧붙여 그린다.
    /// </summary>
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8f);
        LocalizationSheetGUI.DrawSecretSection();

        EditorGUILayout.Space(4f);
        LocalizationSheetGUI.DrawImportButton((LocalizationSheetImporterSO)target, null);
    }

    /// <summary>
    /// 설정 SO를 찾아(없으면 생성해) 임포트를 실행하는 메뉴 항목이다.
    /// </summary>
    [MenuItem("Tools/Localization/로컬라이즈 업데이트")]
    private static void ImportFromMenu()
    {
        LocalizationSheetGUI.RunImport(LocalizationSheetImporter.LoadOrCreateSettings(), null);
    }

    /// <summary>
    /// 설정 SO를 프로젝트 창에서 선택해 인스펙터로 연다.
    /// </summary>
    [MenuItem("Tools/Localization/임포터 설정 열기")]
    private static void OpenSettings()
    {
        LocalizationSheetImporterSO settings = LocalizationSheetImporter.LoadOrCreateSettings();
        Selection.activeObject = settings;
        EditorGUIUtility.PingObject(settings);
    }
}
