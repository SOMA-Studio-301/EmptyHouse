using EmptyHouse.MapGen.Core;
using UnityEngine;

namespace EmptyHouse.MapGen.Runtime
{
    /// <summary>
    /// 절차 맵 생성 파라미터의 단일 출처(9절) — 런타임 드라이버와 에디터 프리뷰 빌더가 같은 에셋을 읽는다.
    /// 이전에는 코드 초기값(프리뷰)·프리팹 직렬화값(런타임)·씬 오버라이드가 따로 놀아
    /// 프리뷰에서 본 맵과 실제 플레이 맵이 달랐다(예: 복도 경유 100% vs 50%). 그 드리프트를 구조적으로 없앤다.
    /// 소비자는 이 에셋을 직접 넘기지 않고 스냅샷(복제본)을 만들어 시드를 박는다 — 에셋 오염 방지.
    /// </summary>
    [CreateAssetMenu(fileName = "SO_MapGenParams", menuName = "EmptyHouse/MapGen/Gen Params")]
    public sealed class MapGenParamsSO : ScriptableObject
    {
        public MapGenParams Params = new MapGenParams(); // 생성 파라미터 본체 — 커스텀 에디터(MapGenParamsSOEditor)가 그룹·검증과 함께 그린다
    }
}
