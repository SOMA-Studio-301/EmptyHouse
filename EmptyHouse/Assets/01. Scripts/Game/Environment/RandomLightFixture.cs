using UnityEngine;

namespace EmptyHouse.Environment
{
    /// <summary>
    /// 조명 픽스처의 초기 상태(소등 / 점등 / 점등+깜빡임)를 확률로 결정하는 연출 컴포넌트.
    /// 월드 좌표 해시를 난수 시드로 쓰므로 맵 시드가 같으면 모든 클라이언트가 같은 결과를 낸다.
    /// 결정된 상태에 맞춰 라이트 on/off, 픽스처 머티리얼(점등/소등), <see cref="FlickeringLight"/> 활성을 함께 맞춘다.
    /// </summary>
    public class RandomLightFixture : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Light targetLight;         // 제어할 자식 라이트
        [SerializeField] private Renderer fixtureRenderer;  // 머티리얼을 교체할 픽스처 메시
        [SerializeField] private FlickeringLight flicker;   // 깜빡임 컴포넌트(자식 라이트에 부착)

        [Header("Materials")]
        [SerializeField] private Material onMaterial;   // 점등 머티리얼
        [SerializeField] private Material offMaterial;  // 소등 머티리얼

        [Header("Probability")]
        [Range(0f, 1f)][SerializeField] private float offChance = 0.25f;      // 소등될 확률
        [Range(0f, 1f)][SerializeField] private float flickerChance = 0.25f;  // 점등된 것 중 깜빡일 확률

        /// <summary>
        /// 배치가 끝난 뒤(Start) 월드 좌표 시드로 상태를 뽑아 라이트·머티리얼·깜빡임을 일괄 적용한다.
        /// Awake가 아닌 Start인 이유는 맵 조립 시점에 월드 좌표가 확정되기 때문이다.
        /// </summary>
        private void Start()
        {
            System.Random rng = new System.Random(ComputeSeed());
            bool isOn = rng.NextDouble() >= offChance;
            bool isFlickering = isOn && rng.NextDouble() < flickerChance;

            targetLight.enabled = isOn;
            ApplyLit(isOn);

            // 깜빡임 중에는 소등 순간마다 머티리얼도 함께 꺼진 것으로 바꾼다
            flicker.LitChanged += ApplyLit;
            flicker.enabled = isFlickering;
        }

        /// <summary>
        /// 구독을 해제한다.
        /// </summary>
        private void OnDestroy()
        {
            flicker.LitChanged -= ApplyLit;
        }

        /// <summary>
        /// 점등 여부에 맞춰 픽스처 머티리얼을 교체한다.
        /// </summary>
        /// <param name="isLit">점등 여부.</param>
        private void ApplyLit(bool isLit)
        {
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
            targetLight = GetComponentInChildren<Light>(true);
            fixtureRenderer = GetComponent<Renderer>();
            if (targetLight != null) flicker = targetLight.GetComponent<FlickeringLight>();
        }
    }
}
