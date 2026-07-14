using Border.Localization;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 구글 시트에서 로컬라이즈를 가져와 LocalizationTable을 갱신하는 EditorWindow이다.
/// 설정 SO의 커스텀 인스펙터를 창 안에 임베드해 인스펙터와 동일한 항목을 편집한다.
/// </summary>
public class LocalizationSheetWindow : EditorWindow
{
    [SerializeField] private LocalizationSheetImporterSO settings;

    private Editor settingsEditor;
    private Vector2 scrollPosition;
    private string statusMessage;
    private MessageType statusType = MessageType.None;

    /// <summary>
    /// 로컬라이즈 시트 윈도우를 연다. Tools 메뉴와 상단 툴바 버튼이 함께 사용한다.
    /// </summary>
    [MenuItem("Tools/Localization/로컬라이즈 시트 윈도우")]
    internal static void Open()
    {
        LocalizationSheetWindow window = GetWindow<LocalizationSheetWindow>("로컬라이즈 시트");
        window.minSize = new Vector2(420f, 320f);
        window.Show();
    }

    /// <summary>
    /// 창이 열릴 때 설정 SO를 찾아(없으면 생성해) 연결한다.
    /// </summary>
    private void OnEnable()
    {
        if (settings == null)
        {
            settings = LocalizationSheetImporter.LoadOrCreateSettings();
        }
    }

    /// <summary>
    /// 임베드한 커스텀 인스펙터를 파기한다.
    /// </summary>
    private void OnDisable()
    {
        if (settingsEditor != null)
        {
            DestroyImmediate(settingsEditor);
            settingsEditor = null;
        }
    }

    /// <summary>
    /// 설정 필드, 시트 설정, Secret 상태, 임포트 버튼, 현재 테이블 현황을 그린다.
    /// </summary>
    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        EditorGUILayout.LabelField("구글 시트 → LocalizationTable", EditorStyles.boldLabel);
        EditorGUILayout.Space(4f);

        using (EditorGUI.ChangeCheckScope changeCheck = new EditorGUI.ChangeCheckScope())
        {
            settings = (LocalizationSheetImporterSO)EditorGUILayout.ObjectField(
                "설정 에셋", settings, typeof(LocalizationSheetImporterSO), false);

            if (changeCheck.changed && settingsEditor != null)
            {
                DestroyImmediate(settingsEditor);
                settingsEditor = null;
            }
        }

        if (settings == null)
        {
            EditorGUILayout.HelpBox("설정 에셋이 없습니다. 아래 버튼으로 생성하세요.", MessageType.Warning);
            if (GUILayout.Button("설정 에셋 생성"))
            {
                settings = LocalizationSheetImporter.LoadOrCreateSettings();
            }

            EditorGUILayout.EndScrollView();
            return;
        }

        EditorGUILayout.Space(6f);
        DrawSettingsInspector();

        EditorGUILayout.Space(8f);
        LocalizationSheetGUI.DrawSecretSection();

        EditorGUILayout.Space(4f);
        LocalizationSheetGUI.DrawImportButton(settings, HandleImportCompleted);

        EditorGUILayout.Space(8f);
        DrawStatus();
        DrawTableSummary();

        EditorGUILayout.EndScrollView();
    }

    /// <summary>
    /// 설정 SO의 필드를 창 안에 그린다.
    /// Secret 상태와 임포트 버튼은 창이 직접 그리므로 인스펙터의 기본 필드만 사용한다.
    /// </summary>
    private void DrawSettingsInspector()
    {
        if (settingsEditor == null)
        {
            settingsEditor = Editor.CreateEditor(settings);
        }

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            settingsEditor.DrawDefaultInspector();
        }
    }

    /// <summary>
    /// 마지막 임포트 결과 메시지를 표시한다.
    /// </summary>
    private void DrawStatus()
    {
        if (string.IsNullOrEmpty(statusMessage))
        {
            return;
        }

        EditorGUILayout.HelpBox(statusMessage, statusType);
    }

    /// <summary>
    /// 출력 경로의 LocalizationTable 에셋 현황(엔트리 수)을 표시한다.
    /// </summary>
    private void DrawTableSummary()
    {
        LocalizationTable table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(settings.OutputAssetPath);
        if (table == null)
        {
            EditorGUILayout.HelpBox("아직 생성된 LocalizationTable이 없습니다.", MessageType.None);
            return;
        }

        EditorGUILayout.LabelField("현재 테이블", $"{table.Entries.Count}개 엔트리");
        if (GUILayout.Button("테이블 선택"))
        {
            Selection.activeObject = table;
            EditorGUIUtility.PingObject(table);
        }
    }

    /// <summary>
    /// 임포트 완료 결과를 상태 메시지로 반영하고 창을 다시 그린다.
    /// </summary>
    /// <param name="result">임포트 결과</param>
    private void HandleImportCompleted(LocalizationImportResult result)
    {
        statusMessage = result.message;
        statusType = result.success ? MessageType.Info : MessageType.Warning;
        Repaint();
    }
}
