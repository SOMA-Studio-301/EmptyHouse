using System;
using EmptyHouse.MapGen.Core;
using Unity.Netcode;
using UnityEngine;

namespace EmptyHouse.MapGen.Runtime
{
    /// <summary>스폰 종류 ↔ 상태 오브젝트 프리팹 매핑 항목.</summary>
    [Serializable]
    public sealed class SpawnPrefabEntry
    {
        public SpawnKind Kind; // 스폰 종류
        public NetworkObject Prefab; // 기본(폴백) 서버 스폰 프리팹 — Variants 가 비었을 때 사용. NetworkManager NetworkPrefabs 등록 필수
        public NetworkObject[] Variants; // 변종 풀(스크랩·투척물 등 같은 역할의 여러 외형) — 비어 있지 않으면 시드 결정론 선택으로 Prefab 대신 스폰. 전 항목 NetworkPrefabs 등록 필수
    }

    /// <summary>열쇠·자물쇠 페어 프리팹 — 배열 인덱스 + 1 = 페어 번호(열쇠_XX ↔ 자물쇠_XX).</summary>
    [Serializable]
    public sealed class PairPrefabEntry
    {
        public NetworkObject Key; // 그 번호의 열쇠 외형 — 열쇠는 공용 프리팹이 없다(외형이 곧 자물쇠와의 짝). 미등재면 그 열쇠가 스폰되지 않아 해당 자물쇠를 열 수 없다
        public NetworkObject Lock; // 그 번호의 자물쇠 외형(DoorLockFace 루트) — 미등재면 자물쇠 없이 잠김(해정 불가) 경고
    }

    /// <summary>
    /// 테마 무관 상호작용·아이템 프리팹 레지스트리(M10-1 — CommonRegistry) — 상태 오브젝트(문 제외 아이템·좀비·설비·페어)의
    /// 서버 스폰 참조 원천. 환경(테마 종속) 프리팹(방 템플릿·봉인 벽·기둥·문·탈출문·미터 규격)은
    /// <see cref="FloorDefinitionSO"/> 로 이관됐다(설계 B′ 분류표). 순수 데이터 — 조회 로직은 소비 컴포넌트 소관.
    /// </summary>
    [CreateAssetMenu(fileName = "SO_MapPrefabRegistry", menuName = "EmptyHouse/MapGen/Prefab Registry")]
    public sealed class MapPrefabRegistrySO : ScriptableObject
    {
        public SpawnPrefabEntry[] SpawnPrefabs; // 스폰 종류 → 상태 오브젝트 프리팹(좀비·아이템·설비)
        public PairPrefabEntry[] PairPrefabs; // 열쇠·자물쇠 페어(인덱스 + 1 = 페어 번호) — 한 줄에 묶어 두 배열이 어긋나 열쇠_3 ↔ 자물쇠_4 가 되는 사고를 구조적으로 막는다
    }
}
