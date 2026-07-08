using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Border.Core;

namespace Border.Pool
{
    /// <summary>
    /// PoolRegistry 가 추적할 수 있도록 풀이 구현해야 하는 비제네릭 인터페이스.
    /// PoolSO&lt;T&gt; 에서 ClearPool() 을 통해 자동 구현된다.
    /// </summary>
    public interface IClearablePool
    {
        /// <summary>
        /// 풀 내부 캐시(스택, 부모 트랜스폼 등)를 모두 비운다.
        /// </summary>
        public void ClearPool();
    }

    /// <summary>
    /// 런타임에 Prewarm 된 풀들을 약한 참조로 등록해두고, 씬 언로드 시 자동으로 ClearPool() 을 호출한다.
    /// ScriptableObject 풀이 destroyed GameObject 참조를 그대로 들고 다음 씬으로 넘어가는 문제를 방지한다.
    /// </summary>
    public static class PoolRegistry
    {
        private static readonly HashSet<IClearablePool> pools = new HashSet<IClearablePool>();
        private static bool hooked;

        /// <summary>
        /// Domain Reload 비활성화 환경(Enter Play Mode Options)에서도 정적 상태를 항상 초기화한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (hooked)
            {
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                hooked = false;
            }
            pools.Clear();
        }

        /// <summary>
        /// 풀을 추적 대상에 추가한다. 첫 등록 시 sceneUnloaded 핸들러를 한 번만 부착한다.
        /// </summary>
        /// <param name="pool">추적할 풀 인스턴스</param>
        public static void Register(IClearablePool pool)
        {
            if (pool == null) return;
            if (!hooked)
            {
                SceneManager.sceneUnloaded += OnSceneUnloaded;
                hooked = true;
            }
            pools.Add(pool);
        }

        /// <summary>
        /// 풀을 추적 대상에서 제거한다. (현재 코드 경로에서는 호출되지 않지만 외부 정리 용으로 노출.)
        /// </summary>
        /// <param name="pool">추적 해제할 풀 인스턴스</param>
        public static void Unregister(IClearablePool pool)
        {
            if (pool == null) return;
            pools.Remove(pool);
        }

        /// <summary>
        /// 씬 언로드 시점에 모든 등록된 풀을 일괄 초기화한다.
        /// ClearPool 도중 컬렉션 변경에 안전하도록 스냅샷을 떠 순회한다.
        /// </summary>
        /// <param name="scene">언로드된 씬</param>
        private static void OnSceneUnloaded(Scene scene)
        {
            if (pools.Count == 0) return;
            var snapshot = new List<IClearablePool>(pools);
            for (int i = 0; i < snapshot.Count; i++)
            {
                try
                {
                    snapshot[i]?.ClearPool();
                }
                catch (System.Exception ex)
                {
                    Log.W($"[PoolRegistry] ClearPool 중 예외: {ex}");
                }
            }
        }
    }
}
