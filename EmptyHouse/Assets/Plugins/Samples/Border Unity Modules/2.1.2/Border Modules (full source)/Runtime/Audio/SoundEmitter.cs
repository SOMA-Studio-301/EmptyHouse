using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Border.Audio
{
    [RequireComponent(typeof(AudioSource))]
    public class SoundEmitter : MonoBehaviour
    {
        public UnityAction<SoundEmitter> OnSoundFinishedPlaying;

        private AudioSource audioSource;
        private AudioLowPassFilter lowPassFilter;
        private AudioReverbFilter reverbFilter;
        private bool useCaveAcoustics = true;
        private Coroutine fadeRoutine;
        private float cueGainMultiplier = 1f;
        private float clipBaseVolume = 1f;
        private float playbackLevel = 1f;
        private float caveBlend;
        private float caveLowPassCutoff = 5500f;
        private float caveLowPassResonanceQ = 1f;
        private float caveReverbEnableThreshold = 0.35f;
        private AudioReverbPreset caveReverbPreset = AudioReverbPreset.Cave;
        private float reverbTailTime;
        private static AudioClip silentClip;

        private void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            lowPassFilter = GetOrAddComponent<AudioLowPassFilter>();
            reverbFilter = GetOrAddComponent<AudioReverbFilter>();
            DisableCaveAcoustics();
            EnsureSilentClip();
        }

        /// <summary>
        /// 리버브 테일 유지를 위한 무음 AudioClip을 런타임에 한 번만 생성한다.
        /// </summary>
        private static void EnsureSilentClip()
        {
            if (silentClip != null)
            {
                return;
            }

            const int sampleRate = 44100;
            silentClip = AudioClip.Create("SilentClip", sampleRate, 1, sampleRate, false);
            silentClip.SetData(new float[sampleRate], 0);
        }

        public AudioClip CurrentClip => audioSource != null ? audioSource.clip : null;

        public void PlayAudioClip(AudioClip clip, AudioConfigurationSO settings, bool isLoop, Vector3 position = default, int priority = 128, float volumeMultiplier = 1f, float gainMultiplier = 1f)
        {
            if (clip == null || settings == null)
            {
                return;
            }

            StopFade();

            audioSource.clip = clip;
            settings.ApplyTo(audioSource);
            ApplyPlaybackSettings(audioSource.volume * Mathf.Clamp01(volumeMultiplier), gainMultiplier);
            useCaveAcoustics = settings.UseCaveAcoustics;
            ApplyCaveAcousticsState();
            audioSource.priority = priority;
            audioSource.loop = isLoop;
            audioSource.transform.position = position;
            audioSource.time = 0f;
            audioSource.Play();

            if (!isLoop)
            {
                StartCoroutine(FinishingPlaying(clip.length));
            }
        }

        public void PlayOrSwapLoop(AudioClip clip, AudioConfigurationSO settings, Vector3 position = default, int priority = 0, float volumeMultiplier = 1f, float gainMultiplier = 1f)
        {
            if (clip == null || settings == null)
            {
                return;
            }

            if (audioSource.isPlaying && audioSource.loop && audioSource.clip == clip)
            {
                return;
            }

            StopFade();
            audioSource.Stop();

            audioSource.clip = clip;
            settings.ApplyTo(audioSource);
            ApplyPlaybackSettings(audioSource.volume * Mathf.Clamp01(volumeMultiplier), gainMultiplier);
            useCaveAcoustics = settings.UseCaveAcoustics;
            ApplyCaveAcousticsState();
            audioSource.priority = priority;
            audioSource.loop = true;
            audioSource.transform.position = position;
            audioSource.time = 0f;
            audioSource.Play();
        }

        public bool IsPlaying()
        {
            return audioSource != null && audioSource.isPlaying;
        }

        public bool IsLooping()
        {
            return audioSource != null && audioSource.loop;
        }

        public void Stop()
        {
            StopFade();
            if (audioSource == null)
            {
                return;
            }

            cueGainMultiplier = 1f;
            clipBaseVolume = 1f;
            playbackLevel = 1f;
            audioSource.Stop();
        }

        public void ForceFinish()
        {
            OnSoundFinishedPlaying?.Invoke(this);
        }

        /// <summary>
        /// 동굴 음향 효과(로우패스, 리버브)의 블렌드 값을 설정한다.
        /// </summary>
        /// <param name="blend">동굴 블렌드 값 (0~1)</param>
        /// <param name="reverbPreset">적용할 리버브 프리셋</param>
        /// <param name="lowPassCutoff">로우패스 필터 컷오프 주파수</param>
        /// <param name="lowPassResonanceQ">로우패스 필터 공명 Q값</param>
        /// <param name="reverbEnableThreshold">리버브 활성화 임계값</param>
        /// <param name="tailTime">리버브 테일 대기 시간(초)</param>
        public void SetCaveAcousticsBlend(float blend, AudioReverbPreset reverbPreset, float lowPassCutoff, float lowPassResonanceQ, float reverbEnableThreshold, float tailTime)
        {
            caveBlend = Mathf.Clamp01(blend);
            caveReverbPreset = reverbPreset;
            caveLowPassCutoff = lowPassCutoff;
            caveLowPassResonanceQ = lowPassResonanceQ;
            caveReverbEnableThreshold = Mathf.Clamp01(reverbEnableThreshold);
            reverbTailTime = Mathf.Max(0f, tailTime);

            ApplyCaveAcousticsState();
        }

        public void SetVolume(float volume01)
        {
            if (audioSource == null)
            {
                return;
            }

            playbackLevel = Mathf.Clamp01(volume01);
            RefreshPlaybackVolume();
        }

        public void FadeVolume(float targetVolume01, float duration, bool stopAfterFade = false, bool invokeFinishAfterStop = false)
        {
            if (audioSource == null)
            {
                return;
            }

            StopFade();
            fadeRoutine = StartCoroutine(FadeVolumeRoutine(Mathf.Clamp01(targetVolume01), duration, stopAfterFade, invokeFinishAfterStop));
        }

        private IEnumerator FadeVolumeRoutine(float target, float duration, bool stopAfterFade, bool invokeFinishAfterStop)
        {
            float start = playbackLevel;

            if (duration <= 0f)
            {
                playbackLevel = target;
                RefreshPlaybackVolume();

                if (stopAfterFade)
                {
                    audioSource.Stop();
                    cueGainMultiplier = 1f;
                    clipBaseVolume = 1f;
                    playbackLevel = 1f;
                    if (invokeFinishAfterStop)
                    {
                        OnSoundFinishedPlaying?.Invoke(this);
                    }
                }

                fadeRoutine = null;
                yield break;
            }

            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float lerp = Mathf.Clamp01(t / duration);
                playbackLevel = Mathf.Lerp(start, target, lerp);
                RefreshPlaybackVolume();
                yield return null;
            }

            playbackLevel = target;
            RefreshPlaybackVolume();

            if (stopAfterFade)
            {
                audioSource.Stop();
                cueGainMultiplier = 1f;
                clipBaseVolume = 1f;
                playbackLevel = 1f;
                if (invokeFinishAfterStop)
                {
                    OnSoundFinishedPlaying?.Invoke(this);
                }
            }

            fadeRoutine = null;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (cueGainMultiplier <= 1f)
            {
                return;
            }

            for (int i = 0; i < data.Length; i++)
            {
                data[i] = Mathf.Clamp(data[i] * cueGainMultiplier, -1f, 1f);
            }
        }

        private void StopFade()
        {
            if (fadeRoutine != null)
            {
                StopCoroutine(fadeRoutine);
                fadeRoutine = null;
            }
        }

        private void ApplyPlaybackSettings(float targetVolume, float gainMultiplier)
        {
            clipBaseVolume = Mathf.Clamp01(targetVolume);
            playbackLevel = 1f;
            cueGainMultiplier = Mathf.Max(1f, gainMultiplier);
            RefreshPlaybackVolume();
        }

        private void RefreshPlaybackVolume()
        {
            if (audioSource == null)
            {
                return;
            }

            audioSource.volume = clipBaseVolume * playbackLevel;
        }

        private void ApplyCaveAcousticsState()
        {
            if (!useCaveAcoustics)
            {
                DisableCaveAcoustics();
                return;
            }

            if (lowPassFilter != null)
            {
                bool shouldEnableLowPass = caveBlend > 0.001f;
                lowPassFilter.enabled = shouldEnableLowPass;
                lowPassFilter.cutoffFrequency = Mathf.Lerp(22000f, caveLowPassCutoff, caveBlend);
                lowPassFilter.lowpassResonanceQ = Mathf.Lerp(1f, caveLowPassResonanceQ, caveBlend);
            }

            if (reverbFilter != null)
            {
                bool shouldEnableReverb = caveBlend >= caveReverbEnableThreshold;
                reverbFilter.enabled = shouldEnableReverb;
                reverbFilter.reverbPreset = shouldEnableReverb ? caveReverbPreset : AudioReverbPreset.Off;
            }
        }

        private void DisableCaveAcoustics()
        {
            caveBlend = 0f;

            if (lowPassFilter != null)
            {
                lowPassFilter.enabled = false;
                lowPassFilter.cutoffFrequency = 22000f;
                lowPassFilter.lowpassResonanceQ = 1f;
            }

            if (reverbFilter != null)
            {
                reverbFilter.enabled = false;
                reverbFilter.reverbPreset = AudioReverbPreset.Off;
            }
        }

        private T GetOrAddComponent<T>() where T : Component
        {
            T component = GetComponent<T>();
            if (component == null)
            {
                component = gameObject.AddComponent<T>();
            }

            return component;
        }

        /// <summary>
        /// 클립 재생 완료 후 종료 이벤트를 발화한다.
        /// 리버브가 활성 상태이면 무음 클립을 재생하여 오디오 파이프라인을 유지하고,
        /// 테일 시간만큼 대기하여 잔향이 자연스럽게 소멸되도록 한다.
        /// </summary>
        /// <param name="length">클립 재생 길이(초)</param>
        private IEnumerator FinishingPlaying(float length)
        {
            yield return new WaitForSeconds(length);

            if (reverbFilter != null && reverbFilter.enabled && reverbTailTime > 0f && silentClip != null)
            {
                audioSource.clip = silentClip;
                audioSource.volume = 0f;
                audioSource.loop = true;
                audioSource.Play();

                yield return new WaitForSeconds(reverbTailTime);

                audioSource.Stop();
                audioSource.loop = false;
            }

            OnSoundFinishedPlaying?.Invoke(this);
        }
    }
}
