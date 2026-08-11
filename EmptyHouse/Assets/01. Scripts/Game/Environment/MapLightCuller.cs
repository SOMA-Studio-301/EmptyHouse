using Border.Core;
using UnityEngine;

namespace EmptyHouse.Environment
{
    /// <summary>
    /// 생성된 맵 루트에 붙어 카메라 주변 방의 조명만 켜두는 컬러.
    /// 절차 맵은 라이트가 250개를 넘겨 URP Forward+ 가시 라이트 한도(데스크톱 256)에 걸리고,
    /// 비닝 비용도 켜진 라이트 수에 비례해 매 프레임 발생한다. 그래서 시야 밖 방은 소등해 둔다.
    /// 기준은 플레이어가 아니라 <see cref="Camera.main"/> — 사망 시 관전 카메라가 몸에서 떨어져
    /// 다른 생존자를 따라가므로, 시점을 따라가야 보는 곳이 밝다.
    /// 순수 로컬 연출이라 서버·복제와 무관하다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapLightCuller : MonoBehaviour
    {
        private const float floorCullHalfHeight = 3f; // 층 컬링 반높이(m) — 시점과 방 중심의 Y 차가 이보다 크면 다른 층으로 보고 강제 소등(N3 — 층고 6m 의 절반)

        private RoomLightGroup[] groups;   // 맵 안의 방 그룹(1회 수집)
        private Vector3[] centers;         // 그룹별 거리 기준점(1회 계산)
        private LightingProfileSO profile; // 컬링 파라미터 출처 — 조립기가 Initialize 로 주입
        private Transform viewer;          // 기준 시점(Camera.main)
        private float nextUpdateAt;        // 다음 갱신 시각

        /// <summary>
        /// 조명 프로파일을 주입한다. 이 컴포넌트는 조립기가 AddComponent 로 붙이므로
        /// 인스펙터 직렬화 경로가 없다 — 부착 직후 반드시 호출해야 한다.
        /// </summary>
        /// <param name="lightingProfile">컬링 파라미터를 담은 프로파일.</param>
        public void Initialize(LightingProfileSO lightingProfile)
        {
            profile = lightingProfile;
        }

        /// <summary>
        /// 첫 프레임에 그룹·기준점을 캐시하고, 이후 갱신 주기마다 거리로 소등/점등을 갱신한다.
        /// Awake가 아닌 Update에서 수집하는 이유: 조립기가 Instantiate 직후에 방을 이동시키고,
        /// RandomLightFixture도 Start에서 점등을 확정하므로 그 뒤여야 값이 맞다.
        /// </summary>
        private void Update()
        {
            if (groups == null && !TryInitialize()) return;

            if (Time.time < nextUpdateAt) return;
            nextUpdateAt = Time.time + profile.CullUpdateInterval;

            if (viewer == null)
            {
                // 넷코드상 맵 조립이 로컬 플레이어 스폰보다 먼저 끝날 수 있다 — 카메라는 의도적으로 optional.
                Camera cam = Camera.main;
                if (cam == null) return; // 전부 소등된 채로 다음 틱에 재시도
                viewer = cam.transform;
            }

            Cull();
        }

        /// <summary>
        /// 방 그룹과 기준점을 수집하고 전부 소등 상태로 시작한다.
        /// </summary>
        /// <returns>수집 성공 여부(방이 없으면 컬러를 끈다).</returns>
        private bool TryInitialize()
        {
            groups = GetComponentsInChildren<RoomLightGroup>(true);
            if (groups.Length == 0)
            {
                Log.D("[LightCull] RoomLightGroup 이 없어 컬러를 비활성화한다");
                enabled = false;
                return false;
            }

            centers = new Vector3[groups.Length];
            for (int i = 0; i < groups.Length; i++)
            {
                centers[i] = groups[i].ComputeCenter();
                groups[i].SetCulled(true); // 시점이 잡히기 전 전부 켜져 있는 공백을 없앤다
            }

            return true;
        }

        /// <summary>
        /// 시점과의 제곱거리로 방을 점등/소등한다. 켜는 반경보다 끄는 반경을 크게 둬(히스테리시스)
        /// 경계에서 왔다갔다할 때 깜빡이지 않게 하고, 동시 점등 방 수가 상한을 넘으면 먼 방부터 끈다.
        /// </summary>
        private void Cull()
        {
            Vector3 p = viewer.position;
            float enableSqr = profile.CullEnableRadius * profile.CullEnableRadius;
            float disableSqr = profile.CullDisableRadius * profile.CullDisableRadius;

            int active = 0;
            for (int i = 0; i < groups.Length; i++)
            {
                float d2 = (centers[i] - p).sqrMagnitude;

                // 층 게이트(N3 — 다층 M9-8): 다른 층 방은 수직 6m 차이뿐이라 3D 반경으로는 켜진다 —
                // 위아래 층 라이트가 바닥 슬래브 너머로 새며 Forward+ 예산만 먹으므로 층이 다르면 강제 소등
                bool otherFloor = Mathf.Abs(centers[i].y - p.y) > floorCullHalfHeight;
                if (groups[i].IsCulled)
                {
                    if (!otherFloor && d2 <= enableSqr) groups[i].SetCulled(false);
                }
                else if (otherFloor || d2 > disableSqr)
                {
                    groups[i].SetCulled(true);
                }

                if (!groups[i].IsCulled) active++;
            }

            // 상한 초과분은 먼 방부터 소등 — Forward+ 라이트 한도 안전판.
            // 초과가 몇 개뿐이라 정렬 없이 최원거리 반복 선택으로 처리한다(할당 없음).
            int max = profile.CullMaxActiveRooms;
            while (active > max)
            {
                int farthest = -1;
                float farthestSqr = -1f;
                for (int i = 0; i < groups.Length; i++)
                {
                    if (groups[i].IsCulled) continue;
                    float d2 = (centers[i] - p).sqrMagnitude;
                    if (d2 <= farthestSqr) continue;
                    farthestSqr = d2;
                    farthest = i;
                }

                groups[farthest].SetCulled(true);
                active--;
            }
        }
    }
}
