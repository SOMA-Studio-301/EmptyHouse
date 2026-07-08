using System;
using System.Collections.Generic;
using UnityEngine;
using Border.Core;
using Border.Events;

namespace Border.Localization
{
    /// <summary>
    /// 로컬라이징 키와 다국어 번역 문자열을 저장하는 테이블 ScriptableObject이다.
    /// </summary>
    [CreateAssetMenu(fileName = "LocalizationTable", menuName = "Localization/Localization Table")]
    public class LocalizationTable : ScriptableObject
    {
        [SerializeField] private List<LocalizationEntry> entries = new();

        /// <summary>
        /// 테이블에 저장된 모든 로컬라이징 엔트리를 읽기 전용으로 반환한다.
        /// </summary>
        public IReadOnlyList<LocalizationEntry> Entries => entries;

    #if UNITY_EDITOR
        /// <summary>
        /// 에디터 Import 과정에서 엔트리 목록을 통째로 교체한다.
        /// </summary>
        /// <param name="newEntries">새로 반영할 로컬라이징 엔트리 목록</param>
        public void SetEntries(List<LocalizationEntry> newEntries)
        {
            entries = newEntries ?? new List<LocalizationEntry>();
        }
    #endif
    }

    /// <summary>
    /// 하나의 로컬라이징 키와 해당 키의 언어별 번역 목록을 보관하는 엔트리 데이터이다.
    /// </summary>
    [Serializable]
    public class LocalizationEntry
    {
        public string key;
        public List<LocalizedTextPair> translations = new();
    }

    /// <summary>
    /// 언어 코드와 번역 문자열의 쌍을 보관하는 직렬화 데이터이다.
    /// </summary>
    [Serializable]
    public class LocalizedTextPair
    {
        public string languageCode;

        [TextArea(1, 3)]
        public string value;
    }

}
