import test from 'node:test';
import assert from 'node:assert/strict';
import { Duel, maxBossHP } from '../src/duel.ts';

test('only lightning on the exposed core interrupts the windup', () => {
  const duel = new Duel();
  duel.reset('battle');
  duel.step(2.2);
  assert.equal(duel.cast('fire', 'core'), 'hit');
  assert.equal(duel.cast('lightning', 'body'), 'hit');
  assert.equal(duel.cast('lightning', 'miss'), 'miss');
  assert.equal(duel.cast('lightning', 'core'), 'interrupt');
  assert.equal(duel.windup, -1);
  assert.equal(duel.interruptions, 1);
  assert.equal(duel.hearts, 3);
});

test('practice cannot damage the player or boss; a barrier blocks a single strike', () => {
  const duel = new Duel();
  duel.cast('rock', 'core');
  assert.equal(duel.bossHP, maxBossHP);
  assert.equal(duel.step(5), 'hit');
  assert.equal(duel.hearts, 3);
  duel.reset('battle');
  duel.cast('barrier', 'miss');
  duel.cast('barrier', 'miss');
  assert.equal(duel.step(5), 'blocked');
  assert.equal(duel.step(5), 'hit');
  assert.equal(duel.hearts, 2);
});

test('victory, defeat and replay reset all combat state', () => {
  const duel = new Duel();
  duel.reset('battle');
  while (duel.bossHP > 0) duel.cast('rock', 'core');
  assert.equal(duel.phase, 'won');
  assert.equal(duel.step(5), null);
  duel.reset('battle');
  for (let i = 0; i < 3; i++) duel.step(5);
  assert.equal(duel.phase, 'lost');
  assert.equal(duel.cast('barrier', 'miss'), null);
  duel.reset('battle');
  assert.equal(duel.hearts, 3);
  assert.equal(duel.bossHP, maxBossHP);
  assert.equal(duel.shield, false);
  assert.equal(duel.windup, -1);
});
