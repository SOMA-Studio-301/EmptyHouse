using System;
using UnityEngine;

/// <summary>
/// 로컬 플레이어의 위장 재료 잔량을 HUD 로 전달하는 SO 채널 (조작상호작용UI.md 5장 — 상시 표시).
/// 잔량 NetworkVariable 이 Owner 읽기 전용이라 소유 클라이언트의 PlayerDisguise 하나만 발행한다 — 프로세스당 발행자는 항상 1개다.
/// 씬 레벨 Canvas-HUD 와 플레이어 프리팹이 서로를 참조하지 않고 만나는 지점이며, 네트워크 전송은 하지 않는다.
/// </summary>
[CreateAssetMenu(fileName = "SO_Event_DisguiseGaugeChanged", menuName = "Events/Player/Disguise Gauge Changed")]
public sealed class DisguiseGaugeChangedEventChannelSO : ScriptableObject
{
    public event Action<float> OnEventRaised;

    public float CurrentGauge01 { get; private set; } // 마지막으로 발행된 잔량(0~1). 늦게 켜진 HUD 가 초기 표시에 읽는다

    /// <summary>SO 활성화 시 캐시를 비운다. 에디터에서 SO 는 플레이 세션 사이에 살아남으므로 이전 세션 잔량이 남지 않게 한다.</summary>
    private void OnEnable()
    {
        CurrentGauge01 = 0f;
    }

    /// <summary>잔량을 캐시에 기록하고 구독자에게 방송한다.</summary>
    /// <param name="gauge01">현재 잔량(0~1).</param>
    public void RaiseEvent(float gauge01)
    {
        CurrentGauge01 = gauge01;
        OnEventRaised?.Invoke(gauge01);
    }
}
