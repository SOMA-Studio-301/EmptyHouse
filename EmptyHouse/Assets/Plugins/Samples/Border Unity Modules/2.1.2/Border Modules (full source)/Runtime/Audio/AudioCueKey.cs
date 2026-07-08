using System;
using UnityEngine;

namespace Border.Audio
{
    /// <summary>
    /// Key, AudioCueSO 쌍을 저장
    /// 특정 SoundEmitter를 종료하기 위함
    /// </summary>
    public struct AudioCueKey : IEquatable<AudioCueKey>
    {
        public static AudioCueKey Invalid = new AudioCueKey(-1, null);

        private int key;
        private AudioCueSO audioCue;

        public AudioCueKey(int key, AudioCueSO audioCue)
        {
            this.key = key;
            this.audioCue = audioCue;
        }

        public static bool operator ==(AudioCueKey a, AudioCueKey b)
        {
            return a.key == b.key && a.audioCue == b.audioCue;
        }
        public static bool operator !=(AudioCueKey a, AudioCueKey b)
        {
            return !(a == b);
        }
        public bool Equals(AudioCueKey other)
        {
            return key == other.key && Equals(audioCue, other.audioCue);
        }
        public override bool Equals(object obj)
        {
            return obj is AudioCueKey other && Equals(other);
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(key, audioCue);
        }
    }
}
