using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Border.Localization;

/// <summary>
/// 공통 UI 버튼의 클릭 이벤트·라벨/스프라이트 갱신·클릭 사운드를 담당한다.
///
/// Border.UI.UIGenericButton 을 포크한 게임 소유 버전이다. 포크한 이유는 하나다:
/// 패키지판은 Border.asmdef 안에 있고 asmdef 는 Assembly-CSharp 을 참조할 수 없어 AudioId 를 볼 수 없다 — 사운드를 넣을 방법이 없다.
/// 라벨 로컬라이징은 여전히 Border.Localization 에 의존한다. 포크는 사운드를 위한 것이지 패키지 의존을 끊은 게 아니다.
///
/// 사운드는 Click() 이 아니라 button.onClick 에 직접 등록한다.
/// 그래야 Clicked 를 구독하지 않고 onClick 을 직접 쓰는 로비/메뉴 버튼도 이 컴포넌트만 붙이면 코드 수정 없이 소리가 난다.
///
/// 필드 이름은 패키지판과 일치시켜야 한다 — 프리팹의 m_Script 를 갈아끼울 때 Unity 가 이름으로 직렬화 데이터를 매칭한다.
/// </summary>
public class UIGenericButton : MonoBehaviour
{
    [SerializeField] private UILocalizeText buttonLocalizeText;
    [SerializeField] private Button button;

    [Header("Audio")]
    [SerializeField] private SFXEventChannelSO sfxEventChannel;
    [SerializeField] private AudioId clickAudioId = AudioId.Sfx_Ui_Click;    // 버튼별로 교체 가능. None 이면 무음

    public UnityAction Clicked;

    /// <summary>버튼 입력 허용 여부. 구독자가 Button 을 따로 참조하지 않아도 되도록 위임한다.</summary>
    public bool Interactable
    {
        get => ResolveButton().interactable;
        set => ResolveButton().interactable = value;
    }

    /// <summary>
    /// 누락된 참조를 자동 보정하고 클릭 사운드를 onClick 에 등록한다.
    /// </summary>
    private void Awake()
    {
        ResolveButton();
        EnsureLocalizeTextReference();

        button.onClick.AddListener(PlayClickSfx);
    }

    /// <summary>Button 참조를 보정해 반환한다. Awake 보다 먼저 접근돼도 안전하도록 프로퍼티도 이걸 거친다.</summary>
    /// <returns>이 오브젝트의 Button</returns>
    private Button ResolveButton()
    {
        button ??= GetComponent<Button>();
        return button;
    }

    /// <summary>
    /// 버튼 클릭 이벤트를 외부 구독자에게 전달한다.
    /// </summary>
    public void Click()
    {
        Clicked?.Invoke();
    }

    /// <summary>
    /// 클릭 사운드를 발행한다. 입력 피드백이므로 핸들러가 클릭을 거부하더라도 울린다.
    /// </summary>
    private void PlayClickSfx()
    {
        sfxEventChannel.RaisePlayEvent(clickAudioId, transform.position);
    }

    /// <summary>
    /// 버튼 라벨에 사용할 로컬라이징 키를 설정한다.
    /// </summary>
    /// <param name="localizationKey">적용할 로컬라이징 키</param>
    public void SetButton(string localizationKey)
    {
        EnsureLocalizeTextReference();

        if (buttonLocalizeText == null)
        {
            return;
        }

        buttonLocalizeText.SetKey(localizationKey);
    }

    /// <summary>
    /// 버튼 이미지 스프라이트를 교체한다.
    /// </summary>
    /// <param name="sprite">적용할 스프라이트</param>
    public void SetSprite(Sprite sprite)
    {
        if (button != null)
            button.image.sprite = sprite;
    }

    /// <summary>
    /// 버튼 하위 텍스트에서 UILocalizeText를 찾고 없으면 자동으로 생성한다.
    /// </summary>
    private void EnsureLocalizeTextReference()
    {
        if (buttonLocalizeText != null)
        {
            return;
        }

        buttonLocalizeText = GetComponentInChildren<UILocalizeText>(true);
        if (buttonLocalizeText != null)
        {
            return;
        }

        TMP_Text tmpText = GetComponentInChildren<TMP_Text>(true);
        if (tmpText == null)
        {
            return;
        }

        buttonLocalizeText = tmpText.GetComponent<UILocalizeText>();
        if (buttonLocalizeText == null)
        {
            buttonLocalizeText = tmpText.gameObject.AddComponent<UILocalizeText>();
        }
    }
}
