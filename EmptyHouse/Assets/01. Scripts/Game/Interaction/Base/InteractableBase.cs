using Border.Core;
using UnityEngine;

/// <summary>
/// 상호작용 가능한 모든 오브젝트의 공통 부모 클래스
/// 이 클래스는 프롬프트 정보 제공과 실행 시점의 소음 발행 계약만 책임진다.
/// </summary>
public abstract class InteractableBase : MonoBehaviour
{
    /// <summary>입력 방식. Tap/Hold 여부에 따라 PlayerInteractor가 입력을 라우팅한다.</summary>
    public abstract InteractInputMethod InputMethod { get; }

    /// <summary>
    /// 다른 플레이어가 이미 점유 중인지 여부
    /// 기본값은 false — 점유 락이 필요한 대상만 override
    /// </summary>
    public virtual bool IsOccupied => false;

    /// <summary>
    /// 현재 프레임의 프롬프트 정보를 반환. UI는 이 값을 그대로 그림
    /// 프롬프트는 "조준 대상 × 손에 든 것"의 2차원 판정이므로 상호작용 주체를 받아
    /// 그의 인벤토리(<see cref="PlayerInteractor.Inventory"/>)를 조회
    /// </summary>
    /// <param name="interactor">이 대상을 조준 중인 상호작용 주체.</param>
    /// <returns>표시 상태·행위명·입력 방식·비활성 사유를 담은 정보.</returns>
    public abstract InteractPromptInfo GetPromptInfo(PlayerInteractor interactor);

    /// <summary>
    /// 이 Interactable의 실제 효과를 실행. Tap형은 입력 즉시, Hold형은 진행률 완료 시점에 호출된다.
    /// 호출 시점 판단은 하위 입력방식 클래스의 책임이며, 이 메서드는 결과 효과만 구현.
    /// </summary>
    /// <param name="interactor">행위를 실행한 상호작용 주체. 인벤 편입·소모는 이 주체의 인벤토리에 적용한다.</param>
    protected abstract void OnActivate(PlayerInteractor interactor);

    /// <summary>
    /// 실행 시점에 소음 이벤트를 1회 발행. (홀드는 완료 시점 1회).
    /// </summary>
    /// <param name="noiseDb">발행할 소음 강도(dB).</param>
    protected void RaiseNoise(float noiseDb)
    {
        // TODO(impl): 소음 이벤트 채널(발생 위치=transform.position, 강도=noiseDb, 발생원=this)로 1회 발행.
        Log.D($"[InteractableBase] RaiseNoise {noiseDb}dB from {name}");
    }
}
