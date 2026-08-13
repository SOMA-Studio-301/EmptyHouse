using System.Collections;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 소유 클라이언트에서만 머리 관련 렌더러(머리카락·얼굴 파츠)를 숨김 레이어로 옮기고,
/// 소유자 카메라의 컬링 마스크에서 그 레이어를 제외해 1인칭 시야 가림을 막는다.
/// 위장 중(숄더뷰)에는 반대로 보여야 하므로, PlayerDisguise 상태에 맞춰 토글한다.
/// 토글은 Vcam 블렌드가 끝난 뒤에만 적용한다 — 전환 중에 바로 뒤집으면 화면이 바뀌는 도중에
/// 머리가 갑자기 나타나거나 사라져 어색하다.
/// 레이어 변경은 소유자 인스턴스에서만 실행되는 로컬 표현이라, 다른 클라이언트에 복제된
/// 이 캐릭터는 기본 레이어 그대로 정상 렌더링된다. 라이트는 숨김 레이어도 계속 그리므로 그림자에는 머리가 남는다.
/// </summary>
public sealed class PlayerLocalHeadHider : NetworkBehaviour
{
    [SerializeField] private GameObject[] headParts; // 숨길 머리 관련 렌더러 오브젝트들. 프리팹 내부 참조
    [SerializeField] private int hiddenLayer = 31; // 옮겨 둘 레이어 인덱스. 31 = LocalHidden(이 용도 전용. Water(4)는 로비 월드 캔버스가 점유 중이라 피한다)

    private PlayerDisguise disguise; // 위장 게이팅 소스 — 형제. 위장 중엔 머리를 다시 보여준다(숄더뷰)
    private CinemachineBrain brain; // 소유자 카메라의 블렌드 상태 확인용. OnNetworkSpawn 에서 캐시
    private int[] originalLayers; // headParts 원래 레이어 캐시 — Show() 복원용
    private Coroutine pendingToggle; // 블렌드 종료를 기다리는 중인 토글. 상태가 빨리 다시 바뀌면 취소하고 새로 건다

    /// <summary>형제 PlayerDisguise 참조를 캐시한다.</summary>
    private void Awake()
    {
        disguise = GetComponent<PlayerDisguise>();
    }

    /// <summary>소유자에 한해 원래 레이어를 캐시하고 1인칭 기본 상태(숨김)로 시작한 뒤, 위장 상태 변화를 구독한다.</summary>
    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        brain = Camera.main.GetComponent<CinemachineBrain>();

        originalLayers = new int[headParts.Length];
        for (int i = 0; i < headParts.Length; i++) originalLayers[i] = headParts[i].layer;

        Hide();

        disguise.DisguiseChanged += HandleDisguiseChanged;
    }

    /// <summary>구독을 해제하고, 대기 중인 토글을 멈추고, 씬에 남는 메인 카메라의 컬링 마스크를 원복한다.</summary>
    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;

        disguise.DisguiseChanged -= HandleDisguiseChanged;

        if (pendingToggle != null) StopCoroutine(pendingToggle);

        if (Camera.main != null) Camera.main.cullingMask |= 1 << hiddenLayer;
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

    /// <summary>머리 파츠를 숨김 레이어로 옮기고 메인 카메라 컬링 마스크에서 제외한다(1인칭 기본).</summary>
    private void Hide()
    {
        foreach (var part in headParts) part.layer = hiddenLayer;
        Camera.main.cullingMask &= ~(1 << hiddenLayer);
    }

    /// <summary>머리 파츠를 원래 레이어로 되돌리고 컬링 마스크를 복원한다(위장 숄더뷰 등 3인칭 표현용).</summary>
    private void Show()
    {
        for (int i = 0; i < headParts.Length; i++) headParts[i].layer = originalLayers[i];
        Camera.main.cullingMask |= 1 << hiddenLayer;
    }
}
