using Dissonance;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Dissonance 위치 추적 상태와 무관하게 로컬 플레이어를 상황에 맞는 음성 방에 연결한다.
/// 생존 중에는 전원이 공유하는 근접(3D) 방으로 송신하고, 관전(사망 OR 귀환) 상태가 되면
/// 그 송신을 닫고 관전자 전용(2D) 방으로만 송신한다 — 산 사람에게는 관전자 목소리가 닿지 않는다.
/// 관전자는 근접 방의 청취 자격은 유지하므로 관전 카메라 주변 생존자들의 대화는 계속 들린다
/// (HearLivingWhileSpectating 으로 끌 수 있다).
/// 씬에 놓인 Dissonance 근접 트리거(VoiceProximityBroadcastTrigger)도 생존자에게 닿는 두 번째 송신 경로라
/// 관전 중에는 함께 잠근다 — 이 방만 닫아서는 목소리가 새어 나간다.
/// 비활성 판정은 PlayerSpectatorController 와 같은 기준(IsDead OR HasExtracted)을 쓴다.
/// </summary>
[DefaultExecutionOrder(1000)]
public sealed class VoiceChatGlobalBridge : MonoBehaviour
{
    private const string LivingRoom = "EmptyHouse.GlobalVoice";       // 생존자 근접 방. 전원이 상시 청취한다
    private const string SpectatorRoom = "EmptyHouse.SpectatorVoice"; // 관전자 전용 방. 관전자만 가입하므로 생존자에게 새지 않는다

    // 관전자가 생존자 근접 방을 계속 청취할지 여부. false 로 두면 관전자끼리의 목소리만 듣는다.
    // 어느 쪽이든 관전자의 송신은 생존자에게 닿지 않는다(방이 다르다). ⚪ 튜닝값
    // const 가 아니라 static readonly 인 이유: const 면 반대쪽 분기가 상수 접힘으로 죽어 CS0162 경고가 난다.
    private static readonly bool HearLivingWhileSpectating = true;

    private DissonanceComms comms;
    private RoomMembership? livingMembership;    // 근접 방 청취 자격
    private RoomMembership? spectatorMembership; // 관전 방 청취 자격. 생존 중에는 반드시 비어 있어야 한다
    private RoomChannel? livingChannel;          // 근접 방 송신 채널(3D)
    private RoomChannel? spectatorChannel;       // 관전 방 송신 채널(2D)

    private bool? lastSpectating;         // 마지막으로 적용한 관전 여부. 전환 시점에만 로그를 남기려는 용도
    private NetworkObject localPlayer;    // 로컬 플레이어 오브젝트 캐시. 바뀔 때만 아래 두 컴포넌트를 다시 찾는다
    private PlayerDeathHandler localDeath;
    private PlayerReturn localReturn;

    private VoiceProximityBroadcastTrigger proximityBroadcast; // 씬 배치 근접 송신 트리거. 관전 중 잠글 대상
    private VoiceProximityReceiptTrigger proximityReceipt;     // 씬 배치 근접 수신 트리거. 관전자 청취 정책에 맞춰 켜고 끈다

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

        bool spectating = IsLocalPlayerSpectating();
        if (spectating)
        {
            ApplySpectatorRouting();
        }
        else
        {
            ApplyLivingRouting();
        }

        ApplyProximityTriggers(spectating);

        if (lastSpectating != spectating)
        {
            lastSpectating = spectating;
            Debug.Log(spectating
                ? "[VoiceGlobal] Spectator channel: talking to spectators only"
                : "[VoiceGlobal] Connected: always-on distance-attenuated voice enabled");
        }
    }

    /// <summary>생존 중 배선. 관전 방에서 완전히 빠지고 근접 방을 3D 로 송수신한다.</summary>
    private void ApplyLivingRouting()
    {
        CloseChannel(ref spectatorChannel);
        LeaveRoom(ref spectatorMembership);

        JoinRoom(ref livingMembership, LivingRoom);
        OpenChannel(ref livingChannel, LivingRoom, positional: true);
    }

    /// <summary>
    /// 관전 중 배선. 근접 방 송신을 닫아 생존자에게 목소리가 가지 않게 하고, 관전 방을 2D 로 송수신한다.
    /// 관전자는 월드 위치가 의미 없으므로(카메라는 남의 주위를 돈다) 관전 방은 비공간 채널이다.
    /// </summary>
    private void ApplySpectatorRouting()
    {
        CloseChannel(ref livingChannel);

        if (HearLivingWhileSpectating)
        {
            JoinRoom(ref livingMembership, LivingRoom);
        }
        else
        {
            LeaveRoom(ref livingMembership);
        }

        JoinRoom(ref spectatorMembership, SpectatorRoom);
        OpenChannel(ref spectatorChannel, SpectatorRoom, positional: false);
    }

    /// <summary>
    /// 씬에 배치된 Dissonance 근접 트리거를 관전 상태에 맞춘다.
    /// 송신 트리거는 관전 중 음소거한다 — 이 방 배선과 별개로 돌아가는 두 번째 송신 경로라 놔두면 생존자에게 목소리가 간다.
    /// 수신 트리거는 관전자가 생존자를 들을지 여부(HearLivingWhileSpectating)에 따라 켜고 끈다.
    /// 트리거가 없는 씬(로비 등)에서는 아무것도 하지 않는다.
    /// </summary>
    /// <param name="spectating">로컬 플레이어의 관전 여부.</param>
    private void ApplyProximityTriggers(bool spectating)
    {
        proximityBroadcast = proximityBroadcast != null
            ? proximityBroadcast
            : FindAnyObjectByType<VoiceProximityBroadcastTrigger>();
        proximityReceipt = proximityReceipt != null
            ? proximityReceipt
            : FindAnyObjectByType<VoiceProximityReceiptTrigger>();

        SetProximityTriggers(spectating);
    }

    /// <summary>이미 찾아둔 근접 트리거에만 상태를 반영한다. 정리 경로에서 새로 탐색하지 않으려고 분리했다.</summary>
    /// <param name="spectating">로컬 플레이어의 관전 여부.</param>
    private void SetProximityTriggers(bool spectating)
    {
        // 세터가 채널 정리와 로그를 겸하므로 값이 바뀔 때만 건드린다.
        if (proximityBroadcast != null && proximityBroadcast.IsMuted != spectating)
        {
            proximityBroadcast.IsMuted = spectating;
        }

        bool receiptEnabled = !spectating || HearLivingWhileSpectating;
        if (proximityReceipt != null && proximityReceipt.enabled != receiptEnabled)
        {
            proximityReceipt.enabled = receiptEnabled; // OnDisable 이 가입한 그리드 방을 정리한다
        }
    }

    /// <summary>
    /// 로컬 플레이어가 비활성(사망 OR 귀환)인지 본다 — PlayerSpectatorController 의 관전 진입 조건과 같은 기준이다.
    /// 두 상태 모두 전 클라에 복제되는 NetworkVariable 이라 소유 클라에서 그대로 읽을 수 있다.
    /// </summary>
    /// <returns>관전 중이면 true. 플레이어가 아직 스폰되지 않았거나 접속 전이면 false.</returns>
    private bool IsLocalPlayerSpectating()
    {
        NetworkManager network = NetworkManager.Singleton;
        NetworkObject current = network != null && network.LocalClient != null
            ? network.LocalClient.PlayerObject
            : null;

        if (current != localPlayer)
        {
            localPlayer = current;
            localDeath = current != null ? current.GetComponent<PlayerDeathHandler>() : null;
            localReturn = current != null ? current.GetComponent<PlayerReturn>() : null;
        }

        if (localPlayer == null || !localPlayer.IsSpawned)
        {
            return false;
        }

        return (localDeath != null && localDeath.IsDead.Value)
            || (localReturn != null && localReturn.HasExtracted.Value);
    }

    private void OnDestroy()
    {
        Disconnect();
    }

    /// <summary>모든 방 가입·송신을 정리한다. comms 가 이미 파괴된 경우엔 핸들만 버린다.</summary>
    private void Disconnect()
    {
        CloseChannel(ref livingChannel);
        CloseChannel(ref spectatorChannel);
        LeaveRoom(ref livingMembership);
        LeaveRoom(ref spectatorMembership);

        // 우리가 걸어둔 관전용 잠금을 남기지 않는다 — 다음 세션이 음소거된 트리거로 시작하면 아무도 말을 못 한다.
        SetProximityTriggers(false);
        lastSpectating = null;
    }

    private void JoinRoom(ref RoomMembership? membership, string room)
    {
        if (membership.HasValue) return;

        membership = comms.Rooms.Join(room);
    }

    private void LeaveRoom(ref RoomMembership? membership)
    {
        if (!membership.HasValue) return;

        // comms 가 파괴된 뒤라면 방 목록도 함께 사라졌으므로 핸들만 버린다.
        if (comms != null)
        {
            comms.Rooms.Leave(membership.Value);
        }

        membership = null;
    }

    private void OpenChannel(ref RoomChannel? channel, string room, bool positional)
    {
        if (channel.HasValue) return;

        channel = comms.RoomChannels.Open(
            new RoomName(room),
            positional,
            ChannelPriority.Default);
    }

    private void CloseChannel(ref RoomChannel? channel)
    {
        if (!channel.HasValue) return;

        if (comms != null)
        {
            channel.Value.Dispose();
        }

        channel = null;
    }
}
