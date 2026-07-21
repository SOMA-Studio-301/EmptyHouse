using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 공용 확인 팝업 요청 채널. 호출자는 "어떤 팝업인지"와 "확인 시 무엇을 할지"만 실어 보낸다.
/// 문구·버튼 구성은 UIPopup 이 PopupType 으로 결정하므로 호출자는 팝업의 생김새를 알지 못한다.
/// 확인 동작을 콜백으로 실어 보내는 이유는, 그 동작의 주인이 호출자이기 때문이다 —
/// UI 매니저가 팝업 타입별 액션을 분기하면 팝업이 늘 때마다 매니저가 부풀고 씬마다 배선이 중복된다.
/// </summary>
[CreateAssetMenu(fileName = "PopupEventChannelSO", menuName = "Events/Popup")]
public class PopupEventChannelSO : ScriptableObject
{
    public UnityAction<PopupType, UnityAction> OnEventRaised = delegate { };

    /// <summary>확인 동작이 있는 팝업을 요청한다.</summary>
    /// <param name="type">띄울 팝업 종류.</param>
    /// <param name="onConfirm">확인 버튼을 눌렀을 때 실행할 동작.</param>
    public void RaiseEvent(PopupType type, UnityAction onConfirm)
    {
        OnEventRaised.Invoke(type, onConfirm);
    }

    /// <summary>확인 동작이 없는 알림형 팝업을 요청한다. 확인은 닫기와 같다.</summary>
    /// <param name="type">띄울 팝업 종류.</param>
    public void RaiseEvent(PopupType type)
    {
        OnEventRaised.Invoke(type, null);
    }
}
