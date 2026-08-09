using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 플레이어의 발소리. 이동한 XZ 거리를 누적해 보폭(strideDistance)마다 1회 울린다.
///
/// 애니메이션 이벤트를 쓰지 않는다 — 걷기 클립이 서드파티 FBX(unity-chan)라 이벤트를 넣으려면
/// 패키지 에셋의 임포터 설정을 고쳐야 하고, BlendTree 안이라 블렌드 구간에서 WALK/RUN 이벤트가 겹쳐 울린다.
///
/// 오브젝트 사운드이므로 IsOwner/IsServer 로 가르지 않는다 — 위치는 NetworkTransform 이 전 클라에 복제하므로
/// 각 클라가 자기 인스턴스의 이동만 보고 스스로 울리고, 누구에게 들리는지는 3D 감쇠가 정한다.
///
/// 웅크린 동안은 울리지 않는다. 이 인스턴스가 남의 캐릭터일 수도 있으므로 웅크림 판단은
/// PlayerController 의 네트워크 사본(<see cref="PlayerController.Crouching"/>)을 읽는다.
/// </summary>
public class PlayerFootstepSfx : NetworkBehaviour
{
    [Header("Audio")]
    [SerializeField] private SFXEventChannelSO sfxEventChannel; // 프로젝트 에셋(SO) — 자기완결 프리팹의 허용 의존
    [SerializeField] private AudioId footstepAudioId = AudioId.Sfx_Foot_Walk_Hard;

    [Header("Stride")]
    [SerializeField] private float strideDistance = 2f; // 발소리 사이의 이동 거리(m). 짧을수록 자주 울린다
    [SerializeField] private float minStepSpeed = 0.2f; // 이 속력(m/s) 미만이면 걷는 게 아니다 — 밀림·미세 진동으로 보폭이 차오르는 것을 막는다
    [SerializeField] private float teleportDistance = 3f; // 한 프레임에 이 거리(m)를 넘게 움직이면 순간이동으로 보고 누적을 버린다(스폰·귀환)

    [Header("Refs")]
    [SerializeField] private PlayerController controller; // 같은 프리팹의 형제 — 접지·웅크림 여부를 읽는다(각각 물리 질의·네트워크 사본이라 비소유 클라에서도 유효하다)

    private Vector3 previousPosition; // 직전 프레임의 위치. 이동량은 이 차이로 구한다
    private float distanceSinceStep; // 마지막 발소리 이후 누적 이동 거리(m)

    /// <summary>이동량 기준점을 현재 위치로 잡는다. 스폰 위치가 첫 프레임에 보폭으로 잡히지 않게 한다.</summary>
    public override void OnNetworkSpawn()
    {
        previousPosition = transform.position;
        distanceSinceStep = 0f;
    }

    /// <summary>이동 거리를 누적해 보폭을 넘으면 발소리를 1회 울린다.</summary>
    private void Update()
    {
        // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
        Vector3 current = transform.position;
        Vector3 delta = current - previousPosition;
        previousPosition = current;
        delta.y = 0f;

        float distance = delta.magnitude;

        // 웅크린 동안은 발소리가 나지 않는다. 누적도 버려서 일어선 직후에 밀린 보폭이 한 번에 터지지 않게 한다.
        // 기준점(previousPosition)은 위에서 이미 갱신했으므로 웅크린 채 이동한 거리가 순간이동으로 오인되지 않는다.
        if (controller.Crouching)
        {
            distanceSinceStep = 0f;
            return;
        }

        // 스폰·귀환 같은 순간이동은 걸은 게 아니다 — 누적을 버리고 다음 프레임부터 다시 센다.
        if (distance > teleportDistance)
        {
            distanceSinceStep = 0f;
            return;
        }

        if (distance < minStepSpeed * Time.deltaTime) return;

        distanceSinceStep += distance;
        if (distanceSinceStep < strideDistance) return;

        distanceSinceStep = 0f;

        // 공중에서는 발을 딛지 않는다. 보폭이 찬 순간에만 물어 매 프레임 SphereCast 를 돌리지 않는다.
        if (!controller.Grounded) return;

        sfxEventChannel.RaisePlayEvent(footstepAudioId, transform.position);
    }
}
