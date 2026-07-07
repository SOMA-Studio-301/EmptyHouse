using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Border.Localization;

namespace Border.Localization.Editor
{
    /// <summary>
    /// [LocalizeKey] 가 부착된 string 필드를 LocalizationTable 의 키 피커 UI 로 표시하는 PropertyDrawer.
    /// UILocalizeTextEditor 의 검색/접두어 필터/드롭다운/검증/미리보기 UI 를 단일 PropertyField 영역에 그린다.
    /// 테이블 자동 탐색은 첫 LocalizationTable 자산을 사용하며, 못 찾으면 경고 메시지만 표시한다.
    /// </summary>
    [CustomPropertyDrawer(typeof(LocalizeKeyAttribute))]
    public class LocalizeKeyDrawer : PropertyDrawer
    {
        private const string EmptyOptionLabel = "<선택 안 함>";
        private const string PrefixAllLabel = "ALL";
        private const string PrefixNoPrefixLabel = "NO_PREFIX";

        private static LocalizationTable cachedTable;
        private static readonly List<string> cachedAllKeys = new();
        private static readonly List<string> cachedPrefixOptions = new();
        private static int cachedEntriesCount = -1;

        // PropertyPath 별 검색어/접두어 상태. 같은 인스펙터에 여러 [LocalizeKey] 가 있어도 독립적으로 동작.
        private readonly Dictionary<string, string> searchByPath = new();
        private readonly Dictionary<string, int> prefixIndexByPath = new();

        private readonly List<string> filteredKeys = new();

        /// <summary>
        /// 인스펙터에 다중 행 UI 를 그리기 위한 총 높이를 계산한다.
        /// HelpBox 는 단일 라인보다 크므로 안전 마진(line * 1.5)을 적용한다.
        /// </summary>
        /// <param name="property">대상 SerializedProperty.</param>
        /// <param name="label">필드 라벨.</param>
        /// <returns>본 드로어가 차지하는 총 픽셀 높이.</returns>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                return EditorGUIUtility.singleLineHeight;
            }

            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float helpBoxHeight = line * 1.5f;

            // 라벨 + 접두어 + 검색 + 키선택 + 결과개수 + 현재키 — 일반 라인 6줄
            float total = line * 6f + spacing * 6f;

            // 검증 HelpBox — 빈 키/유효/잘못된 키 어느 경로든 1줄 출력됨.
            total += helpBoxHeight + spacing;

            EnsureCacheBuilt();
            if (cachedTable != null && !string.IsNullOrWhiteSpace(property.stringValue))
            {
                if (TryGetEntry(property.stringValue, out LocalizationEntry entry) && entry.translations != null)
                {
                    total += line + spacing; // "미리보기" 헤더
                    total += (line + spacing) * entry.translations.Count;
                }
            }

            return total + spacing;
        }

        /// <summary>
        /// [LocalizeKey] 부착 string 필드에 키 피커 UI 를 그린다.
        /// String 이외의 타입에 부착되어 있으면 기본 PropertyField 로 폴백한다.
        /// </summary>
        /// <param name="position">렌더 영역.</param>
        /// <param name="property">대상 SerializedProperty.</param>
        /// <param name="label">필드 라벨.</param>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                EditorGUI.HelpBox(position, "[LocalizeKey] 는 string 필드에만 적용할 수 있습니다.", MessageType.Error);
                return;
            }

            EnsureCacheBuilt();

            using (new EditorGUI.PropertyScope(position, label, property))
            {
                float line = EditorGUIUtility.singleLineHeight;
                float spacing = EditorGUIUtility.standardVerticalSpacing;
                Rect cursor = new Rect(position.x, position.y, position.width, line);

                EditorGUI.LabelField(cursor, label, EditorStyles.boldLabel);
                cursor.y += line + spacing;

                if (cachedTable == null)
                {
                    EditorGUI.HelpBox(cursor, "LocalizationTable 자산을 찾지 못했습니다. (raw 입력만 가능)", MessageType.Warning);
                    cursor.y += line + spacing;
                    property.stringValue = EditorGUI.TextField(cursor, "키 (raw)", property.stringValue);
                    return;
                }

                string path = property.propertyPath;
                string searchText = searchByPath.TryGetValue(path, out string s) ? s : string.Empty;
                int prefixIndex = prefixIndexByPath.TryGetValue(path, out int p) ? p : 0;

                int nextPrefix = EditorGUI.Popup(cursor, "접두어", prefixIndex, cachedPrefixOptions.ToArray());
                if (nextPrefix != prefixIndex)
                {
                    prefixIndex = nextPrefix;
                    prefixIndexByPath[path] = prefixIndex;
                }
                cursor.y += line + spacing;

                string nextSearch = EditorGUI.TextField(cursor, "검색", searchText);
                if (nextSearch != searchText)
                {
                    searchText = nextSearch;
                    searchByPath[path] = searchText;
                }
                cursor.y += line + spacing;

                ApplyFilter(searchText, prefixIndex);

                List<string> options = new List<string>(filteredKeys.Count + 1) { EmptyOptionLabel };
                options.AddRange(filteredKeys);

                string currentKey = property.stringValue;
                int selectedIndex = 0;
                for (int i = 0; i < filteredKeys.Count; i++)
                {
                    if (filteredKeys[i] == currentKey)
                    {
                        selectedIndex = i + 1;
                        break;
                    }
                }

                int nextIndex = EditorGUI.Popup(cursor, "키 선택", selectedIndex, options.ToArray());
                if (nextIndex != selectedIndex)
                {
                    property.stringValue = nextIndex == 0 ? string.Empty : filteredKeys[nextIndex - 1];
                }
                cursor.y += line + spacing;

                EditorGUI.LabelField(cursor, "결과 개수", filteredKeys.Count.ToString());
                cursor.y += line + spacing;

                EditorGUI.LabelField(cursor, "현재 키", string.IsNullOrWhiteSpace(property.stringValue) ? "(없음)" : property.stringValue);
                cursor.y += line + spacing;

                DrawValidationAndPreview(ref cursor, property.stringValue, line, spacing);
            }
        }

        /// <summary>
        /// 현재 키에 대한 검증 메시지와 언어별 미리보기를 렌더링한다. cursor 는 호출 후 다음 그리기 위치로 이동한다.
        /// HelpBox 는 단일 라인보다 크므로 GetPropertyHeight 와 동일한 line * 1.5 높이/이동량을 사용한다.
        /// </summary>
        private void DrawValidationAndPreview(ref Rect cursor, string key, float line, float spacing)
        {
            float helpBoxHeight = line * 1.5f;
            Rect helpBoxRect = new Rect(cursor.x, cursor.y, cursor.width, helpBoxHeight);

            if (string.IsNullOrWhiteSpace(key))
            {
                EditorGUI.HelpBox(helpBoxRect, "키가 비어 있습니다. (선택 사항이면 무시)", MessageType.None);
                cursor.y += helpBoxHeight + spacing;
                return;
            }

            if (!TryGetEntry(key, out LocalizationEntry entry))
            {
                EditorGUI.HelpBox(helpBoxRect, "존재하지 않는 키입니다.", MessageType.Error);
                cursor.y += helpBoxHeight + spacing;
                return;
            }

            EditorGUI.HelpBox(helpBoxRect, "유효한 키입니다.", MessageType.None);
            cursor.y += helpBoxHeight + spacing;

            if (entry.translations == null || entry.translations.Count == 0) return;

            EditorGUI.LabelField(cursor, "미리보기", EditorStyles.boldLabel);
            cursor.y += line + spacing;

            using (new EditorGUI.DisabledScope(true))
            {
                for (int i = 0; i < entry.translations.Count; i++)
                {
                    LocalizedTextPair pair = entry.translations[i];
                    if (pair == null) continue;
                    string lang = string.IsNullOrWhiteSpace(pair.languageCode) ? "(empty)" : pair.languageCode.Trim().ToLowerInvariant();
                    EditorGUI.TextField(cursor, lang, pair.value ?? string.Empty);
                    cursor.y += line + spacing;
                }
            }
        }

        /// <summary>
        /// LocalizationTable 자산을 1회 탐색하고 키/접두어 캐시를 빌드한다.
        /// 테이블 엔트리 수가 변경되면 자동 재빌드한다.
        /// </summary>
        private static void EnsureCacheBuilt()
        {
            if (cachedTable == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:LocalizationTable");
                if (guids != null && guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    cachedTable = AssetDatabase.LoadAssetAtPath<LocalizationTable>(path);
                }
            }

            if (cachedTable == null)
            {
                cachedAllKeys.Clear();
                cachedPrefixOptions.Clear();
                cachedEntriesCount = 0;
                return;
            }

            int currentCount = cachedTable.Entries != null ? cachedTable.Entries.Count : 0;
            if (currentCount == cachedEntriesCount && cachedAllKeys.Count > 0) return;

            cachedAllKeys.Clear();
            cachedPrefixOptions.Clear();
            cachedPrefixOptions.Add(PrefixAllLabel);
            cachedEntriesCount = currentCount;

            HashSet<string> dynamicPrefixes = new HashSet<string>();
            IReadOnlyList<LocalizationEntry> entries = cachedTable.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                LocalizationEntry entry = entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.key)) continue;
                cachedAllKeys.Add(entry.key);
                dynamicPrefixes.Add(ExtractPrefix(entry.key));
            }

            List<string> sorted = new List<string>(dynamicPrefixes);
            sorted.Sort();
            for (int i = 0; i < sorted.Count; i++) cachedPrefixOptions.Add(sorted[i]);
            cachedAllKeys.Sort();
        }

        /// <summary>
        /// 현재 검색어/접두어 인덱스에 맞춰 filteredKeys 를 다시 채운다.
        /// </summary>
        private void ApplyFilter(string searchText, int prefixIndex)
        {
            filteredKeys.Clear();
            if (cachedAllKeys.Count == 0) return;

            string selectedPrefix = (prefixIndex >= 0 && prefixIndex < cachedPrefixOptions.Count)
                ? cachedPrefixOptions[prefixIndex]
                : PrefixAllLabel;

            bool hasSearch = !string.IsNullOrWhiteSpace(searchText);
            string lowered = hasSearch ? searchText.Trim().ToLowerInvariant() : string.Empty;

            for (int i = 0; i < cachedAllKeys.Count; i++)
            {
                string key = cachedAllKeys[i];
                if (string.IsNullOrEmpty(key)) continue;
                if (selectedPrefix != PrefixAllLabel && ExtractPrefix(key) != selectedPrefix) continue;
                if (hasSearch && !key.ToLowerInvariant().Contains(lowered)) continue;
                filteredKeys.Add(key);
            }
        }

        /// <summary>
        /// 키 문자열의 첫 언더스코어 앞부분을 접두어로 추출한다. 언더스코어가 없으면 NO_PREFIX.
        /// </summary>
        private static string ExtractPrefix(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return PrefixNoPrefixLabel;
            string trimmed = key.Trim();
            int sep = trimmed.IndexOf('_');
            if (sep <= 0) return PrefixNoPrefixLabel;
            return trimmed.Substring(0, sep).ToUpperInvariant();
        }

        /// <summary>
        /// 캐시된 테이블에서 키에 대응하는 LocalizationEntry 를 찾는다.
        /// </summary>
        private static bool TryGetEntry(string key, out LocalizationEntry found)
        {
            found = null;
            if (cachedTable == null || cachedTable.Entries == null || string.IsNullOrWhiteSpace(key)) return false;
            IReadOnlyList<LocalizationEntry> entries = cachedTable.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                LocalizationEntry entry = entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.key)) continue;
                if (entry.key == key)
                {
                    found = entry;
                    return true;
                }
            }
            return false;
        }
    }

}
