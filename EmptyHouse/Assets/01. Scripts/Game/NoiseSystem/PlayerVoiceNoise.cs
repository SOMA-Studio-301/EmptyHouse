using Dissonance;
using Unity.Netcode;
using UnityEngine;
using Log = Border.Core.Log; // Dissonance.Log 와 이름이 겹쳐 별칭으로 고정한다

namespace EmptyHouse.NoiseSystem
{
    /// <summary>
    /// 로컬 마이크 발화량을 소음 파이프로 넘긴다 — <see cref="VoiceNoiseEventChannelSO"/> 훅(소음시스템.md 7장)의 유일한 발신자.
    /// 마이크 진폭은 Dissonance 가 소유 클라이언트에서만 캡처하므로 오너가 측정해 ServerRpc 로 올리고,
    /// 서버는 상한 검증만 한 뒤 <b>서버 측 위치</b>로 발행한다 — 위치까지 클라 보고를 믿으면 아무 데서나 소음을 만들 수 있다.
    /// 이후는 NoiseFeatureEventBridge → NoiseEmittedEvent 로 기존 판정 파이프를 그대로 탄다.
    /// 걷기 소음(PlayerNoise)과 같은 합산 창에 들어가므로 미터·좀비 판정에 별도 규칙이 생기지 않는다(9-3).
    ///
    /// 관전자(사망 OR 귀환)는 발행하지 않는다 — 관전자 음성은 관전 방으로만 흐르는 메타 대화라
    /// 게임 세계의 소리가 아니다(VoiceChatGlobalBridge 와 같은 기준). 서버도 같은 조건으로 RPC 를 걸러
    /// 죽은 클라가 조작 RPC 로 소음을 만드는 길을 막는다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerVoiceNoise : NetworkBehaviour
    {
        [SerializeField] private VoiceNoiseEventChannelSO voiceChannel;
        // 합산 창(0.5초)보다 길어야 한 창에 표본이 두 번 들어가 부풀지 않는다 — PlayerNoise 와 같은 하한
        [Min(0.51f)] [SerializeField] private float emissionIntervalSeconds = 0.6f;
        [Range(0f, 1f)] [SerializeField] private float speakingAmplitudeThreshold = 0.05f; // 이 미만은 숨소리·배경 잡음으로 보고 버린다
        [Min(0f)] [SerializeField] private float voiceMinDb = 15f; // 임계 진폭에서의 dB — 속삭임. 걷기(20dB)보다 조용하게
        [Min(0f)] [SerializeField] private float voiceMaxDb = 40f; // 진폭 1.0 에서의 dB — 고함. 서버 검증 상한을 겸한다

        private DissonanceComms comms;          // 씬 오브젝트라 플레이어보다 늦게 뜰 수 있어 매 프레임 재확인한다
        private VoicePlayerState localVoice;    // 로컬 마이크 진폭의 원천. comms 가 바뀌면 다시 찾는다
        private PlayerDeathHandler deathHandler; // 관전 판정용 형제 컴포넌트. 복제 상태라 서버에서도 읽힌다
        private PlayerReturn playerReturn;       // 관전 판정용 형제 컴포넌트. 위와 같음
        private float elapsed;
        private float peakAmplitude; // 구간 내 최대 진폭. 순간 표본은 0.6초 사이의 발화를 통째로 놓칠 수 있다

        /// <summary>사망·귀환 어느 쪽이든 비활성이면 관전으로 본다 — PlayerRadioVoiceController 와 같은 기준.</summary>
        private bool IsSpectating =>
            (deathHandler != null && deathHandler.IsDead.Value)
            || (playerReturn != null && playerReturn.HasExtracted.Value);

        private void Awake()
        {
            deathHandler = GetComponent<PlayerDeathHandler>();
            playerReturn = GetComponent<PlayerReturn>();
        }

        public override void OnNetworkSpawn()
        {
            Log.D($"[PlayerVoiceNoise] Spawn — id {NetworkObjectId} / owner {IsOwner}");

            if (!IsOwner)
            {
                enabled = false; // 측정은 오너 전용이고 서버 몫은 RPC 핸들러뿐이라 Update 를 돌릴 이유가 없다
            }
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner) return;

            if (IsSpectating)
            {
                elapsed = 0f;
                peakAmplitude = 0f;
                return;
            }

            TrackPeakAmplitude();

            elapsed += Time.deltaTime;
            if (elapsed < emissionIntervalSeconds) return;

            float amplitude = peakAmplitude;
            elapsed = 0f;
            peakAmplitude = 0f;

            if (amplitude < speakingAmplitudeThreshold) return;

            // 임계~최대 진폭을 속삭임~고함 dB 로 선형 매핑한다. 곡선이 필요해지면 여기만 바꾸면 된다.
            float loudness = Mathf.InverseLerp(speakingAmplitudeThreshold, 1f, amplitude);
            SubmitVoiceNoiseServerRpc(Mathf.Lerp(voiceMinDb, voiceMaxDb, loudness));
        }

        /// <summary>
        /// 이번 프레임의 마이크 진폭을 구간 최대치에 반영한다. Dissonance 초기화 전이면 조용히 넘어간다 —
        /// DissonanceComms 는 씬 오브젝트라 플레이어 스폰 시점에 없을 수 있다(PlayerRadioVoiceController 와 같은 사정).
        /// </summary>
        private void TrackPeakAmplitude()
        {
            // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
            DissonanceComms current = FindAnyObjectByType<DissonanceComms>();
            if (current != comms)
            {
                comms = current;
                localVoice = null;
            }

            if (comms == null || !comms.IsNetworkInitialized) return;

            if (localVoice == null && !string.IsNullOrEmpty(comms.LocalPlayerName))
            {
                localVoice = comms.FindPlayer(comms.LocalPlayerName);
            }

            if (localVoice == null) return;

            peakAmplitude = Mathf.Max(peakAmplitude, localVoice.Amplitude);
        }

        /// <summary>
        /// 오너가 측정한 발화 dB 를 받아 서버 권위로 발행한다. dB 는 서버가 아는 상한(<see cref="voiceMaxDb"/>)으로
        /// 자르고 위치는 서버 측 transform 을 쓴다 — 클라 보고값 중 믿는 것은 "말했다"는 사실과 크기의 하한뿐이다.
        /// </summary>
        /// <param name="voiceDb">오너가 진폭 매핑으로 계산한 발화 dB.</param>
        [ServerRpc]
        private void SubmitVoiceNoiseServerRpc(float voiceDb)
        {
            // 발화 구간마다(0.6초) 호출되므로 진입 트레이스를 두지 않는다.
            if (voiceChannel == null || IsSpectating) return;

            float clampedDb = Mathf.Clamp(voiceDb, 0f, voiceMaxDb);
            if (clampedDb <= 0f) return;

            voiceChannel.RaiseEvent(new VoiceNoiseEvent(NetworkObjectId, transform.position, clampedDb));
        }
    }
}
