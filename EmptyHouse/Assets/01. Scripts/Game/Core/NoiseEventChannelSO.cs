using UnityEngine;
using UnityEngine.Events;

public readonly struct NoiseEvent
{
    public readonly Vector3 Origin;
    public readonly float Decibel;
    public readonly GameObject Source;

    public NoiseEvent(Vector3 origin, float decibel, GameObject source)
    {
        Origin = origin;
        Decibel = decibel;
        Source = source;
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
