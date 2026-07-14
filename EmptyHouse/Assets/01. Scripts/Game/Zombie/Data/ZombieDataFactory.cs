using UnityEngine;

public static class ZombieDataFactory
{
    public static ZombieDataSO CreateDefaultFromName(string zombieName)
    {
        string lower = zombieName.ToLowerInvariant();

        if (lower.Contains("listener"))
        {
            return CreateDefault(ZombieType.Listener);
        }

        if (lower.Contains("watcher"))
        {
            return CreateDefault(ZombieType.Watcher);
        }

        return CreateDefault(ZombieType.Walker);
    }

    public static ZombieDataSO CreateDefault(ZombieType type)
    {
        ZombieDataSO data = ScriptableObject.CreateInstance<ZombieDataSO>();
        data.ZombieType = type;

        data.WanderSpeed = 1.2f;
        data.PatrolRadius = 5f;
        data.PatrolSampleAttempts = 12;
        data.AlertSpeed = 0f;
        data.InvestigateBaseSpeed = 1.6f;
        data.InvestigateCapSpeed = 3.5f;
        data.InvestigateDbToSpeed = 0.05f;
        data.ChaseSpeed = 3f;
        data.SubsideSpeed = 1.2f;
        data.TurnSpeedDegreesPerSecond = 120f;
        data.TurnDeadZoneDegrees = 8f;
        data.DestinationRefreshDistance = 0.3f;
        data.AttackRange = 1.5f;
        data.AttackWindupSeconds = 0.3f;
        data.AlertMotionSeconds = 0.5f;
        data.ChaseToInvestigateSeconds = 2f;
        data.AttackLockSeconds = 5f;
        data.InvestigateToWanderSeconds = 5f;
        data.SuspicionGraceSeconds = 1.5f;
        data.CoolRate = 8f;
        data.SyncRadius = 15f;
        data.VisGainBase = 25f;
        data.VisInstantRange = 5f;
        data.VisDistNear = 2f;
        data.VisDistFar = 0.5f;
        data.VisFront = 1.5f;
        data.VisLightBright = 1.3f;
        data.VisLightDark = 0.7f;
        data.VisLightFlashlight = 1.8f;
        data.VisPoseWalk = 1f;
        data.VisPoseCrouch = 0.6f;
        data.VisPoseIdle = 0.4f;
        data.HearFloor = 60f;
        data.HearingStimulusSeconds = 0.5f;
        data.SoundFalloffDbPerMeter = 1.5f;
        data.DefaultWallOcclusionDb = 20f;

        switch (type)
        {
            case ZombieType.Listener:
                data.VisionAngle = 90f;
                data.VisionDistance = 10f;
                data.HearMinDb = 20f;
                data.HearDetectDb = 60f;
                break;
            case ZombieType.Watcher:
                data.VisionAngle = 140f;
                data.VisionDistance = 22f;
                data.HearMinDb = 45f;
                data.HearDetectDb = 85f;
                data.VisLightBright = 0f;
                data.VisLightDark = 0f;
                break;
            default:
                data.VisionAngle = 110f;
                data.VisionDistance = 15f;
                data.HearMinDb = 30f;
                data.HearDetectDb = 70f;
                break;
        }

        return data;
    }
}
