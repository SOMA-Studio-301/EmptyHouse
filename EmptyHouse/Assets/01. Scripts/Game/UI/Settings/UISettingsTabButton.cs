using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 설정 창 탭 바의 버튼 하나. 클릭 처리는 UISettings 가 하고, 이쪽은 선택 상태 표시만 책임진다.
///
/// 선택과 마우스 호버는 별개 상태로 들고 OR 로 합친다.
/// 그래야 선택된 탭 위를 마우스가 스쳐 지나가도 표시가 꺼지지 않는다.
///
/// 실제 버튼은 자식(중첩 프리팹)이지만 포인터 Enter/Exit 는 계층을 타고 부모까지 올라오므로 여기서 바로 받는다.
/// </summary>
public class UISettingsTabButton : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    [SerializeField] private UIGenericButton button;
    [SerializeField] private GameObject selectedIndicator;

    private bool isSelected; // 이 탭이 선택된 상태인지. Selected 의 백킹 필드
    private bool isHovered; // 마우스 포인터가 위에 올라와 있는 상태

    public UIGenericButton Button => button; // UISettings 가 Clicked 를 구독하기 위한 통로

    public bool Selected // 이 탭의 선택 상태. 호버와 OR 로 합쳐져 선택 표시(밑줄)를 결정한다
    {
        get => isSelected;
        set
        {
            isSelected = value;
            ApplyIndicator();
        }
    }

    /// <summary>
    /// 비활성화될 때 호버가 켜진 채로 남는 것을 방지한다. 선택 상태는 유지한다.
    /// </summary>
    private void OnDisable()
    {
        isHovered = false;
        ApplyIndicator();
    }

    /// <summary>
    /// 마우스 포인터가 올라왔을 때 선택 표시를 켠다.
    /// </summary>
    /// <param name="eventData">PointerEventData</param>
    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovered = true;
        ApplyIndicator();
    }

    /// <summary>
    /// 마우스 포인터가 벗어났을 때 호버 상태를 푼다. 선택된 탭이면 표시는 유지된다.
    /// </summary>
    /// <param name="eventData">PointerEventData</param>
    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        ApplyIndicator();
    }

    /// <summary>
    /// 선택 또는 호버 중 하나라도 참이면 선택 표시(밑줄)를 켠다.
    /// </summary>
    private void ApplyIndicator()
    {
        selectedIndicator.SetActive(isSelected || isHovered);
    }
}
