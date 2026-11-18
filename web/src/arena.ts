import * as THREE from 'three';
import { createArtMaterials } from './arena-art';
import { createEffects } from './arena-effects';
import { createGolem } from './arena-golem';
import type { ArenaPart, ArenaPhase } from './arena-golem';
import { createScenery } from './arena-scenery';
import type { Spell } from './runes';

export function createArena(scene: THREE.Scene) {
  const art = createArtMaterials();
  const updateSky = createScenery(scene, art);
  const golem = createGolem(scene, art);
  const effects = createEffects(scene, art);
  new THREE.TextureLoader().load(`${import.meta.env.BASE_URL}art/twilight-valley.jpg`, (panorama) => {
    panorama.colorSpace = THREE.SRGBColorSpace;
    panorama.mapping = THREE.EquirectangularReflectionMapping;
    scene.background = panorama;
    scene.backgroundBlurriness = 0;
    updateSky();
  }, undefined, () => { /* The procedural dusk remains visible. */ });

  return {
    update(dt: number, windup: number, shield: boolean, phase: ArenaPhase, coreAimed: boolean) {
      const step = Math.min(0.05, Math.max(0, dt));
      updateSky();
      golem.update(step, windup, phase, coreAimed);
      effects.update(step, shield);
    },
    aim: golem.aim,
    cast(spell: Spell, from: THREE.Vector3, to: THREE.Vector3,
      stance?: { head: THREE.Vector3; forward: THREE.Vector3 }, part: ArenaPart = 'miss') {
      effects.cast(spell, from, to, stance);
      if (spell !== 'barrier' && part !== 'miss') {
        golem.flinch(spell === 'lightning' && part === 'core');
      }
    },
    impact(blocked: boolean) {
      golem.strike();
      effects.impact(blocked);
    },
    reset() {
      golem.reset();
      effects.reset();
    },
  };
}
