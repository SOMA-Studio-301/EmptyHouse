using Border.Localization;
using UnityEngine;

/// <summary>
/// 무전기 전용 탭 픽업. 일반 인벤토리를 사용하지 않고 플레이어의
/// <see cref="PlayerRadioSlot"/> 한 칸만 채운 뒤 월드 오브젝트를 제거한다.
/// </summary>
public sealed class RadioPickupInteractable : SingleClickInteractableBase
{
    [Header("Radio")]
    [LocalizeKey] [SerializeField] private string pickupVerbKey = "INT_PROMPT_PICKUP";
    [LocalizeKey] [SerializeField] private string alreadyOwnedReasonKey = "INT_MSG_RADIO_OWNED";
    [SerializeField] private float pickupNoiseDb = 12f;

    public override InteractPromptInfo GetPromptInfo(PlayerInteractor interactor)
    {
        PlayerRadioSlot slot = interactor.GetComponent<PlayerRadioSlot>();
        if (slot == null || slot.IsFilled)
        {
            return new InteractPromptInfo(
                InteractPromptState.Inactive,
                null,
                InteractInputMethod.Tap,
                0f,
                alreadyOwnedReasonKey);
        }

        return new InteractPromptInfo(
            InteractPromptState.Active,
            pickupVerbKey,
            InteractInputMethod.Tap,
            0f,
            null);
    }

    protected override void OnActivate(PlayerInteractor interactor)
    {
        PlayerRadioSlot slot = interactor.GetComponent<PlayerRadioSlot>();
        if (slot == null || !slot.TryFill()) return;

        interactor.Inventory.EquipRadioOnPickup(); // 주운 즉시 손에 든다 — TryAdd 자동 장착과 같은 규칙(전환 딜레이 없음)

        RaiseNoise(pickupNoiseDb);
        DespawnSelf();
    }
}
