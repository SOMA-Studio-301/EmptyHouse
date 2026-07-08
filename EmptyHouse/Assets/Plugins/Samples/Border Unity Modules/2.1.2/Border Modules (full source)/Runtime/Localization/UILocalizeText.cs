using System.Text;
using TMPro;
using UnityEngine;
using Border.Core;
using Border.Events;

namespace Border.Localization
{
    /// <summary>
    /// 로컬라이징 키를 기반으로 UI 텍스트를 자동 갱신하는 컴포넌트이다.
    /// 본문 키 외에 선택적 prefix/suffix 로컬라이즈 키와, 런타임 주입형 동적 prefix/suffix(StringBuilder) 를 지원한다.
    /// 정적(로컬라이즈) 조각과 동적(StringBuilder) 조각의 합성 순서는 인스펙터 토글로 좌우할 수 있다.
    /// 최종 출력 형태: [prefix 그룹] + 본문 + [suffix 그룹].
    /// </summary>
    public class UILocalizeText : MonoBehaviour
    {
        [SerializeField] private bool usePrefix;
        [LocalizeKey][SerializeField] private string prefixKey;

        [LocalizeKey][SerializeField] private string key;

        [SerializeField] private bool useSuffix;
        [LocalizeKey][SerializeField] private string suffixKey;

        // true 면 동적 prefix 가 정적 prefix 보다 앞에 온다. (dynamicPrefix + prefix), false 면 (prefix + dynamicPrefix)
        [SerializeField] private bool prefixDynamicFirst;
        // true 면 동적 suffix 가 정적 suffix 보다 앞에 온다. (dynamicSuffix + suffix), false 면 (suffix + dynamicSuffix)
        [SerializeField] private bool suffixDynamicFirst = true;

        [SerializeField] private TMP_Text tmpText;

        // 언어 변경 시에만 갱신되는 로컬라이즈 캐시. 매 합성 시 Get(key) 재호출 비용 회피.
        private string cachedPrefix;
        private string cachedLocalized;
        private string cachedSuffix;

        // 외부에서 주입받은 동적 prefix/suffix StringBuilder 참조. null/Length 0 이면 미사용.
        private StringBuilder dynamicPrefix;
        private StringBuilder dynamicSuffix;

        // 본문(이름)에 적용할 리치텍스트 색 여는 태그(예: "<color=#4FC3F7>"). null 이면 색 미적용.
        private string bodyColorOpenTag;

        // 합성 출력 전용 버퍼. SetText(StringBuilder) 로 전달해 string 할당을 피한다.
        private readonly StringBuilder composeBuilder = new StringBuilder(128);

        /// <summary>
        /// 대상 Text 컴포넌트를 자동 탐색한다.
        /// </summary>
        private void Awake()
        {
            if (tmpText == null)
            {
                tmpText = GetComponent<TMP_Text>();
            }
        }

        /// <summary>
        /// 언어 변경 이벤트를 구독하고 즉시 텍스트를 갱신한다.
        /// </summary>
        private void OnEnable()
        {
            ILocalizationProvider localizationManager = LocalizationManager.Current;
            if (localizationManager != null)
            {
                localizationManager.OnLanguageChanged += OnLanguageChanged;
            }

            RefreshLocalizedCache();
            Apply();
        }

        /// <summary>
        /// 언어 변경 이벤트 구독을 해제한다.
        /// </summary>
        private void OnDisable()
        {
            ILocalizationProvider localizationManager = LocalizationManager.Current;
            if (localizationManager != null)
            {
                localizationManager.OnLanguageChanged -= OnLanguageChanged;
            }
        }

        /// <summary>
        /// 언어 변경 이벤트 콜백. 로컬라이즈 캐시를 재구성한 뒤 합성 결과를 다시 출력한다.
        /// </summary>
        private void OnLanguageChanged()
        {
            RefreshLocalizedCache();
            Apply();
        }

        /// <summary>
        /// prefix/본문/suffix 키로 로컬라이즈 문자열을 다시 조회해 각 캐시에 저장한다.
        /// 토글이 꺼져 있거나 키가 비어 있는 조각은 빈 문자열로 캐시한다.
        /// </summary>
        private void RefreshLocalizedCache()
        {
            ILocalizationProvider localizationManager = LocalizationManager.Current;

            cachedPrefix = (usePrefix && !string.IsNullOrWhiteSpace(prefixKey)) ? Lookup(localizationManager, prefixKey) : string.Empty;
            cachedLocalized = !string.IsNullOrWhiteSpace(key) ? Lookup(localizationManager, key) : string.Empty;
            cachedSuffix = (useSuffix && !string.IsNullOrWhiteSpace(suffixKey)) ? Lookup(localizationManager, suffixKey) : string.Empty;
        }

        /// <summary>
        /// 로컬라이즈 매니저에서 키를 조회한다. 매니저가 없으면 키를 그대로 반환한다.
        /// </summary>
        /// <param name="manager">로컬라이즈 매니저(null 허용).</param>
        /// <param name="lookupKey">조회할 로컬라이즈 키.</param>
        /// <returns>조회된 번역 문자열 또는 키.</returns>
        private string Lookup(ILocalizationProvider manager, string lookupKey)
        {
            return manager != null ? manager.Get(lookupKey) : lookupKey;
        }

        /// <summary>
        /// 정적/동적 조각을 합성 순서에 맞춰 TMP 에 출력한다.
        /// 모든 조각이 비어 있으면 빈 문자열을 출력한다.
        /// </summary>
        private void Apply()
        {
            if (tmpText == null)
            {
                return;
            }

            composeBuilder.Length = 0;
            AppendAffixGroup(cachedPrefix, dynamicPrefix, prefixDynamicFirst);
            AppendBody(cachedLocalized);
            AppendAffixGroup(cachedSuffix, dynamicSuffix, suffixDynamicFirst);

            tmpText.SetText(composeBuilder);
        }

        /// <summary>
        /// 본문(이름) 조각을 composeBuilder 에 추가한다. bodyColorOpenTag 가 있으면 리치텍스트 색으로 감싼다.
        /// </summary>
        /// <param name="body">본문 로컬라이즈 문자열.</param>
        private void AppendBody(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return;
            }

            if (bodyColorOpenTag != null)
            {
                composeBuilder.Append(bodyColorOpenTag);
                composeBuilder.Append(body);
                composeBuilder.Append("</color>");
            }
            else
            {
                composeBuilder.Append(body);
            }
        }

        /// <summary>
        /// 정적(로컬라이즈) 조각과 동적(StringBuilder) 조각을 지정 순서로 composeBuilder 에 추가한다.
        /// </summary>
        /// <param name="staticPart">정적 로컬라이즈 문자열(빈 문자열이면 스킵).</param>
        /// <param name="dynamicPart">동적 StringBuilder(null/Length 0 이면 스킵).</param>
        /// <param name="dynamicFirst">true 면 동적 조각을 먼저 추가한다.</param>
        private void AppendAffixGroup(string staticPart, StringBuilder dynamicPart, bool dynamicFirst)
        {
            if (dynamicFirst)
            {
                AppendDynamic(dynamicPart);
                AppendStatic(staticPart);
            }
            else
            {
                AppendStatic(staticPart);
                AppendDynamic(dynamicPart);
            }
        }

        /// <summary>
        /// 정적 조각이 비어있지 않으면 composeBuilder 에 추가한다.
        /// </summary>
        /// <param name="staticPart">정적 로컬라이즈 문자열.</param>
        private void AppendStatic(string staticPart)
        {
            if (!string.IsNullOrEmpty(staticPart))
            {
                composeBuilder.Append(staticPart);
            }
        }

        /// <summary>
        /// 동적 조각이 비어있지 않으면 composeBuilder 에 추가한다.
        /// </summary>
        /// <param name="dynamicPart">동적 StringBuilder.</param>
        private void AppendDynamic(StringBuilder dynamicPart)
        {
            if (dynamicPart != null && dynamicPart.Length > 0)
            {
                composeBuilder.Append(dynamicPart);
            }
        }

        /// <summary>
        /// 런타임에서 본문 로컬라이징 키를 변경하고 즉시 반영한다.
        /// </summary>
        /// <param name="newKey">새 본문 로컬라이징 키.</param>
        public void SetKey(string newKey)
        {
            key = newKey;
            RefreshLocalizedCache();
            Apply();
        }

        /// <summary>
        /// 런타임에서 prefix/본문/suffix 로컬라이즈 키를 한 번에 설정한다.
        /// prefix/suffix 키가 비어 있으면 해당 토글을 자동으로 끈다(미출력).
        /// 동일 알림 HUD 를 다양한 케이스(광석 발견/업그레이드/해금 등)에 재사용할 때 사용한다.
        /// </summary>
        /// <param name="newPrefixKey">prefix 로컬라이즈 키(빈 문자열이면 prefix 미사용).</param>
        /// <param name="newBodyKey">본문 로컬라이즈 키.</param>
        /// <param name="newSuffixKey">suffix 로컬라이즈 키(빈 문자열이면 suffix 미사용).</param>
        public void SetKeys(string newPrefixKey, string newBodyKey, string newSuffixKey)
        {
            bodyColorOpenTag = null;
            prefixKey = newPrefixKey;
            key = newBodyKey;
            suffixKey = newSuffixKey;
            usePrefix = !string.IsNullOrWhiteSpace(newPrefixKey);
            useSuffix = !string.IsNullOrWhiteSpace(newSuffixKey);
            RefreshLocalizedCache();
            Apply();
        }

        /// <summary>
        /// 런타임에서 prefix/본문/suffix 로컬라이즈 키를 한 번에 설정하고, 본문(이름)에 리치텍스트 색을 적용한다.
        /// prefix/suffix 키가 비어 있으면 해당 토글을 자동으로 끈다(미출력).
        /// </summary>
        /// <param name="newPrefixKey">prefix 로컬라이즈 키(빈 문자열이면 prefix 미사용).</param>
        /// <param name="newBodyKey">본문 로컬라이즈 키.</param>
        /// <param name="newSuffixKey">suffix 로컬라이즈 키(빈 문자열이면 suffix 미사용).</param>
        /// <param name="bodyColor">본문에 적용할 리치텍스트 색.</param>
        public void SetKeys(string newPrefixKey, string newBodyKey, string newSuffixKey, Color bodyColor)
        {
            bodyColorOpenTag = "<color=#" + ColorUtility.ToHtmlStringRGB(bodyColor) + ">";
            prefixKey = newPrefixKey;
            key = newBodyKey;
            suffixKey = newSuffixKey;
            usePrefix = !string.IsNullOrWhiteSpace(newPrefixKey);
            useSuffix = !string.IsNullOrWhiteSpace(newSuffixKey);
            RefreshLocalizedCache();
            Apply();
        }

        /// <summary>
        /// 외부에서 동적 prefix StringBuilder 의 참조를 등록한다.
        /// 동일 참조의 내용 변경 시 본 메서드를 다시 호출해 합성/출력을 갱신한다.
        /// null 또는 빈 StringBuilder 전달 시 prefix 동적 조각 없이 출력한다.
        /// </summary>
        /// <param name="prefix">합성에 사용할 StringBuilder(참조 보관). null 허용.</param>
        public void SetDynamicPrefix(StringBuilder prefix)
        {
            dynamicPrefix = prefix;
            Apply();
        }

        /// <summary>
        /// 외부에서 동적 suffix StringBuilder 의 참조를 등록한다.
        /// 동일 참조의 내용 변경 시 본 메서드를 다시 호출해 합성/출력을 갱신한다.
        /// null 또는 빈 StringBuilder 전달 시 suffix 동적 조각 없이 출력한다.
        /// </summary>
        /// <param name="suffix">합성에 사용할 StringBuilder(참조 보관). null 허용.</param>
        public void SetDynamicSuffix(StringBuilder suffix)
        {
            dynamicSuffix = suffix;
            Apply();
        }

    #if UNITY_EDITOR
        /// <summary>
        /// 인스펙터 값 변경 시 에디터에서도 텍스트를 갱신한다.
        /// </summary>
        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            RefreshLocalizedCache();
            Apply();
        }
    #endif
    }

}
