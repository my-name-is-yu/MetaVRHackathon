using UnityEngine;

namespace Runeveil
{
    internal enum DuelPhase { Practice, Battle, Won, Lost }
    internal enum AttackResult { None, Hit, Blocked }
    internal enum CastResult { None, Hit, Miss, Shield, Interrupt }

    internal sealed class RuneveilDuel
    {
        public const int MaxBossHealth = 120;
        const float IdleSeconds = 2f;
        const float WindupSeconds = 2.4f;
        float attackIn = IdleSeconds;

        public DuelPhase Phase { get; private set; } = DuelPhase.Practice;
        public int BossHealth { get; private set; } = MaxBossHealth;
        public int Hearts { get; private set; } = 3;
        public bool Shield { get; private set; }
        public float Windup { get; private set; } = -1;
        public int Interruptions { get; private set; }
        public int Blocks { get; private set; }
        public bool Finished => Phase == DuelPhase.Won || Phase == DuelPhase.Lost;

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

        public CastResult Cast(Spell spell, ArenaTarget target)
        {
            if (Finished) return CastResult.None;
            if (spell == Spell.Barrier)
            {
                Shield = true;
                return CastResult.Shield;
            }
            if (target == ArenaTarget.Miss) return CastResult.Miss;

            var interrupted = spell == Spell.Lightning && target == ArenaTarget.Core && Windup >= 0;
            if (interrupted)
            {
                Interruptions++;
                Recover();
            }

            if (Phase == DuelPhase.Battle)
            {
                int damage = spell switch
                {
                    Spell.Fire => 16,
                    Spell.Lightning => 12,
                    Spell.Ice => 14,
                    Spell.Rock => 22,
                    Spell.Wind => 14,
                    _ => 0
                };
                BossHealth = Mathf.Max(0, BossHealth - damage - (target == ArenaTarget.Core ? 4 : 0));
                if (BossHealth == 0)
                {
                    Phase = DuelPhase.Won;
                    Shield = false;
                    Recover();
                }
            }

            return interrupted ? CastResult.Interrupt : CastResult.Hit;
        }

        void Recover()
        {
            attackIn = IdleSeconds;
            Windup = -1;
        }
    }
}
