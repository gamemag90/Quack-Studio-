// VERTICAL SLICE - NOT FOR PRODUCTION
// Validation Question: Does ADR-0002's Q16.16 fixed-point design actually
// work and feel good when built, ahead of the (still-unrun) on-device
// cross-platform spike proof this ADR requires before Accepted status?
// Date: 2026-07-11

namespace QuackStudio.SharedSimCore
{
    /// <summary>
    /// Q16.16 fixed-point number (16 integer bits, 16 fractional bits,
    /// stored in a signed Int32). Per ADR-0002 Decision 1: all arithmetic
    /// is unchecked with Int64 multiply/divide intermediates, no float in
    /// the scored path.
    /// </summary>
    public readonly struct Fix32
    {
        public const int FractionalBits = 16;
        public const int One = 1 << FractionalBits;

        public readonly int Raw;

        public Fix32(int raw)
        {
            Raw = raw;
        }

        public static readonly Fix32 Zero = new Fix32(0);

        public static Fix32 FromInt(int value)
        {
            unchecked
            {
                return new Fix32(value << FractionalBits);
            }
        }

        // Debug/config-time convenience only - used to author human-readable
        // level-config constants at startup. Never called inside the scored
        // per-frame simulation loop.
        public static Fix32 FromFloat(float value)
        {
            return new Fix32((int)(value * One));
        }

        public float ToFloatForDisplay()
        {
            return Raw / (float)One;
        }

        public Fix32 Abs()
        {
            unchecked
            {
                return Raw < 0 ? new Fix32(-Raw) : this;
            }
        }

        public static Fix32 operator +(Fix32 a, Fix32 b)
        {
            unchecked
            {
                return new Fix32(a.Raw + b.Raw);
            }
        }

        public static Fix32 operator -(Fix32 a, Fix32 b)
        {
            unchecked
            {
                return new Fix32(a.Raw - b.Raw);
            }
        }

        public static Fix32 operator -(Fix32 a)
        {
            unchecked
            {
                return new Fix32(-a.Raw);
            }
        }

        public static Fix32 operator *(Fix32 a, Fix32 b)
        {
            unchecked
            {
                long product = (long)a.Raw * (long)b.Raw;
                return new Fix32((int)(product >> FractionalBits));
            }
        }

        public static Fix32 operator /(Fix32 a, Fix32 b)
        {
            unchecked
            {
                long numerator = (long)a.Raw << FractionalBits;
                long quotient = numerator / b.Raw;
                return new Fix32((int)quotient);
            }
        }

        public static bool operator >(Fix32 a, Fix32 b) => a.Raw > b.Raw;
        public static bool operator <(Fix32 a, Fix32 b) => a.Raw < b.Raw;
        public static bool operator >=(Fix32 a, Fix32 b) => a.Raw >= b.Raw;
        public static bool operator <=(Fix32 a, Fix32 b) => a.Raw <= b.Raw;
        public static bool operator ==(Fix32 a, Fix32 b) => a.Raw == b.Raw;
        public static bool operator !=(Fix32 a, Fix32 b) => a.Raw != b.Raw;

        public override bool Equals(object obj)
        {
            return obj is Fix32 other && Raw == other.Raw;
        }

        public override int GetHashCode()
        {
            return Raw;
        }

        public override string ToString()
        {
            return ToFloatForDisplay().ToString("F4");
        }

        /// <summary>
        /// Deterministic integer square root via binary search - same
        /// algorithm on every .NET target, no platform-dependent float
        /// Math.Sqrt. Used only where normalization needs it (aim-vector
        /// normalization), per ADR-0002 Decision 1.
        /// </summary>
        public static Fix32 Sqrt(Fix32 value)
        {
            if (value.Raw <= 0)
            {
                return Zero;
            }

            unchecked
            {
                long targetShifted = (long)value.Raw << FractionalBits;

                // Search ceiling chosen so mid*mid never overflows Int64
                // (3,000,000,000^2 < long.MaxValue) - far above any Raw
                // value this slice's normalized 0..1 board coordinates
                // ever produce, so it never actually clips a real result.
                const long SearchCeiling = 3_000_000_000L;

                long low = 0;
                long high = SearchCeiling;
                long result = 0;

                while (low <= high)
                {
                    long mid = low + (high - low) / 2;
                    long midSquared = mid * mid;

                    if (midSquared <= targetShifted)
                    {
                        result = mid;
                        low = mid + 1;
                    }
                    else
                    {
                        high = mid - 1;
                    }
                }

                return new Fix32((int)result);
            }
        }
    }
}
