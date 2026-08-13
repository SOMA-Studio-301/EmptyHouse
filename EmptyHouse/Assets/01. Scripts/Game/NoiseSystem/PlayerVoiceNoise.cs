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
        // 마이크는 지속 소음이라 표본이 누적되지 않고 레벨을 갱신한다(3-2) — 합산 창보다 짧아도 값이 부풀지 않는다.
        // 진폭 구간 최대치를 뜨는 창이기도 해서 너무 짧으면 발화 순간을 놓친다
        [Min(0.02f)] [SerializeField] private float emissionIntervalSeconds = 0.1f;
        [Range(0f, 1f)] [SerializeField] private float speakingAmplitudeThreshold = 0.05f; // 이 미만은 숨소리·배경 잡음으로 보고 버린다
        [Min(0f)] [SerializeField] private float voiceMinDb = 15f; // 임계 진폭에서의 dB — 속삭임. 걷기(20dB)보다 조용하게
        [Min(0f)] [SerializeField] private float voiceMaxDb = 40f; // 진폭 1.0 에서의 dB — 고함. 서버 검증 상한을 겸한다

        private DissonanceComms comms;          // 씬 오브젝트라 플레이어보다 늦게 뜰 수 있어 매 프레임 재확인한다
        private VoicePlayerState localVoice;    // 로컬 마이크 진폭의 원천. comms 가 바뀌면 다시 찾는다
        private PlayerDeathHandler deathHandler; // 관전 판정용 형제 컴포넌트. 복제 상태라 서버에서도 읽힌다
        private PlayerReturn playerReturn;       // 관전 판정용 형제 컴포넌트. 위와 같음
        private float elapsed;
        private float peakAmplitude; // 구간 내 최대 진폭. 순간 표본은 구간 사이의 발화를 통째로 놓칠 수 있다
        private float publishedVoiceDb; // 마지막으로 서버에 올린 발화 레벨. 값이 바뀐 구간에만 RPC 를 쏘기 위한 비교값

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

        /// <summary>
        /// 서버가 남은 발화 레벨을 0 으로 내린다. 발화 중 접속이 끊기면 0 을 올릴 오너가 없어져
        /// 지속 슬롯에 dB 가 박히고 그 자리에서 좀비를 계속 부른다 — <c>enabled=false</c> 인 서버 인스턴스도 이 콜백은 받는다.
        /// </summary>
        public override void OnNetworkDespawn()
        {
            if (!IsServer) return;

            voiceChannel.RaiseEvent(new VoiceNoiseEvent(NetworkObjectId, transform.position, 0f));
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner) return;

            if (IsSpectating)
            {
                elapsed = 0f;
                peakAmplitude = 0f;
                PublishVoiceLevel(0f); // 관전 전환 순간 남아 있던 레벨을 내린다
                return;
            }

            TrackPeakAmplitude();

            elapsed += Time.deltaTime;
            if (elapsed < emissionIntervalSeconds) return;

            float amplitude = peakAmplitude;
            elapsed = 0f;
            peakAmplitude = 0f;

            // 임계 미만은 숨소리·배경 잡음이라 0 으로 올린다 — 발행을 건너뛰면 마지막 발화 레벨이 서버 버킷에 그대로 박힌다.
            // 임계~최대 진폭은 속삭임~고함 dB 로 선형 매핑한다. 곡선이 필요해지면 여기만 바꾸면 된다.
            float voiceDb = amplitude < speakingAmplitudeThreshold
                ? 0f
                : Mathf.Lerp(voiceMinDb, voiceMaxDb, Mathf.InverseLerp(speakingAmplitudeThreshold, 1f, amplitude));

            PublishVoiceLevel(voiceDb);
        }

        /// <summary>발화 레벨을 서버로 올린다. 값이 실제로 바뀐 구간에만 RPC 를 쏴 주기를 줄인 만큼 트래픽이 늘지 않게 한다.</summary>
        /// <param name="db">이번 구간의 발화 dB. 임계 미만이거나 관전 중이면 0.</param>
        private void PublishVoiceLevel(float db)
        {
            // 발화 구간마다 호출되므로 진입 트레이스를 두지 않는다.
            if (Mathf.Approximately(db, publishedVoiceDb)) return;

            publishedVoiceDb = db;
            SubmitVoiceNoiseServerRpc(db);
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
        /// 관전자는 소음원이 아니므로(E2) 0 으로 눌러서 흘린다 — 아예 막으면 사망 직전 레벨이 버킷에 남는다.
        /// </summary>
        /// <param name="voiceDb">오너가 진폭 매핑으로 계산한 발화 dB. 0 은 "말이 그쳤다"는 뜻이다.</param>
        [ServerRpc]
        private void SubmitVoiceNoiseServerRpc(float voiceDb)
        {
            // 발화 구간마다 호출되므로 진입 트레이스를 두지 않는다.
            if (voiceChannel == null) return;

            float clampedDb = IsSpectating ? 0f : Mathf.Clamp(voiceDb, 0f, voiceMaxDb);
            voiceChannel.RaiseEvent(new VoiceNoiseEvent(NetworkObjectId, transform.position, clampedDb));
        }
    }
}
