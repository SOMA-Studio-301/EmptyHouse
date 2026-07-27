using System.Text;
using Dissonance;
using Dissonance.Integrations.Unity_NFGO;
using UnityEngine;

/// <summary>
/// 에디터 및 개발 빌드에서 Dissonance 음성 경로를 한 줄로 진단한다.
/// 씬에 직접 배치할 필요 없이 런타임에 자동 생성된다.
/// </summary>
public sealed class VoiceChatDiagnostics : MonoBehaviour
{
    private const float LogInterval = 1f;
    private readonly StringBuilder builder = new StringBuilder(512);

    private DissonanceComms comms;
    private float nextLogTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (FindAnyObjectByType<VoiceChatDiagnostics>() != null)
        {
            return;
        }

        GameObject instance = new GameObject(nameof(VoiceChatDiagnostics));
        DontDestroyOnLoad(instance);
        instance.AddComponent<VoiceChatDiagnostics>();
#endif
    }

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Time.unscaledTime < nextLogTime)
        {
            return;
        }

        nextLogTime = Time.unscaledTime + LogInterval;

        if (comms == null)
        {
            comms = FindAnyObjectByType<DissonanceComms>();
        }

        if (comms == null)
        {
            Debug.LogWarning("[VoiceDiag] DissonanceComms not found");
            return;
        }

        builder.Clear();
        builder.Append("[VoiceDiag] network=").Append(comms.IsNetworkInitialized);
        builder.Append(" deafened=").Append(comms.IsDeafened);
        builder.Append(" remoteVolume=").Append(comms.RemoteVoiceVolume.ToString("F2"));
        builder.Append(" listeners=").Append(FindObjectsByType<AudioListener>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None).Length);

        VoiceProximityBroadcastTrigger proximity =
            FindAnyObjectByType<VoiceProximityBroadcastTrigger>();
        if (proximity != null)
        {
            builder.Append(" proximityMode=").Append(proximity.Mode);
            builder.Append(" proximityTx=").Append(proximity.IsTransmitting);
            builder.Append(" proximityMuted=").Append(proximity.IsMuted);
        }

        builder.Append(" players=").Append(comms.Players.Count);
        for (int i = 0; i < comms.Players.Count; i++)
        {
            VoicePlayerState player = comms.Players[i];
            builder.Append(" | ");
            builder.Append(player.IsLocalPlayer ? "LOCAL" : "REMOTE");
            builder.Append(":connected=").Append(player.IsConnected);
            builder.Append(",speaking=").Append(player.IsSpeaking);
            builder.Append(",amp=").Append(player.Amplitude.ToString("F4"));
            builder.Append(",muted=").Append(player.IsLocallyMuted);
            builder.Append(",rooms=").Append(player.Rooms.Count);
            builder.Append(",tracked=").Append(player.Tracker != null && player.Tracker.IsTracking);
        }

        NfgoPlayer[] trackers = FindObjectsByType<NfgoPlayer>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        builder.Append(" | nfgo=").Append(trackers.Length);
        for (int i = 0; i < trackers.Length; i++)
        {
            NfgoPlayer tracker = trackers[i];
            builder.Append(" [id=").Append(tracker.PlayerId);
            builder.Append(",owner=").Append(tracker.IsOwner);
            builder.Append(",local=").Append(tracker.IsLocalPlayer);
            builder.Append(",tracking=").Append(tracker.IsTracking);
            builder.Append(']');
        }

        Debug.Log(builder.ToString());
#endif
    }
}
