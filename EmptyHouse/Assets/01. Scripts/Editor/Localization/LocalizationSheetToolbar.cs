using UnityEditor.Toolbars;

/// <summary>
/// 에디터 상단 메인 툴바에 로컬라이즈 시트 윈도우를 여는 버튼을 추가한다.
/// </summary>
public static class LocalizationSheetToolbar
{
    /// <summary>
    /// 메인 툴바 우측에 배치할 '로컬라이즈' 버튼을 생성한다.
    /// </summary>
    /// <returns>클릭 시 로컬라이즈 시트 윈도우를 여는 툴바 버튼</returns>
    [MainToolbarElement("Localization/OpenSheetWindow", defaultDockPosition = MainToolbarDockPosition.Right)]
    public static MainToolbarButton CreateOpenWindowButton()
    {
        MainToolbarContent content = new MainToolbarContent("로컬라이즈", "구글 시트에서 로컬라이즈를 가져오는 창을 연다.");
        return new MainToolbarButton(content, LocalizationSheetWindow.Open);
    }
}
