using System.Text;
using Border.Core;
using Border.Localization;
using TMPro;
using UnityEngine;

/// <summary>
/// 조준선 아래 상호작용 프롬프트를 그리는 HUD 위젯 (조작상호작용UI.md 3-3).
/// Screen Space - Overlay 캔버스에 놓이되, 캔버스는 플레이어 프리팹의 자식이라 인스펙터 직접 참조가 가능하다
/// (비소유자 인스턴스는 PlayerController 가 캔버스째 끈다).
/// 이 클래스는 <see cref="InteractPromptInfo"/> 를 그대로 그릴 뿐 대상 타입으로 분기하지 않는다(3-2).
/// 신규 기믹이 추가돼도 이 파일은 수정될 이유가 없다.
/// 입력키는 프레임 안 <see cref="keyText"/> 가, 문구는 <see cref="promptText"/> 가 따로 그린다 —
/// 번역·언어 전환은 후자가 맡고, 전자는 현재 바인딩을 런타임 조회한 결과라 번역 대상이 아니다.
/// </summary>
public class UIInteractPrompt : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputReader inputReader;

    [Header("Widgets")]
    [SerializeField] private GameObject keyRoot; // 입력키 프레임 루트. 비활성 상태에선 통째로 끈다(3-3 입력키 미표기)
    [SerializeField] private TMP_Text keyText; // 프레임 안 입력키 글자. 하드코딩 금지 — 바인딩을 런타임 조회해 채운다
    [SerializeField] private GameObject promptRoot; // 본문 문구 루트

    /// <summary>본문(행위명·사유) 로컬라이즈 텍스트. 홀드 표기는 동적 prefix 로 주입한다.</summary>
    [SerializeField] private UILocalizeText promptText;

    /// <summary>색만 제어한다. promptText 와 같은 오브젝트의 TMP 다.</summary>
    [SerializeField] private TMP_Text promptLabel;

    [Header("Colors")]
    [SerializeField] private Color activeColor = Color.white;
    [SerializeField] private Color inactiveColor = Color.gray;

    [Header("Localization")]
    [LocalizeKey][SerializeField] private string holdKey = "INT_PROMPT_HOLD";

    // 홀드 표기 버퍼("[홀드] "). UILocalizeText 가 참조를 들고 합성하므로 재사용한다.
    private readonly StringBuilder prefixBuilder = new StringBuilder(16);

    // 직전에 그린 내용. Render 는 매 프레임 호출되므로 실제로 바뀐 프레임에만 위젯을 건드린다.
    private InteractPromptState renderedState = InteractPromptState.Hidden;
    private InteractInputMethod renderedInputMethod;
    private string renderedKey;

    /// <summary>프롬프트를 숨긴 상태로 시작해 캐시 초기값(Hidden)과 실제 위젯 상태를 일치시킨다.</summary>
    private void Awake()
    {
        keyRoot.SetActive(false);
        promptRoot.SetActive(false);
    }

    /// <summary>언어 변경을 구독한다. 홀드 표기는 UILocalizeText 가 모르는 조각이라 직접 다시 만들어야 한다.</summary>
    private void OnEnable()
    {
        LocalizationManager.Current.OnLanguageChanged += OnLanguageChanged;
    }

    /// <summary>언어 변경 구독을 해제한다.</summary>
    private void OnDisable()
    {
        LocalizationManager.Current.OnLanguageChanged -= OnLanguageChanged;
    }

    /// <summary>언어가 바뀌면 렌더 캐시를 무효화해 다음 Render 에서 홀드 표기를 새 언어로 다시 조립하게 한다.</summary>
    private void OnLanguageChanged()
    {
        renderedKey = null;
    }

    /// <summary>
    /// 프롬프트 정보를 화면에 반영한다. PlayerInteractor 가 매 프레임 호출한다.
    /// Hidden 이면 전부 끄고, Inactive 면 회색 사유 문구만(입력키 프레임 미표시),
    /// Active 면 프레임에 입력키를, 본문에 `귀환` / `[홀드] 적재` 를 그린다(3-3).
    /// </summary>
    /// <param name="info">이번 프레임의 프롬프트 정보.</param>
    public void Render(InteractPromptInfo info)
    {
        string key = info.State == InteractPromptState.Inactive ? info.InactiveReasonKey : info.ActionKey;

        // 조준 대상·손에 든 것이 바뀔 때만 갱신한다. 매 프레임 표기 조립·바인딩 조회를 돌리면 프레임마다 GC 가 쌓인다.
        if (info.State == renderedState && info.InputMethod == renderedInputMethod && key == renderedKey) return;

        renderedState = info.State;
        renderedInputMethod = info.InputMethod;
        renderedKey = key;

        if (info.State == InteractPromptState.Hidden)
        {
            keyRoot.SetActive(false);
            promptRoot.SetActive(false);
            return;
        }

        // SetKey 는 UILocalizeText 의 Awake 가 돈 뒤라야 TMP 에 반영되므로 루트를 먼저 켠다.
        promptRoot.SetActive(true);

        if (info.State == InteractPromptState.Inactive)
        {
            keyRoot.SetActive(false); // 비활성에 입력키를 붙이면 "누르면 된다"는 오독을 부른다(3-3).
            promptLabel.color = inactiveColor;
            promptText.SetDynamicPrefix(null);
            promptText.SetKey(info.InactiveReasonKey);
            return;
        }

        keyRoot.SetActive(true);
        keyText.SetText(inputReader.GetInteractBindingDisplayString());

        promptLabel.color = activeColor;
        promptText.SetDynamicPrefix(info.InputMethod == InteractInputMethod.Hold ? BuildHoldPrefix() : null);
        promptText.SetKey(info.ActionKey);
    }

    /// <summary>
    /// 홀드 표기를 만든다. 대괄호는 구두점이라 코드에 두고, 안의 단어만 <see cref="holdKey"/> 로 번역한다. 예: `[홀드] `.
    /// </summary>
    /// <returns>본문 앞에 붙일 표기 버퍼.</returns>
    private StringBuilder BuildHoldPrefix()
    {
        prefixBuilder.Length = 0;
        prefixBuilder.Append('[');
        prefixBuilder.Append(LocalizationManager.Current.Get(holdKey));
        prefixBuilder.Append("] ");

        return prefixBuilder;
    }
}
