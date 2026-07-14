using UnityEngine;

public class ZombieSoundOccluder : MonoBehaviour
{
    [SerializeField] private ZombieRuntimeRegistrySO runtimeRegistry;
    [SerializeField] private Collider targetCollider;
    [Min(0f)] [SerializeField] private float attenuationDb = 12f;
    public float AttenuationDb => attenuationDb;

    private void OnEnable() => runtimeRegistry?.RegisterSoundOccluder(targetCollider, this);
    private void OnDisable() => runtimeRegistry?.UnregisterSoundOccluder(targetCollider, this);
}
