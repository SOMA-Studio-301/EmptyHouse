using System;
using Border.Core;
using UnityEngine;

namespace EmptyHouse.MapGen.Runtime
{
    /// <summary>카탈로그 항목 — 빈 집 정의 + 세션 메타(필요 기름). 후속 확장 자리: 난이도·해금·선택 가중치.</summary>
    [Serializable]
    public sealed class MapCatalogEntry
    {
        public MapDefinitionSO Definition; // 빈 집 정의
        public float FuelCost; // 이 빈 집으로의 외출에 드는 기름 ⚪ — 수치·소모 규칙은 경제상점 E7 확정 대기(소모·게이트 로직은 세션루프·경제 소관, 맵젠은 값 보관까지만)
    }

    /// <summary>
    /// 빈집들 카탈로그 SO(M10-2, 설계 B′) — 사용 가능한 빈 집 목록의 전역 단일 출처.
    /// 서버가 외출 시작 시 여기서 골라 MapId 를 복제한다(N5 — v1 은 랜덤, 로비 선택 UI 후속).
    /// 전 클라가 같은 에셋을 가져야 한다(AC-02 — 카탈로그 지문이 드리프트를 잡는다).
    /// </summary>
    [CreateAssetMenu(fileName = "SO_MapCatalog", menuName = "EmptyHouse/MapGen/Map Catalog")]
    public sealed class MapCatalogSO : ScriptableObject
    {
        public MapCatalogEntry[] Entries = new MapCatalogEntry[0]; // 빈 집 목록 — 순서는 표시용일 뿐, 식별은 MapId 로만

        /// <summary>MapId 로 빈 집 정의를 찾는다 — 복제 수신 측의 조회 경로. 미등재 = null(에셋 드리프트 — AC-02 로 표면화).</summary>
        /// <param name="mapId">맵 식별 키.</param>
        /// <returns>빈 집 정의 — 없으면 null.</returns>
        public MapDefinitionSO FindByMapId(string mapId)
        {
            Log.D("[MapCatalogSO] FindByMapId");
            for (int i = 0; i < Entries.Length; i++)
            {
                // 편집 중 빈 항목이 다른 맵 조회를 막지 않도록 건너뛴다(결손 자체는 카탈로그 린트 소관)
                if (Entries[i].Definition != null && Entries[i].Definition.MapId == mapId)
                {
                    return Entries[i].Definition;
                }
            }

            return null;
        }
    }
}
