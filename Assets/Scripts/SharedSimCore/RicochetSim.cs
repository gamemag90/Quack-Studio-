// VERTICAL SLICE - NOT FOR PRODUCTION
// Validation Question: Does a player feel Super Ricochet's "Ready, Aim,
// Fire!" fantasy within 2-3 minutes, and can ADR-0002's fixed-point
// physics design actually be built and feel good at representative
// quality in a bounded session? This class does NOT attempt or claim to
// satisfy ADR-0002's blocking on-device cross-platform spike proof - that
// requires a physical device build pipeline this session doesn't have,
// and remains a separate, still-open gate before ADR-0002 reaches
// Accepted or before any of this logic is rewritten for production.
// Date: 2026-07-11

using System;
using System.Collections.Generic;

namespace QuackStudio.SharedSimCore
{
    public enum RicochetState
    {
        Ready,
        Aiming,
        Firing,
        Over
    }

    /// <summary>
    /// Single hardcoded level's worth of Super Ricochet: grid, aim-fire,
    /// sub-stepped fixed-point ball collision (ADR-0002), boss damage
    /// (ADR-0011 / boss-ai-damage-model.md), brick spawn rolls seeded via
    /// Pcg32Rng (ADR-0001). Implements IDeterministicPhysics2D for the
    /// ball-physics surface, plus the surrounding turn/win/loss
    /// orchestration super-ricochet.md and boss-ai-damage-model.md
    /// describe as separate systems in production but are combined here
    /// for a single-file, hand-written slice.
    /// </summary>
    public sealed class RicochetSim : IDeterministicPhysics2D
    {
        // --- Layout constants (normalized playfield, width = 1.0) ---
        // Matches ADR-0002's own worked example exactly.
        public const int Columns = 7;
        public const int TopSpawnRow = 9;
        public const int DangerLineRow = 2;
        public const int TotalRows = TopSpawnRow + 1;

        public static readonly Fix32 CellSize = Fix32.One / Fix32.FromInt(Columns);
        public static readonly Fix32 BallRadius = CellSize * Fix32.FromFloat(0.15f);
        public static readonly Fix32 HalfRadius = BallRadius / Fix32.FromInt(2);
        public static readonly Fix32 BallSpeed = CellSize * Fix32.FromInt(11);
        public static readonly Fix32 MinVerticalVelocity = BallSpeed * Fix32.FromFloat(0.22f);
        public static readonly Fix32 FrameDt = Fix32.One / Fix32.FromInt(60);
        public static readonly Fix32 FallOutMargin = CellSize; // one cell below launcher = off the bottom

        // sin(8.6 degrees) - a unit aim vector whose |Y| falls below this is
        // "within ~8.6 degrees of horizontal", blocked per Rule 2.
        private static readonly Fix32 MinVerticalAimComponent = Fix32.FromFloat(0.1495f);

        public const int SimHz = 60;
        public const int VolleyCapFrames = 720; // 12s @ 60Hz (super-ricochet.md Rule 5)
        public const int LaunchStaggerFrames = 3; // 0.05s @ 60Hz (super-ricochet.md Rule 2)

        private sealed class Brick
        {
            public int Id;
            public int Column;
            public int Row;
            public int Hp;
        }

        private sealed class Ball
        {
            public int Id;
            public Fix32 X;
            public Fix32 Y;
            public Fix32 Vx;
            public Fix32 Vy;
            public bool Retired;
            public int AirborneFrames;
        }

        private QuackStudio.SharedSimCore.IDeterministicRng _rng;
        private List<Brick> _bricks;
        private List<Ball> _balls;
        private BossDamageModel _bossDamageModel;

        private int _nextBrickId;
        private int _nextBallId;
        private int _maxBrickHp;
        private float _spawnDensity;
        private int _startingBalls;

        private Fix32 _launcherX;
        private Fix32 _launcherY;
        private Fix32 _lastLandingX;

        private Fix32 _pendingLaunchDirX;
        private Fix32 _pendingLaunchDirY;
        private int _pendingLaunchCount;
        private int _pendingLaunchLaunchedSoFar;
        private int _pendingLaunchNextFrame;
        private int _volleyFrameCounter;

        private List<BallState> _ballStatesSnapshot = new List<BallState>();
        private List<HitEvent> _lastFrameHits = new List<HitEvent>();

        public RicochetState State { get; private set; }
        public bool Won { get; private set; }
        public int TurnNumber { get; private set; }
        public int BricksDestroyed { get; private set; }
        public int CoinsCollected { get; private set; }

        public int BossHp => _bossDamageModel.CurrentHp;
        public int BossMaxHp => _bossDamageModel.MaxHp;
        public string BossName => _bossDamageModel.BossName;
        public Fix32 LauncherX => _launcherX;
        public Fix32 LauncherY => _launcherY;

        public IReadOnlyList<BallState> Balls => _ballStatesSnapshot;

        public struct BrickState
        {
            public int Id;
            public int Column;
            public int Row;
            public int Hp;
        }

        private List<BrickState> _brickStatesSnapshot = new List<BrickState>();
        public IReadOnlyList<BrickState> BrickStates => _brickStatesSnapshot;

        public void Initialize(ulong seed)
        {
            _rng = new Pcg32Rng();
            _rng.Seed(seed, Pcg32Rng.AlgorithmVersion);

            // Level 1's fixed config, level-difficulty-config-ricochet.md -
            // this slice hardcodes level 1 only, no progression.
            int bossMaxHp = 800;
            int initialRows = 4;
            _maxBrickHp = 6;
            _spawnDensity = 0.45f;
            _startingBalls = 3;

            _bossDamageModel = new BossDamageModel();
            _bossDamageModel.Initialize(bossMaxHp, "Slice Boss");

            _bricks = new List<Brick>();
            _balls = new List<Ball>();
            _nextBrickId = 0;
            _nextBallId = 0;
            BricksDestroyed = 0;
            CoinsCollected = 0;
            TurnNumber = 0;
            Won = false;

            _launcherX = Fix32.One / Fix32.FromInt(2);
            _launcherY = Fix32.Zero;
            _lastLandingX = _launcherX;

            for (int i = 0; i < initialRows; i++)
            {
                SpawnRow(TopSpawnRow - i);
            }

            State = RicochetState.Ready;
            RebuildSnapshots();
        }

        public void BeginAiming()
        {
            if (State == RicochetState.Ready)
            {
                State = RicochetState.Aiming;
            }
        }

        /// <summary>
        /// aimDirX/aimDirY need not be normalized - this normalizes
        /// internally. Returns false (no-op) if fire isn't currently legal:
        /// wrong state, degenerate zero-length aim, or within the forbidden
        /// ~8.6 degree horizontal cone (super-ricochet.md Rule 2 / Edge Cases).
        /// </summary>
        public bool Fire(Fix32 aimDirX, Fix32 aimDirY)
        {
            if (State != RicochetState.Aiming)
            {
                return false;
            }

            Fix32 magnitude = Fix32.Sqrt(aimDirX * aimDirX + aimDirY * aimDirY);
            if (magnitude.Raw <= 0)
            {
                return false;
            }

            Fix32 unitX = aimDirX / magnitude;
            Fix32 unitY = aimDirY / magnitude;

            if (unitY.Abs().Raw < MinVerticalAimComponent.Raw)
            {
                return false;
            }

            // Always fire upward toward the board, never downward.
            if (unitY.Raw < 0)
            {
                unitY = -unitY;
            }

            _pendingLaunchDirX = unitX;
            _pendingLaunchDirY = unitY;
            _pendingLaunchCount = _startingBalls;
            _pendingLaunchLaunchedSoFar = 0;
            _pendingLaunchNextFrame = 0;
            _volleyFrameCounter = 0;

            State = RicochetState.Firing;
            return true;
        }

        public void AdvanceFrame()
        {
            if (State != RicochetState.Firing)
            {
                return;
            }

            _lastFrameHits.Clear();

            if (_pendingLaunchLaunchedSoFar < _pendingLaunchCount
                && _volleyFrameCounter >= _pendingLaunchNextFrame)
            {
                var ball = new Ball
                {
                    Id = _nextBallId,
                    X = _launcherX,
                    Y = _launcherY,
                    Vx = _pendingLaunchDirX * BallSpeed,
                    Vy = _pendingLaunchDirY * BallSpeed,
                    Retired = false,
                    AirborneFrames = 0
                };
                _nextBallId++;
                _balls.Add(ball);
                _pendingLaunchLaunchedSoFar++;
                _pendingLaunchNextFrame += LaunchStaggerFrames;
            }

            int hitCountThisFrame = 0;

            for (int i = 0; i < _balls.Count; i++)
            {
                Ball ball = _balls[i];
                if (ball.Retired)
                {
                    continue;
                }

                AdvanceBallOneFrame(ball, ref hitCountThisFrame);
            }

            if (hitCountThisFrame > 0)
            {
                _bossDamageModel.ApplyDamage(hitCountThisFrame);
            }

            _volleyFrameCounter++;

            // Win is checked at the frame boundary BEFORE the danger-line
            // loss check, and takes priority even with balls still
            // airborne - boss-ai-damage-model.md Core Rule 3 / Edge Cases.
            if (_bossDamageModel.IsDefeated)
            {
                State = RicochetState.Over;
                Won = true;
                RebuildSnapshots();
                return;
            }

            bool allLaunched = _pendingLaunchLaunchedSoFar >= _pendingLaunchCount;
            bool allResolved = allLaunched && AllBallsRetired();

            if (allResolved)
            {
                CompleteTurn();
            }

            RebuildSnapshots();
        }

        public IReadOnlyList<HitEvent> ConsumeHitEvents()
        {
            var result = new List<HitEvent>(_lastFrameHits);
            _lastFrameHits.Clear();
            return result;
        }

        private bool AllBallsRetired()
        {
            for (int i = 0; i < _balls.Count; i++)
            {
                if (!_balls[i].Retired)
                {
                    return false;
                }
            }
            return true;
        }

        private void AdvanceBallOneFrame(Ball ball, ref int hitCountThisFrame)
        {
            ball.AirborneFrames++;

            if (ball.AirborneFrames >= VolleyCapFrames)
            {
                // 12-second hard cap, force-retired (super-ricochet.md Rule 5).
                ball.Retired = true;
                _lastLandingX = ball.X;
                return;
            }

            Fix32 remaining = BallSpeed * FrameDt;

            while (remaining.Raw > 0)
            {
                Fix32 step = HalfRadius.Raw < remaining.Raw ? HalfRadius : remaining;

                Fix32 currentSpeed = Fix32.Sqrt(ball.Vx * ball.Vx + ball.Vy * ball.Vy);
                if (currentSpeed.Raw <= 0)
                {
                    break;
                }

                Fix32 dirX = ball.Vx / currentSpeed;
                Fix32 dirY = ball.Vy / currentSpeed;

                ball.X = ball.X + dirX * step;
                ball.Y = ball.Y + dirY * step;

                ResolveCollisions(ball, ref hitCountThisFrame);
                ApplyMinVerticalNudge(ball);

                remaining = remaining - step;
            }

            Fix32 fallOutThreshold = _launcherY - FallOutMargin;
            if (ball.Y.Raw < fallOutThreshold.Raw)
            {
                ball.Retired = true;
                _lastLandingX = ball.X;
            }
        }

        private void ResolveCollisions(Ball ball, ref int hitCountThisFrame)
        {
            // Left/right board walls (normalized 0..1).
            if (ball.X.Raw < BallRadius.Raw)
            {
                ball.X = BallRadius;
                ball.Vx = -ball.Vx;
            }
            else
            {
                Fix32 rightWall = Fix32.One - BallRadius;
                if (ball.X.Raw > rightWall.Raw)
                {
                    ball.X = rightWall;
                    ball.Vx = -ball.Vx;
                }
            }

            for (int i = 0; i < _bricks.Count; i++)
            {
                Brick brick = _bricks[i];

                Fix32 brickMinX = Fix32.FromInt(brick.Column) * CellSize;
                Fix32 brickMaxX = brickMinX + CellSize;
                Fix32 brickMinY = Fix32.FromInt(brick.Row) * CellSize;
                Fix32 brickMaxY = brickMinY + CellSize;

                Fix32 closestX = Clamp(ball.X, brickMinX, brickMaxX);
                Fix32 closestY = Clamp(ball.Y, brickMinY, brickMaxY);

                Fix32 dx = ball.X - closestX;
                Fix32 dy = ball.Y - closestY;
                Fix32 distSq = dx * dx + dy * dy;
                Fix32 radiusSq = BallRadius * BallRadius;

                if (distSq.Raw <= radiusSq.Raw)
                {
                    // Axis-based reflection: reflect whichever axis has the
                    // larger penetration. Preserves speed magnitude exactly
                    // (no sqrt needed for the response) - standard
                    // brick-breaker collision response, slice-appropriate
                    // simplification of a true circle contact normal.
                    if (dx.Abs().Raw > dy.Abs().Raw)
                    {
                        ball.Vx = -ball.Vx;
                    }
                    else
                    {
                        ball.Vy = -ball.Vy;
                    }

                    brick.Hp -= 1;
                    hitCountThisFrame++;
                    _lastFrameHits.Add(new HitEvent(brick.Id));

                    if (brick.Hp <= 0)
                    {
                        _bricks.RemoveAt(i);
                        BricksDestroyed++;
                    }

                    // One brick hit per substep - sufficient for
                    // slice-quality response; avoids double-resolving two
                    // overlapping bricks in the same tiny substep.
                    break;
                }
            }
        }

        private static Fix32 Clamp(Fix32 value, Fix32 min, Fix32 max)
        {
            if (value.Raw < min.Raw)
            {
                return min;
            }
            if (value.Raw > max.Raw)
            {
                return max;
            }
            return value;
        }

        private void ApplyMinVerticalNudge(Ball ball)
        {
            // Prevents degenerate horizontal-skimming loops that never
            // resolve (super-ricochet.md Rule 4). Whether this preserves
            // total speed magnitude is left open by ADR-0002 ("the loop is
            // correct either way") - this slice does not renormalize, and
            // the sub-step loop above recomputes current speed fresh every
            // iteration so that's safe.
            if (ball.Vy.Abs().Raw < MinVerticalVelocity.Raw)
            {
                ball.Vy = ball.Vy.Raw >= 0 ? MinVerticalVelocity : -MinVerticalVelocity;
            }
        }

        private void CompleteTurn()
        {
            _launcherX = _lastLandingX;
            TurnNumber++;

            // Board descends one row per turn (super-ricochet.md Rule 7).
            for (int i = 0; i < _bricks.Count; i++)
            {
                _bricks[i].Row -= 1;
            }

            SpawnRow(TopSpawnRow);

            bool dangerLineBreached = false;
            for (int i = 0; i < _bricks.Count; i++)
            {
                if (_bricks[i].Row <= DangerLineRow)
                {
                    dangerLineBreached = true;
                    break;
                }
            }

            if (dangerLineBreached)
            {
                State = RicochetState.Over;
                Won = false;
                return;
            }

            _balls.Clear();
            State = RicochetState.Aiming;
        }

        private void SpawnRow(int row)
        {
            for (int column = 0; column < Columns; column++)
            {
                float roll = _rng.NextFloat01();
                if (roll >= _spawnDensity)
                {
                    continue;
                }

                // brick_hp_roll = ceil(pow(random(),1.6) * max_brick_hp),
                // super-ricochet.md Formulas. NOTE: unlike the ball-physics
                // path above (which is bit-exact Fix32), this roll uses
                // System.Math.Pow (float/double) to match the GDD's literal
                // formula. Whether this specific roll also needs to be
                // cross-platform bit-exact for Anti-Cheat replay is outside
                // ADR-0002's stated scope (ball physics only) - a genuine
                // open item, not resolved here or by this slice.
                float hpRoll = _rng.NextFloat01();
                int hp = (int)Math.Ceiling(Math.Pow(hpRoll, 1.6) * _maxBrickHp);
                if (hp < 1)
                {
                    hp = 1;
                }

                _bricks.Add(new Brick
                {
                    Id = _nextBrickId,
                    Column = column,
                    Row = row,
                    Hp = hp
                });
                _nextBrickId++;
            }
        }

        private void RebuildSnapshots()
        {
            _ballStatesSnapshot.Clear();
            for (int i = 0; i < _balls.Count; i++)
            {
                Ball b = _balls[i];
                _ballStatesSnapshot.Add(new BallState(b.Id, b.X, b.Y, b.Vx, b.Vy, b.Retired));
            }

            _brickStatesSnapshot.Clear();
            for (int i = 0; i < _bricks.Count; i++)
            {
                Brick brick = _bricks[i];
                _brickStatesSnapshot.Add(new BrickState
                {
                    Id = brick.Id,
                    Column = brick.Column,
                    Row = brick.Row,
                    Hp = brick.Hp
                });
            }
        }
    }
}
