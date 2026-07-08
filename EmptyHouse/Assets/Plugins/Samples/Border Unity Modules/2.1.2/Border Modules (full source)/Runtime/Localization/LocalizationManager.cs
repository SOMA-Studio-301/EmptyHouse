using System;
using System.Collections.Generic;
using UnityEngine;
using Border.Core;
using Border.Events;

namespace Border.Localization
{
    /// <summary>
    /// 로컬라이징 테이블을 기반으로 언어별 조회 캐시를 구성하고 언어 변경 이벤트를 전파하는 매니저이다.
    /// </summary>
    public class LocalizationManager : MonoBehaviour, ILocalizationProvider
    {
        private const string DefaultLanguageCode = "ko";
        private const string FallbackLanguageCode = "en";

        [Header("Data")]
        [SerializeField] private LocalizationTable localizationTable;

        [Header("Language")]
        [SerializeField] private string defaultLanguageCode = DefaultLanguageCode;
        [SerializeField] private string fallbackLanguageCode = FallbackLanguageCode;

        [Header("Listening on")]
        [SerializeField] private StringEventChannelSO changeLanguageEvent;

        // 실제 언어 별 텍스트 데이터의 런타임 자료구조
        private readonly Dictionary<string, Dictionary<string, string>> languageBuckets = new(StringComparer.Ordinal);
        // 현재 언어 설정에 맞는 텍스트들
        private Dictionary<string, string> currentLanguage = new(StringComparer.Ordinal);
        // 폴백 언어 설정에 맞는 텍스트들
        private Dictionary<string, string> fallbackLanguage = new(StringComparer.Ordinal);

        /// <summary>
        /// 현재 활성화된 언어 코드이다.
        /// </summary>
        public string CurrentLanguageCode { get; private set; } = DefaultLanguageCode;

        /// <summary>
        /// 언어가 변경될 때 모든 구독자에게 전달되는 이벤트이다.
        /// </summary>
        public event Action OnLanguageChanged;

        /// <summary>
        /// 활성 로컬라이즈 provider를 가리키는 정적 접근점이다.
        /// UI 컴포넌트는 게임의 서비스 로케이터 대신 이 값으로 provider를 얻는다(싱글톤처럼 동작).
        /// 첫 인스턴스가 OnEnable에서 자기 자신을 등록하고 OnDisable에서 해제한다.
        /// 게임이 자체 구현을 끼우려면 다른 <see cref="ILocalizationProvider"/>를 할당하면 된다.
        /// </summary>
        public static ILocalizationProvider Current { get; set; }

        /// <summary>
        /// 씬 내 단일 인스턴스를 보장하고 로컬라이징 캐시를 초기화한다.
        /// </summary>
        private void Awake()
        {
            Initialize();
        }

        /// <summary>
        /// 언어 변경 이벤트 채널을 구독한다.
        /// </summary>
        private void OnEnable()
        {
            if (Current == null)
            {
                Current = this;
            }

            if (changeLanguageEvent != null)
            {
                changeLanguageEvent.OnEventRaised += HandleLanguageChanged;
            }
        }

        /// <summary>
        /// 언어 변경 이벤트 채널 구독을 해제한다.
        /// </summary>
        private void OnDisable()
        {
            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }

            if (changeLanguageEvent != null)
            {
                changeLanguageEvent.OnEventRaised -= HandleLanguageChanged;
            }
        }

        /// <summary>
        /// 로컬라이징 데이터를 캐시로 구성하고 초기 언어를 적용한다.
        /// 폴백 언어로 안전하게 언어를 적용한 뒤, 현재 설정된 언어로 설정한다.
        /// </summary>
        private void Initialize()
        {
            RebuildBuckets();
            ApplyLanguage(GetSafeCode(fallbackLanguageCode), true);
            ApplyLanguage(GetSafeCode(defaultLanguageCode), true);
        }

        /// <summary>
        /// 현재 활성 언어와 폴백 언어를 기준으로 번역 문자열을 조회한다.
        /// UILocalizeText에 키에 따른 현재 언어 텍스트 제공한다.
        /// </summary>
        public string Get(string key) => GetInternal(key);

        /// <summary>
        /// 언어 변경 이벤트를 받아 내부 언어를 전환한다.
        /// 설정창의 '언어 변경' 드롭다운 변경 시 실행된다.
        /// </summary>
        private void HandleLanguageChanged(string languageCode)
        {
            ApplyLanguage(languageCode, false);
        }

        /// <summary>
        /// 실제 번역 조회를 수행한다.
        /// </summary>
        private string GetInternal(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            // 현재 키 코드에 맞는 텍스트로 이뤄진 currentLanguage에서 key에 따른 텍스트를 가져옴
            if (currentLanguage != null && currentLanguage.TryGetValue(key, out string localized))
            {
                return localized;
            }

            // 현재 언어에서 텍스트 찾기에 실패했다면, 폴백 언어로 출력
            if (fallbackLanguage != null && fallbackLanguage.TryGetValue(key, out string fallback))
            {
                return fallback;
            }

            return key;
        }

        /// <summary>
        /// 언어 코드를 검증하고 현재/폴백 캐시를 재설정한다.
        /// </summary>
        private void ApplyLanguage(string languageCode, bool silent)
        {
            string normalized = GetSafeCode(languageCode);

            // 언어 테이블에서 캐싱해서 언어 코드에 맞는 텍스트들 가져오기
            if (!languageBuckets.TryGetValue(normalized, out Dictionary<string, string> nextLanguage))
            {
                Log.W($"[LocalizationManager] 언어 코드 '{normalized}' 데이터가 없어 fallback을 사용합니다.");
                normalized = GetSafeCode(fallbackLanguageCode);
                languageBuckets.TryGetValue(normalized, out nextLanguage);
            }

            // Fallback 텍스트도 가져오기
            if (!languageBuckets.TryGetValue(GetSafeCode(fallbackLanguageCode), out Dictionary<string, string> nextFallback))
            {
                nextFallback = nextLanguage ?? new Dictionary<string, string>(StringComparer.Ordinal);
            }

            currentLanguage = nextLanguage ?? new Dictionary<string, string>(StringComparer.Ordinal);
            fallbackLanguage = nextFallback;

            // 동일한 언어 코드일 때도 모든 LocalizeText에 전파한다.
            if (CurrentLanguageCode == normalized && !silent)
            {
                OnLanguageChanged?.Invoke();
                return;
            }

            // 새로운 언어 코드로 설정하고 모든 LocalizeText에 전파한다.
            CurrentLanguageCode = normalized;

            if (!silent)
            {
                OnLanguageChanged?.Invoke();
            }
        }

        /// <summary>
        /// LocalizationTable 엔트리 목록으로 언어별 버킷 캐시를 재구성한다.
        /// 런타임에 1회 실행되는 메서드 이다.
        /// </summary>
        private void RebuildBuckets()
        {
            languageBuckets.Clear();

            if (localizationTable == null || localizationTable.Entries == null)
            {
                Log.W("[LocalizationManager] LocalizationTable이 비어 있습니다.");
                return;
            }

            IReadOnlyList<LocalizationEntry> entries = localizationTable.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                LocalizationEntry entry = entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.key) || entry.translations == null)
                {
                    continue;
                }

                for (int j = 0; j < entry.translations.Count; j++)
                {
                    LocalizedTextPair pair = entry.translations[j];
                    if (pair == null || string.IsNullOrWhiteSpace(pair.languageCode))
                    {
                        continue;
                    }

                    string code = GetSafeCode(pair.languageCode);
                    if (!languageBuckets.TryGetValue(code, out Dictionary<string, string> bucket))
                    {
                        bucket = new Dictionary<string, string>(StringComparer.Ordinal);
                        languageBuckets.Add(code, bucket);
                    }

                    bucket[entry.key] = pair.value ?? string.Empty;
                }
            }
        }

        /// <summary>
        /// 언어 코드를 소문자 표준값으로 정규화한다.
        /// </summary>
        private string GetSafeCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return DefaultLanguageCode;
            }

            return code.Trim().ToLowerInvariant();
        }
    }

}
