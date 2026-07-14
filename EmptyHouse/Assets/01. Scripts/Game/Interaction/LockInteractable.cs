using System.Collections;
using Border.Core;
using Border.Localization;
using UnityEngine;

/// <summary>
/// 자물쇠 오브젝트
/// 열쇠_NN과 1:1로 짝지어지며, 판정은 인벤 조회가 아니라 손에 든 아이템
/// 개방 성공 시 열쇠는 소멸하고 해당 슬롯이 비며 자동 맨손
/// 개방 애니메이션 구간 동안 재입력을 무시하고(IsBusy), 개방 후에는 영구적으로 상호작용 대상에서 제외
/// </summary>
public class LockInteractable : SingleClickInteractableBase
{
    [Header("Pairing")]
    [SerializeField] private int pairId;

    [Header("Open Animation")]
    [SerializeField] private float openAnimationSeconds = 0.8f; // 3-10 lock_open_anim_sec ⚪

    [Header("Noise")]
    [SerializeField] private float openNoiseDb = 45f; // 3-10 lock_open_noise_db ⚪

    [Header("Localization")]
    [LocalizeKey][SerializeField] private string openVerbKey = "INT_PROMPT_OPEN"; // 손에 짝맞는 열쇠를 들었을 때의 활성 프롬프트 행위명
    [LocalizeKey][SerializeField] private string needsKeyReasonKey = "INT_MSG_KEY_REQUIRED"; // 손에 짝맞는 열쇠가 없을 때의 비활성 사유

    private bool isOpening;
    private bool isOpened;

    /// <summary>개방 애니메이션 중에는 재입력을 무시</summary>
    protected override bool IsBusy => isOpening;

    /// <summary>
    /// 프롬프트를 반환: 개방 완료·개방 중 → 미표시 / 손에 열쇠_NN → 활성 / 그 외 → 비활성
    /// 판정 기준은 오직 손에 든 것 — 인벤 소지 여부는 보지 않음
    /// </summary>
    /// <param name="interactor">이 자물쇠를 조준 중인 상호작용 주체.</param>
    /// <returns>자물쇠 상태·주체의 손에 따른 프롬프트 정보.</returns>
    public override InteractPromptInfo GetPromptInfo(PlayerInteractor interactor)
    {
        // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
        if (isOpened || isOpening)
        {
            return new InteractPromptInfo(InteractPromptState.Hidden, null, InteractInputMethod.Tap, 0f, null);
        }

        if (!HasMatchingKeyInHand(interactor))
        {
            return new InteractPromptInfo(InteractPromptState.Inactive, null, InteractInputMethod.Tap, 0f, needsKeyReasonKey);
        }

        return new InteractPromptInfo(InteractPromptState.Active, openVerbKey, InteractInputMethod.Tap, 0f, null);
    }

    /// <summary>손에 든 것이 이 자물쇠와 짝맞는 열쇠(Key · PairId 일치)인지 판정한다. 맨손이면 false.</summary>
    /// <param name="interactor">판정할 상호작용 주체.</param>
    /// <returns>짝맞는 열쇠를 손에 들었는지 여부.</returns>
    private bool HasMatchingKeyInHand(PlayerInteractor interactor)
    {
        InventorySlot held = interactor.Inventory.HeldSlot;

        // 맨손이면 HeldSlot 이 빈 값이라 Data 가 null 이다 — 여기서 걸러진다.
        return !held.IsEmpty && held.Data.Kind == ItemKind.Key && held.PairId == pairId;
    }

    /// <summary>
    /// 개방을 시작한다: 소음 1회 발행(3-4: Tap형 실행 시점) → 주체의 손에 든 열쇠 소멸(ConsumeHeld, 3-6)
    /// → 개방 애니메이션 후 영구 개방 상태로 전환한다.
    /// </summary>
    /// <param name="interactor">E 를 입력한 상호작용 주체. 이 주체의 손에 든 열쇠가 소모된다.</param>
    protected override void OnActivate(PlayerInteractor interactor)
    {
        Log.D($"[LockInteractable] OnActivate pairId={pairId}");

        isOpening = true;

        RaiseNoise(openNoiseDb);
        interactor.Inventory.ConsumeHeld(); // 열쇠 소멸 → 슬롯 빔 → 자동 맨손(4-2)

        StartCoroutine(OpenRoutine());
    }

    /// <summary>개방 애니메이션 구간을 대기한 뒤 영구 개방 상태로 전환한다.</summary>
    /// <returns>코루틴 열거자.</returns>
    private IEnumerator OpenRoutine()
    {
        yield return new WaitForSeconds(openAnimationSeconds);

        isOpening = false;
        isOpened = true;

        Log.D($"[LockInteractable] Opened pairId={pairId}");
    }
}
