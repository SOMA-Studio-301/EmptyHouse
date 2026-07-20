using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Selectable 에 붙어 EventSystem 의 Select/Deselect 를 이벤트로 올리는 릴레이.
/// 포커스를 받는 건 자식 Selectable 인데 표시를 바꾸는 건 부모라, 그 사이를 잇는 용도다.
/// 마우스·키보드·게임패드가 모두 EventSystem 을 거치므로 입력 장치를 가리지 않는다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Selectable))]
public class UIFocusRelay : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    /// <summary>발행: 포커스 변화. 얻었으면 true 를 싣는다.</summary>
    public event Action<bool> FocusChanged;

    public bool HasFocus => hasFocus; // 이 Selectable 이 포커스를 쥐고 있는지

    private bool hasFocus; // 현재 포커스 보유 상태. 같은 값의 중복 발행을 막는다

    /// <summary>비활성화될 때 포커스가 켜진 채로 남지 않게 잃음을 올린다.</summary>
    private void OnDisable()
    {
        SetFocus(false);
    }

    /// <summary>포커스를 얻었음을 올린다.</summary>
    /// <param name="eventData">EventSystem 이벤트 데이터. 쓰지 않는다</param>
    public void OnSelect(BaseEventData eventData)
    {
        SetFocus(true);
    }

    /// <summary>포커스를 잃었음을 올린다.</summary>
    /// <param name="eventData">EventSystem 이벤트 데이터. 쓰지 않는다</param>
    public void OnDeselect(BaseEventData eventData)
    {
        SetFocus(false);
    }

    /// <summary>포커스 상태를 바꾸고 변화가 있을 때만 발행한다.</summary>
    /// <param name="value">새 포커스 보유 여부</param>
    private void SetFocus(bool value)
    {
        if (hasFocus == value) return;

        hasFocus = value;
        FocusChanged?.Invoke(value);
    }
}
