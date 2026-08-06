using Border.Core;
using Border.Localization;
using UnityEngine;

/// <summary>
/// 벽장 바깥 문 상호작용부 (조작상호작용UI.md 3-5-1, 3-5).
/// 홀드 완료 시 벽장 루트에 진입을 요청한다. 점유 판정·은신 세팅은 루트가 전담하고, 이 면은 프롬프트만 갖는다(3-2).
/// </summary>
public sealed class WardrobeEntrance : HoldInteractableBase
{
    [Header("Wardrobe")]
    [SerializeField] private WardrobeInteractable wardrobe; // 점유 단일 소스인 벽장 루트(프리팹 내부 부모 참조)

    [Header("Hold / Noise")]
    [SerializeField] private float holdWardrobeSeconds = 1.5f; // 3-10 hold_wardrobe_sec ⚪ — 진입·탈출 공통
    [SerializeField] private float doorNoiseDb = 20f; // 3-10 wardrobe_door_noise_db ⚪ — 완료 시 1회

    [Header("Localization")]
    [LocalizeKey][SerializeField] private string hideVerbKey = "INT_PROMPT_HIDE"; // 활성 프롬프트 행위명 '숨기'
    [LocalizeKey][SerializeField] private string inUseReasonKey = "INT_MSG_IN_USE"; // 점유 중 비활성 사유 '사용 중'(M1 공통 키)

    public override float HoldDurationSeconds => holdWardrobeSeconds;

    // 점유 중이면 베이스 파이프라인이 홀드 시작 자체를 막는다(3-9 M1). GetPromptInfo 의 Inactive 표시와 짝을 이룬다.
    public override bool IsOccupied => wardrobe.IsOccupied;

    /// <summary>
    /// 프롬프트를 반환한다: 점유 중이면 '사용 중' 비활성, 아니면 '숨기' 홀드 활성(3-5-1, 2-1).
    /// </summary>
    /// <param name="interactor">이 문을 조준 중인 상호작용 주체.</param>
    /// <returns>점유 상태에 따른 프롬프트 정보.</returns>
    public override InteractPromptInfo GetPromptInfo(PlayerInteractor interactor)
    {
        // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
        if (wardrobe.IsOccupied)
        {
            return new InteractPromptInfo(
                InteractPromptState.Inactive,
                null,
                InteractInputMethod.Hold,
                holdWardrobeSeconds,
                inUseReasonKey);
        }

        return new InteractPromptInfo(
            InteractPromptState.Active,
            hideVerbKey,
            InteractInputMethod.Hold,
            holdWardrobeSeconds,
            null);
    }

    /// <summary>
    /// 진입 홀드 완료 시점의 효과: 소음 1회 발행(3-4) 후 벽장 루트에 진입을 요청한다.
    /// </summary>
    /// <param name="interactor">홀드를 완료한 상호작용 주체.</param>
    protected override void OnActivate(PlayerInteractor interactor)
    {
        RaiseNoise(doorNoiseDb);
        wardrobe.RequestEnter(interactor);
    }
}
