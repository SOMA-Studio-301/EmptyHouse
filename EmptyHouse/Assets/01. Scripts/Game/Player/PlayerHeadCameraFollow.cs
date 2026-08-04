using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 소유자 카메라 피벗의 "위치"를 머리 본 아래 눈 앵커에 추종시킨다.
/// 회전은 PlayerController(입력)가 소유하므로 건드리지 않는다 — 머리 본 회전을 상속하면
/// LookAt 이 돌린 고개가 시야를 함께 돌려 조준·시선이 순환 참조로 출렁이기 때문에 위치만 따라간다.
/// LateUpdate(애니메이션·IK 적용 후) 시점에 복사해, 고개가 돌아가도 카메라가 머리를 따라 이동해
/// 목·얼굴 메시가 시야에 걸리지 않는다.
/// </summary>
public sealed class PlayerHeadCameraFollow : NetworkBehaviour
{
    [SerializeField] private Transform cameraPivot; // 따라 움직일 카메라 피벗(루트 자식). 회전은 PlayerController 소유
    [SerializeField] private Transform eyeAnchor; // 머리 본 아래 눈 위치 앵커. 애니메이션이 움직인 최종 머리 위치를 대표한다

    /// <summary>원격 인스턴스는 카메라가 없으므로 추종을 끈다.</summary>
    public override void OnNetworkSpawn()
    {
        enabled = IsOwner;
    }

    /// <summary>애니메이션·IK 가 끝난 뒤 카메라 피벗 위치를 눈 앵커로 옮긴다.</summary>
    private void LateUpdate()
    {
        cameraPivot.position = eyeAnchor.position;
    }
}
