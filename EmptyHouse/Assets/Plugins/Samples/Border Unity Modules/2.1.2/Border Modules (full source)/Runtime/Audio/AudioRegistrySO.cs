using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Border.Core;

namespace Border.Audio
{
    [CreateAssetMenu(fileName = "AudioRegistry", menuName = "Audio/Audio Registry")]
    public class AudioRegistrySO : ScriptableObject
    {
        [Serializable]
        private struct AudioEntry
        {
            public AudioId id;
            public AudioCueSO cue;
            public AudioConfigurationSO configuration;
        }

        [Header("SFX Entries")]
        [FormerlySerializedAs("entries")]
        [SerializeField] private List<AudioEntry> sfxEntries = new List<AudioEntry>();

        [Header("BGM Entries")]
        [SerializeField] private List<AudioEntry> bgmEntries = new List<AudioEntry>();

        [NonSerialized] private Dictionary<AudioId, AudioEntry> lookup;
        [NonSerialized] private bool isInitialized;

        /// <summary>
        /// 에셋 활성화 시 레지스트리 캐시를 초기화한다.
        /// </summary>
        private void OnEnable()
        {
            Initialize();
        }

        /// <summary>
        /// 레지스트리 캐시를 초기화한다. AudioManager에서 호출한다.
        /// </summary>
        public void Initialize()
        {
            int sfxCount = sfxEntries != null ? sfxEntries.Count : 0;
            int bgmCount = bgmEntries != null ? bgmEntries.Count : 0;
            lookup = new Dictionary<AudioId, AudioEntry>(sfxCount + bgmCount);
            isInitialized = true;

            if (sfxEntries != null)
            {
                for (int i = 0; i < sfxEntries.Count; i++)
                {
                    AudioEntry entry = sfxEntries[i];
                    if (lookup.ContainsKey(entry.id))
                    {
                        Log.E($"AudioRegistry 중복 ID: {entry.id}");
                    }
                    lookup[entry.id] = entry;
                }
            }

            if (bgmEntries == null)
                return;

            for (int i = 0; i < bgmEntries.Count; i++)
            {
                AudioEntry entry = bgmEntries[i];
                if (lookup.ContainsKey(entry.id))
                {
                    Log.E($"AudioRegistry 중복 ID: {entry.id}");
                }
                lookup[entry.id] = entry;
            }
        }

        /// <summary>
        /// ID에 해당하는 오디오 데이터를 조회한다.
        /// </summary>
        /// <param name="id">오디오 ID</param>
        /// <param name="cue">조회된 AudioCue</param>
        /// <param name="configuration">조회된 AudioConfiguration</param>
        /// <returns>조회 성공 여부</returns>
        public bool TryGetAudio(AudioId id, out AudioCueSO cue, out AudioConfigurationSO configuration)
        {
            EnsureInitialized();
            cue = null;
            configuration = null;

            if (!lookup.TryGetValue(id, out AudioEntry entry))
                return false;

            cue = entry.cue;
            configuration = entry.configuration;
            return true;
        }

        /// <summary>
        /// 레지스트리가 초기화되지 않았을 경우 초기화를 수행한다.
        /// </summary>
        private void EnsureInitialized()
        {
            if (!isInitialized)
                Initialize();
        }

    #if UNITY_EDITOR
        /// <summary>
        /// 에디터에서 레지스트리 데이터의 중복 및 누락을 검증한다.
        /// </summary>
        private void OnValidate()
        {
            var set = new HashSet<AudioId>();

            if (sfxEntries != null)
            {
                for (int i = 0; i < sfxEntries.Count; i++)
                {
                    AudioEntry entry = sfxEntries[i];
                    if (!set.Add(entry.id))
                    {
                        Log.E($"AudioRegistry 중복 ID: {entry.id}");
                    }

                    if (entry.cue == null)
                    {
                        Log.E($"AudioRegistry cue 누락: {entry.id}");
                    }

                    if (entry.configuration == null)
                    {
                        Log.E($"AudioRegistry configuration 누락: {entry.id}");
                    }
                }
            }

            if (bgmEntries == null)
                return;

            for (int i = 0; i < bgmEntries.Count; i++)
            {
                AudioEntry entry = bgmEntries[i];
                if (!set.Add(entry.id))
                {
                    Log.E($"AudioRegistry 중복 ID: {entry.id}");
                }

                if (entry.cue == null)
                {
                    Log.E($"AudioRegistry cue 누락: {entry.id}");
                }

                if (entry.configuration == null)
                {
                    Log.E($"AudioRegistry configuration 누락: {entry.id}");
                }
            }
        }
    #endif
    }
}
