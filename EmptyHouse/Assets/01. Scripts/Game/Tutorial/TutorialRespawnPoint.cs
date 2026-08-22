using UnityEngine;

/// <summary>
/// S2_3 실패 리스폰 지점 마커 — 통과 복도 시작 (튜토리얼.md 4-4-4 · 5장 tut_respawn_point).
/// 위치·방향만 제공하는 씬 마커이며, 리스폰 실행은 TutorialDirector 가 한다.
/// </summary>
public sealed class TutorialRespawnPoint : MonoBehaviour
{
    public Vector3 Position => transform.position;    // 리스폰 위치
    public Quaternion Rotation => transform.rotation; // 리스폰 방향
}
