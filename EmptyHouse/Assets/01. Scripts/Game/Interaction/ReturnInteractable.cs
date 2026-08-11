using Border.Core;
using Border.Localization;
using UnityEngine;

/// <summary>
/// 귀환 지점(버스) — 홀드 완료 시 그 플레이어를 귀환시키는 Interactable(세션루프.md 0-3·3장).
/// ItemPickup 과 달리 인벤에 넣거나 자신을 Destroy 하지 않는다: 홀드가 끝나면 주체 플레이어의 네트워크
/// 컴포넌트(PlayerReturn)를 통해 서버에 귀환을 통보하고, 서버가 로스터를 Extracted 로 바꾼다(개인 단위, D36).
/// 여러 명이 각자 귀환할 수 있으므로 점유 락(IsOccupied)은 두지 않는다.
/// </summary>
public class ReturnInteractable : HoldInteractableBase
{
    [Header("Hold")]
    [SerializeField] private float holdDurationSeconds = 3.0f; // 귀환 완료까지 유지 시간(초). ⚪ 튜닝값, 인스펙터 노출.

    [Header("Localization")]
    [LocalizeKey][SerializeField] private string returnActionKey = "INT_PROMPT_RETURN"; // 프롬프트 동사 키(예: 버스 탑승).

    /// <summary>홀드 완료까지 필요한 유지 시간(초). 인스펙터 값으로 내려간다.</summary>
    public override float HoldDurationSeconds => holdDurationSeconds;

    /// <summary>
    /// 귀환 프롬프트 정보를 반환한다 — 기본은 활성(귀환 동사·Hold 입력).
    /// </summary>
    /// <param name="interactor">이 귀환 지점을 조준 중인 상호작용 주체.</param>
    /// <returns>귀환 프롬프트 정보.</returns>
    public override InteractPromptInfo GetPromptInfo(PlayerInteractor interactor)
    {
        // 매 프레임 호출되므로 진입 트레이스를 두지 않는다(HoldInteractableBase.Update 규약).
        // 이미 귀환/사망 주체 분기는 관전 구현 후 얹는다 — 지금은 항상 활성(Hold)이다.
        return new InteractPromptInfo(InteractPromptState.Active, returnActionKey, InteractInputMethod.Hold, holdDurationSeconds, null);
    }

    /// <summary>
    /// 홀드 완료 시 호출된다. 주체 플레이어의 PlayerReturn 을 통해 서버에 귀환을 통보한다(client→server).
    /// 회수가 아니므로 인벤 편입·Destroy 는 하지 않는다.
    /// </summary>
    /// <param name="interactor">홀드를 완료한 주체(귀환하는 본인, 소유자 클라).</param>
    protected override void OnActivate(PlayerInteractor interactor)
    {
        // 회수가 아니므로 인벤 편입·Destroy 없이, 주체 플레이어의 네트워크 창구로 서버에 귀환을 통보한다.
        // 소음 발행(RaiseNoise) 여부는 데이터 확정 후 얹는다.
        interactor.GetComponentInParent<PlayerReturn>().RequestReturn();
    }
}
