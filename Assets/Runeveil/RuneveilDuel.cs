using UnityEngine;

namespace Runeveil
{
    internal enum DuelPhase { Practice, Battle, Won, Lost }
    internal enum AttackResult { None, Hit, Blocked }
    internal enum CastResult { None, Hit, Miss, Shield, Interrupt, FocusHit }

    internal sealed class RuneveilDuel
    {
        public const int MaxBossHealth = 120;
        public const float FocusSeconds = 0.35f;
        const float IdleSeconds = 2f;
        const float WindupSeconds = 2.4f;
        const int FocusBonus = 8;
        float attackIn = IdleSeconds;
        float focusedSeconds;

        public DuelPhase Phase { get; private set; } = DuelPhase.Practice;
        public int BossHealth { get; private set; } = MaxBossHealth;
        public int Hearts { get; private set; } = 3;
        public bool Shield { get; private set; }
        public float Windup { get; private set; } = -1;
        public int Interruptions { get; private set; }
        public int Blocks { get; private set; }
        public bool Finished => Phase == DuelPhase.Won || Phase == DuelPhase.Lost;
        public float FocusProgress => FocusReady ? 1f : focusedSeconds / FocusSeconds;
        public bool FocusReady => focusedSeconds >= FocusSeconds - 0.000001f;

        public void StartBattle()
        {
            Phase = DuelPhase.Battle;
            BossHealth = MaxBossHealth;
            Hearts = 3;
            Shield = false;
            Interruptions = 0;
            Blocks = 0;
            Recover();
        }

        public AttackResult Step(float deltaTime)
        {
            if (Finished) return AttackResult.None;

            attackIn -= deltaTime;
            if (attackIn > 0) return AttackResult.None;

            Windup = Mathf.Min(1, -attackIn / WindupSeconds);
            if (Windup < 1) return AttackResult.None;

            var blocked = Shield;
            if (blocked) Blocks++;
            else if (Phase == DuelPhase.Battle && --Hearts == 0) Phase = DuelPhase.Lost;

            Shield = false;
            Recover();
            return blocked ? AttackResult.Blocked : AttackResult.Hit;
        }

        public void TrackFocus(bool gazeOnCore, float deltaTime)
        {
            if (Finished || Windup < 0 || !gazeOnCore)
            {
                focusedSeconds = 0f;
                return;
            }

            // When this frame crossed into windup, count only its time within the warning.
            if (deltaTime > 0f)
                focusedSeconds = Mathf.Min(FocusSeconds, focusedSeconds + Mathf.Min(deltaTime, -attackIn));
        }

        /// <summary>Level 1 is a normal cast; each extra level adds 50% damage. Ward blocks one strike at any level.</summary>
        public CastResult Cast(Spell spell, ArenaTarget target, bool focusedAtRelease = false, int level = 1)
        {
            if (Finished) return CastResult.None;
            level = Mathf.Max(1, level);
            if (spell == Spell.Barrier)
            {
                Shield = true;
                return CastResult.Shield;
            }
            if (target == ArenaTarget.Miss) return CastResult.Miss;

            int damage = spell switch
            {
                Spell.Fire => 16,
                Spell.Lightning => 12,
                Spell.Ice => 14,
                Spell.Rock => 22,
                Spell.Wind => 14,
                _ => 0
            };
            if (damage == 0) return CastResult.None;

            bool coreHit = target == ArenaTarget.Core;
            bool activeWarning = Windup >= 0f;
            // The pinch debounce can finish after the eyes have moved. Use readiness at release
            // but require the warning to still be active when the cast is committed.
            bool focused = focusedAtRelease && coreHit && activeWarning;
            bool interrupted = spell == Spell.Lightning && coreHit && activeWarning;
            if (interrupted)
            {
                Interruptions++;
                Recover();
            }

            if (Phase == DuelPhase.Battle)
            {
                int total = damage + (coreHit ? 4 : 0) + (focused ? FocusBonus : 0);
                total = Mathf.RoundToInt(total * (1f + 0.5f * (level - 1)));
                BossHealth = Mathf.Max(0, BossHealth - total);
                if (BossHealth == 0)
                {
                    Phase = DuelPhase.Won;
                    Shield = false;
                    Recover();
                }
            }

            if (interrupted) return CastResult.Interrupt;
            return focused ? CastResult.FocusHit : CastResult.Hit;
        }

        void Recover()
        {
            attackIn = IdleSeconds;
            Windup = -1;
            focusedSeconds = 0f;
        }
    }
}
