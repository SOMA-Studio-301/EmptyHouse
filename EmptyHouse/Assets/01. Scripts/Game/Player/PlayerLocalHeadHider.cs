using System.Collections;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 소유 클라이언트에서만 머리 본 스케일을 0 에 가깝게 붕괴시켜 1인칭 시야 가림을 막는다.
/// 캐릭터가 단일 스킨 메시라 머리 렌더러만 레이어로 분리할 수 없어, 머리 본에 가중된 정점을
/// 한 점으로 모으는 방식을 쓴다 — 휴머노이드 애니메이션은 본 스케일을 건드리지 않아 한 번만 적용하면 유지된다.
/// 위장 중(숄더뷰)에는 반대로 보여야 하므로, PlayerDisguise 상태에 맞춰 토글한다.
/// 토글은 Vcam 블렌드가 끝난 뒤에만 적용한다 — 전환 중에 바로 뒤집으면 화면이 바뀌는 도중에
/// 머리가 갑자기 나타나거나 사라져 어색하다.
/// 본 스케일 변경은 소유자 인스턴스에서만 실행되는 로컬 표현이라(스케일은 복제되지 않는다),
/// 다른 클라이언트 화면에는 머리가 정상 표시된다. 그림자는 스킨 결과를 그대로 쓰므로 그림자에서도 머리가 사라진다.
/// </summary>
public sealed class PlayerLocalHeadHider : NetworkBehaviour
{
    [SerializeField] private Transform headBone; // 숨길 머리 본(mixamorig:Head). 프리팹 변형별로 자기 모델의 본을 배선

    private const float hiddenScale = 0.0001f; // 0 은 스키닝 행렬 특이점을 만들 수 있어 입실론으로 붕괴시킨다

    private PlayerDisguise disguise; // 위장 게이팅 소스 — 형제. 위장 중엔 머리를 다시 보여준다(숄더뷰)
    private CinemachineBrain brain; // 소유자 카메라의 블렌드 상태 확인용. OnNetworkSpawn 에서 캐시
    private Vector3 originalScale; // 머리 본 원래 스케일 캐시 — Show() 복원용
    private Coroutine pendingToggle; // 블렌드 종료를 기다리는 중인 토글. 상태가 빨리 다시 바뀌면 취소하고 새로 건다

    /// <summary>형제 PlayerDisguise 참조를 캐시한다.</summary>
    private void Awake()
    {
        disguise = GetComponent<PlayerDisguise>();
    }

    /// <summary>소유자에 한해 원래 본 스케일을 캐시하고 1인칭 기본 상태(숨김)로 시작한 뒤, 위장 상태 변화를 구독한다.</summary>
    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        brain = Camera.main.GetComponent<CinemachineBrain>();

        originalScale = headBone.localScale;

        Hide();

        disguise.DisguiseChanged += HandleDisguiseChanged;
    }

    /// <summary>구독을 해제하고, 대기 중인 토글을 멈추고, 머리 본 스케일을 원복한다.</summary>
    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;

        disguise.DisguiseChanged -= HandleDisguiseChanged;

        if (pendingToggle != null) StopCoroutine(pendingToggle);

        Show();
    }

    /// <summary>
    /// 위장 상태 변화를 받는다. 즉시 뒤집지 않고, Vcam 블렌드가 끝난 뒤에 적용되도록 코루틴을 건다.
    /// 이미 대기 중인 토글이 있으면 취소하고 새 목표 상태로 다시 건다 — 빠른 연타로 상태가 여러 번
    /// 바뀌어도 마지막 요청만 반영된다.
    /// </summary>
    /// <param name="isDisguised">변경된 위장 상태.</param>
    private void HandleDisguiseChanged(bool isDisguised)
    {
        if (pendingToggle != null) StopCoroutine(pendingToggle);
        pendingToggle = StartCoroutine(ToggleAfterBlend(isDisguised));
    }

    /// <summary>
    /// 최소 한 프레임을 미룬 뒤(같은 프레임에 아직 블렌드가 시작되지 않았을 수 있어서) IsBlending 이 꺼질 때까지 기다리고 토글을 적용한다.
    /// </summary>
    /// <param name="isDisguised">적용할 위장 상태.</param>
    private IEnumerator ToggleAfterBlend(bool isDisguised)
    {
        yield return null;
        while (brain != null && brain.IsBlending) yield return null;

        if (isDisguised) Show();
        else Hide();

        pendingToggle = null;
    }

    /// <summary>머리 본을 입실론 스케일로 붕괴시켜 머리를 감춘다(1인칭 기본).</summary>
    private void Hide()
    {
        headBone.localScale = originalScale * hiddenScale;
    }

    /// <summary>머리 본 스케일을 원래대로 되돌린다(위장 숄더뷰 등 3인칭 표현용).</summary>
    private void Show()
    {
        headBone.localScale = originalScale;
    }
}
