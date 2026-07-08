using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Border.Core;
using Border.Events;

namespace Border.Audio
{
    /// <summary>
    /// SUMMARY
    /// - SFX/BGM 재생 이벤트를 받아 SoundEmitter를 풀에서 꺼내 재생한다.
    /// - 동굴 오디오는 bool 상태가 아니라 onCaveBlendChanged(0~1) 기반으로 처리한다.
    /// - 활성화된 월드 emitter는 currentCaveBlend를 따라 low-pass / reverb가 갱신된다.
    /// - UI 오디오는 UseCaveAcoustics = false 라서 동굴 영향을 받지 않는다.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        [Header("SoundEmitters Pool")]
        [Tooltip("The first pool in the list will be used as the default for SFX and Music")]
        [SerializeField] private List<SoundEmitterPoolSO> pools = new List<SoundEmitterPoolSO>();
        [SerializeField] private int initialSize = 10;

        [Header("Audio Control")]
        [SerializeField] private AudioMixer audioMixer;
        [Range(0f, 1f)][SerializeField] private float masterVolume = 1f;
        [Range(0f, 1f)][SerializeField] private float musicVolume = 0.8f;
        [Range(0f, 1f)][SerializeField] private float sfxVolume = 1f;

        [Header("Audio Registry")]
        [SerializeField] private AudioRegistrySO audioRegistry;

        [Header("Listening to")]
        // Audio Cue 실행 이벤트
        [SerializeField] private AudioCueEventChannelSO musicEventChannel;
        [SerializeField] private AudioCueEventChannelSO sfxEventChannel;
        // Volume 조절 이벤트
        [SerializeField] private FloatEventChannelSO changeMasterVolumeEvent;
        [SerializeField] private FloatEventChannelSO changeMusicVolumeEvent;
        [SerializeField] private FloatEventChannelSO changeSfxVolumeEvent;

        [Header("Cave Acoustics")]
        [SerializeField] private FloatEventChannelSO onCaveBlendChanged;
        [Min(0.01f)][SerializeField] private float blendResponseTime = 0.25f;
        [SerializeField] private AudioReverbPreset caveReverbPreset = AudioReverbPreset.Cave;
        [Range(500f, 22000f)][SerializeField] private float caveLowPassCutoff = 5500f;
        [Range(1f, 10f)][SerializeField] private float caveLowPassResonanceQ = 1f;
        [Range(0f, 1f)][SerializeField] private float caveReverbEnableThreshold = 0.35f;
        [Tooltip("리버브 테일이 자연스럽게 소멸될 때까지의 추가 대기 시간(초)")]
        [Range(0f, 5f)][SerializeField] private float reverbTailTime = 2f;

        private SoundEmitterVault soundEmitterVault; // 활성화된 Sound Emitters 관리
        private SoundEmitter musicSoundEmitter;      // BGM Sound Emitter
        // current는 실제 적용 중인 값, target은 존/승강기에서 들어온 목표값이다.
        private float currentCaveBlend;
        private float targetCaveBlend;

        private SoundEmitterPoolSO DefaultPool
        {
            get
            {
                if (pools.Count > 0)
                    return pools[0];

                Log.E("Any SoundEmitter Pool has not been assigned.");
                return null;
            }
        }

        private void Awake()
        {
            // Audio Registry 초기화
            InitializeAudioRegistry();

            soundEmitterVault = new SoundEmitterVault();

            foreach (var pool in pools)
            {
                if (!pool.HasLimit)
                    pool.Prewarm(initialSize);
                else
                    pool.Prewarm(pool.LimitActivation);
                pool.SetParent(this.transform);
            }
        }

        /// <summary>
        /// 오디오 레지스트리 캐시를 초기화한다.
        /// </summary>
        private void InitializeAudioRegistry()
        {
            if (audioRegistry == null)
            {
                Log.E("AudioRegistrySO가 설정되지 않았습니다.");
                return;
            }

            audioRegistry.Initialize();
        }

        private void OnEnable()
        {
            musicEventChannel.OnAudioCuePlayRequested += PlayMusicTrack;
            musicEventChannel.OnAudioCueStopRequested += StopMusic;
            sfxEventChannel.OnAudioCuePlayRequested   += PlayAudioCue;
            sfxEventChannel.OnAudioCuePlayWithPoolRequested += PlayAudioCue;
            sfxEventChannel.OnAudioCueStopRequested   += StopAudioCue;

            changeMasterVolumeEvent.OnEventRaised += ChangeMasterVolume;
            changeMusicVolumeEvent.OnEventRaised  += ChangeMusicVolume;
            changeSfxVolumeEvent.OnEventRaised    += ChangeSfxVolume;

            if (onCaveBlendChanged != null)
                onCaveBlendChanged.OnEventRaised += SetCaveBlend;

        }

        private void OnDisable()
        {
            musicEventChannel.OnAudioCuePlayRequested -= PlayMusicTrack;
            musicEventChannel.OnAudioCueStopRequested -= StopMusic;
            sfxEventChannel.OnAudioCuePlayRequested   -= PlayAudioCue;
            sfxEventChannel.OnAudioCuePlayWithPoolRequested -= PlayAudioCue;
            sfxEventChannel.OnAudioCueStopRequested   -= StopAudioCue;

            changeMasterVolumeEvent.OnEventRaised -= ChangeMasterVolume;
            changeMusicVolumeEvent.OnEventRaised  -= ChangeMusicVolume;
            changeSfxVolumeEvent.OnEventRaised    -= ChangeSfxVolume;

            if (onCaveBlendChanged != null)
                onCaveBlendChanged.OnEventRaised -= SetCaveBlend;
        }

        /// <summary>
        /// 씬 전환(파괴) 시 현재 실행 중인 모든 오디오 소스를 안전하게 제거해야 함.
        /// 그렇지 않으면 씬 전환 시 오류 발생(없는 오디오 소스에 신호를 보냄)
        /// </summary>
        private void OnDestroy()
        {
            StopMusic(AudioCueKey.Invalid);

            var emitters = soundEmitterVault.GetAll();
            foreach (var emitter in emitters)
            {
                StopAndCleanEmitter(emitter);
            }
            soundEmitterVault.Clear();

            foreach (var pool in pools)
            {
                pool.ClearPool();
            }
        }

        private void Update()
        {
            UpdateCaveBlend();
        }

        #region VOLUME

        private void ChangeMasterVolume(float value)
        {
            masterVolume = Mathf.Clamp01(value);
            SetGroupVolume("MasterVolume", NormalizedToMixerValue(masterVolume));
        }

        private void ChangeMusicVolume(float value)
        {
            musicVolume = Mathf.Clamp01(value);
            SetGroupVolume("MusicVolume", NormalizedToMixerValue(musicVolume));
        }

        private void ChangeSfxVolume(float value)
        {
            sfxVolume = Mathf.Clamp01(value);
            SetGroupVolume("SFXVolume", NormalizedToMixerValue(sfxVolume));
        }

        /// <summary>
        /// 오디오 믹서 파라미터에 볼륨 값을 적용한다.
        /// </summary>
        private void SetGroupVolume(string parameterName, float normalizedVolume)
        {
            bool volumeSet = audioMixer.SetFloat(parameterName, normalizedVolume);
            if (!volumeSet)
                Log.E("오디오 믹서의 파라미터를 찾을 수 없습니다.");
        }

        private float NormalizedToMixerValue(float normalizedValue)
        {
            // -80dB~0dB로 볼륨 조절
            float ret = (normalizedValue - 1f) * 80f;
            return ret;
        }

        #endregion

        /// <summary>
        /// 오디오 재생 이벤트 정의
        /// </summary>
        /// <param name="audioCue">AudioClip을 담는 SO</param>
        /// <param name="position">AudioSource가 재생될 월드 좌표</param>
        public AudioCueKey PlayAudioCue(AudioCueSO audioCue, AudioConfigurationSO settings, Vector3 position)
        {
            return PlayAudioCue(DefaultPool, audioCue, settings, position);
        }

        public AudioCueKey PlayAudioCue(SoundEmitterPoolSO pool, AudioCueSO audioCue, AudioConfigurationSO settings, Vector3 position)
        {
            if (audioCue == null || settings == null)
            {
                return AudioCueKey.Invalid;
            }

            if (pool is null)
            {
                Log.E("Pool is null");
                return AudioCueKey.Invalid;
            }

            AudioClip clip = audioCue.GetClip();
            if (clip == null)
            {
                return AudioCueKey.Invalid;
            }

            SoundEmitter soundEmitter = pool.Request();
            if (soundEmitter is null)
            {
                return AudioCueKey.Invalid;
            }

            soundEmitter.PlayAudioClip(clip, settings, audioCue.Looping, position, audioCue.priority, audioCue.Volume, audioCue.GainMultiplier);
            soundEmitter.SetCaveAcousticsBlend(currentCaveBlend, caveReverbPreset, caveLowPassCutoff, caveLowPassResonanceQ, caveReverbEnableThreshold, reverbTailTime);
            soundEmitter.OnSoundFinishedPlaying += OnSoundEmitterFinishedPlaying;

            return soundEmitterVault.Add(audioCue, soundEmitter, pool);
        }

        /// <summary>
        /// 오디오 종료 이벤트 정의
        /// </summary>
        /// <param name="audioCueKey">AudioCue Key에 해당하는 SoundEmitter 종료</param>
        private bool StopAudioCue(AudioCueKey audioCueKey)
        {
            bool isFound = soundEmitterVault.Get(audioCueKey, out SoundEmitter soundEmitter);
            // 만약 현재 Key에 대한 SoundEmitter가 존재한다면, 종료
            if (isFound)
            {
                soundEmitter.OnSoundFinishedPlaying -= OnSoundEmitterFinishedPlaying;
                soundEmitter.Stop();
                StopAndCleanEmitter(soundEmitter);
            }
            else
            {
                //Log.W("현재 Key에 대한 SoundEmitter가 존재하지 않습니다.");
            }
            return isFound;
        }

        /// <summary>
        /// SoundEmitter가 오디오 클립을 끝냈을 때의 콜백 함수
        /// </summary>
        private void OnSoundEmitterFinishedPlaying(SoundEmitter soundEmitter)
        {
            soundEmitter.OnSoundFinishedPlaying -= OnSoundEmitterFinishedPlaying;

            StopAndCleanEmitter(soundEmitter);
        }

        /// <summary>
        /// 종료된 SoundEmitter를 제거한다.
        /// </summary>
        private void StopAndCleanEmitter(SoundEmitter soundEmitter)
        {
            soundEmitterVault.Release(soundEmitter);
        }

        /// <summary>
        /// BGM 재생
        /// </summary>
        private AudioCueKey PlayMusicTrack(AudioCueSO audioCue, AudioConfigurationSO audioConfiguration, Vector3 position)
        {
            if (audioCue == null || audioConfiguration == null)
                return AudioCueKey.Invalid;

            AudioClip clip = audioCue.GetClip();
            if (clip == null)
                return AudioCueKey.Invalid;

            // 1) 이미 뮤직 이미터가 있으면: 무시하지 말고 "교체"
            if (musicSoundEmitter != null)
            {
                // 같은 곡이면 굳이 재시작 안 함(선택)
                if (musicSoundEmitter.IsPlaying() && musicSoundEmitter.IsLooping() && musicSoundEmitter.CurrentClip == clip)
                    return AudioCueKey.Invalid;

                // 기존 재생 정지 + 정리(풀 반납)
                StopMusic(AudioCueKey.Invalid);
            }

            // 2) 새 이미터 요청해서 재생
            musicSoundEmitter = DefaultPool.Request();
            if (musicSoundEmitter == null)
                return AudioCueKey.Invalid;

            musicSoundEmitter.PlayAudioClip(clip, audioConfiguration, audioCue.Looping, position, 0, audioCue.Volume, audioCue.GainMultiplier);
            musicSoundEmitter.SetCaveAcousticsBlend(currentCaveBlend, caveReverbPreset, caveLowPassCutoff, caveLowPassResonanceQ, caveReverbEnableThreshold, reverbTailTime);

            // Looping이면 OnSoundFinishedPlaying이 안 불릴 수 있음(그래도 StopMusic에서 정리함)
            musicSoundEmitter.OnSoundFinishedPlaying += StopMusicEmitter;

            soundEmitterVault.Add(audioCue, musicSoundEmitter, DefaultPool);
            return AudioCueKey.Invalid;
        }


        /// <summary>
        /// BGM 종료
        /// </summary>
        private bool StopMusic(AudioCueKey key)
        {
            if (musicSoundEmitter != null && musicSoundEmitter.IsPlaying())
            {
                musicSoundEmitter.Stop();
                StopMusicEmitter(musicSoundEmitter);
                return true;    
            }
            return false;
        }

        private void StopMusicEmitter(SoundEmitter soundEmitter)
        {
            soundEmitter.OnSoundFinishedPlaying -= StopMusicEmitter;

            if (musicSoundEmitter == soundEmitter)
            {
                musicSoundEmitter = null;
            }

            StopAndCleanEmitter(soundEmitter);
        }

        private void SetCaveBlend(float blend)
        {
            float nextBlend = Mathf.Clamp01(blend);
            if (Mathf.Approximately(targetCaveBlend, nextBlend))
            {
                return;
            }

            targetCaveBlend = nextBlend;

            if (blendResponseTime <= 0f)
            {
                currentCaveBlend = targetCaveBlend;
                ApplyCaveAcousticsToActiveEmitters();
            }
        }

        private void ApplyCaveAcousticsToActiveEmitters()
        {
            if (soundEmitterVault == null)
            {
                return;
            }

            List<SoundEmitter> emitters = soundEmitterVault.GetAll();
            for (int i = 0; i < emitters.Count; i++)
            {
                SoundEmitter emitter = emitters[i];
                if (emitter == null)
                {
                    continue;
                }

                emitter.SetCaveAcousticsBlend(currentCaveBlend, caveReverbPreset, caveLowPassCutoff, caveLowPassResonanceQ, caveReverbEnableThreshold, reverbTailTime);
            }
        }

        private void UpdateCaveBlend()
        {
            // 입구/승강기 전환에서 월드 SFX가 급변하지 않도록 짧게 보간한다.
            float nextBlend = MoveBlendTowards(currentCaveBlend, targetCaveBlend);
            if (Mathf.Approximately(currentCaveBlend, nextBlend))
            {
                return;
            }

            currentCaveBlend = nextBlend;
            ApplyCaveAcousticsToActiveEmitters();
        }

        private float MoveBlendTowards(float current, float target)
        {
            if (blendResponseTime <= 0f)
            {
                return target;
            }

            float step = Time.unscaledDeltaTime / blendResponseTime;
            return Mathf.MoveTowards(current, target, step);
        }
    }
}
