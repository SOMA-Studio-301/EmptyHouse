using UnityEngine;

// Player-side camouflage/pose/flashlight systems implement this interface.
// Zombie AI only consumes the read-only signals and never owns those systems.
public interface IZombiePerceptionSource
{
    bool IsDisguised { get; }
    bool IsSpectator { get; }
    bool IsCrouching { get; }
    bool IsMoving { get; }
    bool IsFlashlightAimingAt(Transform zombie);
}
