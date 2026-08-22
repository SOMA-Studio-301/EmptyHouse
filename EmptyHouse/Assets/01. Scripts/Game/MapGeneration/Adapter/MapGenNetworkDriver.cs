using System.Collections.Generic;
using Border.Core;
using Border.Events;
using EmptyHouse.MapGen.Core;
using Unity.Netcode;
using UnityEngine;

namespace EmptyHouse.MapGen.Runtime
{
    /// <summary>
    /// 시드 복제·맵 준비 시퀀스 드라이버(8절·X7·X8).
    /// 서버: 시드 확정(X8) → 생성·검증 → (시드, 채택 리롤) 복제. 전원(서버 포함): 생성 키 수신 → 로컬 재생성·조립(MapRuntimeAssembler)
    /// → 해시 보고. 서버: 전 클라 보고 수집(X7) + 해시 일치 확인(AC-02) → onMapAssembledServer 발화
    /// (→ NavMesh 베이크 → 상태 오브젝트 스폰으로 이어진다).
    /// 늦은 합류자는 생성 키가 이미 확정값이라 OnNetworkSpawn 시점에 같은 경로로 조립한다.
    /// </summary>
    public sealed class MapGenNetworkDriver : NetworkBehaviour
    {
        [Header("Config")]
        [SerializeField] private MapDefinitionSO mapDefinition; // 빈 집 정의(M10-1) — 구 직참조 4건(파라미터·층 스택·조명·레지스트리)이 이 한 장으로 접힌다. 클라 전원이 같은 에셋을 가져야 한다(AC-02)

        [Header("Event Channels")]
        [SerializeField] private VoidEventChannelSO onMapAssembledServer; // 서버 전용 발화 — 전 클라 조립 완료(X7). NavMesh 베이커가 구독
        [SerializeField] private MapOverviewEventChannelSO onMapOverviewReady; // 클라 로컬 발화(EH-62) — 자기 조립 직후 안내도 모델을 실어 발행. UIMapOverview 가 구독

        private readonly NetworkVariable<int> mapSeed = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server); // 확정 시드(0 = 미확정) — 블루프린트 대신 작은 생성 키만 복제(정적 지오메트리 대역폭 0)
        private readonly NetworkVariable<int> mapAttempt = new NetworkVariable<int>(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server); // 서버가 검증 후 채택한 0 기반 리롤 인덱스 — 시드와 함께 최종 후보를 유일하게 식별

        private readonly Dictionary<ulong, uint> reportedHashes = new Dictionary<ulong, uint>(); // 서버 전용 — 클라 Id → 보고 해시(X7 집계)
        private uint localHash; // 로컬 조립 블루프린트 해시 — 서버에서는 보고 대조 기준(AC-02)
        private bool assembledEventFired; // onMapAssembledServer 1회 발화 가드

        public MapBlueprint LocalBlueprint { get; private set; } // 로컬 재생성 블루프린트 — 스포너·베이커·툴이 소비
        public GameObject LocalMapRoot { get; private set; } // 로컬 조립 맵 루트
        public System.Collections.Generic.IReadOnlyList<RoomTemplateDef> LocalTemplates { get; private set; } // 이번 생성에 쓴 평탄화 템플릿(M9-8) — 다층은 접미사 ID 라 레지스트리 재추출로 대체 불가

        /// <summary>스포너·베이커가 소비하는 빈 집 정의 접근자 — 문·탈출문·아이템 프리팹의 층별 원천.</summary>
        public MapDefinitionSO MapDefinition => mapDefinition;

        /// <summary>층 서수의 바닥면 Y 오프셋(m) — 스포너 마커·좀비 좌표의 층 가산에 쓴다. 단층은 0.</summary>
        /// <param name="floorIndex">층 서수.</param>
        /// <returns>Y 오프셋(m).</returns>
        public float FloorPlaneY(int floorIndex)
        {
            return FloorGeometry.FloorPlaneY(mapDefinition, floorIndex);
        }

        /// <summary>층별 바닥면 Y 오프셋 목록(베이커 슬래브 판정용) — 단층은 {0}.</summary>
        /// <returns>층 평면 Y 배열.</returns>
        public float[] FloorPlaneYs()
        {
            if (LocalBlueprint == null || mapDefinition.Floors.Length <= 1)
            {
                return new[] { 0f };
            }

            var planes = new float[LocalBlueprint.Floors.Count];
            for (int f = 0; f < LocalBlueprint.Floors.Count; f++)
            {
                planes[f] = FloorGeometry.FloorPlaneY(mapDefinition, LocalBlueprint.Floors[f].FloorIndex);
            }

            return planes;
        }

        /// <summary>계획을 조립한다 — 빈 집 정의 단일 경로(M10-1). 단층·다층 모두 MapPlanBuilder 가 처리한다.</summary>
        /// <param name="snapshot">시드 확정 파라미터 스냅샷.</param>
        /// <param name="flatAssets">평탄화 템플릿 SO(출력) — 실패 시 null.</param>
        /// <returns>생성 계획 — 린트 실패 시 null(조립 거부, R4).</returns>
        private MapGenPlan BuildPlan(MapGenParams snapshot, out RoomTemplateSO[] flatAssets)
        {
            return MapPlanBuilder.Build(mapDefinition, snapshot, out flatAssets);
        }

        /// <summary>
        /// 서버 전용 진입점 — 게임 씬 진입 시퀀스(세션 관리자)가 호출한다.
        /// Seed 0 이면 실제 시드를 확정해 로그에 남기고(X8), MapGenerator 로 생성·검증 후 시드와 채택 리롤을 복제한다.
        /// 생성 실패(X2)는 에러 로그 후 중단 — 폴백 맵 없음.
        /// </summary>
        public void ServerStartMapFlow()
        {
            Log.D("[MapGenNetworkDriver] ServerStartMapFlow");
            if (!IsServer || mapSeed.Value != 0)
            {
                return; // 서버 전용 · 시드는 세션 중 불변(재실행 없음)
            }

            int confirmedSeed = mapDefinition.GenParams.Seed;
            while (confirmedSeed == 0)
            {
                confirmedSeed = Random.Range(int.MinValue, int.MaxValue); // 0 이 나오면 다시 — 0 은 "미확정" 표지라 시드로 못 쓴다(X8)
            }

            Log.D($"[MapGenNetworkDriver] 시드 확정 {confirmedSeed} (X8) — 재현 키");

            // 복제 전 서버 선검증 — 실패 시드는 뿌리지 않는다(X2: 폴백 맵 없음)
            MapGenPlan preflight = BuildPlan(SnapshotParams(confirmedSeed), out _);
            if (preflight == null)
            {
                return; // 층 스택 린트 실패 — 조립 거부(R4)
            }

            MapGenResult result = new MapGenerator().Generate(preflight);
            if (!result.Success)
            {
                Log.E($"[MapGenNetworkDriver] 생성 실패(X2) 시드={confirmedSeed} 리롤={result.RerollCount} — {string.Join(" / ", result.FailReasons)}");
                return;
            }

            mapAttempt.Value = result.RerollCount; // 시드보다 먼저 기록 — 변화 콜백이 어느 순서로 와도 TryAssembleConfirmedMap 가 둘 다 준비될 때만 진행한다
            mapSeed.Value = confirmedSeed;
        }

        /// <summary>생성 키 변화 구독 + 이미 확정된 키(늦은 합류) 즉시 처리.</summary>
        public override void OnNetworkSpawn()
        {
            Log.D("[MapGenNetworkDriver] OnNetworkSpawn");
            mapSeed.OnValueChanged += HandleSeedChanged;
            mapAttempt.OnValueChanged += HandleAttemptChanged;
            TryAssembleConfirmedMap(); // 이미 확정된 시드·리롤을 받은 늦은 합류 처리

            if (IsServer)
            {
                ServerStartMapFlow(); // 임시 자동 시작 — 게임 시작 시퀀스(세션 관리자) 훅 확정 시 이 줄만 제거(멱등이라 중복 호출 무해)
            }
        }

        /// <summary>생성 키 구독 해제.</summary>
        public override void OnNetworkDespawn()
        {
            Log.D("[MapGenNetworkDriver] OnNetworkDespawn");
            mapSeed.OnValueChanged -= HandleSeedChanged;
            mapAttempt.OnValueChanged -= HandleAttemptChanged;
        }

        /// <summary>
        /// 시드 확정 콜백 — 생성 키가 모두 준비되면 서버가 채택한 정확한 리롤 후보를 재생한다.
        /// </summary>
        /// <param name="previous">이전 시드(0 = 미확정).</param>
        /// <param name="current">확정 시드.</param>
        private void HandleSeedChanged(int previous, int current)
        {
            Log.D($"[MapGenNetworkDriver] HandleSeedChanged {previous}->{current}");
            TryAssembleConfirmedMap();
        }

        /// <summary>서버 채택 리롤 변화 콜백 — 시드와 리롤이 모두 준비된 순간 조립을 시도한다.</summary>
        /// <param name="previous">이전 리롤 인덱스.</param>
        /// <param name="current">현재 리롤 인덱스.</param>
        private void HandleAttemptChanged(int previous, int current)
        {
            Log.D($"[MapGenNetworkDriver] HandleAttemptChanged {previous}->{current}");
            TryAssembleConfirmedMap();
        }

        /// <summary>확정 시드와 서버 채택 리롤을 원자적 생성 키처럼 소비해 로컬 후보를 재생·조립한다.</summary>
        private void TryAssembleConfirmedMap()
        {
            int current = mapSeed.Value;
            int requiredAttempt = mapAttempt.Value;
            if (current == 0 || requiredAttempt < 0 || LocalBlueprint != null)
            {
                return; // 생성 키 미완성·중복 콜백 무시
            }

            // 템플릿 단일 출처 = 빈 집 정의 SO(M10-1) — 전 클라 같은 에셋 = 같은 계획(AC-02)
            MapGenPlan plan = BuildPlan(SnapshotParams(current), out RoomTemplateSO[] flatAssets);
            if (plan == null)
            {
                return; // 정의 린트 실패 — 조립 거부(R4)
            }

            MapGenResult result = new MapGenerator().GenerateAtAttempt(plan, requiredAttempt);
            if (!result.Success)
            {
                Log.E($"[MapGenNetworkDriver] 로컬 재생성 실패 시드={current} 리롤={requiredAttempt} — 서버와 생성기·계획 입력이 다르다: {string.Join(" / ", result.FailReasons)}");
                return;
            }

            LocalBlueprint = result.Blueprint;
            LocalTemplates = plan.FlatTemplates;
            LocalMapRoot = MapRuntimeAssembler.Assemble(LocalBlueprint, plan.FlatTemplates, mapDefinition, transform, null, flatAssets);
            localHash = BlueprintHash.Compute(LocalBlueprint);
            Log.D($"[MapGenNetworkDriver] 로컬 조립 완료 시드={current} 리롤={requiredAttempt} 해시={localHash:X8}");
            // 안내도 모델 발행(EH-62) — 클라 로컬. 서버 X7 집계와 무관하게 자기 맵이 준비되면 즉시
            onMapOverviewReady.RaiseEvent(MapOverviewModel.Build(LocalBlueprint, plan.FlatTemplates, mapDefinition, LocalMapRoot.transform.position));
            ReportAssembledServerRpc(current, requiredAttempt, localHash);
        }

        /// <summary>
        /// 클라 조립 완료 보고 수신(X7) — 서버가 접속 클라 전원의 보고와 해시 일치(AC-02)를 집계한다.
        /// 전원 완료 시 onMapAssembledServer 발화. 해시 불일치는 에러 로그(시드·양측 해시 — 버그 재현 키, AC-17).
        /// </summary>
        /// <param name="assembledSeed">보고자가 실제 재생한 시드.</param>
        /// <param name="assembledAttempt">보고자가 실제 재생한 서버 채택 리롤.</param>
        /// <param name="blueprintHash">보고자의 로컬 블루프린트 해시.</param>
        /// <param name="rpcParams">송신자 식별용 RPC 파라미터.</param>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ReportAssembledServerRpc(int assembledSeed, int assembledAttempt, uint blueprintHash, RpcParams rpcParams = default)
        {
            Log.D($"[MapGenNetworkDriver] ReportAssembledServerRpc 시드={assembledSeed} 리롤={assembledAttempt} 해시={blueprintHash:X8}");
            if (!IsServer)
            {
                return;
            }

            ulong senderClientId = rpcParams.Receive.SenderClientId;
            if (assembledSeed != mapSeed.Value || assembledAttempt != mapAttempt.Value)
            {
                Log.E($"[MapGenNetworkDriver] 생성 키 불일치 클라={senderClientId} 서버=({mapSeed.Value},{mapAttempt.Value}) 클라측=({assembledSeed},{assembledAttempt})");
                return;
            }

            reportedHashes[senderClientId] = blueprintHash;
            if (blueprintHash != localHash)
            {
                Log.E($"[MapGenNetworkDriver] 해시 불일치(AC-02) 시드={mapSeed.Value} 리롤={mapAttempt.Value} 클라={senderClientId} 서버={localHash:X8} 클라측={blueprintHash:X8} — 생성 로직·계획 입력 드리프트 의심");
                return; // 불일치 보고는 완료 집계에 넣지 않는다 — 어긋난 맵 위에서 스폰을 시작하지 않기 위해
            }

            if (assembledEventFired)
            {
                return; // 늦은 합류 보고 — 검증만 하고 재발화하지 않는다
            }

            // 전원 완료 판정 — 접속 클라 전원이 일치 해시를 보고했는가(X7)
            IReadOnlyList<ulong> connected = NetworkManager.ConnectedClientsIds;
            for (int i = 0; i < connected.Count; i++)
            {
                if (!reportedHashes.TryGetValue(connected[i], out uint reported) || reported != localHash)
                {
                    return;
                }
            }

            assembledEventFired = true;
            Log.D($"[MapGenNetworkDriver] 전 클라 조립 완료(X7) 시드={mapSeed.Value} 리롤={mapAttempt.Value} 해시={localHash:X8} — onMapAssembledServer 발화");
            onMapAssembledServer.RaiseEvent();
        }

        /// <summary>파라미터 에셋을 오염시키지 않는 스냅샷을 만들고 확정 시드를 박는다.</summary>
        /// <param name="seed">확정 시드(0 아님).</param>
        /// <returns>생성에 쓸 파라미터 복제본.</returns>
        private MapGenParams SnapshotParams(int seed)
        {
            MapGenParams snapshot = JsonUtility.FromJson<MapGenParams>(JsonUtility.ToJson(mapDefinition.GenParams));
            snapshot.Seed = seed;
            return snapshot;
        }
    }
}
