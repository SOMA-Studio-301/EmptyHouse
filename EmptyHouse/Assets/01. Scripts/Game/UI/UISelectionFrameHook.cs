using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// <see cref="Selectable"/>에 부착되어 EventSystem의 Select/Deselect 이벤트를 가로채
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
/// 필드 이름은 패키지판과 일치시켜야 한다 — 프리팹의 m_Script 를 갈아끼울 때 Unity 가 이름으로 직렬화 데이터를 매칭한다.
/// ColorTint 트랜지션 색상은 에디터에서 "Reset ColorBlock To White" 메뉴로 1회 베이킹해 둔다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Selectable))]
public class UISelectionFrameHook : MonoBehaviour,
    ISelectHandler,
    IDeselectHandler
{
    [Tooltip("선택 상태에서 활성화될 9-slice 프레임 루트 GameObject.")]
    [SerializeField] private GameObject selectionFrame;

    /// <summary>
    /// 프레임 초기 상태를 비활성으로 설정한다.
    /// </summary>
    private void Awake()
    {
        if (selectionFrame != null)
        {
            selectionFrame.SetActive(false);
        }
    }

    /// <summary>
    /// Selectable이 비활성/파괴될 때 프레임이 켜진 상태로 남는 것을 방지한다.
    /// </summary>
    private void OnDisable()
    {
        if (selectionFrame != null)
        {
            selectionFrame.SetActive(false);
        }
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

        if (selectionFrame != null)
        {
            selectionFrame.SetActive(true);
        }
    }

    /// <summary>
    /// EventSystem이 본 Selectable의 선택을 해제했을 때 호출되어 프레임을 비활성화한다.
    /// </summary>
    /// <param name="eventData">BaseEventData</param>
    public void OnDeselect(BaseEventData eventData)
    {
        if (selectionFrame != null)
        {
            selectionFrame.SetActive(false);
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
