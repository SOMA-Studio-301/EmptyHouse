using System.Collections.Generic;
using UnityEngine;

public class ZombieSquadSync : MonoBehaviour
{
    [SerializeField] private ZombieController controller;
    [SerializeField] private ZombieRuntimeRegistrySO runtimeRegistry;

    private ZombieSquadSync leader;
    private readonly List<ZombieSquadSync> followers = new List<ZombieSquadSync>();
    private bool snapshotFormed;

    public bool IsFollower => leader != null;
    public ZombieController Leader => leader != null ? leader.controller : null;

    private void OnEnable() => runtimeRegistry?.RegisterSquad(this);
    private void OnDisable() => runtimeRegistry?.UnregisterSquad(this);

    public void ServerFormSquadSnapshot()
    {
        if (controller == null || controller.Data == null || runtimeRegistry == null || leader != null || snapshotFormed) return;

        snapshotFormed = true;
        followers.Clear();

        float radiusSquared = controller.Data.SyncRadius * controller.Data.SyncRadius;
        IReadOnlyList<ZombieSquadSync> squads = runtimeRegistry.Squads;
        for (int i = 0; i < squads.Count; i++)
        {
            ZombieSquadSync other = squads[i];
            if (other == null || other == this || other.controller == null || other.IsFollower || other.snapshotFormed) continue;
            if (!other.controller.IsSpawned || !other.controller.IsServer) continue;
            if ((other.controller.transform.position - controller.transform.position).sqrMagnitude > radiusSquared) continue;

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

    public void ServerReleaseFollowersIfSettled()
    {
        if (leader != null || !snapshotFormed || controller == null) return;
        if (controller.CurrentState != ZombieStateKind.Subside && controller.CurrentState != ZombieStateKind.Wander) return;

        for (int i = 0; i < followers.Count; i++)
        {
            ZombieSquadSync follower = followers[i];
            if (follower == null) continue;
            follower.ServerReleaseFromLeader(this);
        }

        followers.Clear();
        snapshotFormed = false;
    }

    private void ServerReleaseFromLeader(ZombieSquadSync expectedLeader)
    {
        if (leader != expectedLeader) return;
        controller.ServerCopyLeaderSnapshot(expectedLeader.controller);
        leader = null;
    }
}
