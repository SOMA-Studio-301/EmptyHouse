using UnityEngine;

/// <summary>
/// 구글 시트 → LocalizationTable 임포트 설정을 보관하는 에디터 전용 ScriptableObject이다.
/// </summary>
[CreateAssetMenu(fileName = "LocalizationSheetImporter", menuName = "Localization/Localization Sheet Importer")]
public class LocalizationSheetImporterSO : ScriptableObject
{
    [Header("Source")]
    [Tooltip("true: 구글 시트에서 다운로드, false: 로컬 캐시 JSON 사용")]
    [SerializeField] private bool useGoogleSheet = true;

    [Tooltip("가져올 시트(탭) 이름")]
    [SerializeField] private string sheetName = "Localization";

    [Header("Output")]
    [SerializeField] private string outputAssetPath = "Assets/03. ScriptableObjects/Localization/LocalizationTable.asset";
    [SerializeField] private bool overwriteExistingAsset = true;

    [Header("Validation")]
    [Tooltip("필수 언어 코드. '/' 기준으로 분리. 시트 언어 컬럼과 일치해야 한다. 예: ko/en/jp/cn/tw")]
    [SerializeField] private string requiredLanguageCodes = "ko/en/jp/cn/tw";

    /// <summary>구글 시트에서 받아올지(true), 로컬 캐시 JSON을 쓸지(false) 여부이다.</summary>
    public bool UseGoogleSheet => useGoogleSheet;

    /// <summary>가져올 시트(탭) 이름이다.</summary>
    public string SheetName => sheetName;

    /// <summary>생성/갱신할 LocalizationTable 에셋 경로이다.</summary>
    public string OutputAssetPath => outputAssetPath;

    /// <summary>기존 에셋이 있을 때 덮어쓸지 여부이다.</summary>
    public bool OverwriteExistingAsset => overwriteExistingAsset;

    /// <summary>'/'로 구분된 필수 언어 코드 문자열이다.</summary>
    public string RequiredLanguageCodes => requiredLanguageCodes;
}
