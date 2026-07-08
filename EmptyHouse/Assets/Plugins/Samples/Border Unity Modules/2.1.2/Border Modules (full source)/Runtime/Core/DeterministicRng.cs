namespace Border.Core
{
    /// <summary>
    /// xorshift32-based deterministic random number generator — a GC-free replacement for
    /// <see cref="System.Random"/>. Reuse one instance and call <see cref="Reseed"/> to inject a
    /// new seed; the same seed always yields the same sequence, which makes it ideal for
    /// reproducible procedural generation on hot paths.
    /// </summary>
    public class DeterministicRng
    {
        private uint _state;

        /// <summary>
        /// Sets a new seed. A seed of 0 would lock xorshift at 0, so it is replaced with a
        /// fallback constant to preserve a non-degenerate sequence.
        /// </summary>
        public void Reseed(int seed)
        {
            unchecked
            {
                _state = (uint)seed;
                if (_state == 0u) _state = 0xDEADBEEFu;
            }
        }

        /// <summary>Advances the internal state once and returns a 32-bit unsigned integer.</summary>
        private uint NextUInt()
        {
            unchecked
            {
                uint x = _state;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                _state = x == 0u ? 0xDEADBEEFu : x;
                return _state;
            }
        }

        /// <summary>Returns an integer in [0, maxExclusive). Returns 0 when maxExclusive &lt;= 0.</summary>
        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0) return 0;
            unchecked
            {
                return (int)(NextUInt() % (uint)maxExclusive);
            }
        }

        /// <summary>Returns an integer in [minInclusive, maxExclusive).</summary>
        public int Next(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            unchecked
            {
                uint range = (uint)(maxExclusive - minInclusive);
                return minInclusive + (int)(NextUInt() % range);
            }
        }

        /// <summary>Returns a double in [0, 1).</summary>
        public double NextDouble()
        {
            return NextUInt() / (double)uint.MaxValue;
        }
    }
}
