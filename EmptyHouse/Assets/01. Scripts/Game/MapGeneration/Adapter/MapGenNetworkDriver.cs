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
    /// 서버: 시드 확정(X8) → 생성·검증 → mapSeed 복제. 전원(서버 포함): 시드 수신 → 로컬 재생성·조립(MapRuntimeAssembler)
    /// → 해시 보고. 서버: 전 클라 보고 수집(X7) + 해시 일치 확인(AC-02) → onMapAssembledServer 발화
    /// (→ NavMesh 베이크 → 상태 오브젝트 스폰으로 이어진다).
    /// 늦은 합류자는 mapSeed 가 이미 확정값이라 OnNetworkSpawn 시점에 같은 경로로 조립한다.
    /// </summary>
    public sealed class MapGenNetworkDriver : NetworkBehaviour
    {
        [Header("Config")]
        [SerializeField] private MapPrefabRegistrySO prefabRegistry; // 프리팹 레지스트리(P5 라이트)
        [SerializeField] private MapGenParamsSO genParamsAsset; // 생성 파라미터 단일 출처(9절) — 에디터 프리뷰도 같은 에셋을 읽는다. Seed 0 = 랜덤(서버가 확정, X8)
        [SerializeField] private EmptyHouse.Environment.LightingProfileSO lightingProfile; // 조명 프로파일 — 조립기가 맵 루트의 조명 컬러에 넘긴다

        [Header("Event Channels")]
        [SerializeField] private VoidEventChannelSO onMapAssembledServer; // 서버 전용 발화 — 전 클라 조립 완료(X7). NavMesh 베이커가 구독

        private readonly NetworkVariable<int> mapSeed = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server); // 확정 시드(0 = 미확정) — 시드만 복제(정적 지오메트리 대역폭 0)

        private readonly Dictionary<ulong, uint> reportedHashes = new Dictionary<ulong, uint>(); // 서버 전용 — 클라 Id → 보고 해시(X7 집계)
        private uint localHash; // 로컬 조립 블루프린트 해시 — 서버에서는 보고 대조 기준(AC-02)
        private bool assembledEventFired; // onMapAssembledServer 1회 발화 가드

        public MapBlueprint LocalBlueprint { get; private set; } // 로컬 재생성 블루프린트 — 스포너·베이커·툴이 소비
        public GameObject LocalMapRoot { get; private set; } // 로컬 조립 맵 루트

        /// <summary>
        /// 서버 전용 진입점 — 게임 씬 진입 시퀀스(세션 관리자)가 호출한다.
        /// Seed 0 이면 실제 시드를 확정해 로그에 남기고(X8), MapGenerator 로 생성·검증 후 mapSeed 를 복제한다.
        /// 생성 실패(X2)는 에러 로그 후 중단 — 폴백 맵 없음.
        /// </summary>
        public void ServerStartMapFlow()
        {
            Log.D("[MapGenNetworkDriver] ServerStartMapFlow");
            if (!IsServer || mapSeed.Value != 0)
            {
                return; // 서버 전용 · 시드는 세션 중 불변(재실행 없음)
            }

            int confirmedSeed = genParamsAsset.Params.Seed;
            while (confirmedSeed == 0)
            {
                confirmedSeed = Random.Range(int.MinValue, int.MaxValue); // 0 이 나오면 다시 — 0 은 "미확정" 표지라 시드로 못 쓴다(X8)
            }

            Log.D($"[MapGenNetworkDriver] 시드 확정 {confirmedSeed} (X8) — 재현 키");

            // 복제 전 서버 선검증 — 실패 시드는 뿌리지 않는다(X2: 폴백 맵 없음)
            MapGenResult result = new MapGenerator().Generate(SnapshotParams(confirmedSeed), prefabRegistry.CreateTemplates());
            if (!result.Success)
            {
                Log.E($"[MapGenNetworkDriver] 생성 실패(X2) 시드={confirmedSeed} 리롤={result.RerollCount} — {string.Join(" / ", result.FailReasons)}");
                return;
            }

            mapSeed.Value = confirmedSeed; // 전원(서버 포함)이 HandleSeedChanged 로 로컬 재생성·조립한다
        }

        /// <summary>mapSeed 변화 구독 + 이미 확정된 시드(늦은 합류) 즉시 처리.</summary>
        public override void OnNetworkSpawn()
        {
            Log.D("[MapGenNetworkDriver] OnNetworkSpawn");
            mapSeed.OnValueChanged += HandleSeedChanged;
            if (mapSeed.Value != 0)
            {
                HandleSeedChanged(0, mapSeed.Value);
            }

            if (IsServer)
            {
                ServerStartMapFlow(); // 임시 자동 시작 — 게임 시작 시퀀스(세션 관리자) 훅 확정 시 이 줄만 제거(멱등이라 중복 호출 무해)
            }
        }

        /// <summary>mapSeed 구독 해제.</summary>
        public override void OnNetworkDespawn()
        {
            Log.D("[MapGenNetworkDriver] OnNetworkDespawn");
            mapSeed.OnValueChanged -= HandleSeedChanged;
        }

        /// <summary>
        /// 시드 확정 콜백 — 로컬에서 같은 파라미터·같은 카탈로그로 재생성(결정론)·조립하고,
        /// BlueprintHash 를 서버에 보고한다. 0 → 확정값 전이만 유효(재실행 없음 — 시드는 세션 중 불변).
        /// </summary>
        /// <param name="previous">이전 시드(0 = 미확정).</param>
        /// <param name="current">확정 시드.</param>
        private void HandleSeedChanged(int previous, int current)
        {
            Log.D($"[MapGenNetworkDriver] HandleSeedChanged {previous}->{current}");
            if (current == 0 || LocalBlueprint != null)
            {
                return; // 미확정 전이·중복 호출(스폰 즉시 처리 + OnValueChanged 이중 진입) 무시
            }

            List<RoomTemplateDef> templates = prefabRegistry.CreateTemplates(); // 템플릿 단일 출처 = 레지스트리 SO(M9-3) — 전 클라 같은 에셋 = 같은 목록(AC-02)
            MapGenResult result = new MapGenerator().Generate(SnapshotParams(current), templates);
            if (!result.Success)
            {
                // 서버 선검증을 통과한 시드가 여기서 실패하면 빌드/카탈로그 불일치다 — AC-02 위반으로 표면화
                Log.E($"[MapGenNetworkDriver] 로컬 재생성 실패 시드={current} — 서버와 빌드·카탈로그가 다르다: {string.Join(" / ", result.FailReasons)}");
                return;
            }

            LocalBlueprint = result.Blueprint;
            LocalMapRoot = MapRuntimeAssembler.Assemble(LocalBlueprint, templates, prefabRegistry, transform, lightingProfile);
            localHash = BlueprintHash.Compute(LocalBlueprint);
            Log.D($"[MapGenNetworkDriver] 로컬 조립 완료 시드={current} 해시={localHash:X8} 리롤={result.RerollCount}");
            ReportAssembledServerRpc(localHash);
        }

        /// <summary>
        /// 클라 조립 완료 보고 수신(X7) — 서버가 접속 클라 전원의 보고와 해시 일치(AC-02)를 집계한다.
        /// 전원 완료 시 onMapAssembledServer 발화. 해시 불일치는 에러 로그(시드·양측 해시 — 버그 재현 키, AC-17).
        /// </summary>
        /// <param name="blueprintHash">보고자의 로컬 블루프린트 해시.</param>
        /// <param name="rpcParams">송신자 식별용 RPC 파라미터.</param>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ReportAssembledServerRpc(uint blueprintHash, RpcParams rpcParams = default)
        {
            Log.D($"[MapGenNetworkDriver] ReportAssembledServerRpc hash={blueprintHash:X8}");
            if (!IsServer)
            {
                return;
            }

            ulong senderClientId = rpcParams.Receive.SenderClientId;
            reportedHashes[senderClientId] = blueprintHash;
            if (blueprintHash != localHash)
            {
                Log.E($"[MapGenNetworkDriver] 해시 불일치(AC-02) 시드={mapSeed.Value} 클라={senderClientId} 서버={localHash:X8} 클라측={blueprintHash:X8} — 빌드·카탈로그 드리프트 의심");
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
            Log.D($"[MapGenNetworkDriver] 전 클라 조립 완료(X7) 시드={mapSeed.Value} 해시={localHash:X8} — onMapAssembledServer 발화");
            onMapAssembledServer.RaiseEvent();
        }

        /// <summary>파라미터 에셋을 오염시키지 않는 스냅샷을 만들고 확정 시드를 박는다.</summary>
        /// <param name="seed">확정 시드(0 아님).</param>
        /// <returns>생성에 쓸 파라미터 복제본.</returns>
        private MapGenParams SnapshotParams(int seed)
        {
            MapGenParams snapshot = JsonUtility.FromJson<MapGenParams>(JsonUtility.ToJson(genParamsAsset.Params));
            snapshot.Seed = seed;
            return snapshot;
        }
    }
}
