using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
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
        // provider 등장을 기다리는 최대 프레임 수. 초과하면 경고 후 키 원문으로 폴백한다.
        private const int BindWaitFrameLimit = 60;

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

        // 실제로 구독한 provider. OnDisable 에서 Current 를 재조회하지 않고 이 참조로 해제해 구독 누수를 막는다.
        private ILocalizationProvider boundProvider;
        // provider 등장 대기 코루틴 핸들. 대기 중이 아니면 null.
        private Coroutine bindRoutine;

        // 합성 출력 전용 버퍼. SetText(StringBuilder) 로 전달해 string 할당을 피한다.
        private readonly StringBuilder composeBuilder = new StringBuilder(128);

        private RectTransform cachedLayoutRoot; // 텍스트 변경 시 무효화할 레이아웃 루트. 조상에 없으면 null
        private bool layoutRootResolved; // cachedLayoutRoot 계산 완료 여부. OnEnable 에서 리셋

        // 조상 탐색용 재사용 버퍼. GetComponents 논알로케이팅 오버로드에 전달한다.
        private static readonly List<ILayoutController> layoutControllerBuffer = new List<ILayoutController>(4);

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
        /// provider 에 바인딩한다. 아직 없으면 등장할 때까지 대기 후 바인딩한다.
        /// 스크립트 실행 순서가 미지정이라 UI 가 LocalizationManager 보다 먼저 깨는 경우가 있다.
        /// 재부모화(풀링)·조상 활성 상태 변화를 반영하기 위해 레이아웃 루트 캐시를 버리고 다시 무효화한다.
        /// 비활성 중에는 TMP/UGUI 양쪽이 마크를 무시하므로, 활성화 시점의 이 호출이 유일한 복구 지점이다.
        /// </summary>
        private void OnEnable()
        {
            layoutRootResolved = false;
            MarkLayoutDirty();

            ILocalizationProvider localizationManager = LocalizationManager.Current;
            if (localizationManager != null)
            {
                Bind(localizationManager);
                return;
            }

            bindRoutine = StartCoroutine(BindWhenReady());
        }

        /// <summary>
        /// 대기 코루틴을 정지하고, 구독했던 provider 에서 언어 변경 이벤트 구독을 해제한다.
        /// </summary>
        private void OnDisable()
        {
            if (bindRoutine != null)
            {
                StopCoroutine(bindRoutine);
                bindRoutine = null;
            }

            if (boundProvider != null)
            {
                boundProvider.OnLanguageChanged -= OnLanguageChanged;
                boundProvider = null;
            }
        }

        /// <summary>
        /// provider 를 보관하고 언어 변경 이벤트를 구독한 뒤 즉시 텍스트를 갱신한다.
        /// </summary>
        /// <param name="provider">바인딩할 로컬라이즈 provider.</param>
        private void Bind(ILocalizationProvider provider)
        {
            boundProvider = provider;
            boundProvider.OnLanguageChanged += OnLanguageChanged;

            RefreshLocalizedCache();
            Apply();
        }

        /// <summary>
        /// provider 가 등장할 때까지 프레임 단위로 대기한 뒤 바인딩한다.
        /// 제한 프레임을 넘기면 경고를 남기고 키 원문을 출력한다(매니저 미배치 상황을 드러내기 위함).
        /// </summary>
        /// <returns>대기 코루틴 열거자.</returns>
        private IEnumerator BindWhenReady()
        {
            for (int frame = 0; frame < BindWaitFrameLimit; frame++)
            {
                yield return null;

                ILocalizationProvider localizationManager = LocalizationManager.Current;
                if (localizationManager != null)
                {
                    bindRoutine = null;
                    Bind(localizationManager);
                    yield break;
                }
            }

            bindRoutine = null;
            Log.W($"[UILocalizeText] '{name}' 이(가) {BindWaitFrameLimit} 프레임 안에 LocalizationManager 를 찾지 못해 키 원문을 출력합니다.");

            RefreshLocalizedCache();
            Apply();
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
        /// 정적/동적 조각을 합성 순서에 맞춰 TMP 에 출력하고 상위 레이아웃을 무효화한다.
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
            MarkLayoutDirty();
        }

        /// <summary>
        /// 텍스트 확정 후 상위 레이아웃을 다시 계산하도록 무효화한다.
        /// 비활성 상태면 TMP·UGUI 양쪽이 마크를 무시하므로 그냥 반환한다. 복구는 OnEnable 이 담당한다.
        /// </summary>
        private void MarkLayoutDirty()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (!layoutRootResolved)
            {
                cachedLayoutRoot = ResolveLayoutRoot();
                layoutRootResolved = true;
            }

            if (cachedLayoutRoot != null)
            {
                LayoutRebuilder.MarkLayoutForRebuild(cachedLayoutRoot);
            }
        }

        /// <summary>
        /// Canvas 경계까지 조상을 거슬러 올라가며 활성 ILayoutController 를 가진 최상위 조상을 찾는다.
        /// UGUI 기본 탐색은 ILayoutGroup 없는 중간 노드에서 끊겨 상위 ContentSizeFitter 를 놓치므로 직접 훑는다.
        /// </summary>
        /// <returns>무효화 대상 레이아웃 루트. 조상에 레이아웃 컨트롤러가 없으면 null.</returns>
        private RectTransform ResolveLayoutRoot()
        {
            RectTransform layoutRoot = null;
            Transform cursor = tmpText.rectTransform;

            while (cursor != null)
            {
                cursor.GetComponents(layoutControllerBuffer);
                for (int i = 0; i < layoutControllerBuffer.Count; i++)
                {
                    if (layoutControllerBuffer[i] is Behaviour behaviour && behaviour.isActiveAndEnabled)
                    {
                        layoutRoot = cursor as RectTransform;
                        break;
                    }
                }

                // 중첩 Canvas 는 레이아웃 격리 경계로 취급해 그 위로는 올라가지 않는다.
                if (cursor.GetComponent<Canvas>() != null)
                {
                    break;
                }

                cursor = cursor.parent;
            }

            layoutControllerBuffer.Clear();
            return layoutRoot;
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
