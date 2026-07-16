using UnityEngine;

namespace EmptyHouse.NoiseSystem
{
    [CreateAssetMenu(fileName = "SO_NoisePropagationSettings", menuName = "Game/Noise V2/Propagation Settings")]
    public sealed class NoisePropagationSettingsSO : ScriptableObject
    {
        [Header("Aggregation")]
        [Min(0.01f)] public float SumWindowSeconds = 0.5f;

        [Header("Propagation")]
        [Min(0.01f)] public float FalloffDbPerMeter = 1.5f;
        [Min(0f)] public float LowestHearingThresholdDb = 12f;
        [Min(0f)] public float WallAttenuationDb = 20f;
        [Min(0f)] public float ClosedDoorAttenuationDb = 12f;
        [Min(0f)] public float OpenDoorAttenuationDb = 0f;

        [Header("Investigation")]
        [Min(0f)] public float InvestigateBaseSpeed = 1.6f;
        [Min(0f)] public float InvestigateDbToSpeed = 0.05f;
        [Min(0f)] public float InvestigateMaxSpeed = 3.5f;
    }
}
