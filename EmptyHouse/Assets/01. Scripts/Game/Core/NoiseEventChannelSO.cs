using UnityEngine;
using UnityEngine.Events;

public readonly struct NoiseEvent
{
    public readonly Vector3 Origin;
    public readonly float Decibel;
    public readonly GameObject Source;
    public readonly ulong SourceId;
    public readonly bool HasSourceId;

    public NoiseEvent(Vector3 origin, float decibel, GameObject source, ulong? sourceId = null)
    {
        Origin = origin;
        Decibel = decibel;
        Source = source;
        SourceId = sourceId.GetValueOrDefault();
        HasSourceId = sourceId.HasValue;
    }
}

[CreateAssetMenu(fileName = "SO_Event_NoiseEmitted", menuName = "Events/Game/Noise Emitted")]
public class NoiseEventChannelSO : ScriptableObject
{
    public event UnityAction<NoiseEvent> OnEventRaised;

    public void RaiseEvent(NoiseEvent noiseEvent)
    {
        OnEventRaised?.Invoke(noiseEvent);
    }
}
