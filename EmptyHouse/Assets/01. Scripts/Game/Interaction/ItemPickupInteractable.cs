using Border.Core;
using Border.Localization;
using UnityEngine;

/// <summary>
/// 탭 회수 픽업 오브젝트 — 열쇠·백신·스크랩·투척 오브젝트 공용
/// 아이템별 차이(프롬프트 동사·소음 dB)는 <see cref="ItemDataSO"/> 데이터로, 페어 번호는 인스펙터 값으로
/// 내려가므로 종류마다 클래스를 만들지 않는다
/// </summary>
public class ItemPickupInteractable : SingleClickInteractableBase
{
    [Header("Item")]
    [SerializeField] private ItemDataSO itemData;

    [Header("Pairing")]
    [SerializeField] private int pairId; // 열쇠만 의미(0-3 넘버링 규약). 그 외 0

    [Header("Localization")]
    [LocalizeKey][SerializeField] private string inventoryFullReasonKey = "INT_MSG_INVENTORY_FULL"; // 슬롯 만재 시 비활성 사유 (3-8 E8)

    /// <summary>
    /// 빈 슬롯이 있으면 활성(itemData 의 동사 키), 풀이면 비활성 "가방이 가득 찼습니다"를 반환
    /// </summary>
    /// <param name="interactor">이 픽업을 조준 중인 상호작용 주체.</param>
    /// <returns>주체의 빈 슬롯 여부에 따른 프롬프트 정보.</returns>
    public override InteractPromptInfo GetPromptInfo(PlayerInteractor interactor)
    {
        if (!interactor.Inventory.HasEmptySlot())
        {
            return new InteractPromptInfo(InteractPromptState.Inactive, null, InteractInputMethod.Tap, 0f, inventoryFullReasonKey);
        }

        return new InteractPromptInfo(InteractPromptState.Active, itemData.PickupVerbKey, InteractInputMethod.Tap, 0f, null);
    }

    /// <summary>
    /// 주체의 빈 슬롯에 아이템을 넣고, 소음을 발행한 뒤 오브젝트를 소멸시킨다
    /// </summary>
    /// <param name="interactor">E 를 입력한 상호작용 주체. 이 주체의 인벤토리에 편입된다.</param>
    protected override void OnActivate(PlayerInteractor interactor)
    {
        Log.D($"[ItemPickupInteractable] OnActivate {itemData.NameKey} pairId={pairId}");

        // 프롬프트가 Active 였어도 실행 프레임에 만재일 수 있다(멀티에서 다른 경로로 슬롯이 찬 경우).
        if (!interactor.Inventory.TryAdd(itemData, pairId)) return;

        // TODO: 지금은 Destroy 인데, 나중에는 비활성화 등으로 바꾸던가 해야 함
        // Destroy 이후에는 transform·name 접근이 위험하므로 소음을 먼저 발행한다.
        RaiseNoise(itemData.PickupNoiseDb);
        Destroy(gameObject);
    }
}
