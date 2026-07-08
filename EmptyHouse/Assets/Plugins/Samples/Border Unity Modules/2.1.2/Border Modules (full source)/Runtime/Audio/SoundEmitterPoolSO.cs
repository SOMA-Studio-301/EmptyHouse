using UnityEngine;
using Border.Pool;

namespace Border.Audio
{
    [CreateAssetMenu(fileName = "NewSoundEmitterPool", menuName = "Pool/Sound Emitter")]
    public class SoundEmitterPoolSO : ComponentPoolSO<SoundEmitter>
    {
        [SerializeField] private SoundEmitterFactorySO factory;
        [SerializeField] private bool hasLimit = false;
        [SerializeField] private int limitActivation = 7;
        public override IFactory<SoundEmitter> Factory => factory;
        public bool HasLimit => hasLimit;
        public int LimitActivation => limitActivation;

        private int activateCount = 0;

        /// <summary>
        /// SoundEmitter의 활성화 수를 조절한다.
        /// 너무 많으면 사운드가 터지기 때문
        /// </summary>
        public override SoundEmitter Request()
        {
            if (hasLimit)
            {
                if (activateCount >= limitActivation)
                    return null;
                activateCount += 1;    
            }
            return base.Request();
        }

        public override void Return(SoundEmitter member)
        {
            if (hasLimit)
            {
                activateCount -= 1;    
            }
            base.Return(member);
        }

        public override void ClearPool()
        {
            if (hasLimit)
            {
                activateCount = 0;    
            }
            base.ClearPool();
        }
    }
}
