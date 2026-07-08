using UnityEngine;
using Border.Pool;

namespace Border.Audio
{
    [CreateAssetMenu(fileName = "SoundEmitterFactory", menuName = "Factory/Sound Emitter")]
    public class SoundEmitterFactorySO : FactorySO<SoundEmitter>
    {
        [SerializeField] private SoundEmitter prefab;

        public override SoundEmitter Create()
        {
            return Instantiate(prefab);
        }
    }
}
