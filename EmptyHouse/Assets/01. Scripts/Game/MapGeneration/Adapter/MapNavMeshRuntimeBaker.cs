using Border.Core;
using Border.Events;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace EmptyHouse.MapGen.Runtime
{
    /// <summary>
    /// 절차 생성 맵 NavMesh 서버 런타임 베이크(스펙 8절 — 좀비 AI 가 서버 전용이라 서버만 베이크, AC-20).
    /// 규칙은 에디터 MapNavMeshBaker v2 이관: NavMeshSurface(전체 수집, 기본 Not Walkable)에
    /// 바닥 슬래브만 Walkable 태깅 — "바닥 위만 보행". 문은 베이크 제외, 차단은 문 프리팹의
    /// NavMeshObstacle(Carve)이 담당한다(개방 시 실시간 개통 — DoorInteractable.HandleOpenChanged 편승).
    /// 시퀀스: onMapAssembledServer(X7) → 베이크 → onMapNavMeshReadyServer 발화(→ 상태 오브젝트 스폰).
    /// </summary>
    public sealed class MapNavMeshRuntimeBaker : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private MapGenNetworkDriver driver; // 로컬 맵 루트 원천

        [Header("Event Channels")]
        [SerializeField] private VoidEventChannelSO onMapAssembledServer; // 구독 — 발화 시 서버 베이크 개시
        [SerializeField] private VoidEventChannelSO onMapNavMeshReadyServer; // 발화 — 베이크 완료(좀비 스폰 개시 신호)

        private const int walkableArea = 0; // NavMesh Walkable 영역 인덱스(MapNavMeshBaker v2 동일)
        private const int notWalkableArea = 1; // NavMesh Not Walkable 영역 인덱스
        private const float slabMaxTopY = 0.35f; // 바닥면 판정 — 층 평면 대비 상면 높이 허용 오차(m)

        /// <summary>onMapAssembledServer 구독.</summary>
        private void OnEnable()
        {
            Log.D("[MapNavMeshRuntimeBaker] OnEnable");
            onMapAssembledServer.OnEventRaised += HandleMapAssembled;
        }

        /// <summary>구독 해제.</summary>
        private void OnDisable()
        {
            Log.D("[MapNavMeshRuntimeBaker] OnDisable");
            onMapAssembledServer.OnEventRaised -= HandleMapAssembled;
        }

        /// <summary>
        /// 맵 조립 완료 수신 — 서버에서만 driver.LocalMapRoot 에 NavMeshSurface 를 구성·베이크하고
        /// 완료 시 onMapNavMeshReadyServer 를 발화한다(채널 발화 자체가 서버 전용 경로임을 보장).
        /// </summary>
        private void HandleMapAssembled()
        {
            Log.D("[MapNavMeshRuntimeBaker] HandleMapAssembled");
            GameObject mapRoot = driver.LocalMapRoot;
            float rootY = mapRoot.transform.position.y;
            float[] floorPlanes = driver.FloorPlaneYs(); // 층별 바닥면(M9-8) — 단층은 {0}

            // 층 침범 콘텐츠 제외(M9-9) — 장식 프리팹이 자기 층고(6m)를 넘어 위층 공간에 낮은 보·판으로 떠서
            // 위층 통행 클리어런스를 죽인다(실측: B1 room_6x9 PropSet 벌크헤드가 1F y1.0 에 침범).
            // 방 루트 Y(=층 평면) 기준 5.5m 이상에서 시작하는 렌더러는 베이크 제외(계단 전용 토큰은 예외)
            foreach (Transform child in mapRoot.transform)
            {
                if (!child.name.StartsWith("Room_"))
                {
                    continue;
                }

                foreach (MeshRenderer renderer in child.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer.name.Contains("Stair") || renderer.bounds.min.y - child.position.y < 5.5f)
                    {
                        continue;
                    }

                    NavMeshModifier intrude = renderer.GetComponent<NavMeshModifier>();
                    if (intrude == null)
                    {
                        intrude = renderer.gameObject.AddComponent<NavMeshModifier>();
                    }

                    intrude.ignoreFromBuild = true;
                }
            }

            // 바닥 슬래브 태깅 — MapNavMeshBaker v2 판정 규칙(두께·상면 높이·최소 크기), 높이는 **어느 한 층 바닥면** 기준(다층 M9-8)
            int tagged = 0;
            foreach (MeshRenderer renderer in mapRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                // 그림자 차폐 전용 렌더러(ShadowsOnly, 예: Screen 판)는 베이크 제외 — 보이지 않는 판이
                // 바닥 위를 덮어 내비를 섬으로 쪼갠다(M9-9 실측: 층 내 방간 통행 전면 단절의 원인)
                if (renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
                {
                    NavMeshModifier ignore = renderer.GetComponent<NavMeshModifier>();
                    if (ignore == null)
                    {
                        ignore = renderer.gameObject.AddComponent<NavMeshModifier>();
                    }

                    ignore.ignoreFromBuild = true;
                    continue;
                }

                // 판정 = "상면이 어느 층 평면과 플러시"(M9-9 개정). 두께·크기 조건을 걸면 아래층 벽·아치의
                // 상단(y 로 층 평면과 정확히 맞닿는 0.4~0.8m 띠)이 NotWalkable 로 남아 위층 내비를 절단한다.
                // 바닥 위 두꺼운 물체는 상면이 평면+0.35 를 넘어 자연 배제되므로 두께 조건이 필요 없다
                Bounds bounds = renderer.bounds;
                bool onFloorPlane = false;
                for (int p = 0; p < floorPlanes.Length && !onFloorPlane; p++)
                {
                    onFloorPlane = Mathf.Abs(bounds.max.y - (rootY + floorPlanes[p])) <= slabMaxTopY;
                }

                if (!onFloorPlane)
                {
                    continue;
                }

                NavMeshModifier modifier = renderer.GetComponent<NavMeshModifier>();
                if (modifier != null && modifier.ignoreFromBuild)
                {
                    continue;
                }

                if (modifier == null)
                {
                    modifier = renderer.gameObject.AddComponent<NavMeshModifier>();
                }

                modifier.overrideArea = true;
                modifier.area = walkableArea;
                tagged++;
            }

            // 계단 전면 차단(EH-90) — 좀비 층간 이동이 기획상 금지라 계단은 보행면이 아니라 장애물이다.
            // Stair* 토큰 전부(플라이트·참·난간 포괄)를 Not Walkable 로 명시 태깅 — 위의 바닥 슬래브
            // 판정이 참·상단 개구 언저리를 Walkable 로 잡아도 뒤에 오는 이 블록이 덮어써, 계단 위에
            // 폴리곤이 생기지 않는다. 다층 좀비 이동을 다시 열면 여기를 Walkable 로 되돌릴 것
            // (그때 MapRuntimeAssembler 의 AddStairRamps/AddTopBridge 복원도 함께 — 해당 주석 참조).
            int stairTagged = 0;
            foreach (MeshRenderer renderer in mapRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.name.Contains("Stair"))
                {
                    continue;
                }

                NavMeshModifier modifier = renderer.GetComponent<NavMeshModifier>();
                if (modifier == null)
                {
                    modifier = renderer.gameObject.AddComponent<NavMeshModifier>();
                }

                modifier.overrideArea = true;
                modifier.area = notWalkableArea;
                stairTagged++;
            }

            // 베이크 radius 정합(M9-9·R5 동반) — 베이크 에이전트가 좀비 에이전트(0.5)보다 좁으면
            // 난간 낀 계단에서 끼임으로 나타난다. 기본 에이전트 설정을 읽어 어긋나면 경고로 표면화
            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            if (settings.agentRadius < 0.45f)
            {
                Log.W($"[MapNavMeshRuntimeBaker] 베이크 agentRadius({settings.agentRadius:F2}) < 좀비 에이전트(0.5) — 계단·난간 폭 판정이 어긋난다. Navigation 설정에서 0.5 로 정렬 필요");
            }

            // 맵 루트 하위만 수집해 베이크 — 문은 아직 스폰 전이라 자동 제외(차단은 스폰될 문의 Carve 소관)
            NavMeshSurface surface = mapRoot.GetComponent<NavMeshSurface>();
            if (surface == null)
            {
                surface = mapRoot.AddComponent<NavMeshSurface>();
            }

            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            surface.defaultArea = notWalkableArea;
            surface.BuildNavMesh();

            Log.D($"[MapNavMeshRuntimeBaker] 베이크 완료 — 바닥 태깅 {tagged}·계단 차단 태깅 {stairTagged} — onMapNavMeshReadyServer 발화");
            onMapNavMeshReadyServer.RaiseEvent();
        }
    }
}
