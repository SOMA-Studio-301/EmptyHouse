using UnityEngine;

namespace EmptyHouse.Environment
{
    /// <summary>
    /// 조명을 불규칙하게 깜빡이게 하는 연출 컴포넌트.
    /// 펄린 노이즈로 강도를 흔들고, 낮은 확률로 잠깐 소등(블랙아웃)한다.
    /// 같은 오브젝트의 Light에 붙이기만 하면 동작한다(셀프 컨테인드).
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class FlickeringLight : MonoBehaviour
    {
        [SerializeField] private float flickerSpeed = 8f;         // 노이즈 진행 속도
        [SerializeField] private float minIntensityRatio = 0.35f; // 기본 강도 대비 최저 비율
        [SerializeField] private float blackoutChance = 0.003f;   // 프레임당 소등 시작 확률
        [SerializeField] private float blackoutDuration = 0.12f;  // 소등 지속 시간(초)

        private Light targetLight;    // 대상 라이트(자기 자신)
        private float baseIntensity;  // 원래 강도
        private float noiseSeed;      // 인스턴스별 노이즈 시드
        private float blackoutUntil;  // 이 시각까지 소등 유지

        /// <summary>
        /// 대상 라이트와 기준 강도를 캐시하고 인스턴스별 노이즈 시드를 만든다.
        /// </summary>
        private void Awake()
        {
            targetLight = GetComponent<Light>();
            baseIntensity = targetLight.intensity;
            noiseSeed = Random.Range(0f, 100f);
        }

        /// <summary>
        /// 매 프레임 강도를 갱신한다: 블랙아웃 중이면 0, 아니면 펄린 노이즈로 흔들고
        /// 낮은 확률로 블랙아웃을 시작한다.
        /// </summary>
        private void Update()
        {
            if (Time.time < blackoutUntil)
            {
                targetLight.intensity = 0f;
                return;
            }

            if (Random.value < blackoutChance)
            {
                blackoutUntil = Time.time + blackoutDuration * Random.Range(0.5f, 2f);
                targetLight.intensity = 0f;
                return;
            }

            float n = Mathf.PerlinNoise(noiseSeed, Time.time * flickerSpeed);
            targetLight.intensity = baseIntensity * Mathf.Lerp(minIntensityRatio, 1f, n);
        }
    }
}
