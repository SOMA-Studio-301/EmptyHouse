using Border.Core;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 관전 컨트롤러(폰)와 관전 UI(HUD) 사이의 클라 로컬 신호 채널.
/// 둘 다 자기완결 프리팹이라 서로를 직접 참조할 수 없어, 이 SO 하나로 양방향을 잇는다.
/// 서버로 복제되지 않는다 — 관전은 본인 화면의 로컬 연출이고, 발행·수신 모두 관전 중인 소유자 클라에서만 일어난다(조작상호작용UI.md 3-8-1).
/// </summary>
[CreateAssetMenu(fileName = "SpectatorEventChannelSO", menuName = "Events/Spectator")]
public class SpectatorEventChannelSO : ScriptableObject
{
    public UnityAction<ulong> OnTargetChanged = delegate { }; // 관전 대상 변경(컨트롤러 → UI). 대상 clientId 전달
    public UnityAction<int> OnCycleRequested = delegate { };  // 대상 순환 요청(UI 좌우 버튼 → 컨트롤러). 방향(+1 다음 / -1 이전) 전달

    /// <summary>관전 대상 변경을 방송한다. 컨트롤러가 카메라를 대상에 붙일 때 호출한다.</summary>
    /// <param name="clientId">새 관전 대상의 clientId.</param>
    public void RaiseTargetChanged(ulong clientId)
    {
        Log.D($"[SpectatorEventChannelSO] RaiseTargetChanged {clientId}");
        OnTargetChanged.Invoke(clientId);
    }

    /// <summary>대상 순환을 요청한다. UI 좌우 버튼이 호출하며 ←/→ 키 입력과 같은 경로로 합류한다.</summary>
    /// <param name="direction">순환 방향(+1 다음 / -1 이전).</param>
    public void RaiseCycleRequested(int direction)
    {
        Log.D($"[SpectatorEventChannelSO] RaiseCycleRequested {direction}");
        OnCycleRequested.Invoke(direction);
    }
}
