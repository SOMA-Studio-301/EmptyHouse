using Border.Core;
using Border.Localization;
using UnityEngine;

/// <summary>
/// 기름 픽업 오브젝트 (조작상호작용UI.md 3-5: 홀드 2.0s 적재).
/// 회수 결과는 <see cref="ItemPickupInteractable"/> 과 동일하게 빈 슬롯 편입이지만,
/// 입력 방식만 홀드라서 클래스가 갈린다 — 진행률·취소는 <see cref="HoldInteractableBase"/> 가 관리한다.
/// </summary>
public class OilPickupInteractable : HoldInteractableBase
{
    [Header("Item")]
    [SerializeField] private ItemDataSO itemData;

    [Header("Hold")]
    [SerializeField] private float holdSeconds = 2.0f; // 3-10 hold_oil_sec ⚪

    [Header("Localization")]
    [LocalizeKey][SerializeField] private string inventoryFullReasonKey = "INT_MSG_INVENTORY_FULL"; // 슬롯 만재 시 비활성 사유 (3-8 E8)

    /// <summary>적재에 필요한 홀드 유지 시간(초).</summary>
    public override float HoldDurationSeconds => holdSeconds;

    /// <summary>
    /// 빈 슬롯이 있으면 활성(홀드 "적재"), 만재면 비활성 "가방이 가득 찼습니다"를 반환한다(3-8 E8).
    /// </summary>
    /// <param name="interactor">이 픽업을 조준 중인 상호작용 주체.</param>
    /// <returns>주체의 빈 슬롯 여부에 따른 프롬프트 정보.</returns>
    public override InteractPromptInfo GetPromptInfo(PlayerInteractor interactor)
    {
        // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
        if (!interactor.Inventory.HasEmptySlot())
        {
            return new InteractPromptInfo(InteractPromptState.Inactive, null, InteractInputMethod.Hold, holdSeconds, inventoryFullReasonKey);
        }

        return new InteractPromptInfo(InteractPromptState.Active, itemData.PickupVerbKey, InteractInputMethod.Hold, holdSeconds, null);
    }

    /// <summary>
    /// 홀드 완료 시점에 호출된다: 주체의 빈 슬롯에 기름을 넣고(자동 장착 금지, 4-2)
    /// 소음을 1회 발행한 뒤 오브젝트를 소멸시킨다(3-4: 홀드는 완료 시점 1회 발행).
    /// </summary>
    /// <param name="interactor">홀드를 완료한 상호작용 주체.</param>
    protected override void OnActivate(PlayerInteractor interactor)
    {
        Log.D($"[OilPickupInteractable] OnActivate {itemData.NameKey}");

        // 프롬프트가 Active 였어도 완료 프레임에 만재일 수 있다(홀드 중 다른 경로로 슬롯이 찬 경우).
        if (!interactor.Inventory.TryAdd(itemData, 0)) return;

        // Destroy 이후에는 transform·name 접근이 위험하므로 소음을 먼저 발행한다.
        RaiseNoise(itemData.PickupNoiseDb);
        Destroy(gameObject);
    }
}
