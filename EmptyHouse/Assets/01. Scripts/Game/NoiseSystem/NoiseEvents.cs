using System;
using UnityEngine;

namespace EmptyHouse.NoiseSystem
{
    public readonly struct NoiseEmittedEvent
    {
        public ulong SourceId { get; }
        public Vector3 Origin { get; }
        public float EmittedDb { get; }

        public NoiseEmittedEvent(ulong sourceId, Vector3 origin, float emittedDb)
        {
            SourceId = sourceId;
            Origin = origin;
            EmittedDb = Mathf.Max(0f, emittedDb);
        }
    }

    public readonly struct NoiseDetectedEvent
    {
        public ulong TargetZombieId { get; }
        public ulong SourceId { get; }
        public Vector3 Origin { get; }
        public float ReachedDb { get; }

        public NoiseDetectedEvent(ulong targetZombieId, ulong sourceId, Vector3 origin, float reachedDb)
        {
            TargetZombieId = targetZombieId;
            SourceId = sourceId;
            Origin = origin;
            ReachedDb = reachedDb;
        }
    }

    public abstract class NoiseEventChannelSO<T> : ScriptableObject where T : struct
    {
        public event Action<T> OnEventRaised;
        public void RaiseEvent(T payload) => OnEventRaised?.Invoke(payload);
    }

    [CreateAssetMenu(fileName = "SO_Event_NoiseEmitted", menuName = "Events/Noise/Noise Emitted")]
    public sealed class NoiseEmittedEventChannelSO : NoiseEventChannelSO<NoiseEmittedEvent> { }

    [CreateAssetMenu(fileName = "SO_Event_NoiseDetected", menuName = "Events/Noise/Noise Detected")]
    public sealed class NoiseDetectedEventChannelSO : NoiseEventChannelSO<NoiseDetectedEvent> { }

}
