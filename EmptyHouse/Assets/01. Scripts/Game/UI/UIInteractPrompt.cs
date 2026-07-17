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
/// 문구는 로컬라이즈 키로 받아 <see cref="UILocalizeText"/> 에 넘긴다 — 번역·언어 전환은 그쪽이 맡는다.
/// </summary>
public class UIInteractPrompt : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputReader inputReader;

    [Header("Widgets")]
    [SerializeField] private GameObject promptRoot;

    /// <summary>본문(행위명·사유) 로컬라이즈 텍스트. 입력키 접두사는 동적 prefix 로 주입한다.</summary>
    [SerializeField] private UILocalizeText promptText;

    /// <summary>색만 제어한다. promptText 와 같은 오브젝트의 TMP 다.</summary>
    [SerializeField] private TMP_Text promptLabel;

    [Header("Colors")]
    [SerializeField] private Color activeColor = Color.white;
    [SerializeField] private Color inactiveColor = Color.gray;

    [Header("Localization")]
    [LocalizeKey][SerializeField] private string holdKey = "INT_PROMPT_HOLD";

    // 입력키 접두사 버퍼("E " / "E 홀드 "). UILocalizeText 가 참조를 들고 합성하므로 재사용한다.
    private readonly StringBuilder prefixBuilder = new StringBuilder(16);

    // 직전에 그린 내용. Render 는 매 프레임 호출되므로 실제로 바뀐 프레임에만 위젯을 건드린다.
    private InteractPromptState renderedState = InteractPromptState.Hidden;
    private InteractInputMethod renderedInputMethod;
    private string renderedKey;

    /// <summary>프롬프트를 숨긴 상태로 시작해 캐시 초기값(Hidden)과 실제 위젯 상태를 일치시킨다.</summary>
    private void Awake()
    {
        promptRoot.SetActive(false);
    }

    /// <summary>언어 변경을 구독한다. 접두사("홀드")는 UILocalizeText 가 모르는 조각이라 직접 다시 만들어야 한다.</summary>
    private void OnEnable()
    {
        LocalizationManager.Current.OnLanguageChanged += OnLanguageChanged;
    }

    /// <summary>언어 변경 구독을 해제한다.</summary>
    private void OnDisable()
    {
        LocalizationManager.Current.OnLanguageChanged -= OnLanguageChanged;
    }

    /// <summary>언어가 바뀌면 렌더 캐시를 무효화해 다음 Render 에서 접두사를 새 언어로 다시 조립하게 한다.</summary>
    private void OnLanguageChanged()
    {
        renderedKey = null;
    }

    /// <summary>
    /// 프롬프트 정보를 화면에 반영한다. PlayerInteractor 가 매 프레임 호출한다.
    /// Hidden 이면 루트를 끄고, Inactive 면 회색 사유 문구만(입력키 미표기), Active 면 `E 회수` / `E 홀드 적재` 를 그린다(3-3).
    /// </summary>
    /// <param name="info">이번 프레임의 프롬프트 정보.</param>
    public void Render(InteractPromptInfo info)
    {
        string key = info.State == InteractPromptState.Inactive ? info.InactiveReasonKey : info.ActionKey;

        // 조준 대상·손에 든 것이 바뀔 때만 갱신한다. 매 프레임 접두사 조립·바인딩 조회를 돌리면 프레임마다 GC 가 쌓인다.
        if (info.State == renderedState && info.InputMethod == renderedInputMethod && key == renderedKey) return;

        renderedState = info.State;
        renderedInputMethod = info.InputMethod;
        renderedKey = key;

        if (info.State == InteractPromptState.Hidden)
        {
            promptRoot.SetActive(false);
            return;
        }

        // SetKey 는 UILocalizeText 의 Awake 가 돈 뒤라야 TMP 에 반영되므로 루트를 먼저 켠다.
        promptRoot.SetActive(true);

        if (info.State == InteractPromptState.Inactive)
        {
            promptLabel.color = inactiveColor;
            promptText.SetDynamicPrefix(null); // 비활성은 사유 문구만 — 입력키를 붙이지 않는다(3-3).
            promptText.SetKey(info.InactiveReasonKey);
            return;
        }

        promptLabel.color = activeColor;
        promptText.SetDynamicPrefix(BuildInputPrefix(info.InputMethod));
        promptText.SetKey(info.ActionKey);
    }

    /// <summary>
    /// 입력키 접두사를 만든다. 키는 하드코딩하지 않고 현재 바인딩을 런타임 조회하며(3-3 ⚠️),
    /// 홀드면 "홀드"(<see cref="holdKey"/>)를 덧붙인다. 예: `E ` / `E 홀드 `.
    /// </summary>
    /// <param name="inputMethod">이번 프롬프트의 입력 방식.</param>
    /// <returns>본문 앞에 붙일 접두사 버퍼.</returns>
    private StringBuilder BuildInputPrefix(InteractInputMethod inputMethod)
    {
        prefixBuilder.Length = 0;
        prefixBuilder.Append(inputReader.GetInteractBindingDisplayString());

        if (inputMethod == InteractInputMethod.Hold)
        {
            prefixBuilder.Append(' ');
            prefixBuilder.Append(LocalizationManager.Current.Get(holdKey));
        }

        prefixBuilder.Append(' ');

        return prefixBuilder;
    }
}
