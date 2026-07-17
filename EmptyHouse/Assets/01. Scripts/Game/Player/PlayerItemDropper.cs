using Border.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 떨군 픽업을 월드에 스폰하는 서버 권위 창구. G 버리기(<see cref="PlayerInventory"/>)와
/// 사망 드롭(<see cref="PlayerDeathDropper"/>)이 공통으로 경유한다 — 스폰은 서버만 할 수 있고,
/// 클라가 로컬 Instantiate 하면 그 아이템은 다른 플레이어에게 보이지 않아 회수가 성립하지 않는다.
///
/// 어느 프리팹을 스폰할지는 NetworkObject 의 PrefabIdHash 로 지목한다 — ItemDataSO 참조는 RPC 로 보낼 수 없다.
/// 프리팹에 itemData·pairId 가 이미 직렬화돼 있으므로 프리팹만 지목하면 아이템 정보가 함께 복원되며,
/// 그래서 열쇠처럼 페어 구분이 필요한 아이템은 페어마다 별도 프리팹(Variant)이어야 한다.
///
/// 인벤은 비소유자·사망 시 <see cref="PlayerController"/> 가 꺼버리므로 RPC 를 얹을 수 없다 —
/// 이 컴포넌트는 항상 켜져 있어야 한다.
/// </summary>
public class PlayerItemDropper : NetworkBehaviour
{
    /// <summary>
    /// 픽업 프리팹을 월드에 스폰해 달라고 서버에 요청한다. 소유 클라가 호출한다.
    /// </summary>
    /// <param name="prefabHash">스폰할 픽업 프리팹의 PrefabIdHash.</param>
    /// <param name="position">스폰할 접지 위치(월드). 콜라이더 높이 보정은 서버가 한다.</param>
    public void RequestDrop(uint prefabHash, Vector3 position)
    {
        RequestDropServerRpc(prefabHash, position);
    }

    /// <summary>
    /// 드롭 요청을 서버에서 수신해 픽업을 스폰한다. 해시로 등록된 프리팹을 찾지 못하면 아무것도 하지 않는다
    /// (NetworkManager 의 NetworkPrefabs 미등록 — 등록 누락은 에러로 알린다).
    /// </summary>
    /// <param name="prefabHash">스폰할 픽업 프리팹의 PrefabIdHash.</param>
    /// <param name="position">스폰할 접지 위치(월드).</param>
    [ServerRpc]
    private void RequestDropServerRpc(uint prefabHash, Vector3 position)
    {
        if (!NetworkManager.NetworkConfig.Prefabs.NetworkPrefabOverrideLinks.TryGetValue(prefabHash, out NetworkPrefab entry))
        {
            Log.E($"[PlayerItemDropper] NetworkPrefabs 미등록 프리팹 해시: {prefabHash}");
            return;
        }

        GameObject instance = Instantiate(entry.Prefab, position, entry.Prefab.transform.rotation);

        // 콜라이더 중심이 피벗인 프리팹(예: Key)은 바닥 좌표에 그대로 놓으면 절반이 파묻힌다 — 바운즈 하단을 접지면에 맞춘다.
        // 픽업에는 NetworkTransform 이 없어 스폰 시점 위치만 전파되므로, 반드시 Spawn 전에 보정해야 한다.
        Collider col = instance.GetComponentInChildren<Collider>();
        if (col != null)
        {
            instance.transform.position += Vector3.up * (position.y - col.bounds.min.y);
        }

        instance.GetComponent<NetworkObject>().Spawn();
    }
}
