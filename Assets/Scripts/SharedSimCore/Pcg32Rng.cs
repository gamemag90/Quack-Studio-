namespace QuackStudio.SharedSimCore
{
    /// <summary>
    /// PCG32 (O'Neill, pcg-random.org) — canonical XSH-RR variant.
    /// Per ADR-0001: integer-only, all arithmetic explicitly unchecked so
    /// overflow wraparound cannot diverge based on either side's project-
    /// level overflow-checking setting. algorithm_version = 1.
    /// </summary>
    public sealed class Pcg32Rng : IDeterministicRng
    {
        public const int AlgorithmVersion = 1;

        // PCG's fixed LCG multiplier (Knuth's MMIX constant).
        private const ulong Multiplier = 6364136223846793005UL;

        private ulong _state;
        private ulong _increment;

        public void Seed(ulong seed, int algorithmVersion)
        {
            // algorithmVersion is accepted (per-seed tag, ADR-0001) but this
            // type only ever implements version 1; a future algorithm change
            // gets its own type, never a silent behavior switch inside this one.
            unchecked
            {
                _increment = (seed << 1) | 1UL; // must be odd
                _state = 0UL;
                _state = _state * Multiplier + _increment;
                _state += seed;
                _state = _state * Multiplier + _increment;
            }
        }

        public uint NextUInt32()
        {
            unchecked
            {
                ulong oldState = _state;
                _state = oldState * Multiplier + _increment;
                uint xorshifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
                int rot = (int)(oldState >> 59);
                return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
            }
        }

        public float NextFloat01()
        {
            // Fixed, test-vectored integer->float conversion (ADR-0001):
            // top 24 bits of the draw, scaled into [0, 1). No floating-point
            // division by a runtime-computed value — the divisor is a
            // compile-time constant, so this step introduces no additional
            // cross-platform drift beyond NextUInt32() itself.
            uint x = NextUInt32();
            return (x >> 8) * (1.0f / (1 << 24));
        }
    }
}
