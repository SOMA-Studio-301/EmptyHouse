using System;
using Border.Core;
using EmptyHouse.MapGen.Core;
using UnityEngine;

namespace EmptyHouse.MapGen.Runtime
{
    /// <summary>앵커가 받아들이는 스폰 종류(아트가 인스펙터에서 지정) — 큰 설비가 선반에 올라가는 사고를 막는 필터.</summary>
    [Flags]
    public enum SpawnAnchorMask
    {
        None = 0,
        Vaccine = 1, // 백신 3종
        Key = 2, // 열쇠
        Fuel = 4, // 연료(큰 물건 — 바닥 앵커 권장)
        Scrap = 8, // 스크랩
        Throwable = 16, // 투척물
        CorpseStation = 32, // 사체 충전소(바닥 전용)
        Generator = 64, // 발전기(바닥 전용)
        Wardrobe = 128, // 벽장(은신) — 바닥·벽면 전용. 사람이 들어가므로 앞이 트인 자리여야 한다
        SmallItems = Vaccine | Key | Scrap | Throwable, // 선반·탁자용 묶음
        All = Vaccine | Key | Fuel | Scrap | Throwable | CorpseStation | Generator | Wardrobe
    }

    /// <summary>
    /// 아이템·설비 배치 앵커 — 방 프리팹 안에 아트가 직접 심는 배치 지점(선반 위·탁자 위·바닥 구석).
    /// 절차 생성기는 "이 방에 무엇을" 까지만 정하고, 실제 좌표·회전은 이 앵커가 준다(MapStateObjectSpawner 소비).
    /// 프리팹 소속이라 서버·클라 로컬 조립본에 동일하게 존재한다 — 서버만 읽어 스폰하므로 정합 문제 없음.
    /// 한 앵커는 한 개만 받는다(점유 표시). 방에 앵커가 없거나 전부 점유면 스포너가 바닥 폴백 + 경고.
    /// </summary>
    public sealed class MapItemAnchor : MonoBehaviour
    {
        [SerializeField] private SpawnAnchorMask allowed = SpawnAnchorMask.All; // 이 앵커가 받는 스폰 종류 — 바닥 앵커만 Fuel/설비 허용 권장
        [SerializeField] private bool bottomAlign = true; // 스폰 오브젝트의 밑면을 이 앵커 지점에 맞출지 — 피벗이 중심인 프리팹이 바닥·선반에 묻히는 것을 막는다. 벽걸이·천장 앵커는 꺼서 피벗 그대로 쓴다

        public bool IsOccupied { get; private set; } // 이번 맵에서 이미 배치에 쓰였는지(스포너 런타임 표시)
        public bool BottomAlign => bottomAlign; // 밑면 정렬 여부(스포너가 읽는다)

        /// <summary>이 앵커가 해당 스폰을 받을 수 있는지 — 미점유 + 마스크 허용.</summary>
        /// <param name="kind">스폰 종류.</param>
        /// <returns>수용 가능 여부.</returns>
        public bool Accepts(SpawnKind kind)
        {
            return !IsOccupied && (allowed & MaskOf(kind)) != SpawnAnchorMask.None;
        }

        /// <summary>배치 확정 표시 — 같은 앵커에 두 개가 겹치지 않게 한다.</summary>
        public void MarkOccupied()
        {
            Log.D($"[MapItemAnchor] MarkOccupied {name}");
            IsOccupied = true;
        }

        /// <summary>스폰 종류 → 앵커 마스크 비트.</summary>
        /// <param name="kind">스폰 종류.</param>
        /// <returns>대응 마스크(대상 아님이면 None).</returns>
        private static SpawnAnchorMask MaskOf(SpawnKind kind)
        {
            switch (kind)
            {
                case SpawnKind.VaccineAntigen:
                case SpawnKind.VaccineSerum:
                case SpawnKind.VaccineStabilizer: return SpawnAnchorMask.Vaccine;
                case SpawnKind.Key: return SpawnAnchorMask.Key;
                case SpawnKind.Fuel: return SpawnAnchorMask.Fuel;
                case SpawnKind.Scrap: return SpawnAnchorMask.Scrap;
                case SpawnKind.Throwable: return SpawnAnchorMask.Throwable;
                case SpawnKind.CorpseStation: return SpawnAnchorMask.CorpseStation;
                case SpawnKind.Generator: return SpawnAnchorMask.Generator;
                case SpawnKind.Wardrobe: return SpawnAnchorMask.Wardrobe;
                default: return SpawnAnchorMask.None;
            }
        }

        /// <summary>씬 가시화 — 빈 GameObject 라 기즈모가 없으면 배치·검수가 불가능하다.</summary>
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.12f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.3f);
        }
    }
}
