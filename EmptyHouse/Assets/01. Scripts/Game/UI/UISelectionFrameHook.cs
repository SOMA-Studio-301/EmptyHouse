using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// <see cref="Selectable"/>에 부착되어 EventSystem의 Select/Deselect·포인터 Enter/Exit 이벤트를 가로채
/// 지정된 9-slice 선택 프레임 GameObject의 활성 상태를 토글한다.
///
/// Border.UI.UISelectionFrameHook 을 포크한 게임 소유 버전이다. 포크한 이유는 하나다:
/// 마우스 클릭 선택 시에는 프레임을 띄우지 않고 키보드/게임패드 네비게이션 선택에만 띄우도록
/// 동작을 바꿔야 하는데, 패키지 샘플 원본을 고치면 재임포트/버전업 때 덮어써진다.
///
/// 입력 출처 구분: 마우스 클릭은 Selectable.OnPointerDown 이 PointerEventData 를
/// SetSelectedGameObject 에 그대로 넘기고, 네비게이션은 AxisEventData 를 넘긴다.
/// 새 Input System 의 포인터도 ExtendedPointerEventData(PointerEventData 파생)라 동일하게 걸러진다.
///
/// 네비게이션 선택과 마우스 호버는 별개 상태로 들고 OR 로 합친다.
/// 그래야 키보드로 선택해 둔 버튼 위를 마우스가 스쳐 지나가도 프레임이 꺼지지 않는다.
///
/// 필드 이름은 패키지판과 일치시켜야 한다 — 프리팹의 m_Script 를 갈아끼울 때 Unity 가 이름으로 직렬화 데이터를 매칭한다.
/// ColorTint 트랜지션 색상은 에디터에서 "Reset ColorBlock To White" 메뉴로 1회 베이킹해 둔다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Selectable))]
public class UISelectionFrameHook : MonoBehaviour,
    ISelectHandler,
    IDeselectHandler,
    IPointerEnterHandler,
    IPointerExitHandler
{
    [Tooltip("선택 상태에서 활성화될 9-slice 프레임 루트 GameObject.")]
    [SerializeField] private GameObject selectionFrame;

    private Selectable selectable; // 상호작용 가능 여부 판정용. RequireComponent 로 보장된다
    private bool isNavigationSelected; // 키보드/게임패드 네비게이션으로 선택된 상태
    private bool isHovered; // 마우스 포인터가 위에 올라와 있는 상태
    private bool isForcedOn; // 호버·네비와 무관하게 프레임을 상시 켜두는 외부 강제 상태

    /// <summary>
    /// 호버·네비게이션과 무관하게 프레임을 상시 켜둘지 여부. 탭 그룹이 "현재 활성 탭"에 대해 켠다.
    /// 이 값은 마우스가 패널 안 다른 위젯으로 옮겨가도 유지되므로, 활성 표시가 EventSystem 선택에 휩쓸리지 않는다.
    /// </summary>
    public bool ForcedOn
    {
        set
        {
            isForcedOn = value;
            ApplyFrame();
        }
    }

    /// <summary>
    /// Selectable 을 캐싱하고 프레임 초기 상태를 비활성으로 설정한다.
    /// </summary>
    private void Awake()
    {
        selectable = GetComponent<Selectable>();
        ApplyFrame();
    }

    /// <summary>
    /// Selectable이 비활성/파괴될 때 프레임이 켜진 상태로 남는 것을 방지한다.
    /// </summary>
    private void OnDisable()
    {
        isNavigationSelected = false;
        isHovered = false;
        ApplyFrame();
    }

    /// <summary>
    /// EventSystem이 본 Selectable을 선택했을 때 호출된다.
    /// 키보드/게임패드 네비게이션 선택에만 프레임을 활성화하고, 마우스/터치 클릭 선택은 무시한다.
    /// </summary>
    /// <param name="eventData">BaseEventData</param>
    public void OnSelect(BaseEventData eventData)
    {
        if (eventData is PointerEventData)
        {
            return; // 마우스/터치 클릭에 의한 선택은 프레임을 띄우지 않음
        }

        isNavigationSelected = true;
        ApplyFrame();
    }

    /// <summary>
    /// EventSystem이 본 Selectable의 선택을 해제했을 때 호출되어 네비게이션 선택 상태를 푼다.
    /// </summary>
    /// <param name="eventData">BaseEventData</param>
    public void OnDeselect(BaseEventData eventData)
    {
        isNavigationSelected = false;
        ApplyFrame();
    }

    /// <summary>
    /// 마우스 포인터가 올라왔을 때 프레임을 활성화한다. 비활성 버튼은 무시한다.
    /// </summary>
    /// <param name="eventData">PointerEventData</param>
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!selectable.IsInteractable())
        {
            return; // 상호작용 불가 상태에서는 호버 표시를 하지 않음
        }

        isHovered = true;
        ApplyFrame();
    }

    /// <summary>
    /// 마우스 포인터가 벗어났을 때 호버 상태를 푼다. 네비게이션 선택 중이면 프레임은 유지된다.
    /// </summary>
    /// <param name="eventData">PointerEventData</param>
    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        ApplyFrame();
    }

    /// <summary>
    /// 네비게이션 선택·호버·강제 ON 중 하나라도 참이면 프레임을 켠다.
    /// </summary>
    private void ApplyFrame()
    {
        if (selectionFrame != null)
        {
            selectionFrame.SetActive(isNavigationSelected || isHovered || isForcedOn);
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// 에디터에서 1회 실행하여 본 Selectable의 ColorBlock(normal/highlighted/pressed/selected)을
    /// 흰색으로 베이킹한다. disabledColor는 디자이너 의도를 보존하기 위해 변경하지 않는다.
    /// 컴포넌트 인스펙터에서 우클릭 → "Reset ColorBlock To White" 로 호출한다.
    /// </summary>
    [ContextMenu("Reset ColorBlock To White")]
    private void ResetColorBlockToWhite()
    {
        if (!TryGetComponent<Selectable>(out var selectable))
        {
            return;
        }

        ColorBlock colors = selectable.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Color.white;
        colors.pressedColor = Color.white;
        colors.selectedColor = Color.white;
        selectable.colors = colors;

        EditorUtility.SetDirty(selectable);
    }
#endif
}
