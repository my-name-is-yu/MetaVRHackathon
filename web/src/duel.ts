import type { Spell } from './runes.ts';

export type Phase = 'practice' | 'battle' | 'won' | 'lost';
export type Target = 'core' | 'body' | 'miss';
export const maxBossHP = 120;
const damage = { fire: 16, lightning: 12, ice: 14, rock: 22, wind: 14 };
const warningTime = 2.4;
const idleTime = 2.0;

export class Duel {
  phase: Phase = 'practice';
  bossHP = maxBossHP;
  hearts = 3;
  shield = false;
  windup = -1;
  attackIn = idleTime;
  interruptions = 0;
  blocks = 0;

  reset(phase: 'practice' | 'battle') {
    this.phase = phase;
    this.bossHP = maxBossHP;
    this.hearts = 3;
    this.shield = false;
    this.windup = -1;
    this.attackIn = idleTime;
    this.interruptions = 0;
    this.blocks = 0;
  }

  step(dt: number): 'blocked' | 'hit' | null {
    if (this.phase === 'lost' || this.phase === 'won') return null;
    this.attackIn -= dt;
    if (this.attackIn > 0) return null;
    this.windup = Math.min(1, -this.attackIn / warningTime);
    if (this.windup < 1) return null;
    const blocked = this.shield;
    if (blocked) this.blocks++;
    if (!blocked && this.phase === 'battle') this.hearts--;
    if (this.hearts === 0) this.phase = 'lost';
    this.shield = false;
    this.recover();
    return blocked ? 'blocked' : 'hit';
  }

  cast(spell: Spell, target: Target): 'shield' | 'interrupt' | 'hit' | 'miss' | null {
    if (this.phase === 'lost' || this.phase === 'won') return null;
    if (spell === 'barrier') {
      this.shield = true;
      return 'shield';
    }
    if (target === 'miss') return 'miss';
    const interrupted = spell === 'lightning' && target === 'core' && this.windup >= 0;
    if (interrupted) {
      this.interruptions++;
      this.recover();
    }
    if (this.phase === 'battle') {
      this.bossHP = Math.max(0, this.bossHP - damage[spell] - (target === 'core' ? 4 : 0));
      if (this.bossHP === 0) {
        this.phase = 'won';
        this.windup = -1;
        this.shield = false;
      }
    }
    return interrupted ? 'interrupt' : 'hit';
  }

  private recover() {
    this.attackIn = idleTime;
    this.windup = -1;
  }
}
