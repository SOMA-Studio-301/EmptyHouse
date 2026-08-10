using System.Collections.Generic;
using UnityEngine;

namespace EmptyHouse.Environment
{
    /// <summary>
    /// 방 프리팹 루트에 붙는 조명 그룹. 방 안의 조명을 한 번만 캐시해두고
    /// <see cref="MapLightCuller"/>의 요청에 따라 통째로 소등/복원한다.
    /// 라이트와 머티리얼을 함께 소유한 <see cref="RandomLightFixture"/>에는 소등을 위임하고,
    /// 픽스처에 속하지 않은 맨 라이트만 직접 토글한다 — 그래야 컬링이 머티리얼과 어긋나지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoomLightGroup : MonoBehaviour
    {
        [SerializeField] private LightingProfileSO profile; // 조명 프로파일(컬링 파라미터·점등 확률 출처)

        private RandomLightFixture[] fixtures; // 라이트+머티리얼을 함께 소유한 픽스처
        private Light[] looseLights;           // 픽스처에 속하지 않은 맨 라이트
        private bool[] restoreEnabled;         // 맨 라이트의 소등 직전 점등 상태
        private bool isCulled;                 // 현재 컬링(소등) 여부

        /// <summary>조명 프로파일.</summary>
        public LightingProfileSO Profile => profile;

        /// <summary>현재 컬링되어 소등된 상태인지.</summary>
        public bool IsCulled => isCulled;

        /// <summary>
        /// 방 안의 픽스처와 맨 라이트를 나눠 캐시하고, 픽스처에 전역 점등 확률을 주입한다.
        /// 픽스처의 추첨은 Start에서 일어나므로 Awake의 주입이 항상 앞선다.
        /// </summary>
        private void Awake()
        {
            fixtures = GetComponentsInChildren<RandomLightFixture>(true);
            for (int i = 0; i < fixtures.Length; i++) fixtures[i].ApplyChances(profile.OffChance, profile.FlickerChance);

            List<Light> loose = new List<Light>();
            foreach (Light light in GetComponentsInChildren<Light>(true))
                if (light.GetComponentInParent<RandomLightFixture>(true) == null) loose.Add(light);

            looseLights = loose.ToArray();
            restoreEnabled = new bool[looseLights.Length];
        }

        /// <summary>
        /// 방 전체를 소등하거나 원래 상태로 복원한다. 상태가 같으면 아무것도 하지 않는다.
        /// </summary>
        /// <param name="culled">true면 소등, false면 복원.</param>
        public void SetCulled(bool culled)
        {
            if (culled == isCulled) return;
            isCulled = culled;

            for (int i = 0; i < fixtures.Length; i++) fixtures[i].SetCulled(culled);

            if (culled)
            {
                for (int i = 0; i < looseLights.Length; i++)
                {
                    restoreEnabled[i] = looseLights[i].enabled;
                    looseLights[i].enabled = false;
                }
                return;
            }

            for (int i = 0; i < looseLights.Length; i++) looseLights[i].enabled = restoreEnabled[i];
        }

        /// <summary>
        /// 거리 판정에 쓸 기준점을 구한다 — 방 안 라이트들의 평균 위치.
        /// 맵 조립이 끝난 뒤 컬러가 1회만 호출한다(조립 중에는 월드 좌표가 확정되지 않는다).
        /// </summary>
        /// <returns>기준 월드 좌표. 라이트가 없으면 자기 위치.</returns>
        public Vector3 ComputeCenter()
        {
            Light[] all = GetComponentsInChildren<Light>(true);
            if (all.Length == 0) return transform.position;

            Vector3 sum = Vector3.zero;
            for (int i = 0; i < all.Length; i++) sum += all[i].transform.position;
            return sum / all.Length;
        }
    }
}
