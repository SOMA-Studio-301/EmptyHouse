using Border.Core;
using Border.Localization;
using UnityEngine;

/// <summary>
/// 벽장 안쪽 상호작용부 (조작상호작용UI.md 3-5-1, 2-1).
/// 은신 중 E는 이 면 전용이다. 홀드 완료 시 벽장 루트에 탈출을 요청한다.
/// 콜라이더가 벽장 안쪽에 있어 점유자만 물리적으로 조준할 수 있고, 신원은 서버 RPC 가 최종 검증한다(비점유자 요청은 서버가 기각).
/// </summary>
public sealed class WardrobeExit : HoldInteractableBase
{
    [Header("Wardrobe")]
    [SerializeField] private WardrobeInteractable wardrobe; // 벽장 루트(프리팹 내부 부모 참조)

    [Header("Hold / Noise")]
    [SerializeField] private float holdWardrobeSeconds = 1.5f; // 3-10 hold_wardrobe_sec ⚪ — 진입과 공통
    [SerializeField] private float doorNoiseDb = 20f; // 3-10 wardrobe_door_noise_db ⚪ — 완료 시 1회

    [Header("Localization")]
    [LocalizeKey][SerializeField] private string exitVerbKey = "INT_PROMPT_WARDROBE_EXIT"; // 활성 프롬프트 행위명 '나가기'

    public override float HoldDurationSeconds => holdWardrobeSeconds;

    /// <summary>
    /// 항상 '나가기' 홀드 활성 프롬프트를 반환한다. 조준 가능한 주체는 사실상 점유자뿐이다(3-5-1).
    /// </summary>
    /// <param name="interactor">이 면을 조준 중인 상호작용 주체.</param>
    /// <returns>탈출 프롬프트 정보.</returns>
    public override InteractPromptInfo GetPromptInfo(PlayerInteractor interactor)
    {
        // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
        return new InteractPromptInfo(
            InteractPromptState.Active,
            exitVerbKey,
            InteractInputMethod.Hold,
            holdWardrobeSeconds,
            null);
    }

    /// <summary>
    /// 탈출 홀드 완료 시점의 효과: 소음 1회 발행(3-4) 후 벽장 루트에 탈출을 요청한다.
    /// </summary>
    /// <param name="interactor">홀드를 완료한 상호작용 주체.</param>
    protected override void OnActivate(PlayerInteractor interactor)
    {
        RaiseNoise(doorNoiseDb);
        wardrobe.RequestExit(interactor);
    }
}
