using System.Collections.Generic;
using Border.Core;

namespace EmptyHouse.MapGen.Core.Tests
{
    /// <summary>
    /// 테스트 공용 픽스처 — 손으로 짠 미니 그래프와 가짜 템플릿 세트.
    /// M1(그래프 유틸)의 기대값 검증 재료이자 M2~M5 생성기 구동 재료. 구현은 M1 세션 담당.
    /// </summary>
    public static class BlueprintFixtures
    {
        /// <summary>
        /// 손으로 짠 미니 블루프린트 — 방 6(0 = 입구), 트리 간선 5 + 루프 간선 1,
        /// 자물쇠 2(1번 = 루프 지름길, 2번 = 깊은 가지 관문), 봉인 간선(RoomB = -1) 1개 포함.
        ///
        /// 그래프(간선 인덱스 순):
        ///   0 ─e0(통로)─ 1 ─e1(문)─ 2 ─e2(잠금2)─ 3
        ///                           │             │
        ///                           e3(통로)      e5(잠금1 루프)
        ///                           │             │
        ///                           4 ─e4(문)──── 5 ─e6(봉인 RoomB=-1)
        ///
        /// 수기 기대값: 깊이 = [0,1,2,3,3,4] · R_1 = {0,1,2,4,5} · R_2 = 전체 · e4 차단 시 = {0,1,2,3,4}.
        /// </summary>
        /// <returns>기대값 검증용 블루프린트.</returns>
        public static MapBlueprint CreateMiniBlueprint()
        {
            Log.D("[BlueprintFixtures] CreateMiniBlueprint");
            var blueprint = new MapBlueprint();
            blueprint.Meta.Seed = 1;
            blueprint.Meta.GeneratorVersion = "fixture";

            blueprint.Rooms.Add(new BlueprintRoom { TemplateId = "entrance", Cell = new CellCoord(0, 0), Rotation = Rotation4.Deg0 });
            blueprint.Rooms.Add(new BlueprintRoom { TemplateId = "room_small", Cell = new CellCoord(0, 3), Rotation = Rotation4.Deg0 });
            blueprint.Rooms.Add(new BlueprintRoom { TemplateId = "room_small", Cell = new CellCoord(0, 6), Rotation = Rotation4.Deg0 });
            blueprint.Rooms.Add(new BlueprintRoom { TemplateId = "room_medium", Cell = new CellCoord(4, 6), Rotation = Rotation4.Deg0 });
            blueprint.Rooms.Add(new BlueprintRoom { TemplateId = "room_small", Cell = new CellCoord(4, 3), Rotation = Rotation4.Deg0 });
            blueprint.Rooms.Add(new BlueprintRoom { TemplateId = "room_small", Cell = new CellCoord(8, 3), Rotation = Rotation4.Deg0 });

            // 트리 간선 5
            blueprint.Edges.Add(new BlueprintEdge { RoomA = 0, SocketA = 0, RoomB = 1, SocketB = 0, State = EdgeState.OpenPassage, LockNumber = 0 });
            blueprint.Edges.Add(new BlueprintEdge { RoomA = 1, SocketA = 1, RoomB = 2, SocketB = 0, State = EdgeState.DoorOpen, LockNumber = 0 });
            blueprint.Edges.Add(new BlueprintEdge { RoomA = 2, SocketA = 1, RoomB = 3, SocketB = 0, State = EdgeState.DoorLocked, LockNumber = 2 });
            blueprint.Edges.Add(new BlueprintEdge { RoomA = 2, SocketA = 2, RoomB = 4, SocketB = 0, State = EdgeState.OpenPassage, LockNumber = 0 });
            blueprint.Edges.Add(new BlueprintEdge { RoomA = 4, SocketA = 1, RoomB = 5, SocketB = 0, State = EdgeState.DoorOpen, LockNumber = 0 });
            // 루프 간선 1 — 지름길 자물쇠 1번
            blueprint.Edges.Add(new BlueprintEdge { RoomA = 3, SocketA = 1, RoomB = 4, SocketB = 2, State = EdgeState.DoorLocked, LockNumber = 1 });
            // 봉인 간선 — 짝 없는 소켓의 막힌 벽(간선 아님)
            blueprint.Edges.Add(new BlueprintEdge { RoomA = 5, SocketA = 1, RoomB = -1, SocketB = -1, State = EdgeState.BlockedWall, LockNumber = 0 });

            return blueprint;
        }

        /// <summary>
        /// 생성기 구동용 가짜 템플릿 세트 — 본문은 DevTemplateSet 으로 이관됐다(0절).
        /// 툴(10절)과 같은 재료를 공유해야 로그 시드 재현(AC-22)이 성립한다 — 위임만 남긴다.
        /// </summary>
        /// <returns>M2~M5 property 테스트용 템플릿 목록.</returns>
        public static IReadOnlyList<RoomTemplateDef> CreateFakeTemplates()
        {
            Log.D("[BlueprintFixtures] CreateFakeTemplates");
            return DevTemplateSet.Create();
        }

        /// <summary>
        /// 테스트 스케일 파라미터 — 총 방 수 축소(10~12) 운용, 나머지는 9절 기본값.
        /// </summary>
        /// <param name="seed">확정 시드(0 금지 — X8).</param>
        /// <returns>테스트용 생성 파라미터.</returns>
        public static MapGenParams CreateTestParams(int seed)
        {
            Log.D($"[BlueprintFixtures] CreateTestParams 시드={seed}");
            return new MapGenParams
            {
                Seed = seed,
                RoomsTotalMin = 10,
                RoomsTotalMax = 12,
            };
        }
    }
}
