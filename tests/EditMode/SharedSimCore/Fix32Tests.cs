using NUnit.Framework;
using QuackStudio.SharedSimCore;

namespace QuackStudio.Tests.EditMode.SharedSimCore
{
    // Per ADR-0002's spike gate (Risk & Staffing Budget), items 1-2: this suite
    // freezes the Q16.16 format and exercises the overflow range-analysis proof
    // that lives alongside it in docs/architecture/adr-0002-spike-report.md.
    //
    // IMPORTANT SCOPE NOTE: everything here runs and passes under the Windows
    // Editor's Mono/.NET runtime only (no Mac/iOS toolchain is available in
    // this environment - see the spike report). These tests prove Fix32 is
    // internally deterministic and that unchecked wraparound behaves exactly
    // as C#'s language spec defines it ON THIS RUNTIME. They do NOT by
    // themselves prove IL2CPP's C++ codegen agrees with the CLR on signed
    // overflow (C++ signed overflow is undefined behavior; the CLR's is not) -
    // that is item 3 of the spike gate and requires an actual on-device
    // ARM64 IL2CPP build to compare against, which this suite cannot produce.
    public class Fix32Tests
    {
        // --- Format freeze (spike item 2: "Freeze the Q-format") ---

        [Test]
        public void Format_IsQ16_16_AsFrozenByAdr0002()
        {
            Assert.AreEqual(16, Fix32.FractionalBits,
                "ADR-0002 Decision 1 freezes Q16.16 (16 fractional bits) as the format this spike validates.");
            Assert.AreEqual(1 << 16, Fix32.One);
            Assert.AreEqual(65536, Fix32.One);
        }

        [Test]
        public void FromInt_RoundTrips()
        {
            Assert.AreEqual(5 * Fix32.One, Fix32.FromInt(5).Raw);
            Assert.AreEqual(-3 * Fix32.One, Fix32.FromInt(-3).Raw);
            Assert.AreEqual(0, Fix32.FromInt(0).Raw);
        }

        // --- Basic arithmetic correctness ---

        [Test]
        public void Add_Subtract_Basic()
        {
            Fix32 a = Fix32.FromInt(3);
            Fix32 b = Fix32.FromInt(2);
            Assert.AreEqual(Fix32.FromInt(5).Raw, (a + b).Raw);
            Assert.AreEqual(Fix32.FromInt(1).Raw, (a - b).Raw);
            Assert.AreEqual(Fix32.FromInt(-3).Raw, (-a).Raw);
        }

        [Test]
        public void Multiply_Basic()
        {
            Fix32 a = Fix32.FromFloat(2.5f);
            Fix32 b = Fix32.FromInt(4);
            Fix32 result = a * b;
            Assert.AreEqual(10.0f, result.ToFloatForDisplay(), 0.0001f);
        }

        [Test]
        public void Divide_Basic()
        {
            Fix32 a = Fix32.FromInt(10);
            Fix32 b = Fix32.FromInt(4);
            Fix32 result = a / b;
            Assert.AreEqual(2.5f, result.ToFloatForDisplay(), 0.0001f);
        }

        // --- Overflow range-analysis proof (spike item 2), executable form ---
        //
        // Claim proved here: for Fix32's Int64-intermediate multiply
        // `(long)a.Raw * (long)b.Raw`, the intermediate itself can NEVER
        // overflow Int64 for ANY two Int32 Raw values whatsoever - this is a
        // property of the type widths involved, not of this game's specific
        // number ranges. Proof: |Int32| <= 2^31, so the product's magnitude
        // is bounded by 2^62, which is still less than Int64.MaxValue (2^63-1).
        // See adr-0002-spike-report.md for the full written derivation this
        // test operationalizes.
        [Test]
        public void Multiply_Int64Intermediate_NeverOverflowsForAnyRawInt32Pair()
        {
            int[] extremes = { int.MinValue, int.MinValue + 1, -1, 0, 1, int.MaxValue - 1, int.MaxValue };

            foreach (int a in extremes)
            {
                foreach (int b in extremes)
                {
                    // If the Int64 intermediate ever overflowed, this checked
                    // multiplication would throw OverflowException. It must not,
                    // for any combination of Int32 extremes.
                    checked
                    {
                        long product = (long)a * (long)b;
                        // Sanity: also confirm it stays comfortably inside Int64,
                        // not just "didn't throw" - proves real headroom, not a
                        // coincidental non-throw.
                        Assert.LessOrEqual(System.Math.Abs(product) / (double)long.MaxValue, 0.5,
                            "Int64 intermediate should stay within half of Int64's range even at Int32 extremes.");
                    }
                }
            }
        }

        // --- Signed unchecked overflow/wraparound (spike item 3's CLR half) ---
        //
        // These construct DELIBERATE overflow beyond what normal gameplay
        // values ever reach (see adr-0002-spike-report.md's range analysis for
        // just how far normal play sits from this boundary), to pin down
        // exactly what "unchecked" wraparound looks like on this runtime. The
        // whole point of the spike's on-device requirement is that IL2CPP's
        // C++ codegen could theoretically diverge from this CLR result (C++
        // signed overflow is undefined behavior); this test is the CLR-side
        // half of that comparison, not the comparison itself.
        [Test]
        public void Add_NearInt32Boundary_WrapsAsTwosComplement()
        {
            var nearMax = new Fix32(int.MaxValue);
            var one = new Fix32(1);
            Fix32 wrapped = nearMax + one;
            Assert.AreEqual(int.MinValue, wrapped.Raw,
                "unchecked Int32 addition must wrap via two's-complement, matching C#'s unchecked semantics exactly.");
        }

        [Test]
        public void Subtract_NearInt32LowerBoundary_WrapsAsTwosComplement()
        {
            var nearMin = new Fix32(int.MinValue);
            var one = new Fix32(1);
            Fix32 wrapped = nearMin - one;
            Assert.AreEqual(int.MaxValue, wrapped.Raw);
        }

        [Test]
        public void Multiply_ResultExceedingInt32Range_WrapsPredictably()
        {
            // Real numbers ~181.02 are where x*x first exceeds 32768^2 = the
            // Int32-representable ceiling in Q16.16 (2^31 / 2^16 = 2^15 = 32768).
            // Gameplay values here never exceed ~1.5 (see spike report) - this
            // is a deliberately out-of-gameplay-range value to pin the
            // wraparound contract itself, independent of whether play ever
            // reaches it.
            Fix32 big = Fix32.FromInt(200);
            Fix32 result = big * big; // mathematically 40000, exceeds Int32's ~32767 ceiling

            int expectedRaw = unchecked((int)(((long)big.Raw * (long)big.Raw) >> Fix32.FractionalBits));
            Assert.AreEqual(expectedRaw, result.Raw,
                "Multiply's final downcast must follow C#'s unchecked (int) truncation exactly - " +
                "this is the CLR-side reference value the on-device IL2CPP build must also reproduce.");
        }

        // --- IntSqrt negative-input/rounding convention (spike item 3) ---

        [Test]
        public void Sqrt_OfZero_ReturnsZero()
        {
            Assert.AreEqual(Fix32.Zero.Raw, Fix32.Sqrt(Fix32.Zero).Raw);
        }

        [Test]
        public void Sqrt_OfNegative_ReturnsZero_ByConvention()
        {
            // Documented, deterministic convention: negative input has no real
            // square root, so this returns Zero rather than throwing or
            // producing a NaN-equivalent sentinel - a convention that must be
            // identical on IL2CPP and the CLR since it's a plain comparison +
            // branch, not a float operation.
            Fix32 negative = Fix32.FromInt(-4);
            Assert.AreEqual(Fix32.Zero.Raw, Fix32.Sqrt(negative).Raw);
        }

        [Test]
        public void Sqrt_OfPerfectSquare_IsExact()
        {
            Assert.AreEqual(Fix32.FromInt(2).Raw, Fix32.Sqrt(Fix32.FromInt(4)).Raw);
            Assert.AreEqual(Fix32.FromInt(3).Raw, Fix32.Sqrt(Fix32.FromInt(9)).Raw);
            Assert.AreEqual(Fix32.FromInt(0).Raw, Fix32.Sqrt(Fix32.FromInt(0)).Raw);
        }

        [Test]
        public void Sqrt_OfNonPerfectSquare_FloorsTowardZero()
        {
            // Rounding convention: binary search returns the largest integer
            // (in raw Q16.16 units) whose square does not exceed the target -
            // i.e. floor, never round-to-nearest. Deterministic and
            // platform-independent because it's pure integer comparison, no
            // Math.Sqrt anywhere.
            Fix32 target = Fix32.FromInt(2);
            Fix32 result = Fix32.Sqrt(target);

            Fix32 resultSquared = result * result;
            Assert.LessOrEqual(resultSquared.Raw, target.Raw,
                "Floor convention: result^2 must not exceed the target.");

            var oneRawUnitMore = new Fix32(result.Raw + 1);
            Fix32 nextSquared = oneRawUnitMore * oneRawUnitMore;
            Assert.Greater(nextSquared.Raw, target.Raw,
                "Floor convention: the next representable value up must overshoot the target - " +
                "confirms `result` is the true floor, not merely *a* valid lower bound.");
        }

        // --- Gameplay-range safety net (operationalizes the range-analysis
        // proof against RicochetSim's ACTUAL constants, so a future tuning
        // change that pushes values toward the overflow ceiling is caught by
        // CI rather than silently regressing the proof in the markdown doc) ---

        [Test]
        public void GameplayConstants_StayFarBelowTheInt32OverflowCeiling()
        {
            // The ceiling: any Fix32 whose real magnitude exceeds ~181.02 risks
            // squaring past the Int32-representable range (32768^2 = 2^31 wraps
            // the sign bit at 2^31 in raw terms). Real gameplay magnitudes here
            // top out in the single digits (board is a normalized 0..1 width).
            const float overflowRiskThreshold = 181.0f;

            float cellSize = RicochetSim.CellSize.ToFloatForDisplay();
            float ballSpeed = RicochetSim.BallSpeed.ToFloatForDisplay();
            float ballRadius = RicochetSim.BallRadius.ToFloatForDisplay();
            float frameTravel = ballSpeed * RicochetSim.FrameDt.ToFloatForDisplay();

            Assert.Less(cellSize, overflowRiskThreshold);
            Assert.Less(ballSpeed, overflowRiskThreshold);
            Assert.Less(ballRadius, overflowRiskThreshold);
            Assert.Less(frameTravel, overflowRiskThreshold);

            // Concretely: BallSpeed^2 (used inside Sqrt's targetShifted<<16 and
            // in velocity-magnitude checks) must not itself approach Int64
            // overflow through the <<16 pre-shift used by Sqrt.
            Fix32 speedSquared = RicochetSim.BallSpeed * RicochetSim.BallSpeed;
            Assert.Less(speedSquared.ToFloatForDisplay(), overflowRiskThreshold * overflowRiskThreshold);
        }
    }
}
