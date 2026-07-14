using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Border-style runtime set for zombie-domain scene members.
/// Scene objects register themselves, so hot paths never scan hierarchies or physics colliders
/// to discover another logic component.
/// </summary>
[CreateAssetMenu(fileName = "SO_Zombie_RuntimeRegistry", menuName = "Game/Zombie Runtime Registry")]
public sealed class ZombieRuntimeRegistrySO : ScriptableObject
{
    private readonly List<ZombieSquadSync> squads = new List<ZombieSquadSync>();
    private readonly List<IZombiePerceptionSource> perceptionSources = new List<IZombiePerceptionSource>();
    private readonly Dictionary<Collider, ZombieSoundOccluder> soundOccluders = new Dictionary<Collider, ZombieSoundOccluder>();

    public IReadOnlyList<ZombieSquadSync> Squads => squads;
    public IReadOnlyList<IZombiePerceptionSource> PerceptionSources => perceptionSources;

    public void RegisterSquad(ZombieSquadSync squad)
    {
        if (squad != null && !squads.Contains(squad)) squads.Add(squad);
    }

    public void UnregisterSquad(ZombieSquadSync squad) => squads.Remove(squad);

    public void RegisterPerceptionSource(IZombiePerceptionSource source)
    {
        if (source != null && !perceptionSources.Contains(source)) perceptionSources.Add(source);
    }

    public void UnregisterPerceptionSource(IZombiePerceptionSource source) => perceptionSources.Remove(source);

    public void RegisterSoundOccluder(Collider target, ZombieSoundOccluder occluder)
    {
        if (target != null && occluder != null) soundOccluders[target] = occluder;
    }

    public void UnregisterSoundOccluder(Collider target, ZombieSoundOccluder occluder)
    {
        if (target != null && soundOccluders.TryGetValue(target, out ZombieSoundOccluder current) && current == occluder)
        {
            soundOccluders.Remove(target);
        }
    }

    public bool TryGetSoundAttenuation(Collider target, out float attenuationDb)
    {
        if (target != null && soundOccluders.TryGetValue(target, out ZombieSoundOccluder occluder) && occluder != null)
        {
            attenuationDb = occluder.AttenuationDb;
            return true;
        }

        attenuationDb = 0f;
        return false;
    }

    private void OnDisable()
    {
        squads.Clear();
        perceptionSources.Clear();
        soundOccluders.Clear();
    }
}
