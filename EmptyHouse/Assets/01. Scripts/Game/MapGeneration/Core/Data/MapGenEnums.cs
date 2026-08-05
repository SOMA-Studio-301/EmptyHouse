using System;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>간선(소켓 쌍) 상태 — 생성기가 문(잠김/열림)/통로/막힌 벽 중 택1로 채운다(2절).</summary>
    public enum EdgeState
    {
        OpenPassage,
        DoorOpen,
        DoorLocked,
        BlockedWall
    }

    /// <summary>자물쇠 용도 구분(4-1절 — 중요 물품 문/지름길). 그래프 뷰 간선 표시(10절)와 어댑터가 소비한다.</summary>
    public enum LockKind
    {
        None,
        ItemDoor,
        Shortcut
    }

    /// <summary>방 템플릿 회전(90도 단위).</summary>
    public enum Rotation4
    {
        Deg0,
        Deg90,
        Deg180,
        Deg270
    }

    /// <summary>소켓이 향하는 그리드 방향.</summary>
    public enum SocketDirection
    {
        North,
        East,
        South,
        West
    }

    /// <summary>허용 층 마스크(2절 RoomTemplateSO). v1은 F1만 사용 — B1·F2는 다층 v2(G6).</summary>
    [Flags]
    public enum FloorMask
    {
        None = 0,
        B1 = 1,
        F1 = 2,
        F2 = 4
    }

    /// <summary>방 태그 마스크(2절 — 어두움 등).</summary>
    [Flags]
    public enum RoomTagMask
    {
        None = 0,
        Dark = 1
    }

    /// <summary>스폰 마커 종류(2절) — "놓아도 되는 자리"는 마커가 보장한다.</summary>
    public enum MarkerKind
    {
        ZombieSpawn,
        ItemSpawn,
        GeneratorSlot,
        CorpseStationSlot,
        HerdArea
    }

    /// <summary>ZombieSpawn 마커의 허용 좀비 타입 마스크(5절).</summary>
    [Flags]
    public enum ZombieTypeMask
    {
        None = 0,
        Walker = 1,
        Listener = 2,
        Watcher = 4
    }

    /// <summary>ItemSpawn 마커의 허용 아이템 카테고리 마스크(5절).</summary>
    [Flags]
    public enum ItemCategoryMask
    {
        None = 0,
        Vaccine = 1,
        Key = 2,
        Fuel = 4, // 연료(스펙 5절의 '기름' — 게임 오브젝트 명칭을 따라 Fuel 로 표기)
        Scrap = 8,
        Throwable = 16
    }

    /// <summary>스폰 항목 종류 — MapBlueprint.Spawns의 종류 필드(1절 데이터 계약).</summary>
    public enum SpawnKind
    {
        ZombieWalker,
        ZombieListener,
        ZombieWatcher,
        VaccineAntigen,
        VaccineSerum,
        VaccineStabilizer,
        Key,
        Fuel, // 연료(스펙 5절의 '기름' — 게임 오브젝트 명칭을 따라 Fuel 로 표기)
        Scrap,
        Throwable,
        CorpseStation,
        Generator,
        HerdArea
    }
}
