using EmptyHouse.MapGen.Core;
using UnityEngine;

namespace EmptyHouse.MapGen.Runtime
{
    /// <summary>
    /// 빈 집 정의 SO(M10-1, 설계 B′) — 맵 1개 = 스테이지 = 난이도·보상 튜닝의 단위.
    /// 드라이버의 전역 직참조 4건(파라미터·층 스택·조명·레지스트리)이 이 에셋 하나로 접힌다.
    /// 층 서수는 저장하지 않는다 — <see cref="Floors"/> 리스트 위치(아래→위)와 <see cref="BasementCount"/> 에서 유도.
    /// </summary>
    [CreateAssetMenu(fileName = "SO_MapDefinition", menuName = "EmptyHouse/MapGen/Map Definition")]
    public sealed class MapDefinitionSO : ScriptableObject
    {
        public string MapId; // 안정 문자열 키 — 네트워크 복제·해시·로그 재현 키(카탈로그 인덱스 금지: 순서 변경에 취약)
        public MapGenParams GenParams = new MapGenParams(); // 맵 전역 생성 파라미터(시드는 런타임 확정) — 자물쇠·열쇠(R 불변식 = 전역 그래프)·아이템 총량·샤프트·리롤 = 빈 집별 난이도·보상 노브
        public FloorDefinitionSO[] Floors = new FloorDefinitionSO[0]; // 층 정의(아래→위, 길이 = 층 개수) — 같은 층 에셋을 여러 빈 집이 공유 가능
        public int BasementCount; // 지하 층 수 — Floors[0..BasementCount-1] 이 지하. 층 서수 유도의 축
        public MapPrefabRegistrySO CommonRegistry; // 테마 무관 상호작용·아이템 프리팹(페어·벽장·아이템·스크랩·좀비) — M10-1 에서 환경 항목이 제거된 축소판
        public EmptyHouse.Environment.LightingProfileSO LightingProfile; // 맵 조명 프로파일 — 조립기의 MapLightCuller 로 전달

        /// <summary>층 슬롯(Floors 배열 인덱스) → 층 서수(부호). 반복 호출되는 순수 계산이라 로그 트레이스 없음(CellMath 규약).</summary>
        /// <param name="floorSlot">Floors 배열 인덱스.</param>
        /// <returns>층 서수(B1 = -1 · 1F = 0 · 2F = +1).</returns>
        public int FloorIndexOf(int floorSlot)
        {
            // TODO(impl): floorSlot - BasementCount
            return default;
        }

        /// <summary>층 서수 → 층 정의. 범위 밖 = null(호출부가 X4/린트로 표면화). 순수 계산 — 로그 없음(CellMath 규약).</summary>
        /// <param name="floorIndex">층 서수.</param>
        /// <returns>층 정의 — 없으면 null.</returns>
        public FloorDefinitionSO FloorOf(int floorIndex)
        {
            // TODO(impl): Floors[floorIndex + BasementCount] — 범위 검사 후 반환
            return default;
        }
    }
}
