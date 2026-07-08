using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Border.Core;

namespace Border.Audio
{
    /// <summary>
    /// 생성된 SoundEmitter들 저장소
    /// </summary>
    public class SoundEmitterVault
    {
        private int _uniqueKey = 0;
        private readonly Dictionary<AudioCueKey, SoundEmitter> _activeEmitters = new Dictionary<AudioCueKey, SoundEmitter>();
        private readonly Dictionary<SoundEmitter, SoundEmitterPoolSO> _emitterToPoolMap = new Dictionary<SoundEmitter, SoundEmitterPoolSO>();

        /// <summary>
        /// 새로운 AudioCue에 대한 AudioCueKey를 반환
        /// </summary>
        private AudioCueKey GetKey(AudioCueSO cue)
        {
            return new AudioCueKey(_uniqueKey++, cue);
        }

        /// <summary>
        /// 새로운 AudioCueSO를 추가
        /// </summary>
        public AudioCueKey Add(AudioCueSO cue, SoundEmitter emitter, SoundEmitterPoolSO pool)
        {
            AudioCueKey key = GetKey(cue);
            _activeEmitters.Add(key, emitter);
            _emitterToPoolMap.Add(emitter, pool);
            return key;
        }

        /// <summary>
        /// Key에 대한 SoundEmitter를 찾아서 할당함
        /// 탐색 여부를 Boolean으로 반환
        /// </summary>
        public bool Get(AudioCueKey key, out SoundEmitter emitter)
        {
            return _activeEmitters.TryGetValue(key, out emitter);
        }

        /// <summary>
        /// 사용이 끝난 이미터를 Vault에서 해제하고, 올바른 풀에 반납합니다.
        /// </summary>
        public bool Release(SoundEmitter emitter)
        {
            if (_emitterToPoolMap.TryGetValue(emitter, out SoundEmitterPoolSO pool))
            {
                pool.Return(emitter);
                _emitterToPoolMap.Remove(emitter);

                // Emitter를 사용하는 activeEmitters Dictionary에서 해당 Entry를 제거
                AudioCueKey keyToRemove = AudioCueKey.Invalid;
                foreach (var pair in _activeEmitters)
                {
                    if (pair.Value == emitter)
                    {
                        keyToRemove = pair.Key;
                        break;
                    }
                }

                if (keyToRemove != AudioCueKey.Invalid)
                {
                    _activeEmitters.Remove(keyToRemove);
                }

                return true;
            }

            Log.W($"Could not find pool for SoundEmitter {emitter.name}");
            return false;
        }

        /// <summary>
        /// 씬 종료 시에 모든 SoundEmiiter를 안전하게 종료하기 위해
        /// AudioManager에서 존재하는 모든 컴포넌트를 가져와야 함.
        /// </summary>
        /// <returns></returns>
        public List<SoundEmitter> GetAll()
        {
            return _activeEmitters.Values.ToList();
        }

        /// <summary>
        /// Vault에 저장된 모든 요소를 제거
        /// </summary>
        public void Clear()
        {
            _activeEmitters.Clear();
            _emitterToPoolMap.Clear();
        }
    }
}
