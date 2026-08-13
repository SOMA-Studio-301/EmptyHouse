using System.Collections.Generic;
using Border.Core;
using EmptyHouse.MapGen.Core;
using Unity.Netcode;
using UnityEngine;

namespace EmptyHouse.MapGen.Runtime
{
    /// <summary>
    /// 층 정의 SO(M10-1, 설계 B′) — 층 하나 = 테마·질감의 단위. 구 층 스택 인라인 항목(FloorPrefabSet)의 승격이다.
    /// **층 서수(FloorIndex)를 갖지 않는다** — 서수는 <see cref="MapDefinitionSO"/> 의 리스트 위치 + 지하 층 수로
    /// 유도되고, 플랜 조립(<see cref="MapPlanBuilder"/>)이 코어 파라미터 복제본에 스탬프한다.
    /// 같은 층 에셋을 여러 빈 집이 재사용할 수 있게 하는 것이 이 분리의 목적이다.
    /// 소유 = 테마 종속 환경(방·문·계단·봉인벽·기둥·탈출문·미터 규격).
    /// 테마 무관 상호작용·아이템(페어·벽장·백신·기름·투척물·스크랩·좀비)은 맵의 CommonRegistry 소관(설계 B′ 분류표).
    /// </summary>
    [CreateAssetMenu(fileName = "SO_Floor", menuName = "EmptyHouse/MapGen/Floor Definition")]
    public sealed class FloorDefinitionSO : ScriptableObject
    {
        [Header("코어 데이터 — 생성 결과를 결정한다")]
        public string ThemeId; // 테마 키 — 해시 폴딩·어댑터 대조 대상(클라 간 테마 불일치를 AC-02 가 잡는다)
        public FloorGenParams GenParams = new FloorGenParams(); // 층 생성 파라미터(예산·사이클·복도·위험 가중·좀비 밴드·탈출문·벽장) — FloorIndex 필드는 무시되고 조립 시 스탬프된다
        public RoomTemplateSO[] Templates = new RoomTemplateSO[0]; // 방/복도 템플릿 목록 — **배열 순서가 곧 코어 후보 순서**(결정론)
        public RoomTemplateSO StairTemplate; // 계단실 템플릿(IsStairAnchor) — 전 층 풋프린트·소켓 배열 동일(X4)

        [Header("환경 프리팹 — 테마 종속")]
        public GameObject StairPrefab; // 완성 계단(직선 3유닛 4×12×9, +Z 상승) — 위층 있는 계단실에 조립기가 삽입, 천장·바닥 절개는 조립기 소관
        public GameObject StairVoidSlabPrefab; // 계단 보이드 슬래브 — 최하층 보이드 봉인 재료(아트 패스)
        public GameObject StairRailingPrefab; // 계단 보이드 난간(N6) — 아트 패스에서 배치
        public GameObject SealWallPrefab; // 복도 개구 봉인 벽(정적)
        public GameObject CornerColumnPrefab; // 코너 이음 기둥(정적)
        public NetworkObject DoorPrefab; // 문 상태 오브젝트(DoorInteractable 루트) — 층 테마 외형. NetworkPrefabs 등록 필수
        public NetworkObject ReturnExitPrefab; // 탈출문(Door-Return, ReturnInteractable 루트) — NetworkPrefabs 등록 필수

        [Header("미터 규격")]
        public float CellMeters = 4f; // 이 층의 셀 실측(m) — 계단 연결 층 쌍은 동일해야 한다(X4 하드 에러)
        public float FloorHeight = 9f; // 이 층 바닥면 → 위층 바닥면 거리(m) — 2026-08-13 확정 9m(계단 유닛 4×4×3 × 3 = 라이즈 9)

        /// <summary>템플릿 SO 목록(계단실 포함)을 코어 순수 데이터로 추출한다 — 호출마다 새 인스턴스(레지스트리 추출과 같은 규약, AC-21).</summary>
        /// <returns>코어 템플릿 목록 — 배열 순서 유지, 말미에 계단실(null 이면 생략). 층 접미사는 MapPlanBuilder 소관.</returns>
        public List<RoomTemplateDef> CreateTemplates()
        {
            Log.D("[FloorDefinitionSO] CreateTemplates");
            var result = new List<RoomTemplateDef>(Templates.Length + 1);
            for (int i = 0; i < Templates.Length; i++)
            {
                result.Add(Templates[i].ToDef());
            }

            if (StairTemplate != null)
            {
                result.Add(StairTemplate.ToDef());
            }

            return result;
        }

        /// <summary>TemplateId 로 템플릿 SO 를 찾는다(계단실 포함) — 조립기의 배치 프리팹 조회용. 미등재 = null(데이터 결함).</summary>
        /// <param name="templateId">템플릿 ID(층 접미사 제거 후의 원 ID).</param>
        /// <returns>일치 템플릿 SO — 없으면 null.</returns>
        public RoomTemplateSO FindTemplate(string templateId)
        {
            Log.D("[FloorDefinitionSO] FindTemplate");
            for (int i = 0; i < Templates.Length; i++)
            {
                if (Templates[i].TemplateId == templateId)
                {
                    return Templates[i];
                }
            }

            if (StairTemplate != null && StairTemplate.TemplateId == templateId)
            {
                return StairTemplate;
            }

            return null;
        }
    }
}
