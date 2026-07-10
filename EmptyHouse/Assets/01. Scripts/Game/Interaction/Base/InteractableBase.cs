using Border.Core;
using UnityEngine;

/// <summary>
/// 상호작용 가능한 모든 오브젝트의 공통 부모 클래스 (조작상호작용UI.md 3-2 Interactable 계약).
/// 판정 파이프라인(사거리·후보 선정·전역 예외)은 <see cref="PlayerInteractor"/> 하나가 전담하며,
/// 이 클래스는 프롬프트 정보 제공과 실행 시점의 소음 발행 계약만 책임진다.
/// 입력 방식(탭/홀드)에 따른 실행 타이밍은 <see cref="SingleClickInteractableBase"/>,
/// <see cref="HoldInteractableBase"/> 가 나눠 갖는다 — 이 클래스에 탭·홀드 분기를 추가하지 않는다.
/// </summary>
public abstract class InteractableBase : MonoBehaviour
{
    /// <summary>입력 방식. Tap/Hold 여부에 따라 PlayerInteractor가 입력을 라우팅한다.</summary>
    public abstract InteractInputMethod InputMethod { get; }

    /// <summary>
    /// 다른 플레이어가 이미 점유 중인지 여부 (조작상호작용UI.md 3-9 M1: 점유 락).
    /// 기본값은 false — 점유 락이 필요한 대상만 override 한다. 네트워크 동기화는 후속 작업이며,
    /// 현재는 로컬 판정용 seam만 제공한다.
    /// </summary>
    public virtual bool IsOccupied => false;

    /// <summary>
    /// 현재 프레임의 프롬프트 정보를 반환한다. UI는 이 값을 그대로 그리며 대상 타입으로 분기하지 않는다.
    /// </summary>
    /// <returns>표시 상태·행위명·입력 방식·비활성 사유를 담은 정보.</returns>
    public abstract InteractPromptInfo GetPromptInfo();

    /// <summary>
    /// 이 Interactable의 실제 효과를 실행한다. Tap형은 입력 즉시, Hold형은 진행률 완료 시점에 호출된다.
    /// 호출 시점 판단은 하위 입력방식 클래스의 책임이며, 이 메서드는 결과 효과만 구현한다.
    /// </summary>
    protected abstract void OnActivate();

    /// <summary>
    /// 실행 시점에 소음 이벤트를 1회 발행한다 (조작상호작용UI.md 3-4: 위치·강도(dB)·발생원 페이로드, 홀드는 완료 시점 1회).
    /// 소음 이벤트 채널 연결은 소음 시스템(소음시스템.md, 미작성) 설계 확정 후 채워진다 — 현재는 계약 자리만 잡아둔다.
    /// </summary>
    /// <param name="noiseDb">발행할 소음 강도(dB).</param>
    protected void RaiseNoise(float noiseDb)
    {
        // TODO(impl): 소음 이벤트 채널(발생 위치=transform.position, 강도=noiseDb, 발생원=this)로 1회 발행.
        Log.D($"[InteractableBase] RaiseNoise {noiseDb}dB from {name}");
    }
}
