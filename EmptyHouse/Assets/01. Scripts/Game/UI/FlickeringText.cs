using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// TMP 텍스트를 형광등처럼 깜빡이게 하고, 마우스 호버 시 깜빡임을 접고 밝게 고정하는 연출 컴포넌트.
/// <see cref="EmptyHouse.Environment.FlickeringLight"/> 의 텍스트판이다 — 커브 한 루프를 반복해 값을 흔든다.
/// 커브의 X축은 초, Y축은 알파(0~1)이며 루프 길이는 커브 마지막 키의 시간이다.
/// 버튼처럼 레이캐스트 대상이 있는 오브젝트에 붙이면 자식 TMP_Text 를 자동으로 찾아 구동한다(셀프 컨테인드).
/// 레이캐스트 대상이 없는 오브젝트에 붙이면 호버 이벤트가 오지 않아 순수 깜빡임만 재생된다.
/// </summary>
public class FlickeringText : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Flicker")]
    [Tooltip("깜빡임 패턴. X=초, Y=알파(0~1). 마지막 키의 시간이 한 루프 길이가 된다. Y가 0 이면 그 순간 글자가 사라진다.")]
    [SerializeField] private AnimationCurve alphaCurve = new AnimationCurve(
        new Keyframe(0f, 1f),
        new Keyframe(1.6f, 0.85f),
        new Keyframe(2.4f, 1f),
        new Keyframe(3.8f, 1f),
        new Keyframe(3.82f, 0.15f),
        new Keyframe(3.94f, 0.15f),
        new Keyframe(3.96f, 1f),
        new Keyframe(4.6f, 1f),
        new Keyframe(4.62f, 0.1f),
        new Keyframe(4.7f, 0.1f),
        new Keyframe(4.72f, 1f),
        new Keyframe(6f, 1f)); // 기본: 6초에 짧은 깜빡임 2회

    [Tooltip("깜빡이는 정도. 0=깜빡임 없음(항상 밝음), 1=커브 원본 그대로. 그 사이 값은 커브와 1.0 사이를 보간해 약하게 깜빡인다.")]
    [Range(0f, 1f)]
    [SerializeField] private float flickerStrength = 1f;

    [Tooltip("켜면 인스턴스마다 루프 시작 위상을 랜덤화한다. 여러 버튼이 같은 박자로 깜빡이는 것을 막는다.")]
    [SerializeField] private bool randomizePhase = true;

    [Header("Hover")]
    [Tooltip("마우스를 올렸을 때 고정할 알파. 보통 1(완전히 밝게).")]
    [Range(0f, 1f)]
    [SerializeField] private float hoverAlpha = 1f;

    [Tooltip("깜빡임 상태와 호버 상태를 오가는 전환 속도(초당). 클수록 즉각적으로 밝아진다.")]
    [SerializeField] private float transitionSpeed = 8f;

    private TMP_Text targetText; // 대상 텍스트(자식에서 자동 탐색)
    private float phaseOffset;   // 루프 시작 위상
    private bool isHovered;      // 현재 호버 여부
    private float hoverBlend;    // 0=깜빡임, 1=호버. 전환을 부드럽게 만드는 보간 계수

    /// <summary>루프 길이(초). 커브 마지막 키의 시간.</summary>
    private float LoopDuration => alphaCurve.length > 0 ? alphaCurve[alphaCurve.length - 1].time : 0f;

    /// <summary>대상 텍스트를 캐시하고 인스턴스별 루프 위상을 정한다.</summary>
    private void Awake()
    {
        targetText = GetComponentInChildren<TMP_Text>(true);
        if (randomizePhase) phaseOffset = Random.Range(0f, Mathf.Max(LoopDuration, 0.0001f));
    }

    /// <summary>호버·알파를 초기 상태로 되돌린다(깜빡임 도중 비활성화되어 흐린 채 멈추는 것 방지).</summary>
    private void OnDisable()
    {
        isHovered = false;
        hoverBlend = 0f;
        targetText.alpha = 1f;
    }

    /// <summary>깜빡임 알파를 계산하고 호버 밝기와 블렌드해 매 프레임 반영한다.</summary>
    private void Update()
    {
        float loop = LoopDuration;
        float curve = loop > 0f ? alphaCurve.Evaluate(Mathf.Repeat(Time.unscaledTime + phaseOffset, loop)) : 1f;
        float flickerAlpha = Mathf.Lerp(1f, curve, flickerStrength);

        float targetBlend = isHovered ? 1f : 0f;
        hoverBlend = Mathf.MoveTowards(hoverBlend, targetBlend, transitionSpeed * Time.unscaledDeltaTime);

        targetText.alpha = Mathf.Lerp(flickerAlpha, hoverAlpha, hoverBlend);
    }

    /// <summary>마우스가 올라오면 밝기 고정으로 전환한다.</summary>
    /// <param name="eventData">포인터 이벤트 데이터(미사용).</param>
    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovered = true;
    }

    /// <summary>마우스가 벗어나면 다시 깜빡임으로 전환한다.</summary>
    /// <param name="eventData">포인터 이벤트 데이터(미사용).</param>
    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
    }
}
