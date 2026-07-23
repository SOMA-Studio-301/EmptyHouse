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

    // 추적 대상은 Transform 이 아니라 지각 소스로 넘긴다. 좀비가 한 번 문 타겟의
    // 사망(관전)·위장을 매 프레임 재검증하려면 상태를 읽을 수 있는 원본이 필요하다 —
    // Transform 만 들고 있으면 시신을 영원히 타겟으로 붙들게 된다.
    public readonly IZombiePerceptionSource PreferredTarget;

    public ZombiePerceptionFrame(
        bool hasVisualStimulus,
        bool hasAuditoryStimulus,
        bool hasInstantVisualDetection,
        bool hasTrackingStimulus,
        float visualGainPerSecond,
        float auditoryEffectiveDb,
        Vector3 visualPosition,
        Vector3 auditoryPosition,
        IZombiePerceptionSource preferredTarget)
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
