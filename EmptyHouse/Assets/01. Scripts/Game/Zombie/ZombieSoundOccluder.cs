using UnityEngine;

public class ZombieSoundOccluder : MonoBehaviour
{
    [Min(0f)] [SerializeField] private float attenuationDb = 12f;
    public float AttenuationDb => attenuationDb;
}
