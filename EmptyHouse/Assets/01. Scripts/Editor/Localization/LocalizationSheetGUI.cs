using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 인스펙터와 EditorWindow가 공유하는 로컬라이즈 임포트 GUI 조각과 실행 진입점이다.
/// </summary>
public static class LocalizationSheetGUI
{
    /// <summary>임포트가 진행 중인지 여부이다. 중복 실행을 막는 데 쓴다.</summary>
    public static bool IsImporting { get; private set; }

    /// <summary>
    /// Secret.json 경로와 존재 여부를 표시하고, 없으면 템플릿 생성 버튼을 그린다.
    /// </summary>
    public static void DrawSecretSection()
    {
        if (LocalizationSheetImporter.HasSecret)
        {
            EditorGUILayout.HelpBox($"Secret: {LocalizationSheetImporter.SecretPath}", MessageType.None);
            return;
        }

        EditorGUILayout.HelpBox($"Secret.json이 없습니다: {LocalizationSheetImporter.SecretPath}", MessageType.Warning);
        if (GUILayout.Button("Secret.json 생성"))
        {
            LocalizationSheetImporter.CreateSecretTemplate();
        }
    }

    /// <summary>
    /// '로컬라이즈 업데이트' 버튼을 그리고, 눌리면 임포트를 실행한다.
    /// </summary>
    /// <param name="settings">임포트 설정 SO</param>
    /// <param name="onCompleted">임포트 완료 콜백. 필요 없으면 null</param>
    public static void DrawImportButton(LocalizationSheetImporterSO settings, Action<LocalizationImportResult> onCompleted)
    {
        bool disabled = settings == null || IsImporting || EditorApplication.isCompiling;
        using (new EditorGUI.DisabledScope(disabled))
        {
            if (GUILayout.Button(IsImporting ? "가져오는 중..." : "로컬라이즈 업데이트", GUILayout.Height(28f)))
            {
                RunImport(settings, onCompleted);
            }
        }
    }

    /// <summary>
    /// 임포트를 비동기로 실행하고 완료 시 콜백을 호출한다.
    /// </summary>
    /// <param name="settings">임포트 설정 SO</param>
    /// <param name="onCompleted">임포트 완료 콜백. 필요 없으면 null</param>
    public static async void RunImport(LocalizationSheetImporterSO settings, Action<LocalizationImportResult> onCompleted)
    {
        if (IsImporting)
        {
            return;
        }

        IsImporting = true;
        try
        {
            LocalizationImportResult result = await LocalizationSheetImporter.ImportAsync(settings);
            onCompleted?.Invoke(result);
        }
        finally
        {
            IsImporting = false;
        }
    }
}
