using NUnit.Framework;
using QuackStudio.SharedSimCore;

namespace QuackStudio.Tests.EditMode.SharedSimCore
{
    // Per ADR-0001: this suite proves the properties Tier-2 replay verification
    // depends on. It intentionally does NOT hardcode "golden" output values yet —
    // see the note on CrossPlatformTestVector below for why, and what still needs
    // to happen before this suite is complete per the ADR.
    public class Pcg32RngTests
    {
        [Test]
        public void Seed_SameSeedTwice_ProducesIdenticalSequence()
        {
            // The core determinism guarantee Tier-2 replay is built on:
            // same seed -> bit-identical sequence, every time, in a fresh instance.
            var rngA = new Pcg32Rng();
            var rngB = new Pcg32Rng();
            rngA.Seed(seed: 42UL, algorithmVersion: Pcg32Rng.AlgorithmVersion);
            rngB.Seed(seed: 42UL, algorithmVersion: Pcg32Rng.AlgorithmVersion);

            for (int i = 0; i < 1000; i++)
            {
                Assert.AreEqual(rngA.NextUInt32(), rngB.NextUInt32(),
                    $"Draw #{i} diverged between two Pcg32Rng instances seeded identically.");
            }
        }

        [Test]
        public void Seed_DifferentSeeds_ProduceDifferentSequences()
        {
            // Sanity check, not a security property: two distinct seeds should not
            // collide on their first draw. (A false failure here would indicate a
            // broken increment/state initialization, not bad luck — PCG32's period
            // makes an accidental first-draw collision astronomically unlikely.)
            var rngA = new Pcg32Rng();
            var rngB = new Pcg32Rng();
            rngA.Seed(seed: 1UL, algorithmVersion: Pcg32Rng.AlgorithmVersion);
            rngB.Seed(seed: 2UL, algorithmVersion: Pcg32Rng.AlgorithmVersion);

            Assert.AreNotEqual(rngA.NextUInt32(), rngB.NextUInt32());
        }

        [Test]
        public void NextUInt32_RepeatedSeed_IsReproducibleAcrossMultipleReseeds()
        {
            // Re-seeding the SAME instance must reset all state cleanly -- no
            // leakage from a prior sequence. This matters because SharedSimCore
            // is re-seeded once per run replay, not constructed fresh each time.
            var rng = new Pcg32Rng();
            rng.Seed(seed: 7UL, algorithmVersion: Pcg32Rng.AlgorithmVersion);
            uint first = rng.NextUInt32();
            uint second = rng.NextUInt32();

            rng.Seed(seed: 7UL, algorithmVersion: Pcg32Rng.AlgorithmVersion);
            Assert.AreEqual(first, rng.NextUInt32());
            Assert.AreEqual(second, rng.NextUInt32());
        }

        [Test]
        public void NextFloat01_StaysWithinUnitInterval()
        {
            var rng = new Pcg32Rng();
            rng.Seed(seed: 123UL, algorithmVersion: Pcg32Rng.AlgorithmVersion);

            for (int i = 0; i < 1000; i++)
            {
                float value = rng.NextFloat01();
                Assert.GreaterOrEqual(value, 0.0f);
                Assert.Less(value, 1.0f);
            }
        }

        [Test]
        public void NextUInt32_DoesNotDegenerateToAConstant()
        {
            // Coarse non-degeneracy check: a broken state-advance step (e.g. a
            // typo dropping the "_state = oldState * Multiplier + _increment"
            // line) would make every draw identical. This is not a statistical
            // quality test -- just a guard against that specific class of bug.
            var rng = new Pcg32Rng();
            rng.Seed(seed: 99UL, algorithmVersion: Pcg32Rng.AlgorithmVersion);
            uint first = rng.NextUInt32();
            bool sawDifferentValue = false;
            for (int i = 0; i < 16; i++)
            {
                if (rng.NextUInt32() != first) { sawDifferentValue = true; break; }
            }
            Assert.IsTrue(sawDifferentValue, "RNG produced the same value repeatedly - state is not advancing.");
        }

        // --- NOT YET IMPLEMENTED: the cross-platform golden vector ---
        //
        // ADR-0001 Decision point 5 requires "a CI-enforced shared test-vector
        // suite: fixed seeds with expected output sequences, asserted identically
        // in the Unity client test suite and the CLI executable's test suite."
        //
        // The tests above prove Pcg32Rng is internally deterministic and self-
        // consistent. They do NOT yet prove Unity-client-IL2CPP-build output ==
        // replay-verifier.exe (x86 .NET CLI) output for the same seed, because:
        //   (a) the replay-verifier.exe CLI host doesn't exist yet (ADR-0001,
        //       written but not implemented), and
        //   (b) this test was authored without executing the code (no Unity
        //       Editor / .NET runtime available in this session) -- hand-picking
        //       "expected" uint32 values here would risk hardcoding a WRONG
        //       number that happens to make the test pass for the wrong reason,
        //       which is worse than not having the test.
        //
        // Correct next step (do this, don't skip it): once both the Unity project
        // opens in a real Editor and replay-verifier.exe exists, run
        // Pcg32Rng with a fixed seed (e.g. 42) on BOTH platforms, capture the
        // actual first N draws, and hardcode that observed sequence as the
        // golden vector in a test added to THIS file and to the CLI's test
        // suite. That captured-from-reality vector is what makes this ADR's
        // determinism claim CI-enforced rather than merely self-consistent.
    }
}
