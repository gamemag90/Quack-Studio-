// VERTICAL SLICE - NOT FOR PRODUCTION
// Validation Question: Does boss-ai-damage-model.md's decoupled 1-hit-1-
// damage rule and win-before-loss priority actually feel right in play?
// Date: 2026-07-11

namespace QuackStudio.SharedSimCore
{
    /// <summary>
    /// Deterministic, bit-reproducible boss HP/defeat state machine.
    /// Lives inside SharedSimCore (not a MonoBehaviour) per ADR-0011.
    /// Damage is decoupled from brick HP - every hit is exactly 1 damage,
    /// per boss-ai-damage-model.md Core Rule 1.
    /// </summary>
    public sealed class BossDamageModel
    {
        public int CurrentHp { get; private set; }
        public int MaxHp { get; private set; }
        public string BossName { get; private set; }
        public bool IsDefeated => CurrentHp <= 0;

        public void Initialize(int maxHp, string bossName)
        {
            MaxHp = maxHp;
            CurrentHp = maxHp;
            BossName = bossName;
        }

        /// <summary>
        /// hitCount = number of brick hits this frame. Each hit is exactly
        /// 1 boss damage regardless of the brick's own HP - decrement by
        /// hit COUNT only, never brick HP/value (control-manifest.md
        /// Core Layer Forbidden Approaches).
        /// </summary>
        public void ApplyDamage(int hitCount)
        {
            CurrentHp -= hitCount;

            if (CurrentHp < 0)
            {
                // Boss HP cannot go negative by construction - every hit is
                // always exactly -1, so there's no overkill case to handle
                // (boss-ai-damage-model.md Edge Cases).
                CurrentHp = 0;
            }
        }

        public void ResetForNewAttempt()
        {
            // No partial-progress carryover between attempts
            // (boss-ai-damage-model.md Acceptance Criteria).
            CurrentHp = MaxHp;
        }
    }
}
