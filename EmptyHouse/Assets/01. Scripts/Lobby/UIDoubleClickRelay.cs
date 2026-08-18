using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 클릭을 받는 오브젝트에 붙어 더블 클릭만 걸러 이벤트로 올리는 릴레이.
/// 버튼의 클릭 이벤트에는 PointerEventData 가 실리지 않아 연속 클릭 횟수를 볼 수 없어, 그 사이를 잇는 용도다.
/// 같은 오브젝트의 Button 과 함께 클릭을 받으므로 단일 클릭은 버튼이, 더블 클릭은 이쪽이 처리한다.
/// 판정 창은 InputSystemUIInputModule 이 쥐고 있는 0.3초 고정값이다.
/// </summary>
[DisallowMultipleComponent]
public class UIDoubleClickRelay : MonoBehaviour, IPointerClickHandler
{
    /// <summary>발행: 더블 클릭.</summary>
    public event Action DoubleClicked;

    /// <summary>연속 클릭 횟수가 2일 때만 올린다. 3연타가 두 번 터지지 않게 정확히 2에서만 받는다.</summary>
    /// <param name="eventData">EventSystem 이 전달하는 포인터 데이터</param>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.clickCount != 2) return;

        DoubleClicked?.Invoke();
    }
}
