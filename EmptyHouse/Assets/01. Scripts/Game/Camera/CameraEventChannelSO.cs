using System;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 연출용 Vcam 전환 요청을 전달하는 SO 채널. CameraManager 가 구독해 활성 상태를 바꾼다.
/// null 을 발행하면 기본(1인칭) 카메라로 복귀한다.
/// </summary>
[CreateAssetMenu(fileName = "SO_Event_CameraChanged", menuName = "Events/Camera Changed")]
public sealed class CameraEventChannelSO : ScriptableObject
{
    public event Action<CinemachineCamera> OnEventRaised;

    /// <summary>연출용 Vcam 전환을 요청한다. null 이면 기본 카메라로 복귀한다.</summary>
    /// <param name="vcam">전환할 Vcam. null 이면 기본 카메라로 복귀.</param>
    public void RaiseEvent(CinemachineCamera vcam) => OnEventRaised?.Invoke(vcam);
}
