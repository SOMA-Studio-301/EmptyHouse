using System;
using UnityEngine;

namespace EmptyHouse.NoiseSystem
{
    /// <summary>
    /// 한 소음원 안에서 dB 가 쌓이는 방식 (소음시스템.md 3-2). <b>지속</b>(이동·마이크·무전)은 채널마다 슬롯 하나를 차지하고
    /// 새 값이 들어오면 <b>갱신</b>되며, <b>단발</b>(문·픽업·투척 착지)만 합산 창 안에서 <b>누적</b>된다.
    /// 지속을 누적으로 다루면 값이 부풀지 않게 발행 주기를 합산 창보다 길게 묶어야 하고, 그 주기가 그대로 미터 반응 지연이 된다.
    /// </summary>
    public enum NoiseSourceChannel
    {
        OneShot = 0, // 단발 — 발생 시점 창에만 들어가고 창이 닫히면 사라진다
        Movement = 1, // 지속 — 이동
        Voice = 2, // 지속 — 마이크
        Radio = 3, // 지속 — 무전 수신(E4). 훅만 열려 있고 발신자는 아직 없다
    }

    public readonly struct NoiseEmittedEvent
    {
        public ulong SourceId { get; }
        public Vector3 Origin { get; }
        public float EmittedDb { get; }
        public NoiseSourceChannel Channel { get; } // 누적 방식. 지속 채널은 이 값이 슬롯 키를 겸한다

        /// <summary>단발 소음을 만든다. 문·픽업·투척 착지처럼 발생 시점 창에만 들어가는 소음이 이 생성자를 쓴다.</summary>
        /// <param name="sourceId">소음원의 NetworkObjectId.</param>
        /// <param name="origin">발생 위치.</param>
        /// <param name="emittedDb">발생 dB.</param>
        public NoiseEmittedEvent(ulong sourceId, Vector3 origin, float emittedDb)
            : this(sourceId, origin, emittedDb, NoiseSourceChannel.OneShot)
        {
        }

        /// <summary>누적 방식을 지정해 소음을 만든다. 지속 채널은 <paramref name="emittedDb"/> 0 이 "그쳤다"는 뜻이다.</summary>
        /// <param name="sourceId">소음원의 NetworkObjectId.</param>
        /// <param name="origin">발생 위치.</param>
        /// <param name="emittedDb">발생 dB. 지속 채널이면 현재 레벨.</param>
        /// <param name="channel">누적 방식(지속 슬롯 또는 단발).</param>
        public NoiseEmittedEvent(ulong sourceId, Vector3 origin, float emittedDb, NoiseSourceChannel channel)
        {
            SourceId = sourceId;
            Origin = origin;
            EmittedDb = Mathf.Max(0f, emittedDb);
            Channel = channel;
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
