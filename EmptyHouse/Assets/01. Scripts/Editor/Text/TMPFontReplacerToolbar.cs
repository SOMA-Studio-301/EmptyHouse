using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

/// <summary>
/// 에디터 상단 메인 툴바 오른쪽에 TMP 폰트 교체 버튼을 등록한다.
/// 클릭 시 검사(Dry Run)/일괄 교체/설정 열기 메뉴를 띄운다. 실제 로직은 TMPFontReplacerEditor를 재사용한다.
/// </summary>
public static class TMPFontReplacerToolbar
{
    /// <summary>
    /// 메인 툴바에 표시할 버튼 요소를 생성한다. Unity가 이 정적 메서드를 호출해 요소를 얻는다.
    /// </summary>
    /// <returns>클릭 시 액션 메뉴를 여는 툴바 버튼.</returns>
    [MainToolbarElement("TMP/폰트 교체", defaultDockPosition = MainToolbarDockPosition.Right)]
    private static MainToolbarElement CreateButton()
    {
        MainToolbarContent content = new MainToolbarContent("TMP 폰트", "씬의 TMP 폰트를 일괄 교체한다");
        return new MainToolbarButton(content, ShowMenu);
    }

    /// <summary>
    /// 버튼 클릭 시 검사/교체/설정 열기 항목을 담은 컨텍스트 메뉴를 띄운다.
    /// </summary>
    private static void ShowMenu()
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("검사 (Dry Run)"), false, RunDryRun);
        menu.AddItem(new GUIContent("폰트 일괄 교체"), false, RunReplace);
        menu.AddSeparator(string.Empty);
        menu.AddItem(new GUIContent("설정 열기"), false, TMPFontReplacerEditor.OpenSettings);
        menu.ShowAsContext();
    }

    /// <summary>
    /// 설정을 찾아 검사(Dry Run)를 실행한다.
    /// </summary>
    private static void RunDryRun()
    {
        TMPFontReplacerSO settings = ResolveSettings();
        if (settings == null) return;
        TMPFontReplacerEditor.Run(settings, dryRun: true);
    }

    /// <summary>
    /// 설정을 찾아 확인 다이얼로그 후 실제 교체를 실행한다. 프리팹 에셋 수정은 Undo가 불가함을 경고한다.
    /// </summary>
    private static void RunReplace()
    {
        TMPFontReplacerSO settings = ResolveSettings();
        if (settings == null) return;

        bool ok = EditorUtility.DisplayDialog(
            "TMP 폰트 일괄 교체",
            $"열린 씬의 TMP 폰트를 '{settings.TargetFont.name}'(으)로 교체한다.\n\n" +
            "프리팹 에셋 수정은 Undo로 되돌릴 수 없다. 먼저 '검사(Dry Run)'로 대상을 확인했는가?",
            "교체 실행", "취소");
        if (!ok) return;

        TMPFontReplacerEditor.Run(settings, dryRun: false);
    }

    /// <summary>
    /// 설정 SO를 찾고 Target Font 유효성을 검사한다. 문제가 있으면 안내 후 null을 반환한다.
    /// </summary>
    /// <returns>실행 가능한 설정 SO, 아니면 null.</returns>
    private static TMPFontReplacerSO ResolveSettings()
    {
        TMPFontReplacerSO settings = TMPFontReplacerEditor.FindSettings();
        if (settings == null)
        {
            EditorUtility.DisplayDialog("TMP 폰트 교체", "설정 에셋이 없다. Create > Tools > TMP Font Replacer로 생성한다.", "확인");
            return null;
        }
        if (settings.TargetFont == null)
        {
            EditorUtility.DisplayDialog("TMP 폰트 교체", "설정의 Target Font가 비어 있다. 폰트를 지정한다.", "확인");
            TMPFontReplacerEditor.OpenSettings();
            return null;
        }
        return settings;
    }
}
