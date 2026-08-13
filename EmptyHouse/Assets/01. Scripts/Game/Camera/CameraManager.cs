using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// CameraEventChannelSO 를 구독해 연출용 Vcam 을 켜고 끈다.
/// Vcam 이 하나도 활성화돼 있지 않으면 CinemachineBrain 이 아무것도 건드리지 않으므로,
/// 항상 켜져 있는 1인칭(수동 카메라 부착) 로직과 충돌 없이 공존한다.
/// </summary>
public class CameraManager : MonoBehaviour
{
    [SerializeField] private CameraEventChannelSO onCameraChanged; // Vcam 전환 요청 채널

    private CinemachineCamera activeVcam; // 현재 활성화된 연출용 Vcam. null 이면 기본(1인칭) 상태

    /// <summary>채널 구독을 시작한다.</summary>
    private void OnEnable()
    {
        onCameraChanged.OnEventRaised += HandleCameraChanged;
    }

    /// <summary>채널 구독을 해제한다.</summary>
    private void OnDisable()
    {
        onCameraChanged.OnEventRaised -= HandleCameraChanged;
    }

    /// <summary>이전에 켜둔 Vcam을 끄고, 새로 요청된 Vcam을 켠다. null 이면 아무것도 켜지 않는다.</summary>
    /// <param name="vcam">전환할 Vcam. null 이면 기본 카메라로 복귀.</param>
    private void HandleCameraChanged(CinemachineCamera vcam)
    {
        if (activeVcam != null) activeVcam.gameObject.SetActive(false);
        activeVcam = vcam;
        if (activeVcam != null) activeVcam.gameObject.SetActive(true);
    }
}
