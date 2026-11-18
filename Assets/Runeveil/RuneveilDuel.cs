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
        public readonly bool WardCounter;

        public SpellTarget(ArenaTarget part, ArenaTarget focusedPart, int weakPointRevision, bool wardCounter = false)
        {
            Part = part;
            FocusedPart = focusedPart;
            WeakPointRevision = weakPointRevision;
            WardCounter = wardCounter;
        }
    }

    internal sealed class RuneveilDuel
    {
        public const int MaxBossHealth = 120;
        public const int MaxHearts = 5;
        public const int MaxChargeLevel = 3;
        public const float FocusSeconds = 0.35f;
        public const int ExposedCoreMultiplier = 2;
        public const float ShieldSeconds = 1.5f;
        const float IdleSeconds = 2f;
        internal const float WindupSeconds = 2.4f;
        const float EnragedIdleSeconds = 1.5f;
        const float StaggerSeconds = 3f;
        const float StuckSeconds = 2.2f;
        const float WeakPointGapSeconds = 0.8f;
        const float BodyDamageRate = 0.55f;
        const int FocusBonus = 8;
        const int ChargeBonusPerLevel = 6;
        readonly float[] cooldowns = new float[(int)Spell.Barrier + 1];
        readonly System.Random random = new();
        float attackIn = IdleSeconds;
        float focusedSeconds;
        float shieldLeft;
        bool? shieldAtStrike;
        AttackResult committedStrike;
        float weakGapLeft;
        ArenaTarget weakPoint = ArenaTarget.Core;
        ArenaTarget nextWeakPoint;
        ArenaTarget focusedPart;
        int weakPointRevision;

        public DuelPhase Phase { get; private set; } = DuelPhase.Practice;
        public int BossHealth { get; private set; } = MaxBossHealth;
        public int Hearts { get; private set; } = MaxHearts;
        public bool Shield { get; private set; }
        public bool StrikeCommitted => committedStrike != AttackResult.None;
        public bool WardVisible => StrikeCommitted ? committedStrike == AttackResult.Blocked : (shieldAtStrike ?? Shield);
        public float Windup { get; private set; } = -1;
        public float WindupSecondsLeft => Windup >= 0 ? Mathf.Max(0, attackIn + WindupSeconds) : -1f;
        public int WindupRevision { get; private set; }
        public bool HeavyStrike { get; private set; }
        /// <summary>雷で拳を止められたボスが膝をついている残り秒数。</summary>
        public float Stagger { get; private set; }
        /// <summary>バリアに防がれた拳が地面に刺さっている残り秒数。</summary>
        public float Stuck { get; private set; }
        public bool WardCounterActive { get; private set; }
        /// <summary>推奨する弱点。結晶が割れてから次が現れるまでは Miss。</summary>
        public ArenaTarget WeakPoint => WardCounterActive ? ArenaTarget.RaisedFist : weakPoint;
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
            Hearts = MaxHearts;
            Shield = false;
            shieldLeft = 0;
            Stagger = 0;
            Stuck = 0;
            WardCounterActive = false;
            weakPoint = ArenaTarget.Core;
            ShatteredPoint = ArenaTarget.Miss;
            weakPointRevision++;
            weakGapLeft = 0;
            Interruptions = 0;
            Blocks = 0;
            System.Array.Clear(cooldowns, 0, cooldowns.Length);
            Recover();
        }

        // Commit the guard decision at the input deadline. The arena resolves it at the fist's contact pose.
        public bool Step(float deltaTime, bool deferStrike = false)
        {
            if (Finished) return false;

            // The shield can expire later in the frame that crosses the strike deadline.
            bool wardAtDeadline = Shield && shieldLeft > Mathf.Max(0f, attackIn + WindupSeconds);
            for (int i = 0; i < cooldowns.Length; i++)
                cooldowns[i] = Mathf.Max(0, cooldowns[i] - deltaTime);
            if (Shield && (shieldLeft -= deltaTime) <= 0) Shield = false;
            if (weakGapLeft > 0 && (weakGapLeft -= deltaTime) <= 0) weakPoint = nextWeakPoint;

            // ダウン中と拳の硬直中は次の攻撃までの時間を進めない
            if (Stagger > 0)
            {
                Stagger = Mathf.Max(0, Stagger - deltaTime);
                return false;
            }
            if (Stuck > 0)
            {
                Stuck = Mathf.Max(0, Stuck - deltaTime);
                if (Stuck == 0 && WardCounterActive)
                {
                    WardCounterActive = false;
                    weakPointRevision++;
                }
                return false;
            }

            if (StrikeCommitted) return false;
            attackIn -= deltaTime;
            if (attackIn > 0) return false;

            if (Windup < 0)
            {
                WindupRevision++;
                HeavyStrike = Enraged;
            }

            Windup = Mathf.Min(1, -attackIn / WindupSeconds);
            if (Windup < 1) return false;
            if (deferStrike)
            {
                shieldAtStrike ??= wardAtDeadline;
                attackIn = -WindupSeconds;
                return false;
            }

            committedStrike = (shieldAtStrike ?? wardAtDeadline) ? AttackResult.Blocked : AttackResult.Hit;
            Shield = false;
            Windup = -1f;
            ClearFocus();
            return true;
        }

        public AttackResult ResolveStrike()
        {
            AttackResult result = committedStrike;
            if (result == AttackResult.None) return result;

            float swingTime = RuneveilArena.StrikeTravelSeconds(HeavyStrike);
            if (result == AttackResult.Blocked)
            {
                Blocks++;
                Stuck = StuckSeconds;
                WardCounterActive = true;
                weakPointRevision++;
            }
            else if (Phase == DuelPhase.Battle)
            {
                Hearts = Mathf.Max(0, Hearts - (HeavyStrike ? 2 : 1));
                if (Hearts == 0)
                {
                    Phase = DuelPhase.Lost;
                    Shield = false;
                }
            }

            Recover();
            // An unblocked punch uses part of the original cooldown to travel to contact.
            if (result == AttackResult.Hit) attackIn -= swingTime;
            return result;
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
            return new SpellTarget(part, focus, weakPointRevision, WardCounterActive && part == ArenaTarget.RaisedFist);
        }

        public ArenaTarget DamageTarget(SpellTarget target) =>
            target.WardCounter && (!WardCounterActive || target.WeakPointRevision != weakPointRevision)
                ? ArenaTarget.Body
                : target.Part;

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
        /// <paramref name="level"/> は溜めのレベルで、倍率の計算後にLv2で6、Lv3で12を加える。胴体では加算分も減る。
        /// </summary>
        public CastResult Hit(Spell spell, SpellTarget target, int level = 1)
        {
            if (Finished || spell == Spell.Barrier) return CastResult.None;
            int chargeBonus = spell == Spell.Rock ? 0 : (Mathf.Clamp(level, 1, MaxChargeLevel) - 1) * ChargeBonusPerLevel;
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
            bool weak = DamageTarget(target).IsWeakPoint();
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
            if (weak && target.WeakPointRevision == weakPointRevision && WeakPoint.IsWeakPoint() &&
                (!WardCounterActive || target.Part == ArenaTarget.RaisedFist))
                BreakWeakPoint(target.Part);

            if (Phase == DuelPhase.Battle)
            {
                // 弱点以外への命中は、狙えていなくても通る代わりに威力を減らす
                int weakDamage = damage + 4 + (focused ? FocusBonus : 0);
                float total = weak
                    ? weakDamage * (exposed ? ExposedCoreMultiplier : 1) + chargeBonus
                    : (damage + chargeBonus) * BodyDamageRate;
                BossHealth = Mathf.Max(0, BossHealth - Mathf.RoundToInt(total));
                if (BossHealth == 0)
                {
                    Phase = DuelPhase.Won;
                    Shield = false;
                    Stagger = 0;
                    Stuck = 0;
                    WardCounterActive = false;
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
            WardCounterActive = false;
            weakPoint = ArenaTarget.Miss;
            weakPointRevision++;
            weakGapLeft = WeakPointGapSeconds;
            ClearFocus();
        }

        void Recover()
        {
            attackIn = Enraged ? EnragedIdleSeconds : IdleSeconds;
            shieldAtStrike = null;
            committedStrike = AttackResult.None;
            Windup = -1;
            HeavyStrike = false;
            ClearFocus();
        }

        void ClearFocus()
        {
            focusedPart = ArenaTarget.Miss;
            focusedSeconds = 0f;
        }
    }
}
