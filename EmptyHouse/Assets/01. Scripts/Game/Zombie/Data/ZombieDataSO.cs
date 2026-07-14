using UnityEngine;

[CreateAssetMenu(fileName = "SO_Zombie_", menuName = "Game/Zombie Data")]
public class ZombieDataSO : ScriptableObject
{
    [Header("Identity")]
    public ZombieType ZombieType = ZombieType.Walker;

    [Header("Vision")]
    public float VisionAngle = 110f;
    public float VisionDistance = 15f;
    public float VisInstantRange = 5f;
    public float VisGainBase = 25f;
    public float VisDistNear = 2f;
    public float VisDistFar = 0.5f;
    public float VisFront = 1.5f;
    public float VisLightBright = 1.3f;
    public float VisLightDark = 0.7f;
    public float VisLightFlashlight = 1.8f;
    public float VisPoseWalk = 1f;
    public float VisPoseCrouch = 0.6f;
    public float VisPoseIdle = 0.4f;

    [Header("Hearing")]
    public float HearMinDb = 30f;
    public float HearDetectDb = 70f;
    public float HearFloor = 60f;
    public float HearingStimulusSeconds = 0.5f;
    public float SoundFalloffDbPerMeter = 1.5f;
    public float DefaultWallOcclusionDb = 20f;

    [Header("Movement")]
    public float WanderSpeed = 1.2f;
    [Min(0f)] public float PatrolRadius = 5f;
    [Min(1)] public int PatrolSampleAttempts = 12;
    public float AlertSpeed = 0f;
    public float InvestigateBaseSpeed = 1.6f;
    public float InvestigateCapSpeed = 3.5f;
    public float InvestigateDbToSpeed = 0.05f;
    public float ChaseSpeed = 3f;
    public float SubsideSpeed = 1.2f;
    [Min(0f)] public float TurnSpeedDegreesPerSecond = 120f;
    [Range(0f, 45f)] public float TurnDeadZoneDegrees = 8f;
    [Min(0.01f)] public float DestinationRefreshDistance = 0.3f;

    [Header("Attack")]
    public float AttackRange = 1.5f;
    public float AttackWindupSeconds = 0.3f;

    [Header("Timing")]
    public float AlertMotionSeconds = 0.5f;
    public float ChaseToInvestigateSeconds = 2f;
    public float AttackLockSeconds = 5f;
    public float InvestigateToWanderSeconds = 5f;
    public float SuspicionGraceSeconds = 1.5f;
    public float WatcherBlindRecoverySeconds = 1f;

    [Header("Suspicion")]
    public float ThAlert = 30f;
    public float ThInvestigate = 60f;
    public float CoolRate = 8f;
    public float SyncRadius = 15f;
}
