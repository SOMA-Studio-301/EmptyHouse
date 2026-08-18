using System;
using System.Collections.Generic;
using EmptyHouse.MapGen.Core;
using UnityEngine;

namespace EmptyHouse.MapGen.Runtime
{
    /// <summary>소켓 저작 항목 — 코어 SocketDef 의 직렬화 가능 투영(CellCoord 가 readonly struct 라 직접 직렬화 불가).</summary>
    [Serializable]
    public sealed class SocketAuthoring
    {
        public int Id; // 템플릿 내 소켓 번호
        public int CellX; // 템플릿 로컬 셀 X(둘레)
        public int CellY; // 템플릿 로컬 셀 Y
        public SocketDirection Direction; // 소켓이 향하는 바깥 방향
        public bool MandatoryDoor; // 의무 문 소켓(항상 방 직결 + 문) — v1 은 입구 앵커 전용
    }

    /// <summary>마커 저작 항목 — 코어 MarkerDef 의 직렬화 가능 투영.</summary>
    [Serializable]
    public sealed class MarkerAuthoring
    {
        public int Id; // 템플릿 내 마커 번호
        public MarkerKind Kind; // 마커 종류
        public int CellX; // 템플릿 로컬 셀 X
        public int CellY; // 템플릿 로컬 셀 Y
        public ZombieTypeMask ZombieMask; // ZombieSpawn 전용 — 허용 좀비 타입
        public ItemCategoryMask ItemMask; // ItemSpawn 전용 — 허용 아이템 카테고리
        public float WanderRadiusCells; // 좀비 배회 반경 기본값(셀)
    }

    /// <summary>
    /// 방/복도 템플릿 SO(M9-3, 스펙 2절) — 풋프린트·소켓·마커 같은 **코어 데이터**와 배치 프리팹·변형 풀 같은
    /// **어댑터 재료**를 한 에셋으로 묶는다. 층·맵 추가가 C# 편집이 아니라 에셋 작업이 되게 하는 기반.
    /// 코어는 이 SO 를 모른다 — 소비는 <see cref="MapPrefabRegistrySO.CreateTemplates"/> 의 순수 데이터 추출을 거친다(AC-03).
    /// ⚠️ 코어 데이터 필드 편집은 곧 생성 결과 변경이다 — 골든 픽스처(MapTemplateCatalog)와 어긋나면
    /// 셋업 린트(Tools/Map/런타임 어댑터 셋업)가 드리프트를 보고한다.
    /// </summary>
    [CreateAssetMenu(fileName = "SO_Template", menuName = "EmptyHouse/MapGen/Room Template")]
    public sealed class RoomTemplateSO : ScriptableObject
    {
        [Header("코어 데이터 — 생성 결과를 결정한다")]
        public string TemplateId; // 템플릿 ID — 전 층 통틀어 유일해야 한다(X4)
        public int WidthCells = 3; // 풋프린트 가로(셀)
        public int HeightCells = 3; // 풋프린트 세로(셀)
        public FloorMask AllowedFloors = FloorMask.F1; // 허용 층
        public RoomTagMask Tags; // 태그(어두움 등)
        public int MinCount; // 최소 등장 횟수
        public int MaxCount = 1; // 최대 등장 횟수
        public bool IsCorridor; // 복도 여부
        public bool IsEntranceAnchor; // 버스 입구 고정 모듈 여부(시드 층 전용)
        public bool IsStairAnchor; // 계단실 여부(M9 SSA) — 전 층 계단 템플릿은 풋프린트·소켓 배열이 동일해야 한다(X4)
        public SocketAuthoring[] Sockets = new SocketAuthoring[0]; // 출입구 소켓 목록 — 정렬 불변식(c ↔ L−1−c 자기 대칭) 준수
        public MarkerAuthoring[] Markers = new MarkerAuthoring[0]; // 스폰 마커 목록

        [Header("어댑터 재료 — 배치 프리팹")]
        public GameObject Prefab; // 기본(폴백) 프리팹 — Variants 가 비었을 때 사용
        public GameObject[] Variants = new GameObject[0]; // 데코 변형 풀(셋업 스캔 산출물 — 이름 오름차순, 시드 결정론 선택)
        public bool ExcludeFromNavMesh; // 안전지대 등 — 조립 시 방 루트에 NavMeshModifier(ignoreFromBuild)를 스탬프해 베이크에서 제외

        /// <summary>코어 순수 데이터로 추출한다 — 호출마다 새 인스턴스(호출자 오버라이드가 에셋을 오염시키지 않게).</summary>
        /// <returns>코어 템플릿 서술자.</returns>
        public RoomTemplateDef ToDef()
        {
            var sockets = new SocketDef[Sockets.Length];
            for (int i = 0; i < Sockets.Length; i++)
            {
                sockets[i] = new SocketDef
                {
                    Id = Sockets[i].Id,
                    LocalCell = new CellCoord(Sockets[i].CellX, Sockets[i].CellY),
                    Direction = Sockets[i].Direction,
                    MandatoryDoor = Sockets[i].MandatoryDoor,
                };
            }

            var markers = new MarkerDef[Markers.Length];
            for (int i = 0; i < Markers.Length; i++)
            {
                markers[i] = new MarkerDef
                {
                    Id = Markers[i].Id,
                    Kind = Markers[i].Kind,
                    LocalCell = new CellCoord(Markers[i].CellX, Markers[i].CellY),
                    ZombieMask = Markers[i].ZombieMask,
                    ItemMask = Markers[i].ItemMask,
                    WanderRadiusCells = Markers[i].WanderRadiusCells,
                };
            }

            return new RoomTemplateDef
            {
                TemplateId = TemplateId,
                WidthCells = WidthCells,
                HeightCells = HeightCells,
                AllowedFloors = AllowedFloors,
                Tags = Tags,
                MinCount = MinCount,
                MaxCount = MaxCount,
                IsCorridor = IsCorridor,
                IsEntranceAnchor = IsEntranceAnchor,
                IsStairAnchor = IsStairAnchor,
                Sockets = sockets,
                Markers = markers,
            };
        }

        /// <summary>배치 프리팹을 고른다 — 변형 풀이 있으면 시드 결정론 선택, 비었으면 기본 프리팹.</summary>
        /// <param name="seed">확정 시드.</param>
        /// <param name="roomIndex">블루프린트 방 인덱스.</param>
        /// <returns>배치할 프리팹.</returns>
        public GameObject SelectPrefab(int seed, int roomIndex)
        {
            if (Variants != null && Variants.Length > 0)
            {
                return Variants[VariantSelector.RoomVariantIndex(seed, roomIndex, Variants.Length)];
            }

            return Prefab;
        }

        /// <summary>코어 서술자의 값을 이 SO 에 복사한다(셋업 시드용 — 소켓·마커까지 전 필드).</summary>
        /// <param name="def">원천 서술자.</param>
        public void CopyFrom(RoomTemplateDef def)
        {
            TemplateId = def.TemplateId;
            WidthCells = def.WidthCells;
            HeightCells = def.HeightCells;
            AllowedFloors = def.AllowedFloors;
            Tags = def.Tags;
            MinCount = def.MinCount;
            MaxCount = def.MaxCount;
            IsCorridor = def.IsCorridor;
            IsEntranceAnchor = def.IsEntranceAnchor;
            IsStairAnchor = def.IsStairAnchor;
            Sockets = new SocketAuthoring[def.Sockets.Length];
            for (int i = 0; i < def.Sockets.Length; i++)
            {
                Sockets[i] = new SocketAuthoring
                {
                    Id = def.Sockets[i].Id,
                    CellX = def.Sockets[i].LocalCell.X,
                    CellY = def.Sockets[i].LocalCell.Y,
                    Direction = def.Sockets[i].Direction,
                    MandatoryDoor = def.Sockets[i].MandatoryDoor,
                };
            }

            Markers = new MarkerAuthoring[def.Markers.Length];
            for (int i = 0; i < def.Markers.Length; i++)
            {
                Markers[i] = new MarkerAuthoring
                {
                    Id = def.Markers[i].Id,
                    Kind = def.Markers[i].Kind,
                    CellX = def.Markers[i].LocalCell.X,
                    CellY = def.Markers[i].LocalCell.Y,
                    ZombieMask = def.Markers[i].ZombieMask,
                    ItemMask = def.Markers[i].ItemMask,
                    WanderRadiusCells = def.Markers[i].WanderRadiusCells,
                };
            }
        }

        /// <summary>두 서술자의 코어 데이터 동일 여부 — 셋업 드리프트 린트용(전 필드·소켓·마커 순서 포함 비교).</summary>
        /// <param name="a">비교 대상 A.</param>
        /// <param name="b">비교 대상 B.</param>
        /// <returns>완전 동일하면 true.</returns>
        public static bool DefEquals(RoomTemplateDef a, RoomTemplateDef b)
        {
            if (a.TemplateId != b.TemplateId || a.WidthCells != b.WidthCells || a.HeightCells != b.HeightCells
                || a.AllowedFloors != b.AllowedFloors || a.Tags != b.Tags || a.MinCount != b.MinCount || a.MaxCount != b.MaxCount
                || a.IsCorridor != b.IsCorridor || a.IsEntranceAnchor != b.IsEntranceAnchor || a.IsStairAnchor != b.IsStairAnchor
                || a.Sockets.Length != b.Sockets.Length || a.Markers.Length != b.Markers.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Sockets.Length; i++)
            {
                if (a.Sockets[i].Id != b.Sockets[i].Id
                    || a.Sockets[i].LocalCell.X != b.Sockets[i].LocalCell.X
                    || a.Sockets[i].LocalCell.Y != b.Sockets[i].LocalCell.Y
                    || a.Sockets[i].Direction != b.Sockets[i].Direction
                    || a.Sockets[i].MandatoryDoor != b.Sockets[i].MandatoryDoor)
                {
                    return false;
                }
            }

            for (int i = 0; i < a.Markers.Length; i++)
            {
                if (a.Markers[i].Id != b.Markers[i].Id || a.Markers[i].Kind != b.Markers[i].Kind
                    || a.Markers[i].LocalCell.X != b.Markers[i].LocalCell.X
                    || a.Markers[i].LocalCell.Y != b.Markers[i].LocalCell.Y
                    || a.Markers[i].ZombieMask != b.Markers[i].ZombieMask
                    || a.Markers[i].ItemMask != b.Markers[i].ItemMask
                    || !Mathf.Approximately(a.Markers[i].WanderRadiusCells, b.Markers[i].WanderRadiusCells))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
