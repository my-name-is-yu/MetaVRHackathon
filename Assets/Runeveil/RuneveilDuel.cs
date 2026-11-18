using UnityEngine;

namespace Runeveil
{
    internal enum DuelPhase { Practice, Battle, Won, Lost }
    internal enum AttackResult { None, Hit, Blocked }
    internal enum CastResult { None, Hit, Miss, Interrupt, Exposed, FocusHit }

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
        int nextWeakPoint;

        public DuelPhase Phase { get; private set; } = DuelPhase.Practice;
        public int BossHealth { get; private set; } = MaxBossHealth;
        public int Hearts { get; private set; } = 3;
        public bool Shield { get; private set; }
        public float Windup { get; private set; } = -1;
        /// <summary>雷で拳を止められたボスが膝をついている残り秒数。</summary>
        public float Stagger { get; private set; }
        /// <summary>バリアに防がれた拳が地面に刺さっている残り秒数。</summary>
        public float Stuck { get; private set; }
        /// <summary>今光っている弱点の部位番号（<see cref="RuneveilArena.WeakPointNames"/> の添字）。割れた直後の間は -1。</summary>
        public int WeakPoint { get; private set; }
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
            WeakPoint = 0;
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
        /// <paramref name="part"/> と <paramref name="focusedAtRelease"/> は指を離した瞬間の状態で、
        /// 弱点扱いかどうかはそのときの見た目で決まる。
        /// <paramref name="level"/> は溜めのレベルで、Lv1 が通常、1つ上がるごとにダメージが50%増える。
        /// </summary>
        public CastResult Hit(Spell spell, ArenaTarget target, int part, bool focusedAtRelease, int level = 1)
        {
            if (Finished || spell == Spell.Barrier) return CastResult.None;
            level = Mathf.Max(1, level);
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

            // 拳を止めた一撃自体には倍率をかけず、ダウン中の追撃だけを倍にする
            bool weak = target == ArenaTarget.Core;
            bool exposed = Stagger > 0 && weak;
            bool focused = focusedAtRelease && weak;
            bool interrupted = spell == Spell.Lightning && weak && Windup >= 0f;
            if (interrupted)
            {
                Interruptions++;
                Stagger = StaggerSeconds;
                Recover();
            }
            // 狙った弱点がまだ光っていれば割り、一拍おいて別の部位へ移す（飛んでいる間に移っていたら二重には移さない）
            if (weak && part == WeakPoint) BreakWeakPoint();

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

        void BreakWeakPoint()
        {
            // 直前と同じ部位を除いた残りから選ぶ
            int pick = random.Next(RuneveilArena.WeakPointCount - 1);
            nextWeakPoint = pick >= WeakPoint ? pick + 1 : pick;
            WeakPoint = -1;
            weakGapLeft = WeakPointGapSeconds;
        }

        void Recover()
        {
            attackIn = Enraged ? EnragedIdleSeconds : IdleSeconds;
            Windup = -1;
            focusedSeconds = 0f;
        }
    }
}
