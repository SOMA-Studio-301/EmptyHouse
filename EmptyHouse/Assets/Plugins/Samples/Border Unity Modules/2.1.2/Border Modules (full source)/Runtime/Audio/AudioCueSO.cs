using UnityEngine;

namespace Border.Audio
{
    [CreateAssetMenu(fileName = "NewAudioCueSO", menuName = "Audio/Audio Cue")]
    public class AudioCueSO : ScriptableObject
    {
        public bool Looping = false;
        [Range(0, 256)]
        public int priority = 128;
        [Header("Level")]
        [Range(0f, 1f)]
        [SerializeField] private float volume = 1f;
        [Range(0f, 12f)]
        [SerializeField] private float gainDb = 0f;
        [SerializeField] private AudioClip audioClip;

        public float Volume => volume;
        public float GainDb => gainDb;
        public float GainMultiplier => Mathf.Pow(10f, gainDb / 20f);

        public AudioClip GetClip()
        {
            return audioClip;
        }
    }
}
