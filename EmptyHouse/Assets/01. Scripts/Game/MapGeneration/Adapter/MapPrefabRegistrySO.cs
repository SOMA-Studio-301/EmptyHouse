using System;
using EmptyHouse.MapGen.Core;
using Unity.Netcode;
using UnityEngine;

namespace EmptyHouse.MapGen.Runtime
{
    /// <summary>템플릿 ID ↔ 방/복도 프리팹 매핑 항목.</summary>
    [Serializable]
    public sealed class RoomPrefabEntry
    {
        public string TemplateId; // RoomTemplateDef.TemplateId 매칭 키
        public GameObject Prefab; // 방/복도 프리팹(정적 지오메트리 — 각 클라 로컬 인스턴스화)
    }

    /// <summary>스폰 종류 ↔ 상태 오브젝트 프리팹 매핑 항목.</summary>
    [Serializable]
    public sealed class SpawnPrefabEntry
    {
        public SpawnKind Kind; // 스폰 종류
        public NetworkObject Prefab; // 서버 스폰 프리팹 — NetworkManager NetworkPrefabs 등록 필수
    }

    /// <summary>
    /// 절차 맵 프리팹 레지스트리(P5 라이트) — 에디터 빌더의 AssetDatabase 경로 의존을 대체하는 런타임 참조 원천.
    /// 정적 지오메트리(방·봉인 벽·기둥)는 로컬 인스턴스화용 GameObject, 상태 오브젝트(문·아이템·좀비)는
    /// 서버 스폰용 NetworkObject 로 구분해 담는다(1절 파이프라인). 순수 데이터 — 조회 로직은 소비 컴포넌트 소관.
    /// </summary>
    [CreateAssetMenu(fileName = "SO_MapPrefabRegistry", menuName = "EmptyHouse/MapGen/Prefab Registry")]
    public sealed class MapPrefabRegistrySO : ScriptableObject
    {
        public RoomPrefabEntry[] RoomPrefabs; // 템플릿 ID → 방/복도 프리팹(PrefabRoomTemplates.PrefabPaths 대체)
        public GameObject SealWallPrefab; // 복도 개구 봉인 벽(정적 — Hall_Wall_6M_1Side)
        public GameObject CornerColumnPrefab; // 코너 이음 기둥(정적 — Hall_Clumn_Large_6M)
        public NetworkObject DoorPrefab; // 문 상태 오브젝트(DoorInteractable 루트) — 서버 스폰(1절)
        public SpawnPrefabEntry[] SpawnPrefabs; // 스폰 종류 → 상태 오브젝트 프리팹(좀비·아이템·설비)
        public NetworkObject[] KeyPrefabs; // 열쇠 변종(인덱스 + 1 = 페어 번호) — 비주얼 구분용. 번호 범위 밖이면 SpawnPrefabs 의 Key 공용 프리팹 폴백
        public float CellMeters = 4f; // 셀 실측(m) — MapTemplateCatalog.CellMeters 와 일치해야 한다(G1)
    }
}
