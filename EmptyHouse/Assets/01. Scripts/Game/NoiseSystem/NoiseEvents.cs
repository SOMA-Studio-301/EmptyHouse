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

    /// <summary>
    /// 소음 이벤트 채널의 제네릭 베이스. 파생 채널은 <b>클래스명과 같은 이름의 파일 하나</b>에 둔다 —
    /// Unity 는 파일명과 일치하는 클래스에만 MonoScript 를 만들어서, 한 파일에 모으면 에셋의 스크립트 참조가 비어버린다.
    /// </summary>
    /// <typeparam name="T">채널이 나르는 페이로드 구조체.</typeparam>
    public abstract class NoiseEventChannelSO<T> : ScriptableObject where T : struct
    {
        public event Action<T> OnEventRaised;

        /// <summary>페이로드를 구독자에게 방송한다.</summary>
        /// <param name="payload">방송할 페이로드.</param>
        public void RaiseEvent(T payload) => OnEventRaised?.Invoke(payload);
    }
}
