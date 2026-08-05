using System.Collections.Generic;
using Border.Core;
using Border.Events;
using EmptyHouse.MapGen.Core;
using Unity.Netcode;
using UnityEngine;

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

        private const float itemGroundClearance = 0.05f; // 아이템 스폰 바닥 겹침 방지 여유(m)

        private readonly HashSet<SpawnKind> missingPrefabWarned = new HashSet<SpawnKind>(); // 미등록 종류 경고 1회 가드

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
            List<RoomTemplateDef> templates = MapTemplateCatalog.Create();
            SpawnDoors(blueprint, templates);
            SpawnItems(blueprint, templates);
            SpawnZombies(blueprint, templates);
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
                door.Spawn();
                door.GetComponent<DoorInteractable>().ServerConfigure(edge.LockNumber, edge.State == EdgeState.DoorLocked);
                spawned++;
            }

            Log.D($"[MapStateObjectSpawner] 문 스폰 {spawned}건");
        }

        /// <summary>
        /// 아이템·설비 스폰 — 열쇠(pairId = 자물쇠 번호 주입)·백신·기름·스크랩·투척물·사체 충전소·발전기를
        /// 마커 좌표에 스폰한다. 종류별 프리팹 미등록은 스폰 생략 + 경고(개발 중 부분 등록 허용).
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

                NetworkObject prefab = FindSpawnPrefab(spawn.Kind);
                if (prefab == null)
                {
                    continue;
                }

                if (spawn.Kind == SpawnKind.Key)
                {
                    NetworkObject variant = KeyVariantPrefab(spawn.KeyNumber);
                    if (variant != null)
                    {
                        prefab = variant; // 번호별 비주얼 변종 우선 — 없으면 공용 Key 프리팹
                    }
                }

                Vector3 position = MarkerWorldPosition(blueprint, templates, spawn) + Vector3.up * itemGroundClearance;
                NetworkObject instance = Object.Instantiate(prefab, position, Quaternion.identity);
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
        /// HerdArea 방(위장 무대)의 Walker 는 무리라 배회를 끈다(ZombieCrowd 수동 배치와 동일 효과 — 제자리 대기).
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

                NetworkObject prefab = FindSpawnPrefab(spawn.Kind);
                if (prefab == null)
                {
                    continue;
                }

                Vector3 position = MarkerWorldPosition(blueprint, templates, spawn);
                NetworkObject instance = Object.Instantiate(prefab, position, Quaternion.identity);
                instance.Spawn();

                ZombieController zombie = instance.GetComponentInChildren<ZombieController>();
                zombie.ServerConfigureSpawn(position, spawn.WanderRadiusCells * prefabRegistry.CellMeters);
                if (spawn.Kind == SpawnKind.ZombieWalker && herdRooms.Contains(spawn.RoomIndex))
                {
                    zombie.SetWanderEnabled(false); // 위장 무대 무리 — 제자리 대기(레벨디자인 4절)
                }

                spawned++;
            }

            Log.D($"[MapStateObjectSpawner] 좀비 스폰 {spawned}건(무리 방 {herdRooms.Count})");
        }

        /// <summary>번호별 열쇠 변종 프리팹을 찾는다(인덱스 + 1 = 페어 번호) — 범위 밖·미등재면 null(공용 폴백).</summary>
        /// <param name="keyNumber">열쇠 번호(1부터).</param>
        /// <returns>변종 프리팹 — 없으면 null.</returns>
        private NetworkObject KeyVariantPrefab(int keyNumber)
        {
            int index = keyNumber - 1;
            if (prefabRegistry.KeyPrefabs == null || index < 0 || index >= prefabRegistry.KeyPrefabs.Length)
            {
                return null;
            }

            return prefabRegistry.KeyPrefabs[index];
        }

        /// <summary>스폰 종류가 좀비인지 판정한다.</summary>
        /// <param name="kind">스폰 종류.</param>
        /// <returns>좀비 여부.</returns>
        private static bool IsZombie(SpawnKind kind)
        {
            return kind == SpawnKind.ZombieWalker || kind == SpawnKind.ZombieListener || kind == SpawnKind.ZombieWatcher;
        }

        /// <summary>레지스트리에서 종류별 스폰 프리팹을 찾는다 — 미등록이면 종류당 1회 경고 후 null.</summary>
        /// <param name="kind">스폰 종류.</param>
        /// <returns>스폰 프리팹 — 미등록이면 null.</returns>
        private NetworkObject FindSpawnPrefab(SpawnKind kind)
        {
            for (int i = 0; i < prefabRegistry.SpawnPrefabs.Length; i++)
            {
                if (prefabRegistry.SpawnPrefabs[i].Kind == kind && prefabRegistry.SpawnPrefabs[i].Prefab != null)
                {
                    return prefabRegistry.SpawnPrefabs[i].Prefab;
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
            return mapOrigin + new Vector3((worldCell.X - minX + 0.5f) * cell, 0f, (worldCell.Y - minY + 0.5f) * cell);
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
