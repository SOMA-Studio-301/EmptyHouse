using Border.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 사망 시 소지 아이템 전량을 시신 위치에 픽업으로 떨구는 서버 권위 컴포넌트(D19).
/// 손에 든 것 포함 전 아이템이 드롭되며 백신도 예외가 없다 — 사망은 처벌이 아니라 손실 회계라는 D19 근거를 따른다.
///
/// 사망 상태의 소유는 <see cref="PlayerDeathHandler"/> 이고 이 컴포넌트는 그 상태를 구독만 한다 —
/// "죽었는가"와 "죽으면 무엇을 떨구는가"는 다른 관심사라 한 클래스에 합치지 않는다.
///
/// 시신은 빈 껍데기로 남고 아이템은 바닥에 스폰된다. 레벨디자인.md 148 의 "죽은 동료 조준 → 소지품 수집"과는
/// 어긋나지만, 같은 문서 149 가 시신 상호작용 사양(입력·프롬프트·소음·시신 소멸 여부)을 🟡 미정으로 남겨 둔 상태다.
/// D19 의 "전 아이템이 시신 위치에 드롭"은 이 방식으로 충족된다. 시신 회수 UX 가 확정되면
/// 스폰 대신 시신 컨테이너에 담는 방식으로 <see cref="DropAll"/> 만 갈아끼우면 되고, 인벤 드레인 경로는 그대로 재사용된다.
/// </summary>
public class PlayerDeathDropper : NetworkBehaviour
{
    [Header("Drop")]
    /// <summary>드롭 픽업을 흩뿌릴 반경(m). 3칸이 한 점에 겹쳐 스폰되면 조준으로 골라 집을 수 없다.</summary>
    [SerializeField] private float dropScatterRadius = 0.5f;

    /// <summary>드롭 스폰 높이 오프셋(m). 캡슐 중심(로컬 y=0)이 아니라 발밑에서 떨궈야 바닥을 뚫지 않는다.</summary>
    [SerializeField] private float dropHeightOffset = -0.5f;

    // 사망 상태 소스 — 형제 PlayerDeathHandler. 소유 클라에서만 구독해 드롭을 1회 실행한다.
    private PlayerDeathHandler deathHandler;

    // 드레인 대상 — 형제 PlayerInventory.
    private PlayerInventory inventory;

    // 스폰 창구 — 형제 PlayerItemDropper. 실제 네트워크 스폰은 이쪽이 서버에 요청한다.
    private PlayerItemDropper dropper;

    /// <summary>형제 컴포넌트 참조를 캐시한다.</summary>
    private void Awake()
    {
        deathHandler = GetComponent<PlayerDeathHandler>();
        inventory = GetComponent<PlayerInventory>();
        dropper = GetComponent<PlayerItemDropper>();
    }

    /// <summary>
    /// 소유 클라에 한해 사망 상태 변화를 구독한다.
    /// 스폰 자체는 여전히 서버 권위지만(<see cref="PlayerItemDropper"/> 가 ServerRpc 로 올린다),
    /// "무엇을 떨굴지"는 소유 클라만 안다 — <see cref="PlayerInventory"/> 슬롯은 복제되지 않는 로컬 데이터라
    /// 서버가 원격 플레이어의 인벤을 읽을 수 없기 때문이다. 그래서 소유자가 드레인해 요청을 올린다.
    /// </summary>
    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        deathHandler.IsDead.OnValueChanged += HandleDeadChanged;
    }

    /// <summary>소유 클라에 한해 사망 상태 구독을 해제한다.</summary>
    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;

        deathHandler.IsDead.OnValueChanged -= HandleDeadChanged;
    }

    /// <summary>
    /// 사망 상태 변화를 받아 드롭을 실행한다(소유 클라 전용).
    /// IsDead 는 원턴킬 이진 상태라 한 번 서면 래치되므로(<see cref="PlayerDeathHandler.Die"/>), 이 경로는 1회만 탄다.
    /// </summary>
    /// <param name="previous">이전 사망 상태.</param>
    /// <param name="current">새 사망 상태.</param>
    private void HandleDeadChanged(bool previous, bool current)
    {
        if (!current) return;

        DropAll();
    }

    /// <summary>
    /// 인벤 전량을 드레인해 시신 위치 주변에 픽업으로 스폰 요청한다(소유 클라 전용, D19).
    /// 페어 번호는 넘기지 않는다 — 열쇠는 페어마다 프리팹(Variant)이 갈리므로 WorldPrefab 자체가 곧 페어 식별자다.
    /// </summary>
    private void DropAll()
    {
        InventorySlot[] drained = inventory.DrainAll();
        if (drained.Length == 0) return;

        for (int i = 0; i < drained.Length; i++)
        {
            Vector3 position = ResolveDropPosition(i, drained.Length);
            dropper.RequestDrop(drained[i].Data.WorldPrefab.GetComponent<NetworkObject>().PrefabIdHash, position);
        }

        // TODO: 드롭 소음 발행 (소음 시스템 미구현 — 기획서 '행위 기반 소음')
    }

    /// <summary>
    /// 드롭 픽업 하나가 놓일 월드 위치를 구한다. 여러 칸이 한 점에 겹치지 않도록 시신 발밑 원주에 고르게 흩뿌린다.
    /// 접지 판정은 <see cref="PlayerInventory.ResolveGroundPosition"/> 에 위임해 G 버리기와 같은 규칙을 쓴다.
    /// </summary>
    /// <param name="index">드롭 순번(0-based).</param>
    /// <param name="count">이번에 드롭할 전체 개수.</param>
    /// <returns>스폰할 월드 좌표.</returns>
    private Vector3 ResolveDropPosition(int index, int count)
    {
        // 원주를 count 등분해 흩뿌린다. 1개뿐이어도 각도 0 방향으로 반경만큼 밀려나 시신과 겹치지 않는다.
        float angle = index / (float)count * Mathf.PI * 2f;
        Vector3 scattered = transform.position
            + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dropScatterRadius
            + Vector3.up * dropHeightOffset;

        return inventory.ResolveGroundPosition(scattered);
    }
}
