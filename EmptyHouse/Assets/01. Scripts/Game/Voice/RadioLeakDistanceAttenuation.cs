using UnityEngine;

/// <summary>
/// 무전기 누출음을 양쪽 채널 중앙에 재생하면서 거리만으로 감쇄한다.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public sealed class RadioLeakDistanceAttenuation : MonoBehaviour
{
    public float BaseVolume { get; set; } = 0.45f;
    public float MinDistance { get; set; } = 1f;
    public float MaxDistance { get; set; } = 8f;

    private AudioSource source;
    private AudioListener listener;

    private void Awake()
    {
        source = GetComponent<AudioSource>();
        source.spatialBlend = 0f;
    }

    private void LateUpdate()
    {
        if (listener == null || !listener.enabled || !listener.gameObject.activeInHierarchy)
        {
            listener = FindActiveListener();
        }

        if (listener == null)
        {
            source.volume = 0f;
            return;
        }

        float distance = Vector3.Distance(transform.position, listener.transform.position);
        float attenuation = distance <= MinDistance
            ? 1f
            : distance >= MaxDistance
                ? 0f
                : 1f - Mathf.InverseLerp(MinDistance, MaxDistance, distance);

        source.volume = BaseVolume * attenuation;
    }

    private static AudioListener FindActiveListener()
    {
        AudioListener[] listeners = FindObjectsByType<AudioListener>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < listeners.Length; i++)
        {
            if (listeners[i].enabled && listeners[i].gameObject.activeInHierarchy)
            {
                return listeners[i];
            }
        }

        return null;
    }
}
