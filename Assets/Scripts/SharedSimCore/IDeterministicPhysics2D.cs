// VERTICAL SLICE - NOT FOR PRODUCTION
// Validation Question: Does ADR-0002's IDeterministicPhysics2D interface
// shape actually work in practice, not just on paper?
// Date: 2026-07-11

using System.Collections.Generic;

namespace QuackStudio.SharedSimCore
{
    /// <summary>
    /// Snapshot of one ball's state for a rendering/consuming layer.
    /// Matches ADR-0002's Key Interfaces section.
    /// </summary>
    public readonly struct BallState
    {
        public readonly int Id;
        public readonly Fix32 X;
        public readonly Fix32 Y;
        public readonly Fix32 Vx;
        public readonly Fix32 Vy;
        public readonly bool Retired;

        public BallState(int id, Fix32 x, Fix32 y, Fix32 vx, Fix32 vy, bool retired)
        {
            Id = id;
            X = x;
            Y = y;
            Vx = vx;
            Vy = vy;
            Retired = retired;
        }
    }

    /// <summary>
    /// One brick hit this frame. Count-based, order not scored - per
    /// boss-ai-damage-model.md Core Rule 1, simultaneous hits each count
    /// individually.
    /// </summary>
    public readonly struct HitEvent
    {
        public readonly int BrickId;

        public HitEvent(int brickId)
        {
            BrickId = brickId;
        }
    }

    /// <summary>
    /// Sole authority for scored ball physics, per ADR-0002 Decision 3.
    /// </summary>
    public interface IDeterministicPhysics2D
    {
        void AdvanceFrame();
        IReadOnlyList<BallState> Balls { get; }
        IReadOnlyList<HitEvent> ConsumeHitEvents();
    }
}
