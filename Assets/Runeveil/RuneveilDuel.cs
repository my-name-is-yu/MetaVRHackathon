using UnityEngine;

namespace Runeveil
{
    internal enum DuelPhase { Practice, Battle, Won, Lost }
    internal enum AttackResult { None, Hit, Blocked }
    internal enum CastResult { None, Hit, Miss, Interrupt, Exposed, FocusHit }

    internal readonly struct SpellTarget
    {
        public readonly ArenaTarget Part;
        public readonly ArenaTarget FocusedPart;
        public readonly int WeakPointRevision;

        public SpellTarget(ArenaTarget part, ArenaTarget focusedPart, int weakPointRevision)
        {
            Part = part;
            FocusedPart = focusedPart;
            WeakPointRevision = weakPointRevision;
        }
    }

    internal sealed class RuneveilDuel
    {
        public const int MaxBossHealth = 120;
        public const float FocusSeconds = 0.35f;
        public const int ExposedCoreMultiplier = 2;
        public const float ShieldSeconds = 1f;
        const float IdleSeconds = 2f;
        const float WindupSeconds = 2.4f;
        const float EnragedIdleSeconds = 1.5f;
        const float EnragedWindupSeconds = 1.8f;
        const float StaggerSeconds = 3f;
        const float StuckSeconds = 1.5f;
        const float WeakPointGapSeconds = 0.8f;
        const float BodyDamageRate = 0.5f;
        const int FocusBonus = 8;
        readonly float[] cooldowns = new float[(int)Spell.Barrier + 1];
        readonly System.Random random = new();
        float attackIn = IdleSeconds;
        float focusedSeconds;
        float shieldLeft;
        float weakGapLeft;
        ArenaTarget nextWeakPoint;
        ArenaTarget focusedPart;
        int weakPointRevision;

        public DuelPhase Phase { get; private set; } = DuelPhase.Practice;
        public int BossHealth { get; private set; } = MaxBossHealth;
        public int Hearts { get; private set; } = 3;
        public bool Shield { get; private set; }
        public float Windup { get; private set; } = -1;
        /// <summary>雷で拳を止められたボスが膝をついている残り秒数。</summary>
        public float Stagger { get; private set; }
        /// <summary>バリアに防がれた拳が地面に刺さっている残り秒数。</summary>
        public float Stuck { get; private set; }
        /// <summary>推奨する弱点。結晶が割れてから次が現れるまでは Miss。</summary>
        public ArenaTarget WeakPoint { get; private set; } = ArenaTarget.Core;
        public ArenaTarget ShatteredPoint { get; private set; }
        public ArenaTarget ExposedTarget => FocusReady ? focusedPart : ArenaTarget.Miss;
        public int Interruptions { get; private set; }
        public int Blocks { get; private set; }
        public bool Finished => Phase == DuelPhase.Won || Phase == DuelPhase.Lost;
        public float FocusProgress => FocusReady ? 1f : focusedSeconds / FocusSeconds;
        public bool FocusReady => focusedSeconds >= FocusSeconds - 0.000001f;
        /// <summary>ボスの体力が半分以下になり、攻撃のテンポが上がった状態。</summary>
        public bool Enraged => Phase == DuelPhase.Battle && BossHealth <= MaxBossHealth / 2;

        public static float CooldownSeconds(Spell spell) => spell switch
        {
            Spell.Fire or Spell.Ice or Spell.Wind => 1.5f,
            Spell.Lightning => 3f,
            Spell.Rock => 4f,
            _ => 3.5f
        };

        public float CooldownLeft(Spell spell) => cooldowns[(int)spell];

        public void StartBattle()
        {
            Phase = DuelPhase.Battle;
            BossHealth = MaxBossHealth;
            Hearts = 3;
            Shield = false;
            shieldLeft = 0;
            Stagger = 0;
            Stuck = 0;
            WeakPoint = ArenaTarget.Core;
            ShatteredPoint = ArenaTarget.Miss;
            weakPointRevision++;
            weakGapLeft = 0;
            Interruptions = 0;
            Blocks = 0;
            System.Array.Clear(cooldowns, 0, cooldowns.Length);
            Recover();
        }

        public AttackResult Step(float deltaTime)
        {
            if (Finished) return AttackResult.None;

            for (int i = 0; i < cooldowns.Length; i++)
                cooldowns[i] = Mathf.Max(0, cooldowns[i] - deltaTime);
            if (Shield && (shieldLeft -= deltaTime) <= 0) Shield = false;
            if (weakGapLeft > 0 && (weakGapLeft -= deltaTime) <= 0) WeakPoint = nextWeakPoint;

            // ダウン中と拳の硬直中は次の攻撃までの時間を進めない
            if (Stagger > 0)
            {
                Stagger = Mathf.Max(0, Stagger - deltaTime);
                return AttackResult.None;
            }
            if (Stuck > 0)
            {
                Stuck = Mathf.Max(0, Stuck - deltaTime);
                return AttackResult.None;
            }

            attackIn -= deltaTime;
            if (attackIn > 0) return AttackResult.None;

            Windup = Mathf.Min(1, -attackIn / (Enraged ? EnragedWindupSeconds : WindupSeconds));
            if (Windup < 1) return AttackResult.None;

            var blocked = Shield;
            if (blocked)
            {
                Blocks++;
                Stuck = StuckSeconds;
            }
            else if (Phase == DuelPhase.Battle && --Hearts == 0) Phase = DuelPhase.Lost;

            Shield = false;
            Recover();
            return blocked ? AttackResult.Blocked : AttackResult.Hit;
        }

        public void TrackFocus(ArenaTarget target, float deltaTime)
        {
            if (Finished || Windup < 0 || !WeakPoint.IsWeakPoint() || !target.IsWeakPoint())
                target = ArenaTarget.Miss;
            if (target != focusedPart)
            {
                ClearFocus();
                focusedPart = target;
            }

            // Count only the part of a frame that fell inside the warning.
            if (target != ArenaTarget.Miss && deltaTime > 0f)
                focusedSeconds = Mathf.Min(FocusSeconds, focusedSeconds + Mathf.Min(deltaTime, -attackIn));
        }

        public SpellTarget CaptureTarget(ArenaTarget part)
        {
            ArenaTarget focus = part.IsWeakPoint() && part == ExposedTarget ? part : ArenaTarget.Miss;
            if (part.IsWeakPoint() && part != WeakPoint && focus == ArenaTarget.Miss)
                part = ArenaTarget.Body;
            return new SpellTarget(part, focus, weakPointRevision);
        }

        /// <summary>
        /// 指を離した瞬間に呼ぶ。クールタイム中なら false を返して何もしない。
        /// バリアはここで張り、攻撃魔法の効果は着弾時の <see cref="Hit"/> で決まる。
        /// </summary>
        public bool TryCast(Spell spell)
        {
            if (Finished || cooldowns[(int)spell] > 0) return false;

            cooldowns[(int)spell] = CooldownSeconds(spell);
            if (spell == Spell.Barrier)
            {
                Shield = true;
                shieldLeft = ShieldSeconds;
            }
            return true;
        }

        /// <summary>
        /// 攻撃魔法が着弾したときに呼び、ダメージと拳の中断を決める。
        /// <paramref name="target"/> は指を離した瞬間に決まった部位と露呈状態。
        /// <paramref name="level"/> は溜めのレベルで、Lv1 が通常、1つ上がるごとにダメージが50%増える。
        /// </summary>
        public CastResult Hit(Spell spell, SpellTarget target, int level = 1)
        {
            if (Finished || spell == Spell.Barrier) return CastResult.None;
            level = Mathf.Max(1, level);
            if (target.Part == ArenaTarget.Miss) return CastResult.Miss;

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

            // 拳を止めた一撃自体には倍率をかけず、ダウン中の追撃だけを倍にする
            bool weak = target.Part.IsWeakPoint();
            bool exposed = Stagger > 0 && weak;
            bool focused = weak && target.FocusedPart == target.Part;
            bool interrupted = spell == Spell.Lightning && weak && Windup >= 0f;
            if (interrupted)
            {
                Interruptions++;
                Stagger = StaggerSeconds;
                Recover();
            }
            // An earlier projectile may already have shattered this set of weak points.
            if (weak && target.WeakPointRevision == weakPointRevision && WeakPoint.IsWeakPoint())
                BreakWeakPoint(target.Part);

            if (Phase == DuelPhase.Battle)
            {
                // 弱点以外への命中は、狙えていなくても通る代わりに威力を減らす
                float total = weak ? damage + 4 + (focused ? FocusBonus : 0) : damage * BodyDamageRate;
                total *= 1f + 0.5f * (level - 1);
                if (exposed) total *= ExposedCoreMultiplier;
                BossHealth = Mathf.Max(0, BossHealth - Mathf.RoundToInt(total));
                if (BossHealth == 0)
                {
                    Phase = DuelPhase.Won;
                    Shield = false;
                    Stagger = 0;
                    Stuck = 0;
                    Recover();
                }
            }

            if (interrupted) return CastResult.Interrupt;
            if (exposed) return CastResult.Exposed;
            return focused ? CastResult.FocusHit : CastResult.Hit;
        }

        void BreakWeakPoint(ArenaTarget part)
        {
            int struck = part.WeakPointIndex();
            int recommended = WeakPoint.WeakPointIndex();
            int first = Mathf.Min(struck, recommended);
            int last = Mathf.Max(struck, recommended);
            int pick = random.Next(RuneveilArena.WeakPointCount - (first == last ? 1 : 2));
            if (pick >= first) pick++;
            if (last != first && pick >= last) pick++;
            nextWeakPoint = (ArenaTarget)((int)ArenaTarget.Core + pick);
            ShatteredPoint = part;
            WeakPoint = ArenaTarget.Miss;
            weakPointRevision++;
            weakGapLeft = WeakPointGapSeconds;
            ClearFocus();
        }

        void Recover()
        {
            attackIn = Enraged ? EnragedIdleSeconds : IdleSeconds;
            Windup = -1;
            ClearFocus();
        }

        void ClearFocus()
        {
            focusedPart = ArenaTarget.Miss;
            focusedSeconds = 0f;
        }
    }
}
