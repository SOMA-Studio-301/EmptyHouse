using UnityEngine;

namespace EmptyHouse.Environment
{
    /// <summary>
    /// 강도 커브 한 루프를 반복 재생해 조명을 깜빡이게 하는 연출 컴포넌트.
    /// 커브의 X축은 초, Y축은 기본 강도 대비 배율이며, 루프 길이는 커브 마지막 키의 시간이다.
    /// 값이 0 이하인 구간은 소등으로 간주해 라이트를 끄고 <see cref="LitChanged"/>로 알린다.
    /// 같은 오브젝트의 Light에 붙이기만 하면 동작한다(셀프 컨테인드).
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class FlickeringLight : MonoBehaviour
    {
        [Tooltip("X=초, Y=기본 강도 대비 배율. 마지막 키의 시간이 루프 길이가 된다. Y가 0 이하인 구간은 소등.")]
        [SerializeField] private AnimationCurve intensityCurve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(1.6f, 0.92f),
            new Keyframe(2.4f, 1f),
            new Keyframe(3.8f, 1f),
            new Keyframe(3.82f, 0f),
            new Keyframe(3.94f, 0f),
            new Keyframe(3.96f, 1f),
            new Keyframe(4.6f, 1f),
            new Keyframe(4.62f, 0f),
            new Keyframe(4.7f, 0f),
            new Keyframe(4.72f, 1f),
            new Keyframe(6f, 1f)); // 강도 루프(기본: 6초에 짧은 소등 2회)

        [SerializeField] private bool randomizePhase = true; // 인스턴스마다 루프 시작 위상을 랜덤화

        /// <summary>소등/점등이 전환될 때 발생한다(true=점등).</summary>
        public event System.Action<bool> LitChanged;

        private Light targetLight;   // 대상 라이트(자기 자신)
        private float baseIntensity; // 원래 강도(커브 배율의 기준)
        private float phaseOffset;   // 루프 시작 위상
        private bool isLit = true;   // 현재 점등 상태

        /// <summary>루프 길이(초). 커브 마지막 키의 시간.</summary>
        private float LoopDuration => intensityCurve.length > 0 ? intensityCurve[intensityCurve.length - 1].time : 0f;

        /// <summary>
        /// 대상 라이트와 기준 강도를 캐시하고 인스턴스별 루프 위상을 정한다.
        /// </summary>
        private void Awake()
        {
            targetLight = GetComponent<Light>();
            baseIntensity = targetLight.intensity;
            if (randomizePhase) phaseOffset = Random.Range(0f, Mathf.Max(LoopDuration, 0.0001f));
        }

        /// <summary>
        /// 점등 상태로 초기화한다.
        /// </summary>
        private void OnEnable()
        {
            isLit = true;
            targetLight.enabled = true;
            targetLight.intensity = baseIntensity;
        }

        /// <summary>
        /// 꺼질 때 강도와 점등 상태를 원래대로 되돌린다(소등 구간에서 비활성화되어 꺼진 채 멈추는 것 방지).
        /// </summary>
        private void OnDisable()
        {
            targetLight.intensity = baseIntensity;
            isLit = true;
        }

        /// <summary>
        /// 루프 위치의 커브 값을 강도에 반영하고, 0 이하 구간에서는 소등으로 전환한다.
        /// </summary>
        private void Update()
        {
            float loop = LoopDuration;
            if (loop <= 0f) return;

            float value = intensityCurve.Evaluate(Mathf.Repeat(Time.time + phaseOffset, loop));
            bool lit = value > 0f;

            if (lit != isLit) SetLit(lit);
            if (lit) targetLight.intensity = baseIntensity * value;
        }

        /// <summary>
        /// 점등 상태를 바꾸고 라이트를 켜거나 끈 뒤 구독자에게 알린다.
        /// </summary>
        /// <param name="lit">점등 여부.</param>
        private void SetLit(bool lit)
        {
            isLit = lit;
            targetLight.enabled = lit;
            LitChanged?.Invoke(lit);
        }
    }
}
