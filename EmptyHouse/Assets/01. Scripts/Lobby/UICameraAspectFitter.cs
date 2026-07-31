using UnityEngine;

/// <summary>
/// 3D UI 카메라의 가로 화각을 기준 종횡비에서 잡은 값으로 고정한다.
/// 기준보다 좁은 화면(16:10, 4:3)에서 세로 FOV를 넓혀 World UI의 가로 잘림을 막는다.
/// 기준 이상(16:9, 21:9)에서는 보정하지 않아 기존 구도가 그대로 유지된다.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class UICameraAspectFitter : MonoBehaviour
{
    [SerializeField] private float baseVerticalFov = 60f; // 기준 종횡비에서 구도를 잡은 세로 FOV(도)
    [SerializeField] private float baseAspect = 16f / 9f; // 구도를 잡은 기준 종횡비

    private Camera targetCamera; // 보정 대상 카메라 캐시

    /// <summary>
    /// 대상 카메라를 캐시하고 즉시 1회 보정한다.
    /// </summary>
    private void OnEnable()
    {
        targetCamera = GetComponent<Camera>();
        ApplyFov();
    }

    /// <summary>
    /// 매 프레임 현재 종횡비에 맞춰 세로 FOV를 갱신한다(해상도 변경 대응).
    /// </summary>
    private void Update()
    {
        ApplyFov();
    }

    /// <summary>
    /// 현재 종횡비가 기준보다 좁으면 가로 화각이 유지되도록 세로 FOV를 넓혀 적용한다.
    /// </summary>
    private void ApplyFov()
    {
        float aspect = targetCamera.aspect;
        float fov = baseVerticalFov;

        if (aspect < baseAspect)
        {
            float baseHalfWidthTan = Mathf.Tan(baseVerticalFov * 0.5f * Mathf.Deg2Rad) * baseAspect;
            fov = 2f * Mathf.Atan(baseHalfWidthTan / aspect) * Mathf.Rad2Deg;
        }

        if (!Mathf.Approximately(targetCamera.fieldOfView, fov))
        {
            targetCamera.fieldOfView = fov;
        }
    }
}
