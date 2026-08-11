using System.Collections.Generic;
using UnityEngine;

public class ZombieSquadSync : MonoBehaviour
{
    [SerializeField] private ZombieController controller;
    [SerializeField] private ZombieRuntimeRegistrySO runtimeRegistry;

    private ZombieSquadSync leader;
    private readonly List<ZombieSquadSync> followers = new List<ZombieSquadSync>();
    private bool snapshotFormed;
    private float snapshotAge;

    public bool IsFollower => leader != null;
    public ZombieController Leader => leader != null ? leader.controller : null;

    private void OnEnable() => runtimeRegistry?.RegisterSquad(this);
    private void OnDisable() => runtimeRegistry?.UnregisterSquad(this);

    public void ServerFormSquadSnapshot()
    {
        if (controller == null || controller.Data == null || runtimeRegistry == null || leader != null || snapshotFormed) return;

        snapshotFormed = true;
        snapshotAge = 0f;
        followers.Clear();

        float radiusSquared = controller.Data.SyncRadius * controller.Data.SyncRadius;
        IReadOnlyList<ZombieSquadSync> squads = runtimeRegistry.Squads;
        for (int i = 0; i < squads.Count; i++)
        {
            ZombieSquadSync other = squads[i];
            if (other == null || other == this || other.controller == null || other.IsFollower || other.snapshotFormed) continue;
            if (!other.controller.IsSpawned || !other.controller.IsServer) continue;

            // 동조는 층 미관통(레벨디자인 3-0·R5) — 3D 구면이면 층고 6m 위아래 좀비까지 삼킨다.
            // 판정 = 수평 반경 + 층 일치(Y 차 3m 미만 — 층고 절반)
            Vector3 delta = other.controller.transform.position - controller.transform.position;
            if (Mathf.Abs(delta.y) >= 3f) continue;
            delta.y = 0f;
            if (delta.sqrMagnitude > radiusSquared) continue;

            if (!other.ServerAssignLeader(this)) continue;
            followers.Add(other);
        }
    }

    private bool ServerAssignLeader(ZombieSquadSync newLeader)
    {
        if (newLeader == null || newLeader.controller == null || leader != null || snapshotFormed || controller == null) return false;
        leader = newLeader;
        controller.ServerCopyLeaderSnapshot(newLeader.controller);
        return true;
    }

    public void ServerSynchronizeFollower()
    {
        if (leader == null || controller == null) return;
        controller.ServerCopyLeaderSnapshot(leader.controller);
    }

    /// <summary>
    /// 리더가 진정됐거나 스냅샷이 너무 오래됐으면 팔로워를 해제한다.
    /// 팔로워는 자기 지각을 돌리지 않고 리더만 복사하므로, 해제 조건을 리더 상태에만 걸면
    /// 리더 하나가 고착됐을 때 무리 전체가 영구히 마비된다 — 그래서 leash 타임아웃을 함께 둔다.
    /// </summary>
    /// <param name="deltaTime">이번 서버 프레임의 경과 시간.</param>
    public void ServerReleaseFollowersIfSettled(float deltaTime)
    {
        if (leader != null || !snapshotFormed || controller == null) return;

        snapshotAge += deltaTime;

        bool settled = controller.CurrentState == ZombieStateKind.Subside
            || controller.CurrentState == ZombieStateKind.Wander;
        bool expired = controller.Data != null && snapshotAge >= controller.Data.SquadLeashSeconds;
        if (!settled && !expired) return;

        for (int i = 0; i < followers.Count; i++)
        {
            ZombieSquadSync follower = followers[i];
            if (follower == null) continue;
            follower.ServerReleaseFromLeader(this);
        }

        followers.Clear();
        snapshotFormed = false;
        snapshotAge = 0f;
    }

    private void ServerReleaseFromLeader(ZombieSquadSync expectedLeader)
    {
        if (leader != expectedLeader) return;
        controller.ServerCopyLeaderSnapshot(expectedLeader.controller);
        leader = null;
    }
}
