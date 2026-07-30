using Border.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 투척 비행체를 월드에 스폰하는 서버 권위 창구. <see cref="PlayerItemDropper"/> 와 같은 역할을
/// 투척 쪽에서 맡는다 — 스폰은 서버만 할 수 있고, 클라가 로컬 Instantiate 하면
/// 그 투척물은 다른 플레이어에게 보이지 않아 착지 소음도 서버에서 나지 않는다.
///
/// 드롭과 달리 속도를 함께 받으므로 서버가 값을 검증한다. 클라가 보낸 초속도를 그대로 믿으면
/// 조작된 클라가 맵 반대편까지 던져 원하는 좌표에 소음을 심을 수 있다.
///
/// 투척 입력을 처리하는 <see cref="PlayerItemUser"/> 는 비소유자·사망 시 꺼지므로 RPC 를 얹을 수 없다 —
/// 이 컴포넌트는 항상 켜져 있어야 한다(<see cref="PlayerItemDropper"/> 와 같은 이유).
/// </summary>
public class PlayerThrower : NetworkBehaviour
{
    [Header("Validation")]
    [Tooltip("허용 초속 상한(m/s). ThrowAimIndicator 의 throwSpeed 보다 약간 높게 잡는다")]
    [SerializeField] private float maxThrowSpeed = 20f;

    [Tooltip("발사 지점이 플레이어에게서 떨어질 수 있는 최대 거리(m). 손 위치를 벗어난 좌표는 거부한다")]
    [SerializeField] private float maxOriginDistance = 2f;

    /// <summary>
    /// 투척 비행체를 스폰해 달라고 서버에 요청한다. 소유 클라가 호출한다.
    /// </summary>
    /// <param name="prefabHash">스폰할 비행체 프리팹의 PrefabIdHash.</param>
    /// <param name="origin">발사 지점(월드).</param>
    /// <param name="velocity">발사 초속도(월드).</param>
    public void RequestThrow(uint prefabHash, Vector3 origin, Vector3 velocity)
    {
        RequestThrowServerRpc(prefabHash, origin, velocity);
    }

    /// <summary>
    /// 투척 요청을 서버에서 수신해 비행체를 스폰·발사한다.
    /// 속도·발사 지점이 허용 범위를 벗어나거나 프리팹이 미등록이면 아무것도 하지 않는다.
    /// </summary>
    /// <param name="prefabHash">스폰할 비행체 프리팹의 PrefabIdHash.</param>
    /// <param name="origin">발사 지점(월드).</param>
    /// <param name="velocity">발사 초속도(월드).</param>
    [ServerRpc]
    private void RequestThrowServerRpc(uint prefabHash, Vector3 origin, Vector3 velocity)
    {
        if (!IsFinite(origin) || !IsFinite(velocity))
        {
            Log.W($"[PlayerThrower] 유효하지 않은 투척 값을 거부했다: origin={origin} velocity={velocity}");
            return;
        }

        if (velocity.sqrMagnitude > maxThrowSpeed * maxThrowSpeed)
        {
            Log.W($"[PlayerThrower] 상한을 넘는 투척 속도를 거부했다: {velocity.magnitude}m/s");
            return;
        }

        if ((origin - transform.position).sqrMagnitude > maxOriginDistance * maxOriginDistance)
        {
            Log.W($"[PlayerThrower] 플레이어에게서 너무 먼 발사 지점을 거부했다: {origin}");
            return;
        }

        if (!NetworkManager.NetworkConfig.Prefabs.NetworkPrefabOverrideLinks.TryGetValue(prefabHash, out NetworkPrefab entry))
        {
            Log.E($"[PlayerThrower] NetworkPrefabs 미등록 프리팹 해시: {prefabHash}");
            return;
        }

        GameObject instance = Instantiate(entry.Prefab, origin, Quaternion.LookRotation(velocity));
        instance.GetComponent<NetworkObject>().Spawn();

        // Spawn 이후에 발사한다 — 스폰 전에 속도를 넣으면 물리가 한 스텝 먼저 돌아
        // 클라가 받는 최초 좌표가 발사 지점에서 이미 어긋난다.
        instance.GetComponent<ThrownProjectile>().ServerLaunch(velocity, gameObject);
    }

    /// <summary>NaN·Infinity 가 섞인 벡터인지 검사한다. 물리에 들어가면 Rigidbody 가 통째로 망가진다.</summary>
    /// <param name="value">검사할 벡터.</param>
    /// <returns>세 성분이 모두 유한하면 true.</returns>
    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
