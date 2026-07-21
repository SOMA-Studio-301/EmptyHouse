using System.Collections.Generic;
using Border.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 비활성(사망 OR 귀환) 플레이어의 관전을 담당하는 소유자 로컬 컨트롤러(D13 · 조작상호작용UI.md 3-8-1 · 세션루프.md 3~4장).
/// 형제 PlayerDeathHandler.IsDead 와 PlayerReturn.HasExtracted 를 구독해 본인이 비활성이 되면 관전에 진입한다 —
/// GameState.Spectating 을 발행하고, 3인칭 카메라를 생존 대상 주위에 궤도로 배치하며,
/// 마우스 Look 으로 대상을 중심으로 카메라를 돌리고 InputReader 의 Next/Previous(←/→)로 생존 대상을 순환한다.
/// 전원 사망은 게임오버라 관전 대상 0 은 존재하지 않는다(3-8-1).
/// 사망 캐릭터의 이동·E·인벤 차단은 PlayerController 소관이다. 보이스 채널 분리(네트워크)·전신 모델(아트)은 선행 의존이라 다루지 않는다.
/// </summary>
[RequireComponent(typeof(PlayerDeathHandler))]
[RequireComponent(typeof(PlayerReturn))]
public class PlayerSpectatorController : NetworkBehaviour
{
    [Header("State")]
    [SerializeField] private GameStateEventChannelSO gameStateChanged; // 관전 진입 시 발행할 클라 상태 채널. Spectating 을 실어 ClientGameManager 가 입력 모드를 전환한다

    [Header("Input")]
    [SerializeField] private InputReader inputReader; // 관전 입력 소스. 마우스 Look(궤도 회전)·Next/Previous(대상 순환) 수신. PlayerController 와 같은 SO

    [Header("Spectator UI")]
    [SerializeField] private SpectatorEventChannelSO spectator; // 관전 HUD 양방향 채널. 대상 변경 발행 · HUD 좌우 버튼 순환 요청 수신(←/→ 키와 같은 경로)

    [Header("Spectator Camera")]
    [SerializeField] private float orbitDistance = 3.5f;   // 대상 중심에서 카메라까지의 거리(m). 3인칭이지 1인칭 승계가 아니다. ⚪ 튜닝값
    [SerializeField] private float orbitHeight = 1.5f;     // 궤도 중심을 대상 발밑에서 올릴 높이(m). 대상 몸통을 겨눈다. ⚪ 튜닝값
    [SerializeField] private float lookSensitivity = 0.1f; // 마우스 Look 델타 → 궤도 회전 각도 배율. PlayerController 시선 감도와 맞춘다. ⚪ 튜닝값
    [SerializeField] private float pitchClamp = 80f;       // 궤도 피치 상한(도). ±값으로 제한해 카메라가 뒤집히지 않게 한다. ⚪ 튜닝값
    [SerializeField] private float initialPitch = 10f;     // 대상 전환 직후의 초기 하향 피치(도). 살짝 내려다본 상태로 시작한다. ⚪ 튜닝값

    private PlayerDeathHandler deathHandler; // 사망 상태 소스. 같은 프리팹의 형제 컴포넌트
    private PlayerReturn playerReturn;       // 귀환 상태 소스. 같은 프리팹의 형제 컴포넌트
    private int currentTargetIndex;          // 현재 관전 대상 인덱스. 생존자 순환의 커서
    private bool isSpectating;               // 관전 중 여부. Look/Next/Previous 소비와 카메라 추적을 관전 상태로만 게이팅한다
    private NetworkObject currentTarget;     // 현재 관전 대상. LateUpdate 가 매 프레임 위치를 추적한다
    private float orbitYaw;                  // 궤도 수평각(도). 마우스 Look 이 누적한다
    private float orbitPitch;                // 궤도 상하각(도). 마우스 Look 이 누적하고 pitchClamp 로 제한된다
    private Transform spectatorCamera;       // 관전 카메라 트랜스폼. EnterSpectate 에서 Camera.main 을 캐시(매 프레임 조회 회피)

    /// <summary>형제 PlayerDeathHandler·PlayerReturn 참조를 캐시한다.</summary>
    private void Awake()
    {
        deathHandler = GetComponent<PlayerDeathHandler>();
        playerReturn = GetComponent<PlayerReturn>();
    }

    /// <summary>소유자에 한해 비활성 상태(사망·귀환)와 관전 입력 구독을 건다. 관전은 본인 화면의 로컬 연출이라 소유자만 관여한다.</summary>
    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        deathHandler.IsDead.OnValueChanged += HandleInactiveChanged;
        playerReturn.HasExtracted.OnValueChanged += HandleInactiveChanged;
        inputReader.LookEvent += OnLook;
        inputReader.NextEvent += OnNext;
        inputReader.PreviousEvent += OnPrevious;
        spectator.OnCycleRequested += HandleCycleRequested;
    }

    /// <summary>비활성 상태·관전 입력 구독을 해제한다. OnNetworkSpawn 과 짝을 맞춘다.</summary>
    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;

        deathHandler.IsDead.OnValueChanged -= HandleInactiveChanged;
        playerReturn.HasExtracted.OnValueChanged -= HandleInactiveChanged;
        inputReader.LookEvent -= OnLook;
        inputReader.NextEvent -= OnNext;
        inputReader.PreviousEvent -= OnPrevious;
        spectator.OnCycleRequested -= HandleCycleRequested;
    }

    /// <summary>관전 중 매 프레임 대상의 현재 위치를 추적해 궤도 카메라를 갱신한다. 대상 이동을 반영하려 LateUpdate 에서 처리한다.</summary>
    private void LateUpdate()
    {
        if (!IsOwner || !isSpectating || currentTarget == null) return;

        PositionCamera();
    }

    /// <summary>마우스 Look 델타로 궤도 yaw/pitch 를 누적한다. 관전 중에만 반응한다.</summary>
    /// <param name="delta">포인터 delta 입력값.</param>
    private void OnLook(Vector2 delta)
    {
        if (!isSpectating) return;

        orbitYaw += delta.x * lookSensitivity;
        orbitPitch -= delta.y * lookSensitivity;
        orbitPitch = Mathf.Clamp(orbitPitch, -pitchClamp, pitchClamp);
    }

    /// <summary>Next 입력을 받아 다음 생존 대상으로 순환한다. 관전 중에만 반응한다.</summary>
    private void OnNext()
    {
        if (!isSpectating) return;

        CycleTarget(1);
    }

    /// <summary>Previous 입력을 받아 이전 생존 대상으로 순환한다. 관전 중에만 반응한다.</summary>
    private void OnPrevious()
    {
        if (!isSpectating) return;

        CycleTarget(-1);
    }

    /// <summary>관전 HUD 좌우 버튼의 순환 요청을 받아 대상을 바꾼다. ←/→ 키와 동일한 경로다. 관전 중에만 반응한다.</summary>
    /// <param name="direction">순환 방향(+1 다음 / -1 이전).</param>
    private void HandleCycleRequested(int direction)
    {
        if (!isSpectating) return;

        CycleTarget(direction);
    }

    /// <summary>
    /// 사망·귀환 어느 쪽의 변화든 받아 관전 진입/이탈을 전환한다. 둘 중 하나라도 비활성이면 진입, 둘 다 풀려야 이탈(MVP 미발동).
    /// 두 채널이 같은 핸들러를 공유하므로 인자는 쓰지 않고 현재 상태를 다시 읽는다.
    /// </summary>
    /// <param name="previous">이전 상태(미사용).</param>
    /// <param name="current">새 상태(미사용).</param>
    private void HandleInactiveChanged(bool previous, bool current)
    {
        bool isInactive = deathHandler.IsDead.Value || playerReturn.HasExtracted.Value;

        if (isInactive == isSpectating) return; // 이미 반영된 상태면 재진입/재이탈하지 않는다(사망+귀환 중복 전이 방어)

        if (isInactive) EnterSpectate();
        else ExitSpectate();
    }

    /// <summary>관전에 진입한다. GameState.Spectating 을 발행하고 카메라를 떼어 첫 생존 대상 궤도에 배치한다.</summary>
    private void EnterSpectate()
    {
        isSpectating = true;
        gameStateChanged.RaiseEvent(GameState.Spectating);

        // 1인칭 pivot 에서 카메라를 떼어 궤도가 월드 좌표로 배치할 수 있게 한다.
        spectatorCamera = Camera.main.transform;
        spectatorCamera.SetParent(null);

        List<NetworkObject> targets = CollectAliveTargets();
        if (targets.Count == 0) return; // 전원 사망은 게임오버라 관전 대상 0 은 없다(3-8-1) — 경합 대비 방어적 스킵

        currentTargetIndex = 0;
        ApplySpectatorCamera(targets[currentTargetIndex]);
    }

    /// <summary>관전을 종료한다(귀환 부활 D18 — MVP 단일 외출이라 미발동). GameState.Game 으로 복귀시킨다.</summary>
    private void ExitSpectate()
    {
        isSpectating = false;
        currentTarget = null;

        // 대상 추적만 해제한다. 1인칭 카메라 원복은 pivot 을 쥔 PlayerController 소관이며 부활(D18) 흐름에서 처리한다 — MVP 미발동.
        if (spectatorCamera != null) spectatorCamera.SetParent(null);
        gameStateChanged.RaiseEvent(GameState.Game);
    }

    /// <summary>생존 플레이어를 방향으로 순환해 관전 대상을 바꾼다. 대상이 1명 이하면 순환이 무의미하므로 입력을 무시한다.</summary>
    /// <param name="direction">순환 방향(+1 다음 / -1 이전).</param>
    private void CycleTarget(int direction)
    {
        List<NetworkObject> targets = CollectAliveTargets();

        // 1명이면 인덱스는 그대로여도 ApplySpectatorCamera 가 궤도를 리셋해 카메라만 튄다 — 아예 무시한다(AC 3-8-1).
        if (targets.Count <= 1) return;

        // 음수 방향도 감싸도록 정규화.
        currentTargetIndex = ((currentTargetIndex + direction) % targets.Count + targets.Count) % targets.Count;
        ApplySpectatorCamera(targets[currentTargetIndex]);
    }

    /// <summary>관전 대상을 지정하고 궤도를 대상 뒤로 초기화한다(1인칭 승계 아님). 이후 위치 추적은 LateUpdate 가 맡는다.</summary>
    /// <param name="target">관전할 대상 플레이어의 NetworkObject.</param>
    private void ApplySpectatorCamera(NetworkObject target)
    {
        currentTarget = target;

        // 궤도를 대상이 바라보는 방향 뒤로 맞춰, 전환 직후 대상을 등지고 같은 방향을 보는 시점으로 시작한다.
        orbitYaw = target.transform.eulerAngles.y;
        orbitPitch = initialPitch;
        PositionCamera();

        // 관전 HUD 가 대상 닉네임을 갱신할 수 있도록 방송한다. HUD 는 폰을 직접 참조하지 않는다.
        spectator.RaiseTargetChanged(target.OwnerClientId);
    }

    /// <summary>궤도 각도(yaw/pitch)와 대상 위치로 카메라를 대상 뒤 3인칭에 배치한다.</summary>
    private void PositionCamera()
    {
        Quaternion rot = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
        Vector3 focus = currentTarget.transform.position + Vector3.up * orbitHeight;

        spectatorCamera.SetPositionAndRotation(focus - rot * Vector3.forward * orbitDistance, rot);
    }

    /// <summary>활성 생존 플레이어 NetworkObject 목록을 수집한다. IsDead 또는 HasExtracted 면 제외한다 — 귀환자는 비활성이라 관전 대상이 아니다(세션루프.md 3장).</summary>
    /// <returns>사망하지도 귀환하지도 않은 플레이어 오브젝트 목록.</returns>
    private List<NetworkObject> CollectAliveTargets()
    {
        // 클라이언트에서도 채워지는 SpawnManager.PlayerObjects 로 전 플레이어를 순회한다(연결 클라 목록은 서버 전용이라 관전 소유자 클라에서 못 쓴다).
        List<NetworkObject> alive = new List<NetworkObject>();
        foreach (NetworkObject player in NetworkManager.SpawnManager.PlayerObjects)
        {
            if (player.GetComponent<PlayerDeathHandler>().IsDead.Value) continue;
            if (player.GetComponent<PlayerReturn>().HasExtracted.Value) continue;
            alive.Add(player);
        }
        return alive;
    }
}
