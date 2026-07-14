using System.Collections.Generic;
using UnityEngine;

public class ZombieSquadSync : MonoBehaviour
{
    [SerializeField] private ZombieController controller;
    [SerializeField] private LayerMask zombieMask = ~0;

    private ZombieController leader;
    private readonly List<ZombieController> followers = new List<ZombieController>();
    private bool snapshotFormed;

    public bool IsFollower => leader != null;
    public ZombieController Leader => leader;

    private void OnValidate()
    {
        if (controller == null) controller = GetComponent<ZombieController>();
    }

    public void ServerFormSquadSnapshot()
    {
        if (controller == null || controller.Data == null || leader != null || snapshotFormed) return;

        snapshotFormed = true;
        followers.Clear();
        Collider[] hits = Physics.OverlapSphere(controller.transform.position, controller.Data.SyncRadius, zombieMask, QueryTriggerInteraction.Ignore);
        var unique = new HashSet<ZombieController>();

        for (int i = 0; i < hits.Length; i++)
        {
            ZombieController other = hits[i].GetComponentInParent<ZombieController>();
            if (other == null || other == controller || other.IsFollower || !unique.Add(other)) continue;

            ZombieSquadSync otherSync = other.GetComponent<ZombieSquadSync>();
            if (otherSync == null || !otherSync.ServerAssignLeader(controller)) continue;
            followers.Add(other);
        }
    }

    private bool ServerAssignLeader(ZombieController newLeader)
    {
        if (newLeader == null || leader != null || controller == null) return false;
        leader = newLeader;
        controller.ServerCopyLeaderSnapshot(newLeader);
        return true;
    }

    public void ServerSynchronizeFollower()
    {
        if (leader == null || controller == null) return;
        controller.ServerCopyLeaderSnapshot(leader);
    }

    public void ServerReleaseFollowersIfSettled()
    {
        if (leader != null || !snapshotFormed || controller == null) return;
        if (controller.CurrentState != ZombieStateKind.Subside && controller.CurrentState != ZombieStateKind.Wander) return;

        for (int i = 0; i < followers.Count; i++)
        {
            ZombieController follower = followers[i];
            if (follower == null) continue;
            ZombieSquadSync sync = follower.GetComponent<ZombieSquadSync>();
            if (sync != null) sync.ServerReleaseFromLeader(controller);
        }

        followers.Clear();
        snapshotFormed = false;
    }

    private void ServerReleaseFromLeader(ZombieController expectedLeader)
    {
        if (leader != expectedLeader) return;
        controller.ServerCopyLeaderSnapshot(expectedLeader);
        leader = null;
    }
}
