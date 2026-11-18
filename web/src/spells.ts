import type { Spell } from './runes.ts';

export const spells: Record<Spell, { name: string; symbol: string; color: string }> = {
  fire: { name: '火球', symbol: '○', color: '#ff997d' },
  lightning: { name: '雷', symbol: 'Z', color: '#ffd56a' },
  ice: { name: '氷槍', symbol: 'V', color: '#a3e4ff' },
  rock: { name: '岩弾', symbol: '△', color: '#e4b99a' },
  wind: { name: '風刃', symbol: 'S', color: '#cfb6ff' },
  barrier: { name: 'バリア', symbol: '―', color: '#99f5d7' },
};
