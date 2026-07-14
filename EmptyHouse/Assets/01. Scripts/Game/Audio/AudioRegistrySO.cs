using System;
using System.Collections.Generic;
using UnityEngine;
using Border.Core;
using BorderAudio = Border.Audio;

/// <summary>
/// AudioId 로 재생 데이터(Cue/Configuration)를 찾는 레지스트리.
/// Border 판과 달리 SFX/BGM 목록을 나누지 않는다 — 룩업은 어차피 평면 딕셔너리이고,
/// 어느 버스로 나갈지는 Configuration 의 OutputAudioMixerGroup 이 정하기 때문에 목록을 쪼갤 이유가 없다.
/// </summary>
[CreateAssetMenu(fileName = "SO_AudioRegistry", menuName = "EmptyHouse/Audio/Audio Registry")]
public class AudioRegistrySO : ScriptableObject
{
    /// <summary>AudioId 와 그 재생 데이터의 짝.</summary>
    [Serializable]
    private struct AudioEntry
    {
        public AudioId Id;
        public BorderAudio.AudioCueSO Cue;
        public BorderAudio.AudioConfigurationSO Configuration;
    }

    [SerializeField] private List<AudioEntry> entries = new List<AudioEntry>();

    [NonSerialized] private Dictionary<AudioId, AudioEntry> lookup;

    /// <summary>에셋이 메모리에 올라올 때 캐시를 세운다.</summary>
    private void OnEnable()
    {
        Initialize();
    }

    /// <summary>엔트리 목록을 딕셔너리로 캐싱한다. 중복 ID 는 에러로 알린다.</summary>
    public void Initialize()
    {
        lookup = new Dictionary<AudioId, AudioEntry>(entries.Count);

        for (int i = 0; i < entries.Count; i++)
        {
            AudioEntry entry = entries[i];
            if (!lookup.TryAdd(entry.Id, entry))
            {
                Log.E($"[AudioRegistry] 중복 AudioId: {entry.Id}");
            }
        }
    }

    /// <summary>AudioId 에 해당하는 재생 데이터를 조회한다.</summary>
    /// <param name="id">조회할 사운드 식별자.</param>
    /// <param name="cue">조회된 AudioCue. 실패 시 null.</param>
    /// <param name="configuration">조회된 AudioConfiguration. 실패 시 null.</param>
    /// <returns>조회 성공 여부.</returns>
    public bool TryGetAudio(AudioId id, out BorderAudio.AudioCueSO cue, out BorderAudio.AudioConfigurationSO configuration)
    {
        if (lookup == null)
        {
            Initialize();
        }

        cue = null;
        configuration = null;

        if (!lookup.TryGetValue(id, out AudioEntry entry))
        {
            return false;
        }

        cue = entry.Cue;
        configuration = entry.Configuration;
        return true;
    }
}
