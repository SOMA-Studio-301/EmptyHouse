using Border.Core;
using UnityEngine;

/// <summary>
/// 탭(단일 클릭) 입력으로 즉시 실행되는 Interactable의 공통 부모.
/// </summary>
public abstract class SingleClickInteractableBase : InteractableBase
{
    /// <summary>탭 입력 방식으로 고정한다.</summary>
    public sealed override InteractInputMethod InputMethod => InteractInputMethod.Tap;

    /// <summary>
    /// true인 동안 프롬프트가 숨겨지고 E 입력이 무반응 처리된다(예: 자물쇠 개방 애니메이션 중, 발전기 점등 중).
    /// 기본값은 false — 잠금구간이 필요한 대상만 override 한다.
    /// </summary>
    protected virtual bool IsBusy => false;

    /// <summary>
    /// PlayerInteractor가 E 입력(Started phase)을 받았을 때 호출한다.
    /// <see cref="IsBusy"/> 인 동안에는 아무 동작도 하지 않는다.
    /// </summary>
    /// <remarks>
    /// 소음 발행은 여기서 하지 않는다. 강도(dB)는 리프의 인스펙터 필드라 베이스가 알 수 없으므로,
    /// 리프의 <see cref="InteractableBase.OnActivate"/> 안에서 RaiseNoise 를 호출
    /// </remarks>
    /// <param name="interactor">E 를 입력한 상호작용 주체.</param>
    public void TryActivate(PlayerInteractor interactor)
    {
        Log.D($"[SingleClickInteractableBase] TryActivate on {name}");

        if (IsBusy) return;

        OnActivate(interactor);
    }
}
