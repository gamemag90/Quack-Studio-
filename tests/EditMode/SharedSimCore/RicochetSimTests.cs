using System.Reflection;
using NUnit.Framework;
using QuackStudio.SharedSimCore;

namespace QuackStudio.Tests.EditMode.SharedSimCore
{
    // Per ADR-0002's spike gate (Risk & Staffing Budget) item 3: this suite
    // exercises the fixed-point physics sim's collision/determinism behavior -
    // straight shots, corner/edge geometry, simultaneous-hit counting, and the
    // near-boundary sub-stepping invariant Rule 3 (tunnelling-impossible)
    // depends on.
    //
    // SCOPE NOTE (read before trusting a green run as "the spike passed"):
    // everything here runs under the Windows Editor's Mono/.NET runtime only.
    // This proves RicochetSim is internally deterministic and behaves
    // correctly on ONE platform/runtime. It does NOT prove the ARM64 IL2CPP
    // vs x86 CLI byte-identical claim the spike gate actually requires - that
    // needs an on-device build this environment cannot produce. See
    // docs/architecture/adr-0002-spike-report.md for the honest status.
    public class RicochetSimTests
    {
        // --- Deterministic seed-search helpers ---
        //
        // RicochetSim has no test seam to inject a hand-picked brick layout -
        // the only lever is the RNG seed. Rather than hand-picking "lucky"
        // seeds and hoping, these helpers deterministically SEARCH a fixed,
        // reproducible sequence of candidate seeds (1, 2, 3, ...) for one that
        // satisfies a needed precondition. This keeps the tests fully
        // reproducible (the same seed is found in the same order every run)
        // without depending on hardcoded knowledge of what a specific seed
        // happens to roll.

        private static ulong FindSeedWithBrickInColumn(int column, out RicochetSim.BrickState targetBrick, int maxSeedsToTry = 500)
        {
            for (ulong seed = 1; seed <= (ulong)maxSeedsToTry; seed++)
            {
                var probe = new RicochetSim();
                probe.Initialize(seed);

                RicochetSim.BrickState? best = null;
                foreach (var brick in probe.BrickStates)
                {
                    if (brick.Column != column)
                    {
                        continue;
                    }
                    if (best == null || brick.Row < best.Value.Row)
                    {
                        best = brick;
                    }
                }

                if (best != null)
                {
                    targetBrick = best.Value;
                    return seed;
                }
            }

            Assert.Fail($"No seed among the first {maxSeedsToTry} produced a brick in column {column}.");
            targetBrick = default;
            return 0;
        }

        private static ulong FindSeedWithColumnEmpty(int column, int maxSeedsToTry = 500)
        {
            for (ulong seed = 1; seed <= (ulong)maxSeedsToTry; seed++)
            {
                var probe = new RicochetSim();
                probe.Initialize(seed);

                bool occupied = false;
                foreach (var brick in probe.BrickStates)
                {
                    if (brick.Column == column)
                    {
                        occupied = true;
                        break;
                    }
                }

                if (!occupied)
                {
                    return seed;
                }
            }

            Assert.Fail($"No seed among the first {maxSeedsToTry} left column {column} empty.");
            return 0;
        }

        // --- Format/board determinism (prerequisite to the full-run claim) ---

        [Test]
        public void Initialize_SameSeedTwice_ProducesByteIdenticalBoard()
        {
            var simA = new RicochetSim();
            var simB = new RicochetSim();
            simA.Initialize(777UL);
            simB.Initialize(777UL);

            Assert.AreEqual(simA.BrickStates.Count, simB.BrickStates.Count);
            for (int i = 0; i < simA.BrickStates.Count; i++)
            {
                Assert.AreEqual(simA.BrickStates[i].Id, simB.BrickStates[i].Id, $"brick[{i}].Id");
                Assert.AreEqual(simA.BrickStates[i].Column, simB.BrickStates[i].Column, $"brick[{i}].Column");
                Assert.AreEqual(simA.BrickStates[i].Row, simB.BrickStates[i].Row, $"brick[{i}].Row");
                Assert.AreEqual(simA.BrickStates[i].Hp, simB.BrickStates[i].Hp, $"brick[{i}].Hp");
            }
            Assert.AreEqual(simA.LauncherX.Raw, simB.LauncherX.Raw);
            Assert.AreEqual(simA.LauncherY.Raw, simB.LauncherY.Raw);
        }

        // --- The central Tier-2 replay claim: same seed + same inputs =>
        // byte-identical trajectory. This is the single most important test
        // in this file for ADR-0001/0002's purpose - it just can't, by
        // itself, stand in for the cross-platform (ARM vs x86) half of the
        // spike gate. ---

        [Test]
        public void FullRun_SameSeedAndInputs_ProducesByteIdenticalTrajectory()
        {
            const ulong seed = 42UL;
            const int framesToRun = 400; // comfortably covers a full first volley

            var simA = new RicochetSim();
            var simB = new RicochetSim();
            simA.Initialize(seed);
            simB.Initialize(seed);

            simA.BeginAiming();
            simB.BeginAiming();
            bool firedA = simA.Fire(Fix32.FromFloat(0.1f), Fix32.FromFloat(1.0f));
            bool firedB = simB.Fire(Fix32.FromFloat(0.1f), Fix32.FromFloat(1.0f));
            Assert.IsTrue(firedA);
            Assert.IsTrue(firedB);

            for (int frame = 0; frame < framesToRun; frame++)
            {
                simA.AdvanceFrame();
                simB.AdvanceFrame();

                var hitsA = simA.ConsumeHitEvents();
                var hitsB = simB.ConsumeHitEvents();
                Assert.AreEqual(hitsA.Count, hitsB.Count, $"frame {frame}: hit count diverged");
                for (int h = 0; h < hitsA.Count; h++)
                {
                    Assert.AreEqual(hitsA[h].BrickId, hitsB[h].BrickId, $"frame {frame}, hit {h}: BrickId diverged");
                }

                Assert.AreEqual(simA.Balls.Count, simB.Balls.Count, $"frame {frame}: ball count diverged");
                for (int b = 0; b < simA.Balls.Count; b++)
                {
                    Assert.AreEqual(simA.Balls[b].X.Raw, simB.Balls[b].X.Raw, $"frame {frame}, ball {b}: X diverged");
                    Assert.AreEqual(simA.Balls[b].Y.Raw, simB.Balls[b].Y.Raw, $"frame {frame}, ball {b}: Y diverged");
                    Assert.AreEqual(simA.Balls[b].Vx.Raw, simB.Balls[b].Vx.Raw, $"frame {frame}, ball {b}: Vx diverged");
                    Assert.AreEqual(simA.Balls[b].Vy.Raw, simB.Balls[b].Vy.Raw, $"frame {frame}, ball {b}: Vy diverged");
                    Assert.AreEqual(simA.Balls[b].Retired, simB.Balls[b].Retired, $"frame {frame}, ball {b}: Retired diverged");
                }

                Assert.AreEqual(simA.BossHp, simB.BossHp, $"frame {frame}: BossHp diverged");
                Assert.AreEqual(simA.State, simB.State, $"frame {frame}: State diverged");

                if (simA.State != RicochetState.Firing)
                {
                    break;
                }
            }
        }

        // --- Wall boundary / near-boundary invariant ---
        //
        // Regardless of what's hit, a ball must never cross a side wall - the
        // wall clamp in ResolveCollisions runs unconditionally every substep.
        // This is checked across a full real run (any seed, any direction),
        // which is exactly where a near-boundary sub-step off-by-one would
        // surface if the distance-driven inner loop had one.

        [Test]
        public void Run_BallNeverEscapesPastSideWalls()
        {
            var sim = new RicochetSim();
            sim.Initialize(seed: 2026UL);
            sim.BeginAiming();
            Assert.IsTrue(sim.Fire(Fix32.FromFloat(0.85f), Fix32.FromFloat(0.53f)));

            Fix32 minAllowedX = RicochetSim.BallRadius;
            Fix32 maxAllowedX = Fix32.One - RicochetSim.BallRadius;

            for (int frame = 0; frame < 400 && sim.State == RicochetState.Firing; frame++)
            {
                sim.AdvanceFrame();
                foreach (var ball in sim.Balls)
                {
                    if (ball.Retired)
                    {
                        continue;
                    }
                    Assert.GreaterOrEqual(ball.X.Raw, minAllowedX.Raw, $"frame {frame}: ball escaped past the left wall");
                    Assert.LessOrEqual(ball.X.Raw, maxAllowedX.Raw, $"frame {frame}: ball escaped past the right wall");
                }
            }
        }

        // --- Simultaneous multi-brick hit counting integrity ---
        //
        // Rather than engineering one specific "two balls hit two bricks in
        // the exact same frame" scenario (fragile without a way to inject a
        // known board), this proves the stronger, seed-independent invariant
        // that actually IS the "simultaneous hits each count" rule: summed
        // across an entire run, the number of HitEvents returned must equal
        // the total boss damage taken (accounting for the HP floor at 0).
        // This holds true for ANY number of simultaneous hits per frame,
        // from any combination of balls/bricks, which is a strictly stronger
        // check than confirming one hand-picked multi-hit frame.

        [Test]
        public void Run_TotalHitEventCount_AlwaysEqualsTotalBossDamage()
        {
            var sim = new RicochetSim();
            sim.Initialize(seed: 99UL);
            sim.BeginAiming();
            Assert.IsTrue(sim.Fire(Fix32.FromFloat(0.05f), Fix32.FromFloat(1.0f)));

            int bossMaxHpBefore = sim.BossHp;
            int totalHitEvents = 0;
            int previousBossHp = sim.BossHp;
            int totalDamageObserved = 0;

            for (int frame = 0; frame < 720 && sim.State == RicochetState.Firing; frame++)
            {
                sim.AdvanceFrame();
                var hits = sim.ConsumeHitEvents();
                totalHitEvents += hits.Count;

                int currentBossHp = sim.BossHp;
                int damageThisFrame = previousBossHp - currentBossHp;
                Assert.GreaterOrEqual(damageThisFrame, 0, $"frame {frame}: boss HP must never increase");
                totalDamageObserved += damageThisFrame;
                previousBossHp = currentBossHp;

                // hitCountThisFrame is applied as ONE ApplyDamage call per
                // AdvanceFrame per RicochetSim.AdvanceFrame - so this frame's
                // damage must equal this frame's hit count, unless the boss
                // was defeated mid-frame and HP clamped at 0 (which can make
                // observed damage LESS than hits, never more).
                Assert.LessOrEqual(damageThisFrame, hits.Count,
                    $"frame {frame}: damage exceeded hit count - hit-to-damage mapping broken");
            }

            if (sim.BossHp == 0)
            {
                // Defeated mid-run: BossDamageModel clamps at 0, so total
                // damage observed equals exactly its starting max HP, which
                // may be LESS than total hit events (the final, overkill hit
                // absorbs whatever hit count pushed it past 0).
                Assert.AreEqual(bossMaxHpBefore, totalDamageObserved,
                    "Boss was defeated: total damage observed must equal its starting max HP exactly (clamped, never overshoot).");
                Assert.LessOrEqual(totalDamageObserved, totalHitEvents);
            }
            else
            {
                // Not defeated: every hit is worth exactly 1 damage and none
                // were clamped away, so the two totals must match exactly -
                // the core "simultaneous hits each count" integrity check,
                // true regardless of how many hits landed in any single frame.
                Assert.AreEqual(totalHitEvents, totalDamageObserved,
                    "Total boss damage must equal total hit events exactly when the boss was never defeated.");
            }
        }

        // --- BossDamageModel's own contract, directly (the actual mechanism
        // "simultaneous hits each count" relies on) ---

        [Test]
        public void BossDamageModel_ApplyDamage_DecrementsByExactHitCount()
        {
            var boss = new BossDamageModel();
            boss.Initialize(maxHp: 100, bossName: "Test Boss");

            boss.ApplyDamage(3);
            Assert.AreEqual(97, boss.CurrentHp, "Applying 3 simultaneous hits must decrement HP by exactly 3, not 1.");

            boss.ApplyDamage(1);
            Assert.AreEqual(96, boss.CurrentHp);
        }

        [Test]
        public void BossDamageModel_ApplyDamage_ClampsAtZero_NeverNegative()
        {
            var boss = new BossDamageModel();
            boss.Initialize(maxHp: 5, bossName: "Test Boss");

            boss.ApplyDamage(9001);
            Assert.AreEqual(0, boss.CurrentHp);
            Assert.IsTrue(boss.IsDefeated);
        }

        // --- Straight shot: real end-to-end hit registration ---

        [Test]
        public void StraightShot_AtColumn3Brick_RegistersHitAndDamagesBrickAndBoss()
        {
            ulong seed = FindSeedWithBrickInColumn(column: 3, out RicochetSim.BrickState target);

            var sim = new RicochetSim();
            sim.Initialize(seed);
            int bossHpBefore = sim.BossHp;
            int targetHpBefore = target.Hp;

            sim.BeginAiming();
            // Column 3 is the launcher's own starting column (X = 0.5 exactly),
            // so a pure-vertical shot (dirX = 0) travels straight up that
            // column with zero horizontal drift - nothing else can be hit
            // first, since no bricks exist below the target's row.
            Assert.IsTrue(sim.Fire(Fix32.Zero, Fix32.One));

            bool hitTarget = false;
            int bossHpAfter = bossHpBefore;

            for (int frame = 0; frame < 500 && sim.State == RicochetState.Firing; frame++)
            {
                sim.AdvanceFrame();
                foreach (var hit in sim.ConsumeHitEvents())
                {
                    if (hit.BrickId == target.Id)
                    {
                        hitTarget = true;
                    }
                }
                bossHpAfter = sim.BossHp;
                if (hitTarget)
                {
                    break;
                }
            }

            Assert.IsTrue(hitTarget, "Straight vertical shot up the launcher's own column must hit the brick directly above it.");
            Assert.AreEqual(bossHpBefore - 1, bossHpAfter, "Exactly one hit must deal exactly one boss damage.");

            bool brickStillPresentWithReducedHp = false;
            bool brickRemoved = true;
            foreach (var brick in sim.BrickStates)
            {
                if (brick.Id == target.Id)
                {
                    brickRemoved = false;
                    brickStillPresentWithReducedHp = brick.Hp == targetHpBefore - 1;
                }
            }
            Assert.IsTrue(brickRemoved || brickStillPresentWithReducedHp,
                "Target brick must either be destroyed (if Hp was 1) or have Hp reduced by exactly 1.");
        }

        // --- Corner geometry: the actual collision primitive, in isolation ---
        //
        // Rather than hoping a real seeded board happens to produce a clean
        // corner-only approach (fragile - any nearby brick in an adjacent
        // column could confound which brick registers first), this drives
        // RicochetSim's private closest-point primitive directly via
        // reflection with hand-constructed coordinates that are unambiguously
        // a corner approach (ball outside the box on BOTH axes at once, vs.
        // a face hit where exactly one axis is inside the box's range).

        [Test]
        public void CornerGeometry_ClosestPointClamp_HandlesDiagonalApproachCorrectly()
        {
            MethodInfo clampMethod = typeof(RicochetSim).GetMethod("Clamp", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(clampMethod, "RicochetSim.Clamp is the closest-point primitive corner/edge detection relies on.");

            Fix32 brickMinX = RicochetSim.CellSize * Fix32.FromInt(3);
            Fix32 brickMaxX = brickMinX + RicochetSim.CellSize;
            Fix32 brickMinY = RicochetSim.CellSize * Fix32.FromInt(6);
            Fix32 brickMaxY = brickMinY + RicochetSim.CellSize;

            // Positioned diagonally outside the box near its bottom-left
            // corner: outside the X range AND outside the Y range at once -
            // the geometric definition of a corner approach, as opposed to a
            // face approach where only one axis is out of range.
            Fix32 ballX = brickMinX - RicochetSim.BallRadius;
            Fix32 ballY = brickMinY - RicochetSim.BallRadius;

            var closestX = (Fix32)clampMethod.Invoke(null, new object[] { ballX, brickMinX, brickMaxX });
            var closestY = (Fix32)clampMethod.Invoke(null, new object[] { ballY, brickMinY, brickMaxY });

            Assert.AreEqual(brickMinX.Raw, closestX.Raw, "Outside-left of the box clamps to the box's min-X corner coordinate.");
            Assert.AreEqual(brickMinY.Raw, closestY.Raw, "Outside-below the box clamps to the box's min-Y corner coordinate.");

            Fix32 dx = ballX - closestX;
            Fix32 dy = ballY - closestY;
            Assert.AreNotEqual(0, dx.Raw, "A corner contact has a non-zero X component (unlike a pure face hit).");
            Assert.AreNotEqual(0, dy.Raw, "A corner contact has a non-zero Y component (unlike a pure face hit).");

            Fix32 distSq = dx * dx + dy * dy;
            Fix32 radiusSq = RicochetSim.BallRadius * RicochetSim.BallRadius;

            // At exactly one ball-radius away on EACH axis, true diagonal
            // distance is radius*sqrt(2), which exceeds one radius - i.e. NOT
            // touching yet at this exact offset. This is precisely why corner
            // cases are the harder tunnelling case: a corner approach needs to
            // close more distance than a face approach at the same per-axis
            // gap before contact registers.
            Assert.Greater(distSq.Raw, radiusSq.Raw,
                "One radius away on each axis diagonally must NOT yet register as touching (sqrt(2) > 1).");

            // Halving the per-axis gap (still outside on both axes) should
            // cross into contact - confirms the same primitive correctly
            // detects the corner once the ball is close enough, not just that
            // it correctly rejects the too-far case above.
            Fix32 closerBallX = brickMinX - (RicochetSim.BallRadius / Fix32.FromInt(2));
            Fix32 closerBallY = brickMinY - (RicochetSim.BallRadius / Fix32.FromInt(2));
            var closerClosestX = (Fix32)clampMethod.Invoke(null, new object[] { closerBallX, brickMinX, brickMaxX });
            var closerClosestY = (Fix32)clampMethod.Invoke(null, new object[] { closerBallY, brickMinY, brickMaxY });
            Fix32 closerDx = closerBallX - closerClosestX;
            Fix32 closerDy = closerBallY - closerClosestY;
            Fix32 closerDistSq = closerDx * closerDx + closerDy * closerDy;
            Assert.LessOrEqual(closerDistSq.Raw, radiusSq.Raw,
                "At half a radius away on each axis, the corner contact must register as touching.");
        }

        // --- Tunnelling-impossible invariant (Rule 3's mathematical basis) ---

        [Test]
        public void SubStepSize_IsStrictlyLessThanBrickThickness_TunnellingIsStructurallyImpossible()
        {
            // Rule 3's entire premise: the sub-step cap (half a ball radius)
            // must be smaller than a brick's thickness (one full cell), or a
            // fast-moving ball could skip clean over a brick within one
            // sub-step. This is the numeric precondition ADR-0002's Decision 2
            // depends on - if a future tuning pass changes BallRadius relative
            // to CellSize, this test catches the regression before it ships.
            Assert.Less(RicochetSim.HalfRadius.Raw, RicochetSim.CellSize.Raw,
                "Sub-step size (half ball radius) must stay smaller than one brick cell, or tunnelling becomes possible again.");

            // With this project's actual constants the margin is large (~13x),
            // not a knife's-edge pass - worth asserting explicitly so a future
            // change that erodes the margin (even without crossing it outright)
            // is visible in a diff, not just a binary pass/fail.
            float ratio = RicochetSim.HalfRadius.ToFloatForDisplay() / RicochetSim.CellSize.ToFloatForDisplay();
            Assert.Less(ratio, 0.5f, "Sub-step should be well under half a cell for a comfortable tunnelling-safety margin.");
        }

        // --- Volley cap: force-retire at 720 sim frames (Rule 5) ---

        [Test]
        public void Ball_StillAirborneAt720Frames_IsForceRetired()
        {
            ulong seed = FindSeedWithColumnEmpty(column: 3);

            var sim = new RicochetSim();
            sim.Initialize(seed);
            sim.BeginAiming();
            // Pure-vertical shot up the (verified empty) launcher column: no
            // brick anywhere on this path, so the ball travels unobstructed,
            // exits past the board's content, and just bounces above it doing
            // nothing until the hard cap forces it down.
            Assert.IsTrue(sim.Fire(Fix32.Zero, Fix32.One));

            // AirborneFrames is counted per-ball from ITS OWN launch frame,
            // not from volley start - with 3 balls staggered every
            // LaunchStaggerFrames, the last ball launches
            // (startingBalls-1)*LaunchStaggerFrames frames into the volley,
            // so it doesn't hit its own 720-frame cap until that much later.
            // A small buffer on top covers the same-frame launch/advance
            // ordering without depending on exact off-by-one bookkeeping.
            const int startingBallsForLevel1 = 3;
            int framesNeededForLastBallToCap =
                RicochetSim.VolleyCapFrames + (startingBallsForLevel1 - 1) * RicochetSim.LaunchStaggerFrames + 2;

            for (int frame = 0; frame < framesNeededForLastBallToCap && sim.State == RicochetState.Firing; frame++)
            {
                sim.AdvanceFrame();
            }

            bool turnResolvedOrAllRetired =
                sim.State != RicochetState.Firing || AllLaunchedBallsRetired(sim);
            Assert.IsTrue(turnResolvedOrAllRetired,
                "By 720 sim frames after each ball's own launch, every airborne ball must be force-retired per Rule 5's hard cap.");
        }

        private static bool AllLaunchedBallsRetired(RicochetSim sim)
        {
            foreach (var ball in sim.Balls)
            {
                if (!ball.Retired)
                {
                    return false;
                }
            }
            return true;
        }

        // --- Aim-cone sanity (Rule 2) - NOT an exact-boundary test ---
        //
        // A boundary-exact (8deg/8.6deg/9deg) test was deliberately NOT
        // written here: the fixed threshold constant (MinVerticalAimComponent
        // = 0.1495) is a decimal approximation of sin(8.6deg) (~0.149536) that
        // rounds DOWN, so the true 8.6deg angle's computed unitY magnitude
        // actually exceeds the stored threshold and is currently ALLOWED to
        // fire - apparently contradicting super-ricochet.md's stated
        // acceptance criterion that exactly 8.6deg is blocked (inclusive
        // boundary). That is a Rule-2 gameplay/constants discrepancy, not a
        // Rule-3 physics-determinism issue, so it is intentionally out of
        // this spike's scope and is flagged separately rather than patched
        // here. This test only checks the gate functions at all.
        [Test]
        public void Fire_RejectsNearHorizontalAim_AcceptsSteepAim()
        {
            var sim = new RicochetSim();
            sim.Initialize(seed: 5UL);
            sim.BeginAiming();

            bool steepFired = sim.Fire(Fix32.FromFloat(0.3f), Fix32.FromFloat(0.9f));
            Assert.IsTrue(steepFired, "A clearly steep (well within the legal cone) aim must be accepted.");
        }

        [Test]
        public void Fire_RejectsAlmostHorizontalAim()
        {
            var sim = new RicochetSim();
            sim.Initialize(seed: 6UL);
            sim.BeginAiming();

            // Vertical component far below the ~8.6deg cone (unitY ~= 0.02).
            bool fired = sim.Fire(Fix32.FromFloat(1.0f), Fix32.FromFloat(0.02f));
            Assert.IsFalse(fired, "An aim well inside the forbidden near-horizontal cone must be rejected.");
        }
    }
}
