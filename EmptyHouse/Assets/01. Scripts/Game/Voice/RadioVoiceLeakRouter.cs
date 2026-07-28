using System.Collections.Generic;
using Dissonance;
using Dissonance.Audio.Playback;
using Dissonance.Integrations.Unity_NFGO;
using UnityEngine;

/// <summary>
/// 원격 무전 송신음을 모든 수신 무전기 위치의 작은 3D 스피커로 복제한다.
/// </summary>
[DefaultExecutionOrder(1100)]
public sealed class RadioVoiceLeakRouter : MonoBehaviour
{
    private sealed class Leak
    {
        public string SpeakerId;
        public PlayerRadioVoiceController Receiver;
        public GameObject Object;
    }

    private readonly List<Leak> leaks = new();
    private DissonanceComms comms;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindAnyObjectByType<RadioVoiceLeakRouter>() != null)
        {
            return;
        }

        GameObject instance = new GameObject(nameof(RadioVoiceLeakRouter));
        DontDestroyOnLoad(instance);
        instance.AddComponent<RadioVoiceLeakRouter>();
    }

    private void Update()
    {
        comms = comms != null ? comms : FindAnyObjectByType<DissonanceComms>();
        if (comms == null || !comms.IsNetworkInitialized)
        {
            ClearLeaks();
            return;
        }

        PlayerRadioVoiceController[] radios =
            FindObjectsByType<PlayerRadioVoiceController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

        for (int i = leaks.Count - 1; i >= 0; i--)
        {
            Leak leak = leaks[i];
            if (leak.Receiver == null
                || !leak.Receiver.HasRadio
                || !IsSpeakerTransmitting(leak.SpeakerId, radios))
            {
                Destroy(leak.Object);
                leaks.RemoveAt(i);
            }
        }

        for (int i = 0; i < comms.Players.Count; i++)
        {
            VoicePlayerState speaker = comms.Players[i];
            if (speaker.IsLocalPlayer || !speaker.IsConnected || speaker.Playback == null)
            {
                continue;
            }

            PlayerRadioVoiceController transmitter = FindRadio(speaker.Name, radios);
            if (transmitter == null || !transmitter.IsTransmitting)
            {
                continue;
            }

            AudioCloneSource cloneSource =
                ((MonoBehaviour)speaker.Playback).GetComponent<AudioCloneSource>();
            if (cloneSource == null)
            {
                continue;
            }

            for (int r = 0; r < radios.Length; r++)
            {
                PlayerRadioVoiceController receiver = radios[r];
                if (!receiver.HasRadio || receiver == transmitter)
                {
                    continue;
                }

                if (!HasLeak(speaker.Name, receiver))
                {
                    CreateLeak(speaker.Name, cloneSource, receiver);
                }
            }
        }
    }

    private static PlayerRadioVoiceController FindRadio(
        string playerId,
        PlayerRadioVoiceController[] radios)
    {
        for (int i = 0; i < radios.Length; i++)
        {
            NfgoPlayer tracker = radios[i].GetComponent<NfgoPlayer>();
            if (tracker != null && tracker.PlayerId == playerId)
            {
                return radios[i];
            }
        }

        return null;
    }

    private static bool IsSpeakerTransmitting(
        string playerId,
        PlayerRadioVoiceController[] radios)
    {
        PlayerRadioVoiceController radio = FindRadio(playerId, radios);
        return radio != null && radio.IsTransmitting;
    }

    private bool HasLeak(string speakerId, PlayerRadioVoiceController receiver)
    {
        for (int i = 0; i < leaks.Count; i++)
        {
            if (leaks[i].SpeakerId == speakerId && leaks[i].Receiver == receiver)
            {
                return true;
            }
        }

        return false;
    }

    private void CreateLeak(
        string speakerId,
        AudioCloneSource source,
        PlayerRadioVoiceController receiver)
    {
        GameObject leakObject = new GameObject($"RadioLeak_{speakerId}");
        leakObject.transform.SetParent(receiver.transform, false);
        leakObject.transform.localPosition = new Vector3(0f, 1.2f, 0f);

        AudioSource audioSource = leakObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = true;
        audioSource.spatialBlend = 0f;
        audioSource.bypassReverbZones = true;

        AudioLowPassFilter radioFilter = leakObject.AddComponent<AudioLowPassFilter>();
        radioFilter.cutoffFrequency = 3500f;
        radioFilter.lowpassResonanceQ = 1.5f;

        RadioLeakDistanceAttenuation attenuation =
            leakObject.AddComponent<RadioLeakDistanceAttenuation>();
        attenuation.BaseVolume = 0.45f;
        attenuation.MinDistance = 1f;
        attenuation.MaxDistance = 8f;

        AudioCloneSink sink = leakObject.AddComponent<AudioCloneSink>();
        sink.Source = source;
        sink.AutoPlay = true;

        leaks.Add(new Leak
        {
            SpeakerId = speakerId,
            Receiver = receiver,
            Object = leakObject
        });
    }

    private void OnDestroy()
    {
        ClearLeaks();
    }

    private void ClearLeaks()
    {
        for (int i = 0; i < leaks.Count; i++)
        {
            if (leaks[i].Object != null)
            {
                Destroy(leaks[i].Object);
            }
        }

        leaks.Clear();
    }
}
