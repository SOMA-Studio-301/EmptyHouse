using Border.Core;
using UnityEngine;

namespace EmptyHouse.MapGen.Runtime
{
    /// <summary>
    /// 안내도 모델 발행 채널(EH-62) — 드라이버가 로컬 조립 직후 빌드한 <see cref="MapOverviewModel"/> 을 싣는다.
    /// UI(UIMapOverview)는 이 채널만 구독한다 — 씬 오브젝트(드라이버) 직참조 금지(자기완결 프리팹 원칙).
    /// 클라 로컬 채널 — 서버 전용 X7 채널(onMapAssembledServer)과 별개로 각 클라이언트가 자기 조립 완료 시 발행한다.
    /// </summary>
    [CreateAssetMenu(fileName = "SO_Event_MapOverviewReady", menuName = "EmptyHouse/Events/Map Overview Ready")]
    public sealed class MapOverviewEventChannelSO : ScriptableObject
    {
        public event System.Action<MapOverviewModel> OnEventRaised; // 구독 — 발행 시점의 모델을 받는다

        /// <summary>모델을 실어 발행한다. 구독자가 없으면 무시된다.</summary>
        /// <param name="model">빌드된 안내도 모델.</param>
        public void RaiseEvent(MapOverviewModel model)
        {
            Log.D("[MapOverviewEventChannelSO] RaiseEvent");
            OnEventRaised?.Invoke(model);
        }
    }
}
