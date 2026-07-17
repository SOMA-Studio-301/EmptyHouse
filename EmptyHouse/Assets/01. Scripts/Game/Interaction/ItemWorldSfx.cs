using Border.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 월드 픽업이 등장·소멸할 때 나는 소리 — 버리기(스폰)와 줍기(소멸).
/// 픽업이 소음(RaiseNoise)을 내는 지점과 짝을 이룬다: 좀비가 듣는 소리는 플레이어도 들어야 한다.
/// 오브젝트 사운드이므로 IsServer/IsOwner 로 가르지 않는다 — 각 클라의 인스턴스가 스스로 울리고
/// 누구에게 들리는지는 3D 감쇠가 정한다.
/// </summary>
public class ItemWorldSfx : NetworkBehaviour
{
    [Header("Audio")]
    [SerializeField] private SFXEventChannelSO sfxEventChannel; // 프로젝트 에셋(SO) — 자기완결 프리팹의 허용 의존
    [SerializeField] private AudioId dropAudioId = AudioId.Sfx_Item_Drop; // G 버리기·사망 드롭으로 스폰될 때
    [SerializeField] private AudioId pickupAudioId = AudioId.Sfx_Item_Pickup; // 회수돼 소멸할 때

    /// <summary>스폰 시 버리기 소리를 낸다. 레벨에 미리 배치된 픽업은 제외한다.</summary>
    public override void OnNetworkSpawn()
    {
        Log.D($"[ItemWorldSfx] OnNetworkSpawn {name} sceneObject={NetworkObject.IsSceneObject}");

        // 씬 배치 픽업도 세션 시작 시 한꺼번에 스폰된다 — 그건 누가 떨군 게 아니다.
        // 동적 스폰(=떨군 것)만 false 이므로, 판정 불가(null)는 배치품으로 보고 울리지 않는다.
        if (NetworkObject.IsSceneObject != false) return;

        sfxEventChannel.RaisePlayEvent(dropAudioId, transform.position);
    }

    /// <summary>소멸 시 줍기 소리를 낸다. 세션 종료로 인한 일괄 소멸은 제외한다.</summary>
    public override void OnNetworkDespawn()
    {
        Log.D($"[ItemWorldSfx] OnNetworkDespawn {name}");

        // 세션이 끝나면 월드의 전 픽업이 despawn 된다 — 그건 회수가 아니다.
        // 종료 중에는 NetworkManager 가 이미 사라졌을 수 있어 여기서만 null 을 의도적으로 허용한다.
        if (NetworkManager == null || NetworkManager.ShutdownInProgress) return;

        // 이미터는 AudioManager 풀 소유라 이 오브젝트보다 오래 산다 — 좌표를 미리 복사해 넘긴다.
        Vector3 position = transform.position;
        sfxEventChannel.RaisePlayEvent(pickupAudioId, position);
    }
}
