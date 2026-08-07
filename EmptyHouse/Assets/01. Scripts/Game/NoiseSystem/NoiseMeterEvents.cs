using UnityEngine;

namespace EmptyHouse.NoiseSystem
{
    /// <summary>
    /// 한 소음원의 합산 창(sum_window_sec)이 닫힐 때 확정된 발생 dB 표본. 데시벨 미터(소음시스템.md 9-1 ①)의 유일한 입력이다.
    /// 좀비 전파용 <see cref="NoiseEmittedEvent"/> 와 달리 <b>가청 임계로 걸러지기 전</b>의 값이라 웅크림(8dB)도 그대로 실려 온다 —
    /// 임계 컷 뒤에서 발행하면 미터가 8dB 를 0 으로 표시한다.
    /// </summary>
    public readonly struct NoiseMeterSampleEvent
    {
        public ulong SourceId { get; } // 소음원(플레이어)의 NetworkObjectId. 미터는 자기 것만 채택한다(9-3)
        public float EmittedDb { get; } // 그 창의 합산 발생 dB. 무전 수신분 포함(7장)

        /// <summary>표본을 만든다. dB 는 0 미만으로 내려가지 않도록 자른다.</summary>
        /// <param name="sourceId">소음원의 NetworkObjectId.</param>
        /// <param name="emittedDb">그 창에서 합산된 발생 dB.</param>
        public NoiseMeterSampleEvent(ulong sourceId, float emittedDb)
        {
            SourceId = sourceId;
            EmittedDb = Mathf.Max(0f, emittedDb);
        }
    }
}
