using System;
using Border.Core;
using EmptyHouse.MapGen.Core;
using UnityEngine;

namespace EmptyHouse.MapGen.Runtime
{
    /// <summary>
    /// 층 하나의 어댑터 설정(M8) — 테마 프리팹 원천 + 그 층의 미터 규격.
    /// 테마 프리팹은 기존 <see cref="MapPrefabRegistrySO"/> 를 층마다 하나씩 물려 재사용한다
    /// (레지스트리를 층별로 쪼개 복제하지 않는다 — 테마 = 레지스트리 1개).
    /// </summary>
    [Serializable]
    public sealed class FloorPrefabSet
    {
        public int FloorIndex; // 층 서수(부호) — 코어 FloorGenParams.FloorIndex 와 일치해야 한다
        public string ThemeId; // 테마 키 — 코어 FloorTemplateSet.ThemeId 와 대조(드리프트 표면화·해시 폴딩)
        public MapPrefabRegistrySO Registry; // 이 층 테마의 프리팹 원천(방·변형·봉인 벽·기둥·문·스폰)
        public FloorGenParams GenParams = new FloorGenParams(); // 이 층 코어 파라미터(예산·난이도·좀비 밴드 — M9-8 플랜 조립 재료)
        public RoomTemplateSO StairTemplate; // 이 층 테마의 계단실 템플릿(IsStairAnchor + 계단실 프리팹) — 전 층 풋프린트·소켓 동일(X4)
        public float CellMeters = 4f; // 이 층의 셀 실측(m) — 계단 연결 층 쌍은 동일해야 한다(X4 린트). 레지스트리의 동명 필드보다 이쪽이 우선
        public float FloorHeight = 9f; // 이 층 바닥면 → 위층 바닥면 거리(m). 2026-08-13 확정 9m — 계단 유닛(4×4×3) 3개 = 라이즈 9. 방 천장고(6m)와 별개(6~9m 는 천장 위 구조 대역)
        public GameObject StairPrefab; // 완성 계단(4×12×9 — 유닛 3개 직선, +Z 상승·진입 z 민 코너) — 위층이 있는 층의 계단실에 조립기가 삽입한다. 천장·바닥 절개는 조립기 소관(프리팹은 닫힌 방)
        public GameObject StairFlightPrefab; // 계단 플라이트(기성 Hall_Stairs = 라이즈 3m) — 구 그레이박스 저작 재료(레거시, StairPrefab 도입 후 미사용)
        public GameObject StairVoidSlabPrefab; // 계단 보이드 슬래브(기성 Hall_Floor_2nd_Floor_Stair_Half/_Full) — 중간·최상 층에 필요
        public GameObject StairRailingPrefab; // 보이드 난간(기성 Hall_Railings_Stair) — 없으면 추락·아이템 낙하 사고(N6)
    }

    /// <summary>
    /// 층 스택(M8) — 맵 하나를 구성하는 층 설정의 모음. 조립기·베이커·스포너가 층 Y 와 테마 프리팹을 여기서 얻는다.
    /// M9(다중 맵)에서는 `MapDefinitionSO` 가 이 스택을 감싸고, 맵 선택이 스택 선택이 된다.
    /// </summary>
    [CreateAssetMenu(fileName = "SO_MapFloorStack", menuName = "EmptyHouse/MapGen/Floor Stack")]
    public sealed class MapFloorStackSO : ScriptableObject
    {
        public FloorPrefabSet[] Floors; // 층 설정 목록. 원소 1개 = 단층(v1 동일 동작)

        /// <summary>층 서수로 층 설정을 찾는다. 없으면 null — 호출부가 X4/린트로 표면화한다.</summary>
        /// <param name="floorIndex">층 서수(B1 = -1 · 1F = 0 · 2F = +1).</param>
        /// <returns>층 설정, 없으면 null.</returns>
        public FloorPrefabSet Find(int floorIndex)
        {
            for (int i = 0; i < Floors.Length; i++)
            {
                if (Floors[i].FloorIndex == floorIndex)
                {
                    return Floors[i];
                }
            }

            return null;
        }
    }
}
