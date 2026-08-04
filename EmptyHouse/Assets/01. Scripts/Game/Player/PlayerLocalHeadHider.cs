using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 소유 클라이언트에서만 머리 관련 렌더러(머리카락·얼굴 파츠)를 숨김 레이어로 옮기고,
/// 소유자 카메라의 컬링 마스크에서 그 레이어를 제외해 1인칭 시야 가림을 막는다.
/// 레이어 변경은 소유자 인스턴스에서만 실행되는 로컬 표현이라, 다른 클라이언트에 복제된
/// 이 캐릭터는 기본 레이어 그대로 정상 렌더링된다. 라이트는 숨김 레이어도 계속 그리므로 그림자에는 머리가 남는다.
/// </summary>
public sealed class PlayerLocalHeadHider : NetworkBehaviour
{
    [SerializeField] private GameObject[] headParts; // 숨길 머리 관련 렌더러 오브젝트들. 프리팹 내부 참조
    [SerializeField] private int hiddenLayer = 31; // 옮겨 둘 레이어 인덱스. 31 = LocalHidden(이 용도 전용. Water(4)는 로비 월드 캔버스가 점유 중이라 피한다)

    /// <summary>소유자에 한해 머리 파츠를 숨김 레이어로 옮기고 메인 카메라 컬링 마스크에서 제외한다.</summary>
    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        foreach (var part in headParts)
        {
            part.layer = hiddenLayer;
        }
        Camera.main.cullingMask &= ~(1 << hiddenLayer);
    }

    /// <summary>디스폰 시 씬에 남는 메인 카메라의 컬링 마스크를 원복한다.</summary>
    public override void OnNetworkDespawn()
    {
        if (!IsOwner || Camera.main == null) return;

        Camera.main.cullingMask |= 1 << hiddenLayer;
    }
}
