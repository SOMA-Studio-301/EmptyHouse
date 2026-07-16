using UnityEngine;

namespace EmptyHouse.NoiseSystem
{
    public enum NoiseOcclusionKind : byte { Wall, Door }

    public sealed class NoiseOcclusionSurface : MonoBehaviour
    {
        [SerializeField] private NoiseOcclusionKind kind;
        [SerializeField] private bool startsOpen;

        private bool isOpen;

        public NoiseOcclusionKind Kind => kind;
        public bool IsOpen => kind == NoiseOcclusionKind.Door && isOpen;

        private void Awake() => isOpen = startsOpen;

        public void SetDoorOpenServer(bool value)
        {
            if (kind == NoiseOcclusionKind.Door) isOpen = value;
        }
    }
}
