using UnityEngine;

public readonly struct ZombiePerceptionFrame
{
    public readonly bool HasVisualStimulus;
    public readonly bool HasAuditoryStimulus;
    public readonly bool HasInstantVisualDetection;
    public readonly bool HasTrackingStimulus;
    public readonly float VisualGainPerSecond;
    public readonly float AuditoryEffectiveDb;
    public readonly Vector3 VisualPosition;
    public readonly Vector3 AuditoryPosition;
    public readonly Transform PreferredTarget;

    public ZombiePerceptionFrame(
        bool hasVisualStimulus,
        bool hasAuditoryStimulus,
        bool hasInstantVisualDetection,
        bool hasTrackingStimulus,
        float visualGainPerSecond,
        float auditoryEffectiveDb,
        Vector3 visualPosition,
        Vector3 auditoryPosition,
        Transform preferredTarget)
    {
        HasVisualStimulus = hasVisualStimulus;
        HasAuditoryStimulus = hasAuditoryStimulus;
        HasInstantVisualDetection = hasInstantVisualDetection;
        HasTrackingStimulus = hasTrackingStimulus;
        VisualGainPerSecond = visualGainPerSecond;
        AuditoryEffectiveDb = auditoryEffectiveDb;
        VisualPosition = visualPosition;
        AuditoryPosition = auditoryPosition;
        PreferredTarget = preferredTarget;
    }
}
