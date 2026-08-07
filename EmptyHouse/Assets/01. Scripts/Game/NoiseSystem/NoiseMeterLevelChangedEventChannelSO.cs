using System;
using UnityEngine;

namespace EmptyHouse.NoiseSystem
{
    /// <summary>
    /// 로컬 플레이어의 현재 발생 dB 를 HUD 로 전달하는 SO 채널 (소음시스템.md 9-1 ① 바).
    /// 발생 dB NetworkVariable 이 Owner 읽기 전용이라 소유 클라이언트의 <see cref="PlayerNoise"/> 하나만 발행한다 — 프로세스당 발행자는 항상 1개다.
    /// 씬 레벨 Canvas-HUD 와 플레이어 프리팹이 서로를 참조하지 않고 만나는 지점이며, 네트워크 전송은 하지 않는다.
    /// ⚠ 정규화한 0~1 이 아니라 <b>raw dB</b> 를 싣는다 — 색 경계(20/45)와 만땅(70)은 UI 튜닝값이라,
    ///   정규화를 플레이어 쪽에 두면 소음 규칙과 UI 스케일이 한 덩어리로 섞인다.
    /// </summary>
    [CreateAssetMenu(fileName = "SO_Event_NoiseMeterLevelChanged", menuName = "Events/Player/Noise Meter Level Changed")]
    public sealed class NoiseMeterLevelChangedEventChannelSO : ScriptableObject
    {
        public event Action<float> OnEventRaised;

        public float CurrentDb { get; private set; } // 마지막으로 발행된 발생 dB. 늦게 켜진 HUD 가 초기 표시에 읽는다

        /// <summary>SO 활성화 시 캐시를 비운다. 에디터에서 SO 는 플레이 세션 사이에 살아남으므로 이전 세션 값이 남지 않게 한다.</summary>
        private void OnEnable()
        {
            CurrentDb = 0f;
        }

        /// <summary>발생 dB 를 캐시에 기록하고 구독자에게 방송한다.</summary>
        /// <param name="db">현재 발생 dB(합산, 무전 수신 포함).</param>
        public void RaiseEvent(float db)
        {
            CurrentDb = db;
            OnEventRaised?.Invoke(db);
        }
    }
}
