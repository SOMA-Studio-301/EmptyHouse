using UnityEngine;
using UnityEngine.Audio;

namespace Border.Audio
{
    [CreateAssetMenu(fileName = "NewAudioConfiguration", menuName = "Audio/AudioConfiguration")]
    public class AudioConfigurationSO : ScriptableObject
    {
        private enum PriorityLevel
        {
            Highest = 0,
            High = 64,
            Standard = 128,
            Low = 194,
            VeryLow = 256,
        }

        public AudioMixerGroup OutputAudioMixerGroup = null;

        [SerializeField] private PriorityLevel priorityLevel = PriorityLevel.Standard;
        public int Priority
        {
            get { return (int)priorityLevel; }
            set { priorityLevel = (PriorityLevel)value; }
        }

        [Header("Sound properties")]
        public bool Mute = false;
        [Range(0f, 1f)] public float MinVolume = 1f;
        [Range(0f, 1f)] public float MaxVolume = 1f;
        [Range(-3f, 3f)] public float MinPitch = 1f;
        [Range(-3f, 3f)] public float MaxPitch = 1f;
        [Range(-1f, 1f)] public float PanStereo = 0f;
        [Range(0f, 1.1f)] public float ReverbZoneMix = 1f;

        [Header("Spatialisation")]
        [Range(0f, 1f)] public float SpatialBlend = 1f;
        public AudioRolloffMode RolloffMode = AudioRolloffMode.Logarithmic;
        [Range(0.01f, 5f)] public float MinDistance = 0.1f;
        [Range(5f, 100f)] public float MaxDistance = 50f;
        [Range(0, 360)] public int Spread = 0;
        [Range(0f, 5f)] public float DopplerLevel = 1f;

        [Header("Ignores")]
        public bool BypassEffects = false;
        public bool BypassListenerEffects = false;
        public bool BypassReverbZones = false;
        public bool IgnoreListenerVolume = false;
        public bool IgnoreListenerPause = false;

        [Header("Cave Acoustics")]
        public bool UseCaveAcoustics = true;

        public void ApplyTo(AudioSource audioSource)
        {
            audioSource.outputAudioMixerGroup = this.OutputAudioMixerGroup;
            audioSource.mute = this.Mute;
            audioSource.bypassEffects = this.BypassEffects;
            audioSource.bypassListenerEffects = this.BypassListenerEffects;
            audioSource.bypassReverbZones = this.BypassReverbZones;
            audioSource.priority = this.Priority;
            audioSource.volume = Random.Range(this.MinVolume, this.MaxVolume);
            audioSource.pitch = Random.Range(this.MinPitch, this.MaxPitch);
            audioSource.panStereo = this.PanStereo;
            audioSource.spatialBlend = this.SpatialBlend;
            audioSource.reverbZoneMix = this.ReverbZoneMix;
            audioSource.dopplerLevel = this.DopplerLevel;
            audioSource.spread = this.Spread;
            audioSource.rolloffMode = this.RolloffMode;
            audioSource.minDistance = this.MinDistance;
            audioSource.maxDistance = this.MaxDistance;
            audioSource.ignoreListenerVolume = this.IgnoreListenerVolume;
            audioSource.ignoreListenerPause = this.IgnoreListenerPause;
        }
    }
}
