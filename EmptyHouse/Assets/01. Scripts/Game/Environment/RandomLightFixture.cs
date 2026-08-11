using UnityEngine;

namespace EmptyHouse.Environment
{
    /// <summary>
    /// 조명 픽스처의 초기 상태(소등 / 점등 / 점등+깜빡임)를 확률로 결정하는 연출 컴포넌트.
    /// 월드 좌표 해시를 난수 시드로 쓰므로 맵 시드가 같으면 모든 클라이언트가 같은 결과를 낸다.
    /// 결정된 상태에 맞춰 라이트 on/off, 픽스처 머티리얼(점등/소등), <see cref="FlickeringLight"/> 활성을 함께 맞춘다.
    /// <see cref="FlickeringLight"/>는 선택 의존성이라 없으면 깜빡임 단계를 건너뛰고 점등/소등만 처리한다.
    /// 확률은 <see cref="LightingProfileSO"/>의 전역값을 <see cref="RoomLightGroup"/>이 주입하며,
    /// 개별 픽스처가 예외를 두려면 오버라이드를 켠다.
    /// </summary>
    public class RandomLightFixture : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Light[] targetLights;      // 함께 켜고 끌 자식 라이트들(주광원 + 보조광)
        [SerializeField] private Renderer fixtureRenderer;  // 머티리얼을 교체할 픽스처 메시
        [SerializeField] private FlickeringLight flicker;   // 깜빡임 컴포넌트(주광원에 부착). 선택 의존성 — 비어 있으면 깜빡임 없이 점등/소등만 한다

        [Header("Materials")]
        [SerializeField] private Material onMaterial;   // 점등 머티리얼
        [SerializeField] private Material offMaterial;  // 소등 머티리얼

        [Header("Probability")]
        [SerializeField] private bool overrideChances;                        // 체크하면 프로파일 전역값 대신 아래 값을 쓴다
        [Range(0f, 1f)][SerializeField] private float offChance = 0.25f;      // 소등될 확률
        [Range(0f, 1f)][SerializeField] private float flickerChance = 0.25f;  // 점등된 것 중 깜빡일 확률

        private bool isLit;        // 확률로 정해진 점등 상태(컬링과 무관한 원래 상태)
        private bool isFlickering; // 확률로 정해진 깜빡임 여부
        private bool isCulled;     // 컬링으로 강제 소등 중인지

        /// <summary>
        /// 프로파일의 전역 확률을 주입한다. 오버라이드가 켜져 있으면 무시한다.
        /// <see cref="RoomLightGroup"/>이 Awake에서 호출하므로 <see cref="Start"/>의 추첨보다 앞선다.
        /// </summary>
        /// <param name="off">소등 확률.</param>
        /// <param name="flickerRate">깜빡임 확률.</param>
        public void ApplyChances(float off, float flickerRate)
        {
            if (overrideChances) return;
            offChance = off;
            flickerChance = flickerRate;
        }

        /// <summary>
        /// 배치가 끝난 뒤(Start) 월드 좌표 시드로 상태를 뽑아 라이트·머티리얼·깜빡임을 일괄 적용한다.
        /// Awake가 아닌 Start인 이유는 맵 조립 시점에 월드 좌표가 확정되기 때문이다.
        /// </summary>
        private void Start()
        {
            System.Random rng = new System.Random(ComputeSeed());
            isLit = rng.NextDouble() >= offChance;
            // 난수 소비 순서를 고정하려고 컴포넌트가 없어도 추첨은 그대로 돌린다
            isFlickering = isLit && rng.NextDouble() < flickerChance && flicker != null;

            // 깜빡임 중에는 소등 순간마다 머티리얼도 함께 꺼진 것으로 바꾼다
            if (flicker != null) flicker.LitChanged += ApplyLit;
            ApplyState();
        }

        /// <summary>
        /// 구독을 해제한다.
        /// </summary>
        private void OnDestroy()
        {
            if (flicker != null) flicker.LitChanged -= ApplyLit;
        }

        /// <summary>
        /// 컬링으로 방 전체를 강제 소등하거나 원래 상태로 되돌린다.
        /// 라이트·머티리얼·깜빡임을 함께 맞추므로 컬링이 머티리얼과 어긋나지 않는다.
        /// <see cref="Start"/>보다 먼저 불려도 안전하다(추첨 후 다시 상태를 적용한다).
        /// </summary>
        /// <param name="culled">true면 강제 소등, false면 원래 상태로 복원.</param>
        public void SetCulled(bool culled)
        {
            if (culled == isCulled) return;
            isCulled = culled;
            ApplyState();
        }

        /// <summary>
        /// 추첨 결과와 컬링 상태를 합쳐 라이트·머티리얼·깜빡임에 반영한다.
        /// </summary>
        private void ApplyState()
        {
            if (flicker != null) flicker.enabled = isFlickering && !isCulled;
            ApplyLit(isLit && !isCulled);
        }

        /// <summary>
        /// 점등 여부에 맞춰 라이트 전체를 켜고 끄고 픽스처 머티리얼을 교체한다.
        /// 보조광까지 함께 꺼야 픽스처만 꺼지고 빛은 남는 어색함이 생기지 않는다.
        /// </summary>
        /// <param name="isLit">점등 여부.</param>
        private void ApplyLit(bool isLit)
        {
            for (int i = 0; i < targetLights.Length; i++) targetLights[i].enabled = isLit;
            fixtureRenderer.sharedMaterial = isLit ? onMaterial : offMaterial;
        }

        /// <summary>
        /// 월드 좌표(cm 단위 반올림)를 해시해 클라이언트 간 일치하는 난수 시드를 만든다.
        /// </summary>
        /// <returns>난수 시드.</returns>
        private int ComputeSeed()
        {
            Vector3 p = transform.position;
            int x = Mathf.RoundToInt(p.x * 100f);
            int y = Mathf.RoundToInt(p.y * 100f);
            int z = Mathf.RoundToInt(p.z * 100f);
            unchecked { return (x * 73856093) ^ (y * 19349663) ^ (z * 83492791); }
        }

        /// <summary>
        /// 컴포넌트를 처음 붙일 때 자식 라이트·픽스처 렌더러·깜빡임 컴포넌트를 자동 연결한다(에디터 편의).
        /// </summary>
        private void Reset()
        {
            targetLights = GetComponentsInChildren<Light>(true);
            fixtureRenderer = GetComponentInChildren<Renderer>(true);
            if (targetLights.Length > 0) flicker = targetLights[0].GetComponent<FlickeringLight>();
        }
    }
}
