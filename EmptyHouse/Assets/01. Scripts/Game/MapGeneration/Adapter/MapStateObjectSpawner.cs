using System.Collections.Generic;
using Border.Core;
using Border.Events;
using EmptyHouse.MapGen.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace EmptyHouse.MapGen.Runtime
{
    /// <summary>
    /// 상태 오브젝트 서버 스폰(1절 — 문·자물쇠·열쇠·백신·기름·스크랩·투척물·설비·좀비는 서버만 스폰, AC-19).
    /// 시퀀스: onMapNavMeshReadyServer(조립 X7 + 베이크 완료) → 문 → 아이템·설비 → 좀비 순서로 스폰한다.
    /// 배치 좌표는 블루프린트 간선·마커(CellMath)에서 계산 — 클라 로컬 조립 지오메트리와 결정론으로 정합(AC-02·AC-15).
    /// 좀비 타입 게이트(EnabledZombieTypes)는 생성 단계에서 반영되어 꺼진 타입은 스폰 목록에 없다.
    /// </summary>
    public sealed class MapStateObjectSpawner : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private MapGenNetworkDriver driver; // 블루프린트·맵 루트 원천
        [SerializeField] private MapPrefabRegistrySO prefabRegistry; // 상태 오브젝트 프리팹 레지스트리

        [Header("Event Channels")]
        [SerializeField] private VoidEventChannelSO onMapNavMeshReadyServer; // 구독 — 발화 시 서버 스폰 개시

        [Header("Zombie Placement")]
        [SerializeField] private float zombieClearanceRadius = 0.45f; // 좀비 스폰 여유 반경(m) — 프랍·벽·먼저 스폰된 좀비와의 최소 간격
        [SerializeField] private float zombieSearchRadius = 3f; // 마커에서 빈 자리를 찾는 최대 거리(m)
        [SerializeField] private LayerMask zombieBlockMask = ~0; // 스폰 차단 판정 레이어(트리거는 항상 무시)

        private const float itemGroundClearance = 0.05f; // 아이템 스폰 바닥 겹침 방지 여유(m)
        private const float clearanceProbeHeight = 1f; // 여유 검사 구 중심 높이(m) — 바닥은 걸리지 않고 프랍·벽만 잡히는 높이

        private readonly HashSet<SpawnKind> missingPrefabWarned = new HashSet<SpawnKind>(); // 미등록 종류 경고 1회 가드
        private readonly Dictionary<int, List<MapItemAnchor>> anchorsByRoom = new Dictionary<int, List<MapItemAnchor>>(); // 방 인덱스 → 아이템 앵커(맵 조립본에서 수집)
        private readonly HashSet<int> anchorMissWarned = new HashSet<int>(); // 앵커 부족 경고 1회 가드(방 단위)
        private readonly List<NetworkObject> variantBuffer = new List<NetworkObject>(); // 변종 후보 재사용 버퍼(스폰마다 호출 — GC 절감)

        /// <summary>onMapNavMeshReadyServer 구독.</summary>
        private void OnEnable()
        {
            Log.D("[MapStateObjectSpawner] OnEnable");
            onMapNavMeshReadyServer.OnEventRaised += HandleNavMeshReady;
        }

        /// <summary>구독 해제.</summary>
        private void OnDisable()
        {
            Log.D("[MapStateObjectSpawner] OnDisable");
            onMapNavMeshReadyServer.OnEventRaised -= HandleNavMeshReady;
        }

        /// <summary>베이크 완료 수신 — 서버 가드 후 문 → 아이템·설비 → 좀비 순서로 전 상태 오브젝트를 스폰한다.</summary>
        private void HandleNavMeshReady()
        {
            Log.D("[MapStateObjectSpawner] HandleNavMeshReady");
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return; // 상태 오브젝트는 서버만 스폰(AC-19)
            }

            MapBlueprint blueprint = driver.LocalBlueprint;
            IReadOnlyList<RoomTemplateDef> templates = driver.LocalTemplates; // 드라이버 생성과 같은 평탄화 목록(M9-8) — 다층은 접미사 ID 라 레지스트리 재추출로 대체 불가
            CollectItemAnchors(blueprint);
            SpawnDoors(blueprint, templates);
            SpawnReturnExits(blueprint);
            SpawnItems(blueprint, templates);
            SpawnZombies(blueprint, templates);
        }

        /// <summary>
        /// 탈출문(ReturnExit 간선)마다 Door-Return 프리팹을 스폰한다 — 조립기가 남긴 ReturnAnchor 기준,
        /// 문틀 정렬은 일반 문과 동일. 문은 열리지 않고 홀드 즉시 탈출이라 잠금·자물쇠 개념이 없다.
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        private void SpawnReturnExits(MapBlueprint blueprint)
        {
            Log.D("[MapStateObjectSpawner] SpawnReturnExits");
            if (prefabRegistry.ReturnExitPrefab == null)
            {
                Log.W("[MapStateObjectSpawner] 탈출문 프리팹 미등록(ReturnExitPrefab) — 탈출구 없음, 세션 종료 불가");
                return;
            }

            Transform doorsRoot = driver.LocalMapRoot.transform.Find("Doors");
            int spawned = 0;
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                if (blueprint.Edges[e].State != EdgeState.ReturnExit)
                {
                    continue;
                }

                Transform anchor = doorsRoot.Find($"ReturnAnchor_e{e}");
                if (anchor == null)
                {
                    Log.W($"[MapStateObjectSpawner] 간선 e{e} 탈출문 앵커 없음 — 스폰 생략");
                    continue;
                }

                NetworkObject exitDoor = Object.Instantiate(prefabRegistry.ReturnExitPrefab, anchor.position, anchor.rotation);
                Bounds frame = MapRuntimeAssembler.FrameBounds(exitDoor.gameObject);
                exitDoor.transform.position += new Vector3(anchor.position.x - frame.center.x, 0f, anchor.position.z - frame.center.z);
                exitDoor.Spawn();
                spawned++;
            }

            Log.D($"[MapStateObjectSpawner] 탈출문 스폰 {spawned}건");
        }

        /// <summary>
        /// DoorOpen·DoorLocked 간선마다 문 프리팹을 스폰하고 ServerConfigure(pairId = LockNumber, locked)를
        /// 주입한다(M7 요구 2). 위치·방향은 간선 소켓 셀에서 계산 — 클라 개구부와 대면 정합.
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="templates">템플릿 목록.</param>
        private void SpawnDoors(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates)
        {
            Log.D("[MapStateObjectSpawner] SpawnDoors");
            Transform doorsRoot = driver.LocalMapRoot.transform.Find("Doors");
            int spawned = 0;
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                BlueprintEdge edge = blueprint.Edges[e];
                if (edge.RoomB < 0 || (edge.State != EdgeState.DoorOpen && edge.State != EdgeState.DoorLocked))
                {
                    continue;
                }

                // 조립기가 남긴 앵커 = 개구 실측 보정이 끝난 문 기준점 — 전 클라 동일 위치(AC-02)
                Transform anchor = doorsRoot.Find($"DoorAnchor_e{e}");
                if (anchor == null)
                {
                    Log.W($"[MapStateObjectSpawner] 간선 e{e} 문 앵커 없음(개구 기하 불일치로 생략된 간선) — 문 스폰 생략");
                    continue;
                }

                NetworkObject door = Object.Instantiate(prefabRegistry.DoorPrefab, anchor.position, anchor.rotation);

                // 문틀 기준 정렬 — 프리팹 피벗이 문틀 중심과 어긋나 있어, 문짝(Hall_Door_L/R) 제외 문틀 실측
                // 바운드 중심을 앵커 XZ 에 맞춘다(에디터 빌더 동일 규칙). Spawn() 전에 정렬해야 초기 복제 포즈에 반영된다
                Bounds frame = MapRuntimeAssembler.FrameBounds(door.gameObject);
                door.transform.position += new Vector3(anchor.position.x - frame.center.x, 0f, anchor.position.z - frame.center.z);
                door.Spawn();
                DoorInteractable doorRoot = door.GetComponent<DoorInteractable>();
                bool locked = edge.State == EdgeState.DoorLocked;
                doorRoot.ServerConfigure(edge.LockNumber, locked);
                if (locked)
                {
                    SpawnLock(doorRoot, edge.LockNumber);
                }

                spawned++;
            }

            Log.D($"[MapStateObjectSpawner] 문 스폰 {spawned}건");
        }

        /// <summary>
        /// 조립본에서 방별 아이템 앵커(MapItemAnchor)를 수집한다 — 방 인스턴스 이름 Room_{인덱스}_{템플릿} 이 연결고리.
        /// 앵커는 방 프리팹 소속이라 변형(데코) 프리팹마다 다르며, 없으면 그 방은 바닥 폴백으로 떨어진다.
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        private void CollectItemAnchors(MapBlueprint blueprint)
        {
            Log.D("[MapStateObjectSpawner] CollectItemAnchors");
            anchorsByRoom.Clear();
            anchorMissWarned.Clear();

            Transform mapRoot = driver.LocalMapRoot.transform;
            int total = 0;
            for (int i = 0; i < mapRoot.childCount; i++)
            {
                Transform child = mapRoot.GetChild(i);
                if (!child.name.StartsWith("Room_"))
                {
                    continue;
                }

                string[] tokens = child.name.Split('_');
                if (tokens.Length < 2 || !int.TryParse(tokens[1], out int roomIndex))
                {
                    continue;
                }

                MapItemAnchor[] anchors = child.GetComponentsInChildren<MapItemAnchor>(false);
                if (anchors.Length == 0)
                {
                    continue;
                }

                anchorsByRoom[roomIndex] = new List<MapItemAnchor>(anchors);
                total += anchors.Length;
            }

            Log.D($"[MapStateObjectSpawner] 아이템 앵커 {total}개 / 앵커 보유 방 {anchorsByRoom.Count}개");
        }

        /// <summary>
        /// 스폰이 놓일 앵커를 고른다 — 그 방의 미점유·마스크 허용 앵커 중 시드 결정적 선택 후 점유 표시.
        /// 후보가 없으면 null(호출자가 바닥 폴백) + 방당 1회 경고 — 어느 프리팹에 앵커를 보강할지 로그로 드러난다.
        /// </summary>
        /// <param name="spawn">배치할 스폰.</param>
        /// <param name="spawnIndex">스폰 목록 인덱스(선택 해시 키).</param>
        /// <returns>선택된 앵커. 없으면 null.</returns>
        private MapItemAnchor TakeAnchor(BlueprintSpawn spawn, int spawnIndex)
        {
            if (!anchorsByRoom.TryGetValue(spawn.RoomIndex, out List<MapItemAnchor> anchors))
            {
                WarnAnchorMiss(spawn);
                return null;
            }

            var candidates = new List<MapItemAnchor>();
            for (int i = 0; i < anchors.Count; i++)
            {
                if (anchors[i].Accepts(spawn.Kind))
                {
                    candidates.Add(anchors[i]);
                }
            }

            if (candidates.Count == 0)
            {
                WarnAnchorMiss(spawn);
                return null;
            }

            // 시드·스폰 인덱스 해시로 고정 선택 — 같은 시드면 같은 앵커(재현성)
            uint hash = (uint)(driver.LocalBlueprint.Meta.Seed * 486187739 + spawnIndex * 31 + (int)spawn.Kind);
            MapItemAnchor picked = candidates[(int)(hash % (uint)candidates.Count)];
            picked.MarkOccupied();
            return picked;
        }

        /// <summary>앵커 부족 경고 — 방당 1회만 남긴다(같은 방에 여러 스폰이 몰려도 로그가 도배되지 않게).</summary>
        /// <param name="spawn">폴백된 스폰.</param>
        private void WarnAnchorMiss(BlueprintSpawn spawn)
        {
            if (anchorMissWarned.Add(spawn.RoomIndex))
            {
                Log.W($"[MapStateObjectSpawner] 방 {spawn.RoomIndex}: {spawn.Kind} 를 받을 MapItemAnchor 부족 — 마커 셀 바닥으로 폴백(방 프리팹에 앵커 보강 필요)");
            }
        }

        /// <summary>
        /// 좀비 스폰 지점을 보행 가능한 빈 자리로 해결한다 — NavMesh 스냅 + 여유 검사(프랍·벽·먼저 스폰된 좀비).
        /// 마커 지점부터 링 탐색(고정 각도·반경 순)으로 결정적으로 훑고, 전부 막히면 NavMesh 스냅 지점 + 경고.
        /// </summary>
        /// <param name="desired">마커가 지시한 원 지점.</param>
        /// <param name="roomIndex">로그용 방 인덱스.</param>
        /// <returns>스폰할 월드 좌표.</returns>
        private Vector3 ResolveWalkablePosition(Vector3 desired, int roomIndex)
        {
            if (IsFreeWalkable(desired, out Vector3 direct))
            {
                return direct;
            }

            for (float radius = zombieSearchRadius / 4f; radius <= zombieSearchRadius + 0.01f; radius += zombieSearchRadius / 4f)
            {
                for (int step = 0; step < 8; step++)
                {
                    float angle = step * 45f * Mathf.Deg2Rad;
                    var candidate = new Vector3(desired.x + Mathf.Cos(angle) * radius, desired.y, desired.z + Mathf.Sin(angle) * radius);
                    if (IsFreeWalkable(candidate, out Vector3 resolved))
                    {
                        return resolved;
                    }
                }
            }

            Log.W($"[MapStateObjectSpawner] 방 {roomIndex}: 좀비 스폰 빈 자리 탐색 실패(반경 {zombieSearchRadius}m) — NavMesh 최근접 지점 사용");
            return NavMesh.SamplePosition(desired, out NavMeshHit fallback, zombieSearchRadius, NavMesh.AllAreas) ? fallback.position : desired;
        }

        /// <summary>후보 지점이 보행 가능하고(NavMesh) 프랍·벽·다른 오브젝트와 겹치지 않는지 검사한다.</summary>
        /// <param name="candidate">검사할 지점.</param>
        /// <param name="resolved">NavMesh 로 스냅된 실제 지점.</param>
        /// <returns>스폰 가능 여부.</returns>
        private bool IsFreeWalkable(Vector3 candidate, out Vector3 resolved)
        {
            resolved = candidate;
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
            {
                return false; // 보행 불가(책장 속·벽 안·바닥 없음)
            }

            resolved = hit.position;
            return !Physics.CheckSphere(resolved + Vector3.up * clearanceProbeHeight, zombieClearanceRadius, zombieBlockMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 아이템·설비 스폰 — 열쇠(pairId = 자물쇠 번호 주입)·백신·기름·스크랩·투척물·사체 충전소·발전기를
        /// 방 프리팹의 아이템 앵커(MapItemAnchor)에 스폰한다. 앵커가 없으면 마커 셀 바닥 폴백 + 경고.
        /// 종류별 프리팹 미등록은 스폰 생략 + 경고(개발 중 부분 등록 허용).
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="templates">템플릿 목록.</param>
        private void SpawnItems(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates)
        {
            Log.D("[MapStateObjectSpawner] SpawnItems");
            int spawned = 0;
            for (int s = 0; s < blueprint.Spawns.Count; s++)
            {
                BlueprintSpawn spawn = blueprint.Spawns[s];
                if (IsZombie(spawn.Kind) || spawn.Kind == SpawnKind.HerdArea)
                {
                    continue; // 좀비는 4단계(SpawnZombies), HerdArea 는 구역 표지라 오브젝트가 아니다
                }

                // 열쇠는 번호별 외형이 곧 자물쇠와의 짝이라 공용 프리팹이 없다 — 페어 목록에서만 온다
                NetworkObject prefab = spawn.Kind == SpawnKind.Key
                    ? PairPrefab(spawn.KeyNumber, true)
                    : FindSpawnPrefab(spawn.Kind, s);
                if (prefab == null)
                {
                    if (spawn.Kind == SpawnKind.Key && missingPrefabWarned.Add(SpawnKind.Key))
                    {
                        Log.W($"[MapStateObjectSpawner] 열쇠_{spawn.KeyNumber} 미등재 — 그 자물쇠를 열 수단이 없다. 레지스트리 페어 목록 등재 필요");
                    }

                    continue;
                }

                // 배치 지점은 방 프리팹의 아이템 앵커가 결정한다 — 앵커가 없거나 전부 점유면 마커 셀 바닥 폴백(+경고)
                Vector3 position;
                Quaternion rotation;
                bool bottomAlign;
                MapItemAnchor anchor = TakeAnchor(spawn, s);
                if (anchor != null)
                {
                    position = anchor.transform.position;
                    rotation = anchor.transform.rotation;
                    bottomAlign = anchor.BottomAlign;
                }
                else if (spawn.MarkerId < 0)
                {
                    continue; // 마커 없는 종류(벽장)는 앵커 전용 — 바닥 폴백을 두면 프랍 속에 박힌다(경고는 TakeAnchor 가 남긴다)
                }
                else
                {
                    position = MarkerWorldPosition(blueprint, templates, spawn) + Vector3.up * itemGroundClearance;
                    rotation = Quaternion.identity;
                    bottomAlign = true;
                }

                NetworkObject instance = Object.Instantiate(prefab, position, rotation);
                if (bottomAlign)
                {
                    AlignBottomTo(instance.transform, position.y);
                }

                instance.Spawn();
                if (spawn.Kind == SpawnKind.Key)
                {
                    instance.GetComponentInChildren<ItemPickupInteractable>().ServerSetPairId(spawn.KeyNumber); // 열쇠_XX ↔ 자물쇠_XX(M7 요구 2) — 변종에도 주입해 인스펙터 값 불일치를 차단
                }

                spawned++;
            }

            Log.D($"[MapStateObjectSpawner] 아이템·설비 스폰 {spawned}건");
        }

        /// <summary>
        /// 좀비 스폰(단계 ④) — 마커 좌표에 타입별 프리팹을 스폰하고, 배회 원점(스폰 지점)·반경
        /// (WanderRadiusCells × CellMeters)을 서버 주입한다. NavMesh 베이크 완료가 전제(AC-20).
        /// HerdArea 방(위장 무대)의 Walker 도 배회를 따로 끄지 않는다 — 마커 반경대로 움직인다.
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="templates">템플릿 목록.</param>
        private void SpawnZombies(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates)
        {
            Log.D("[MapStateObjectSpawner] SpawnZombies");

            // 위장 무대 방 집합 — 이 방의 Walker 는 무리(배회 OFF)
            var herdRooms = new HashSet<int>();
            for (int s = 0; s < blueprint.Spawns.Count; s++)
            {
                if (blueprint.Spawns[s].Kind == SpawnKind.HerdArea)
                {
                    herdRooms.Add(blueprint.Spawns[s].RoomIndex);
                }
            }

            int spawned = 0;
            for (int s = 0; s < blueprint.Spawns.Count; s++)
            {
                BlueprintSpawn spawn = blueprint.Spawns[s];
                if (!IsZombie(spawn.Kind))
                {
                    continue;
                }

                NetworkObject prefab = FindSpawnPrefab(spawn.Kind, s);
                if (prefab == null)
                {
                    continue;
                }

                // 마커는 템플릿 셀 좌표라 방 변형(책장·가구)을 모른다 — 베이크된 NavMesh + 여유 검사로 빈 자리를 찾는다
                Vector3 position = ResolveWalkablePosition(MarkerWorldPosition(blueprint, templates, spawn), spawn.RoomIndex);
                NetworkObject instance = Object.Instantiate(prefab, position, Quaternion.identity);
                instance.Spawn();

                ZombieController zombie = instance.GetComponentInChildren<ZombieController>();
                zombie.ServerConfigureSpawn(position, spawn.WanderRadiusCells * prefabRegistry.CellMeters);
                spawned++;
            }

            Log.D($"[MapStateObjectSpawner] 좀비 스폰 {spawned}건(무리 방 {herdRooms.Count})");
        }

        /// <summary>
        /// 잠긴 문의 LockPos 에 번호별 자물쇠 변종을 스폰하고 문↔자물쇠를 상호 연결한다(M7 요구 1·2).
        /// 변종 미등재면 경고 — 문은 잠긴 채 자물쇠가 없어 해정 불가(레지스트리 등재 필요).
        /// </summary>
        /// <param name="door">잠긴 문 루트.</param>
        /// <param name="lockNumber">자물쇠 번호(1부터).</param>
        private void SpawnLock(DoorInteractable door, int lockNumber)
        {
            NetworkObject prefab = PairPrefab(lockNumber, false);
            if (prefab == null)
            {
                Log.W($"[MapStateObjectSpawner] 자물쇠_{lockNumber} 변종 미등재 — 잠긴 문에 자물쇠 미부착(해정 불가). 레지스트리 페어 목록 등재 필요");
                return;
            }

            Transform lockPos = door.LockPos;
            NetworkObject lockObject = Object.Instantiate(prefab, lockPos.position, lockPos.rotation);
            lockObject.Spawn();
            lockObject.GetComponentInChildren<DoorLockFace>().ServerAttachDoor(door);
            door.ServerAttachLock(lockObject);
        }

        /// <summary>
        /// 항목의 변종 풀에서 하나를 고른다 — 풀이 비었거나 전부 미할당이면 기본 프리팹.
        /// 선택 키는 앵커 선택과 같은 (시드, 스폰 인덱스, 종류) 해시라 같은 시드면 같은 외형이 나온다.
        /// </summary>
        /// <param name="entry">스폰 종류 항목.</param>
        /// <param name="kind">스폰 종류(해시 키).</param>
        /// <param name="spawnIndex">스폰 목록 인덱스(해시 키).</param>
        /// <returns>선택된 프리팹 — 기본·변종 모두 없으면 null.</returns>
        private NetworkObject PickVariant(SpawnPrefabEntry entry, SpawnKind kind, int spawnIndex)
        {
            variantBuffer.Clear();
            for (int i = 0; entry.Variants != null && i < entry.Variants.Length; i++)
            {
                if (entry.Variants[i] != null)
                {
                    variantBuffer.Add(entry.Variants[i]);
                }
            }

            if (variantBuffer.Count == 0)
            {
                return entry.Prefab;
            }

            uint hash = (uint)(driver.LocalBlueprint.Meta.Seed * 486187739 + spawnIndex * 31 + (int)kind);
            return variantBuffer[(int)(hash % (uint)variantBuffer.Count)];
        }

        /// <summary>페어 번호의 열쇠·자물쇠 프리팹을 찾는다(인덱스 + 1 = 페어 번호) — 범위 밖·미등재면 null.</summary>
        /// <param name="pairNumber">페어 번호(1부터).</param>
        /// <param name="wantKey">true = 열쇠, false = 자물쇠.</param>
        /// <returns>페어 프리팹 — 없으면 null.</returns>
        private NetworkObject PairPrefab(int pairNumber, bool wantKey)
        {
            int index = pairNumber - 1;
            if (prefabRegistry.PairPrefabs == null || index < 0 || index >= prefabRegistry.PairPrefabs.Length)
            {
                return null;
            }

            PairPrefabEntry entry = prefabRegistry.PairPrefabs[index];
            return wantKey ? entry.Key : entry.Lock;
        }

        /// <summary>
        /// 오브젝트 밑면을 기준 높이에 맞춘다 — 렌더러(없으면 콜라이더) 합성 바운즈 하단이 기준선에 닿게 올린다.
        /// 피벗이 중심인 프리팹을 앵커 지점에 그대로 놓으면 절반이 바닥·선반에 묻히는 것을 막는다.
        /// 회전 적용 후의 월드 바운즈를 쓰므로 눕힌 오브젝트도 정확하다. 바운즈가 없으면 원위치 유지.
        /// </summary>
        /// <param name="instance">배치된 인스턴스 루트.</param>
        /// <param name="baseY">밑면이 닿아야 할 월드 Y.</param>
        private static void AlignBottomTo(Transform instance, float baseY)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = default;
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] is ParticleSystemRenderer)
                {
                    continue; // 파티클은 외형 경계가 아니다 — 바운즈를 엉뚱하게 늘린다
                }

                if (!found)
                {
                    bounds = renderers[i].bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            if (!found)
            {
                var colliders = instance.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; i++)
                {
                    if (colliders[i].isTrigger)
                    {
                        continue; // 상호작용 트리거는 실제 외형보다 크다
                    }

                    if (!found)
                    {
                        bounds = colliders[i].bounds;
                        found = true;
                    }
                    else
                    {
                        bounds.Encapsulate(colliders[i].bounds);
                    }
                }
            }

            if (!found)
            {
                return;
            }

            instance.position += Vector3.up * (baseY - bounds.min.y);
        }

        /// <summary>스폰 종류가 좀비인지 판정한다.</summary>
        /// <param name="kind">스폰 종류.</param>
        /// <returns>좀비 여부.</returns>
        private static bool IsZombie(SpawnKind kind)
        {
            return kind == SpawnKind.ZombieWalker || kind == SpawnKind.ZombieListener || kind == SpawnKind.ZombieWatcher;
        }

        /// <summary>
        /// 레지스트리에서 종류별 스폰 프리팹을 찾는다 — 변종 풀이 있으면 시드 결정론으로 하나 고르고,
        /// 비어 있으면 기본 프리팹을 쓴다. 미등록이면 종류당 1회 경고 후 null.
        /// 선택은 서버에서만 하고 결과가 스폰으로 복제되므로 클라 정합 문제는 없다.
        /// </summary>
        /// <param name="kind">스폰 종류.</param>
        /// <param name="spawnIndex">스폰 목록 인덱스(선택 해시 키 — 같은 방의 여러 개가 같은 외형으로 몰리지 않게).</param>
        /// <returns>스폰 프리팹 — 미등록이면 null.</returns>
        private NetworkObject FindSpawnPrefab(SpawnKind kind, int spawnIndex)
        {
            for (int i = 0; i < prefabRegistry.SpawnPrefabs.Length; i++)
            {
                SpawnPrefabEntry entry = prefabRegistry.SpawnPrefabs[i];
                if (entry.Kind != kind)
                {
                    continue;
                }

                NetworkObject picked = PickVariant(entry, kind, spawnIndex);
                if (picked != null)
                {
                    return picked;
                }
            }

            if (missingPrefabWarned.Add(kind))
            {
                Log.W($"[MapStateObjectSpawner] {kind} 프리팹 미등록 — 해당 스폰 생략(레지스트리 SpawnPrefabs 등재 필요)");
            }

            return null;
        }

        /// <summary>스폰 마커의 월드 좌표(마커 전역 셀 중심, 바닥 높이) — 조립기와 같은 정규화·셀 실측을 쓴다.</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="templates">템플릿 목록.</param>
        /// <param name="spawn">대상 스폰.</param>
        /// <returns>마커 셀 중심 월드 좌표.</returns>
        private Vector3 MarkerWorldPosition(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, BlueprintSpawn spawn)
        {
            RoomTemplateDef template = MapRuntimeAssembler.FindTemplate(templates, blueprint.Rooms[spawn.RoomIndex].TemplateId);
            MarkerDef marker = FindMarker(template, spawn.MarkerId);
            CellCoord worldCell = CellMath.WorldCell(blueprint.Rooms[spawn.RoomIndex], template, marker.LocalCell);
            (int minX, int minY) = MapRuntimeAssembler.MinCellBounds(blueprint);

            float cell = prefabRegistry.CellMeters;
            Vector3 mapOrigin = driver.LocalMapRoot.transform.position;
            float floorY = driver.FloorPlaneY(blueprint.Rooms[spawn.RoomIndex].FloorIndex); // 층 바닥면 가산(M9-8)
            return mapOrigin + new Vector3((worldCell.X - minX + 0.5f) * cell, floorY, (worldCell.Y - minY + 0.5f) * cell);
        }

        /// <summary>템플릿에서 마커 Id 로 마커를 찾는다.</summary>
        /// <param name="template">대상 템플릿.</param>
        /// <param name="markerId">마커 Id.</param>
        /// <returns>마커 정의.</returns>
        private static MarkerDef FindMarker(RoomTemplateDef template, int markerId)
        {
            for (int m = 0; m < template.Markers.Length; m++)
            {
                if (template.Markers[m].Id == markerId)
                {
                    return template.Markers[m];
                }
            }

            return null;
        }
    }
}
