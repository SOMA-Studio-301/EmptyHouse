using System.Collections.Generic;
using UnityEngine;
using Border.Core;
using Border.Events;
using BorderAudio = Border.Audio;

/// <summary>
/// 좀비의 위협 수준을 읽어 추격 BGM 과 조우 스팅을 지휘한다. 씬에 하나만 둔다.
///
/// 판단 근거는 좀비의 의심도·발각 여부다. 둘 다 NetworkVariable(ReadPermission.Everyone)이라
/// 각 클라이언트가 자기 화면의 연출을 스스로 굴린다 — 서버가 재생을 지시하지 않는다.
/// 그래서 이 컴포넌트에는 서버 게이트가 없고, ZombieRuntimeRegistrySO 의 좀비 집합도
/// 비서버 인스턴스까지 포함해야 한다(ZombieController.OnEnable).
///
/// 의심도는 좀비별 스칼라일 뿐 "누구를 향한 의심인지"는 복제되지 않는다(currentTargetNetworkObjectId 는
/// 서버 전용 필드다). 따라서 맵 전체 좀비를 훑는다 — 팀원이 쫓기는 중이어도 내 화면에 추격 BGM 이 깔린다.
/// </summary>
public class ZombieThreatAudioDirector : MonoBehaviour
{
    /// <summary>의심도 0 판정 여유. 냉각은 MoveTowards 라 정확히 0 에 닿지만, 복제 오차에 대비한 문턱이다.</summary>
    private const float SuspicionEpsilon = 0.01f;

    [Header("Source")]
    [SerializeField] private ZombieRuntimeRegistrySO zombieRegistry; // 의심도를 훑을 좀비 집합
    [SerializeField] private AudioRegistrySO audioRegistry;          // AudioId → cue/config 룩업

    [Header("Audio Channels")]
    [SerializeField] private BorderAudio.AudioCueEventChannelSO musicEventChannel; // AudioManager 가 구독하는 BGM 채널
    [SerializeField] private FloatEventChannelSO fadeOutMusicEvent;                // BGM 페이드아웃 요청(초)
    [SerializeField] private SFXEventChannelSO sfxEventChannel;                    // AudioId 단발 재생 채널

    [Header("Track")]
    [SerializeField] private AudioId chaseTrackAudioId = AudioId.Bgm_Chased_By_Zombie; // 의심도가 오르면 깔릴 곡
    [SerializeField] private AudioId encounterAudioId = AudioId.Amb_Encounter;         // 발각 순간의 단발 스팅

    [Header("Timing")]
    [Tooltip("모든 좀비의 의심도가 0 이 된 뒤 이만큼 버틴 다음에 페이드아웃을 시작한다.")]
    [SerializeField, Min(0f)] private float safeHoldSeconds = 3f;
    [Tooltip("BGM 이 무음까지 잦아드는 데 걸리는 시간(초).")]
    [SerializeField, Min(0f)] private float fadeOutSeconds = 3f;

    /// <summary>추격 BGM 재생을 요청했고 아직 페이드아웃을 걸지 않았다.</summary>
    private bool chaseMusicOn;

    /// <summary>이번 조우의 스팅을 이미 울렸다. 완전 안전 상태로 돌아올 때까지 다시 울리지 않는다.</summary>
    private bool encounterStingFired;

    /// <summary>어떤 좀비도 의심하지 않은 채 흐른 시간(초).</summary>
    private float safeSeconds;

    /// <summary>참조가 하나라도 비면 연출 전체가 조용히 죽으므로 시작 시 확인한다.</summary>
    private void Awake()
    {
        if (zombieRegistry == null || audioRegistry == null
            || musicEventChannel == null || fadeOutMusicEvent == null || sfxEventChannel == null)
        {
            Log.E($"[{nameof(ZombieThreatAudioDirector)}] 참조가 비어 있다. 위협 오디오 연출을 끈다.");
            enabled = false;
        }
    }

    /// <summary>
    /// 꺼질 때 자기가 깔아 둔 추격 BGM 을 즉시 거둔다. 길이 0 의 페이드로 요청하는 이유는
    /// 정지 채널(RaiseStopEvent)과 달리 이쪽은 구독자가 없어도 조용하기 때문이다 —
    /// 씬 해제 순서상 AudioManager 가 먼저 사라지면 정지 채널은 매번 경고를 남긴다.
    /// </summary>
    private void OnDisable()
    {
        if (!chaseMusicOn) return;

        chaseMusicOn = false;
        fadeOutMusicEvent.RaiseEvent(0f);
    }

    /// <summary>위협 수준을 매 프레임 훑어 BGM 과 스팅을 갱신한다.</summary>
    private void Update()
    {
        // 매 프레임 도는 경로라 진입 트레이스를 두지 않는다. 상태가 바뀌는 순간에만 로그를 남긴다.
        ScanThreat(out bool anySuspicion, out bool anyDiscovery);

        if (anyDiscovery)
        {
            PlayEncounterSting();
        }

        // 의심도가 오르기 시작하면 100 을 기다리지 않고 곧바로 깐다.
        if (anySuspicion)
        {
            safeSeconds = 0f;
            StartChaseMusic();
            return;
        }

        safeSeconds += Time.deltaTime;
        if (safeSeconds < safeHoldSeconds) return;

        ReleaseThreatAudio();
    }

    /// <summary>살아 있는 좀비를 훑어 의심 중인 개체와 발각에 성공한 개체가 있는지 본다.</summary>
    /// <param name="anySuspicion">의심도가 0 을 넘긴 좀비가 하나라도 있는가.</param>
    /// <param name="anyDiscovery">플레이어를 발각(latch)한 좀비가 하나라도 있는가.</param>
    private void ScanThreat(out bool anySuspicion, out bool anyDiscovery)
    {
        anySuspicion = false;
        anyDiscovery = false;

        IReadOnlyList<ZombieController> zombies = zombieRegistry.Zombies;
        for (int i = 0; i < zombies.Count; i++)
        {
            ZombieController zombie = zombies[i];
            if (zombie == null || !zombie.IsSpawned) continue;

            if (zombie.SuspicionValue > SuspicionEpsilon) anySuspicion = true;

            if (zombie.IsLatched)
            {
                // 발각은 의심도 100 에서만 서지만 둘은 서로 다른 NetworkVariable 이라 복제 순서를 보장받지 못한다.
                // latch 만 먼저 도착한 프레임에 BGM 이 빠지지 않도록 위협도 함께 세운다.
                anySuspicion = true;
                anyDiscovery = true;
                return; // 둘 다 잡혔으니 나머지는 볼 것이 없다
            }
        }
    }

    /// <summary>
    /// 추격 BGM 을 요청한다. 페이드아웃 도중에 다시 불려도 옳게 동작한다 —
    /// AudioManager 가 같은 곡의 재생 요청을 받으면 걸려 있던 페이드를 취소하고 음량을 되돌린다.
    /// </summary>
    private void StartChaseMusic()
    {
        if (chaseMusicOn) return;

        if (!audioRegistry.TryGetAudio(chaseTrackAudioId, out BorderAudio.AudioCueSO cue, out BorderAudio.AudioConfigurationSO config))
        {
            Log.W($"[{nameof(ZombieThreatAudioDirector)}] 레지스트리 룩업 실패: {chaseTrackAudioId}");
            enabled = false; // 매 프레임 같은 경고를 쏟지 않는다
            return;
        }

        Log.D($"[{nameof(ZombieThreatAudioDirector)}] 추격 BGM ON {chaseTrackAudioId}");

        chaseMusicOn = true;
        musicEventChannel.RaisePlayEvent(cue, config, transform.position);
    }

    /// <summary>발각 순간의 단발 스팅을 울린다. 한 번의 조우에 한 번뿐이다.</summary>
    private void PlayEncounterSting()
    {
        if (encounterStingFired) return;

        Log.D($"[{nameof(ZombieThreatAudioDirector)}] 조우 스팅 {encounterAudioId}");

        encounterStingFired = true;
        sfxEventChannel.RaisePlayEvent(encounterAudioId, transform.position);
    }

    /// <summary>
    /// 완전 안전 상태가 <see cref="safeHoldSeconds"/> 만큼 이어졌을 때 BGM 을 서서히 끄고 스팅을 재무장한다.
    /// 안전한 동안 매 프레임 불리므로 두 작업 모두 한 번만 일어나게 막아 둔다.
    /// </summary>
    private void ReleaseThreatAudio()
    {
        // 스팅 재무장은 여기서 한다. 의심도가 0 으로 식은 직후에 풀면 좀비가 놓쳤다 다시 찾는
        // 짧은 사이에도 스팅이 거듭 울린다 — BGM 이 잦아드는 시점과 같은 순간에 맞춘다.
        encounterStingFired = false;

        if (!chaseMusicOn) return;

        Log.D($"[{nameof(ZombieThreatAudioDirector)}] 추격 BGM 페이드아웃 {fadeOutSeconds}s");

        chaseMusicOn = false;
        fadeOutMusicEvent.RaiseEvent(fadeOutSeconds);
    }
}
