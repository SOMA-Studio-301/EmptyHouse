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
        private const float slabMaxThickness = 0.35f; // 바닥 슬래브 판정 — 최대 두께(m)
        private const float slabMaxTopY = 0.35f; // 바닥 슬래브 판정 — 맵 원점 기준 최대 상면 높이(m)
        private const float slabMinXZ = 0.9f; // 바닥 슬래브 판정 — 최소 XZ 크기(m)

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

            // 바닥 슬래브 태깅 — MapNavMeshBaker v2 판정 규칙(두께·상면 높이·최소 크기), 높이는 맵 원점 기준
            int tagged = 0;
            foreach (MeshRenderer renderer in mapRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                Bounds bounds = renderer.bounds;
                if (bounds.size.y > slabMaxThickness
                    || bounds.max.y > rootY + slabMaxTopY
                    || bounds.size.x < slabMinXZ || bounds.size.z < slabMinXZ)
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

            Log.D($"[MapNavMeshRuntimeBaker] 베이크 완료 — 바닥 태깅 {tagged} — onMapNavMeshReadyServer 발화");
            onMapNavMeshReadyServer.RaiseEvent();
        }
    }
}
