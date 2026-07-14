using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Border.Core;
using Border.Localization;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Secret.json에 등록된 Apps Script URL로 구글 시트 JSON을 받아 LocalizationTable 에셋을 생성/갱신하는 에디터 임포터이다.
/// 시트 스키마: id | src | ko | en | jp | cz (id는 키, src는 번역 원문이라 테이블에 포함하지 않는다)
/// </summary>
public static class LocalizationSheetImporter
{
    /// <summary>시트에서 로컬라이즈 키로 사용할 컬럼명 후보이다.</summary>
    private static readonly string[] KeyAliases = { "id", "key" };

    /// <summary>언어 컬럼으로 취급하지 않는 예약 컬럼명이다.</summary>
    private static readonly HashSet<string> ReservedColumns = new(StringComparer.OrdinalIgnoreCase) { "id", "key", "src", "note" };

    /// <summary>Unity 프로젝트 루트 경로(Assets의 상위)이다.</summary>
    private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath).Replace("\\", "/");

    /// <summary>구글 시트 URL이 담긴 Secret.json 경로이다. Assets 밖이라 빌드에 포함되지 않는다.</summary>
    public static string SecretPath => $"{ProjectRoot}/Secret.json";

    /// <summary>다운로드한 시트 JSON 원문을 보관하는 로컬 캐시 경로이다.</summary>
    public static string CacheJsonPath => $"{ProjectRoot}/GenerateGoogleSheet/LocalizationSheetJson.json";

    /// <summary>Secret.json 존재 여부이다.</summary>
    public static bool HasSecret => File.Exists(SecretPath);

    /// <summary>설정 SO의 기본 생성 경로이다.</summary>
    private const string DefaultSettingsPath = "Assets/03. ScriptableObjects/Localization/LocalizationSheetImporter.asset";

    /// <summary>googleSheetUrl이 비어 있는 Secret.json 템플릿 내용이다.</summary>
    private const string SecretTemplate = "{\n  \"googleSheetUrl\": \"\"\n}\n";

    /// <summary>
    /// 설정을 기준으로 시트를 가져와 파싱/검증 후 LocalizationTable 에셋을 생성하거나 갱신한다.
    /// </summary>
    /// <param name="settings">임포트 설정 SO</param>
    /// <returns>성공 여부와 엔트리 수, 사용자에게 보여줄 메시지</returns>
    public static async Task<LocalizationImportResult> ImportAsync(LocalizationSheetImporterSO settings)
    {
        Log.D($"[LocalizationSheetImporter] Import 시작 (source: {(settings.UseGoogleSheet ? "GoogleSheet" : "LocalJson")}, sheet: {settings.SheetName})");

        string rawJson = settings.UseGoogleSheet
            ? await DownloadSheetJsonAsync()
            : LoadCachedJson();

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return Fail("JSON 데이터가 비어 있습니다. 콘솔 로그를 확인하세요.");
        }

        if (settings.UseGoogleSheet)
        {
            SaveCacheJson(rawJson);
        }

        if (!TryGetRows(rawJson, settings.SheetName, out JArray rows))
        {
            return Fail($"시트 '{settings.SheetName}'를 찾지 못했거나 형식이 올바르지 않습니다.");
        }

        if (rows.Count == 0)
        {
            return Fail($"시트 '{settings.SheetName}'에 데이터 행이 없습니다.");
        }

        string[] requiredCodes = ParseRequiredLanguageCodes(settings.RequiredLanguageCodes);
        bool hasError = BuildEntries(rows, requiredCodes, out List<LocalizationEntry> entries);
        if (hasError)
        {
            return Fail("검증 오류가 있어 에셋 갱신을 중단했습니다. 콘솔 로그를 확인하세요.");
        }

        if (!SaveLocalizationTableAsset(settings, entries))
        {
            return Fail("기존 에셋이 있고 덮어쓰기가 꺼져 있어 갱신을 건너뛰었습니다.");
        }

        return new LocalizationImportResult
        {
            success = true,
            entryCount = entries.Count,
            message = $"{entries.Count}개 엔트리를 갱신했습니다."
        };
    }

    /// <summary>
    /// 실패 결과를 만들고 경고 로그를 남긴다.
    /// </summary>
    /// <param name="message">실패 사유</param>
    /// <returns>실패 결과</returns>
    private static LocalizationImportResult Fail(string message)
    {
        Log.W($"[LocalizationSheetImporter] Import 중단 - {message}");
        return new LocalizationImportResult
        {
            success = false,
            entryCount = 0,
            message = message
        };
    }

    /// <summary>
    /// 프로젝트의 첫 설정 SO를 찾고, 없으면 기본 경로에 생성한다.
    /// </summary>
    /// <returns>임포트 설정 SO</returns>
    public static LocalizationSheetImporterSO LoadOrCreateSettings()
    {
        string[] guids = AssetDatabase.FindAssets($"t:{nameof(LocalizationSheetImporterSO)}");
        if (guids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<LocalizationSheetImporterSO>(path);
        }

        EnsureOutputFolder(DefaultSettingsPath);

        LocalizationSheetImporterSO settings = ScriptableObject.CreateInstance<LocalizationSheetImporterSO>();
        AssetDatabase.CreateAsset(settings, DefaultSettingsPath);
        AssetDatabase.SaveAssets();
        Log.D($"[LocalizationSheetImporter] 설정 에셋을 생성했습니다: {DefaultSettingsPath}");

        return settings;
    }

    /// <summary>
    /// googleSheetUrl이 빈 Secret.json 템플릿을 프로젝트 루트에 생성한다.
    /// </summary>
    public static void CreateSecretTemplate()
    {
        File.WriteAllText(SecretPath, SecretTemplate, new UTF8Encoding(false));
        Log.D($"[LocalizationSheetImporter] Secret.json을 생성했습니다. googleSheetUrl을 채워주세요: {SecretPath}");
    }

    /// <summary>
    /// Secret.json의 googleSheetUrl로 시트 JSON 원문을 다운로드한다.
    /// </summary>
    /// <returns>JSON 원문. 실패 시 null</returns>
    private static async Task<string> DownloadSheetJsonAsync()
    {
        if (!File.Exists(SecretPath))
        {
            Log.E($"[LocalizationSheetImporter] Secret 파일을 찾을 수 없습니다: {SecretPath}");
            return null;
        }

        string secretJson = File.ReadAllText(SecretPath, Encoding.UTF8);
        LocalizationSecretData secret = JsonUtility.FromJson<LocalizationSecretData>(secretJson);
        if (secret == null || string.IsNullOrWhiteSpace(secret.googleSheetUrl))
        {
            Log.E("[LocalizationSheetImporter] Secret 데이터에 googleSheetUrl이 없습니다.");
            return null;
        }

        using HttpClient client = new HttpClient();
        try
        {
            byte[] bytes = await client.GetByteArrayAsync(secret.googleSheetUrl);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception exception)
        {
            Log.E($"[LocalizationSheetImporter] 다운로드 실패: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// 로컬 캐시 JSON 파일에서 시트 원문을 읽어온다.
    /// </summary>
    /// <returns>JSON 원문. 파일이 없으면 null</returns>
    private static string LoadCachedJson()
    {
        if (!File.Exists(CacheJsonPath))
        {
            Log.W($"[LocalizationSheetImporter] 로컬 캐시 JSON이 없습니다: {CacheJsonPath}");
            return null;
        }

        return File.ReadAllText(CacheJsonPath, Encoding.UTF8);
    }

    /// <summary>
    /// 다운로드한 JSON 원문을 로컬 캐시 경로에 UTF-8(BOM 없음)로 저장한다.
    /// </summary>
    /// <param name="rawJson">저장할 JSON 원문</param>
    private static void SaveCacheJson(string rawJson)
    {
        string directory = Path.GetDirectoryName(CacheJsonPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(CacheJsonPath, rawJson, new UTF8Encoding(false));
    }

    /// <summary>
    /// JSON 원문에서 대상 시트의 row 배열을 찾는다. 루트가 배열이면 그대로 사용한다.
    /// </summary>
    /// <param name="rawJson">시트 JSON 원문</param>
    /// <param name="sheetName">대상 시트 이름</param>
    /// <param name="rows">파싱된 row 배열. 행이 0개일 수도 있다.</param>
    /// <returns>대상 시트를 찾았는지 여부</returns>
    private static bool TryGetRows(string rawJson, string sheetName, out JArray rows)
    {
        rows = null;

        JToken root;
        try
        {
            root = JToken.Parse(rawJson);
        }
        catch (Exception exception)
        {
            Log.E($"[LocalizationSheetImporter] JSON 파싱 실패: {exception.Message}");
            return false;
        }

        if (root is JArray rootArray)
        {
            rows = rootArray;
            return true;
        }

        if (root is not JObject rootObject)
        {
            return false;
        }

        foreach (JProperty property in rootObject.Properties())
        {
            if (!string.Equals(property.Name, sheetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value is JArray sheetRows)
            {
                rows = sheetRows;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// row 배열을 LocalizationEntry 목록으로 변환하고 검증 오류 여부를 반환한다.
    /// </summary>
    /// <param name="rows">시트 row 배열</param>
    /// <param name="requiredCodes">필수 언어 코드 목록</param>
    /// <param name="entries">변환된 엔트리 목록</param>
    /// <returns>검증 오류가 하나라도 있으면 true</returns>
    private static bool BuildEntries(JArray rows, string[] requiredCodes, out List<LocalizationEntry> entries)
    {
        entries = new List<LocalizationEntry>();
        bool hasError = false;
        HashSet<string> seenKeys = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i] is not JObject row)
            {
                continue;
            }

            string key = ReadKey(row);
            if (string.IsNullOrWhiteSpace(key))
            {
                Log.W($"[LocalizationSheetImporter] row {i + 1}: id가 비어 있어 건너뜁니다.");
                continue;
            }

            if (!seenKeys.Add(key))
            {
                hasError = true;
                Log.E($"[LocalizationSheetImporter] 중복 id: {key}");
                continue;
            }

            Dictionary<string, string> translationMap = ReadTranslations(row);
            hasError |= ValidateRequiredTranslations(key, translationMap, requiredCodes);

            entries.Add(new LocalizationEntry
            {
                key = key,
                translations = ToPairs(translationMap)
            });
        }

        entries.Sort((left, right) => string.CompareOrdinal(left.key, right.key));
        Log.D($"[LocalizationSheetImporter] 파싱 완료 - entries: {entries.Count}");
        return hasError;
    }

    /// <summary>
    /// row에서 id(또는 key) 컬럼 값을 읽는다.
    /// </summary>
    /// <param name="row">시트의 단일 row</param>
    /// <returns>키 문자열. 없으면 빈 문자열</returns>
    private static string ReadKey(JObject row)
    {
        for (int i = 0; i < KeyAliases.Length; i++)
        {
            foreach (JProperty property in row.Properties())
            {
                if (!string.Equals(property.Name, KeyAliases[i], StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return property.Value?.ToString()?.Trim();
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// row의 언어 컬럼(예약 컬럼 제외)을 언어 코드별 Dictionary로 변환한다.
    /// </summary>
    /// <param name="row">시트의 단일 row</param>
    /// <returns>언어 코드별 번역 문자열</returns>
    private static Dictionary<string, string> ReadTranslations(JObject row)
    {
        Dictionary<string, string> translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (JProperty property in row.Properties())
        {
            if (ReservedColumns.Contains(property.Name))
            {
                continue;
            }

            string languageCode = property.Name.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(languageCode))
            {
                continue;
            }

            if (translations.ContainsKey(languageCode))
            {
                Log.W($"[LocalizationSheetImporter] 중복 언어 컬럼 감지: {property.Name}");
                continue;
            }

            translations.Add(languageCode, property.Value?.ToString() ?? string.Empty);
        }

        return translations;
    }

    /// <summary>
    /// 필수 언어의 번역 누락/공백 여부를 검증한다.
    /// </summary>
    /// <param name="key">검증 대상 키</param>
    /// <param name="translations">언어 코드별 번역</param>
    /// <param name="requiredCodes">필수 언어 코드 목록</param>
    /// <returns>검증 오류가 있으면 true</returns>
    private static bool ValidateRequiredTranslations(string key, Dictionary<string, string> translations, string[] requiredCodes)
    {
        bool hasError = false;

        for (int i = 0; i < requiredCodes.Length; i++)
        {
            string requiredCode = requiredCodes[i];
            if (!translations.TryGetValue(requiredCode, out string value))
            {
                hasError = true;
                Log.E($"[LocalizationSheetImporter] ERROR: {key} - {requiredCode.ToUpperInvariant()} 컬럼이 없습니다.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                hasError = true;
                Log.E($"[LocalizationSheetImporter] ERROR: {key} - {requiredCode.ToUpperInvariant()} 번역이 비어 있습니다.");
            }
        }

        return hasError;
    }

    /// <summary>
    /// 언어 코드 Dictionary를 직렬화용 LocalizedTextPair 목록으로 변환한다.
    /// </summary>
    /// <param name="translationMap">언어 코드별 번역</param>
    /// <returns>언어 코드 순으로 정렬된 번역 쌍 목록</returns>
    private static List<LocalizedTextPair> ToPairs(Dictionary<string, string> translationMap)
    {
        List<LocalizedTextPair> pairs = new List<LocalizedTextPair>(translationMap.Count);
        foreach (KeyValuePair<string, string> pair in translationMap)
        {
            pairs.Add(new LocalizedTextPair
            {
                languageCode = pair.Key,
                value = pair.Value
            });
        }

        pairs.Sort((left, right) => string.CompareOrdinal(left.languageCode, right.languageCode));
        return pairs;
    }

    /// <summary>
    /// '/' 구분 문자열을 소문자 필수 언어 코드 배열로 파싱한다.
    /// </summary>
    /// <param name="rawCodes">필수 언어 코드 문자열</param>
    /// <returns>중복이 제거된 언어 코드 배열</returns>
    private static string[] ParseRequiredLanguageCodes(string rawCodes)
    {
        if (string.IsNullOrWhiteSpace(rawCodes))
        {
            return Array.Empty<string>();
        }

        string[] splitCodes = rawCodes.Split('/');
        List<string> normalized = new List<string>(splitCodes.Length);

        for (int i = 0; i < splitCodes.Length; i++)
        {
            string code = splitCodes[i].Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(code) || normalized.Contains(code))
            {
                continue;
            }

            normalized.Add(code);
        }

        return normalized.ToArray();
    }

    /// <summary>
    /// LocalizationTable 에셋을 생성하거나 엔트리를 갱신한다.
    /// </summary>
    /// <param name="settings">임포트 설정 SO</param>
    /// <param name="entries">저장할 엔트리 목록</param>
    /// <returns>에셋을 생성/갱신했으면 true, 덮어쓰기가 꺼져 건너뛰었으면 false</returns>
    private static bool SaveLocalizationTableAsset(LocalizationSheetImporterSO settings, List<LocalizationEntry> entries)
    {
        string assetPath = settings.OutputAssetPath;
        EnsureOutputFolder(assetPath);

        LocalizationTable table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(assetPath);
        bool created = false;

        if (table == null)
        {
            table = ScriptableObject.CreateInstance<LocalizationTable>();
            table.SetEntries(entries);
            AssetDatabase.CreateAsset(table, assetPath);
            created = true;
        }
        else
        {
            if (!settings.OverwriteExistingAsset)
            {
                return false;
            }

            table.SetEntries(entries);
            EditorUtility.SetDirty(table);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Log.D($"[LocalizationSheetImporter] Import 완료 (entries: {entries.Count}, created: {created}, path: {assetPath})");
        return true;
    }

    /// <summary>
    /// 출력 에셋 경로의 상위 폴더가 없으면 순차 생성한다.
    /// </summary>
    /// <param name="assetPath">생성 대상 에셋 경로</param>
    private static void EnsureOutputFolder(string assetPath)
    {
        string folderPath = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(folderPath) || AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }
}

/// <summary>
/// Secret.json의 구글 시트 접근 정보를 담는 직렬화 데이터이다.
/// </summary>
[Serializable]
public class LocalizationSecretData
{
    public string googleSheetUrl;
}

/// <summary>
/// 임포트 1회 실행의 결과를 담는 데이터이다. 에디터 UI의 상태 표시에 사용한다.
/// </summary>
public struct LocalizationImportResult
{
    public bool success;
    public int entryCount;
    public string message;
}
