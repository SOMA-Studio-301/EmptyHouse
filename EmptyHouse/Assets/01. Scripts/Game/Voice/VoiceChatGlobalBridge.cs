using Dissonance;
using UnityEngine;

/// <summary>
/// Dissonance 위치 추적 상태와 무관하게 모든 플레이어를 하나의 음성 방에 연결한다.
/// </summary>
[DefaultExecutionOrder(1000)]
public sealed class VoiceChatGlobalBridge : MonoBehaviour
{
    private const string RoomName = "EmptyHouse.GlobalVoice";

    private DissonanceComms comms;
    private RoomMembership? membership;
    private RoomChannel? transmitChannel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindAnyObjectByType<VoiceChatGlobalBridge>() != null)
        {
            return;
        }

        GameObject instance = new GameObject(nameof(VoiceChatGlobalBridge));
        DontDestroyOnLoad(instance);
        instance.AddComponent<VoiceChatGlobalBridge>();
    }

    private void Update()
    {
        DissonanceComms current = FindAnyObjectByType<DissonanceComms>();
        if (current != comms)
        {
            Disconnect();
            comms = current;
        }

        if (comms == null || !comms.IsNetworkInitialized)
        {
            Disconnect();
            return;
        }

        if (!membership.HasValue)
        {
            membership = comms.Rooms.Join(RoomName);
        }

        if (!transmitChannel.HasValue)
        {
            transmitChannel = comms.RoomChannels.Open(
                new RoomName(RoomName),
                positional: true,
                priority: ChannelPriority.Default);

            Debug.Log("[VoiceGlobal] Connected: always-on distance-attenuated voice enabled");
        }
    }

    private void OnDestroy()
    {
        Disconnect();
    }

    private void Disconnect()
    {
        if (transmitChannel.HasValue)
        {
            transmitChannel.Value.Dispose();
            transmitChannel = null;
        }

        if (membership.HasValue)
        {
            if (comms != null)
            {
                comms.Rooms.Leave(membership.Value);
            }

            membership = null;
        }
    }
}
