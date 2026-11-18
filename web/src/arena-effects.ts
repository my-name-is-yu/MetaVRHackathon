import * as THREE from 'three';
import { createSpot, placeMesh, seededRandom } from './arena-art';
import type { ArtMaterials } from './arena-art';
import { glyphs } from './runes';
import type { Spell } from './runes';

type Attack = Exclude<Spell, 'barrier'>;
type Stance = { head: THREE.Vector3; forward: THREE.Vector3 };

const styles = {
  fire: { color: 0xff714e, light: 0xffd28c, flight: 0.38, arc: 0.29 },
  lightning: { color: 0xffbb43, light: 0xfff6c7, flight: 0.10, arc: 0 },
  ice: { color: 0x6ecafe, light: 0xdafbff, flight: 0.32, arc: 0.10 },
  rock: { color: 0xbf8a68, light: 0xffcca8, flight: 0.53, arc: 0.63 },
  wind: { color: 0xb194ff, light: 0xe3d9ff, flight: 0.37, arc: 0.07 },
};
const attacks = Object.keys(styles) as Attack[];
const mint = 0x8ff8d3;
const up = new THREE.Vector3(0, 1, 0);
const forward = new THREE.Vector3(0, 0, 1);

function glow(color: number) {
  return new THREE.MeshBasicMaterial({ color, transparent: true, depthWrite: false, toneMapped: false,
    blending: THREE.AdditiveBlending });
}

function stroke(points: THREE.Vector3[], radius: number, closed = false) {
  const curve = new THREE.CatmullRomCurve3(points, closed);
  return new THREE.TubeGeometry(curve, Math.max(17, points.length * 3), radius, 5, closed);
}

function boltPath(points: number[][], radius: number) {
  const curve = new THREE.CurvePath<THREE.Vector3>();
  for (let index = 1; index < points.length; index++) {
    const a = points[index - 1];
    const b = points[index];
    curve.add(new THREE.LineCurve3(new THREE.Vector3(a[0], a[1], a[2]), new THREE.Vector3(b[0], b[1], b[2])));
  }
  return new THREE.TubeGeometry(curve, 45, radius, 6, false);
}

function runeMark(parent: THREE.Object3D, spell: Spell, material: THREE.MeshBasicMaterial) {
  const samples = glyphs[spell];
  const points = samples.filter((_, index) => index % 3 === 0 || index === samples.length - 1)
    .map((point) => new THREE.Vector3(point.x * 0.20, point.y * 0.20, 0));
  return placeMesh(parent, stroke(points, 0.006), material, [0, 0, 0]);
}

function createShield(stage: THREE.Group) {
  const root = new THREE.Group();
  root.name = 'Mint ward';
  stage.add(root);
  const shell = new THREE.ShaderMaterial({
    transparent: true,
    side: THREE.DoubleSide,
    depthWrite: false,
    forceSinglePass: true,
    toneMapped: false,
    uniforms: {
      mint: { value: new THREE.Color(mint) }, amount: { value: 0 }, pulse: { value: 0 }, time: { value: 0 },
    },
    vertexShader: `
      varying vec3 vSurface;
      varying vec3 vNormal;
      varying vec2 vUv;
      void main() {
        vec4 p = modelViewMatrix * vec4(position, 1.0);
        vSurface = p.xyz;
        vNormal = normalMatrix * normal;
        vUv = uv;
        gl_Position = projectionMatrix * p;
      }
    `,
    fragmentShader: `
      uniform vec3 mint;
      uniform float amount;
      uniform float pulse;
      uniform float time;
      varying vec3 vSurface;
      varying vec3 vNormal;
      varying vec2 vUv;
      void main() {
        float rim = pow(1.0 - abs(dot(normalize(vNormal), normalize(vSurface))), 2.0);
        float ripple = max(0.0, sin(vUv.y * 17.0 - vUv.x * 1.4 + time * 6.0));
        float opacity = amount * (0.09 + rim * 0.19 + ripple * 0.017) + pulse * (0.06 + rim * 0.11);
        gl_FragColor = vec4(mint, opacity);
        #include <colorspace_fragment>
      }
    `,
  });
  placeMesh(root, new THREE.SphereGeometry(1, 34, 13, Math.PI, Math.PI, 0, Math.PI / 2),
    shell, [0, 0.12, -0.30], [1.85, 1.14, 1.48]);
  const edgeMaterial = glow(mint);
  edgeMaterial.blending = THREE.NormalBlending;
  // Trace the shield's apparent upper edge from the neutral eye in its local space.
  const crest = Array.from({ length: 29 }, (_, index) => {
    const x = (index / 28 * 2 - 1) * 0.982;
    const radius = Math.sqrt(1 - x * x);
    const angle = Math.acos(0.761 * radius) - 0.155;
    return new THREE.Vector3(x * 1.85, 0.12 + Math.cos(angle) * 1.14 * radius,
      -0.30 - Math.sin(angle) * 1.48 * radius);
  });
  placeMesh(root, new THREE.TubeGeometry(new THREE.CatmullRomCurve3(crest), 48, 0.016, 6, false), edgeMaterial, [0, 0, 0]);
  const ringMat = glow(0xb5ffe8);
  const ripple = placeMesh(root, new THREE.TorusGeometry(0.26, 0.010, 5, 24), ringMat, [0, 0.82, -1.54], [1, 1, 1]);

  let amount = 0;
  let flash = 0;
  let time = 0;
  function update(dt: number, active: boolean) {
    time += dt;
    amount = THREE.MathUtils.damp(amount, active ? 1 : 0, 19, dt);
    flash = Math.max(0, flash - dt * 2.65);
    const visibility = Math.max(amount, flash);
    root.visible = visibility > 0.015;
    if (!root.visible) return;
    shell.uniforms.amount.value = visibility;
    shell.uniforms.pulse.value = flash;
    shell.uniforms.time.value = time;
    edgeMaterial.opacity = 0.70 * visibility + 0.18 * flash;
    ripple.visible = flash > 0.04;
    ripple.scale.setScalar(0.45 + (1 - flash) * 3.5);
    ringMat.opacity = flash * 0.77;
  }
  function place(stance?: Stance) {
    if (stance) {
      root.position.set(stance.head.x, stance.head.y - 1.6, stance.head.z);
      if (Math.hypot(stance.forward.x, stance.forward.z) > 0.01) {
        root.rotation.y = Math.atan2(-stance.forward.x, -stance.forward.z);
      }
    }
    flash = 0.62;
  }
  function reset() {
    flash = amount = time = 0;
    root.visible = false;
    root.position.set(0, 0, 0);
    root.rotation.set(0, 0, 0);
  }
  return { update, place, flash: () => { flash = 1; }, reset };
}

function createProjectile(parent: THREE.Group, spell: Attack, art: ArtMaterials) {
  const spec = styles[spell];
  const root = new THREE.Group();
  root.name = `${spell} cast`;
  root.visible = false;
  parent.add(root);

  const launchMat = glow(spec.light);
  const rune = runeMark(root, spell, launchMat);
  const projectile = new THREE.Group();
  root.add(projectile);
  const hot = glow(spec.light);
  const tint = glow(spec.color);
  const sphere = art.ellipsoid;
  const tail = createSpot(projectile, art.spot, spec.color, 0.90);
  tail.scale.setScalar(0.62);
  const shards = art.crystal;
  const line = new THREE.TorusGeometry(0.133, 0.009, 5, 27, Math.PI * 1.79);
  let rockChunks: THREE.MeshBasicMaterial | undefined;

  switch (spell) {
    case 'fire': {
      placeMesh(projectile, sphere, hot, [0, 0.02, 0], [0.101, 0.146, 0.1]);
      placeMesh(projectile, sphere, tint, [0, -0.074, 0], [0.127, 0.185, 0.12]);
      placeMesh(projectile, line, tint, [0, -0.172, 0], [1, 1.32, 1], [0.88, 0.13, 0.66]);
      placeMesh(projectile, line, hot, [0, -0.34, 0], [0.62, 0.88, 1], [1.05, -0.30, -1.8]);
      break;
    }
    case 'ice': {
      placeMesh(projectile, shards, hot, [0, 0.12, 0], [0.092, 0.29, 0.098]);
      placeMesh(projectile, shards, tint, [0, 0.012, 0.014], [0.132, 0.26, 0.130]);
      for (const side of [-1, 1]) {
        placeMesh(projectile, shards, tint, [side * 0.109, -0.12, 0.035], [0.052, 0.19, 0.052], [0.15, 0, -side * 0.34]);
      }
      break;
    }
    case 'rock': {
      rockChunks = new THREE.MeshBasicMaterial({ color: 0xc68971, map: art.stone, vertexColors: true,
        transparent: true, depthWrite: false });
      placeMesh(projectile, art.rock, rockChunks, [0, 0, 0], [0.162, 0.172, 0.167], [0.31, 0.43, 0.085]);
      placeMesh(projectile, art.rock, rockChunks, [0.171, -0.185, 0.024], [0.065, 0.069, 0.050]);
      placeMesh(projectile, art.rock, rockChunks, [-0.142, -0.253, -0.042], [0.077, 0.082, 0.076]);
      placeMesh(projectile, line, hot, [-0.050, -0.023, 0.01], [1.33, 1.40, 1], [0.2, 0.62, 1.26]);
      break;
    }
    case 'wind': {
      const crescent = new THREE.TorusGeometry(0.21, 0.018, 6, 34, Math.PI * 0.95);
      placeMesh(projectile, crescent, hot, [0, -0.04, 0], [1, 1.5, 1], [0, 0, 0.15]);
      placeMesh(projectile, crescent, tint, [0, -0.152, 0.041], [1.22, 1.83, 0.83], [0.27, -0.39, 0.23]);
      placeMesh(projectile, crescent, tint, [0, -0.265, -0.048], [0.77, 1.4, 1], [-0.24, 0.45, 0.05]);
      break;
    }
    case 'lightning': {
      tail.visible = false;
      const path = [[0, 0, 0], [0.17, 0.18, -0.098], [-0.16, 0.29, 0.128], [0.14, 0.43, 0.113],
        [-0.19, 0.61, -0.08], [0.13, 0.74, 0.13], [-0.07, 0.87, 0.072], [0, 1, 0]];
      placeMesh(projectile, boltPath(path, 0.012), hot, [0, 0, 0]);
      placeMesh(projectile, boltPath(path, 0.027), tint, [0, 0, 0]);
      placeMesh(projectile, boltPath([[0.14, 0.43, 0.113], [0.29, 0.54, -0.12], [0.45, 0.72, 0.09],
        [0.36, 0.86, 0.10]], 0.010), tint, [0, 0, 0]);
      break;
    }
  }

  const burst = new THREE.Group();
  root.add(burst);
  const hitGlow = createSpot(burst, art.spot, spec.color, 0.76);
  hitGlow.scale.setScalar(1.04);
  const ringMat = glow(spec.light);
  const wave = placeMesh(burst, new THREE.TorusGeometry(0.27, spell === 'rock' ? 0.022 : 0.011, 5, 24),
    ringMat, [0, 0, 0]);
  const random = seededRandom(spec.color);
  const speeds = Array.from({ length: 13 }, () => new THREE.Vector3((random() - 0.5) * 1.8, (random() - 0.38) * 1.92,
    (random() - 0.36) * 1.47));
  const coords = new THREE.BufferAttribute(new Float32Array(speeds.length * 3), 3).setUsage(THREE.DynamicDrawUsage);
  const sparkGeo = new THREE.BufferGeometry().setAttribute('position', coords);
  const sparkMat = new THREE.PointsMaterial({ color: spec.light, size: spell === 'rock' ? 0.14 : 0.11, map: art.spot,
    transparent: true, depthWrite: false, toneMapped: false, blending: THREE.AdditiveBlending });
  const sparks = new THREE.Points(sparkGeo, sparkMat);
  sparks.frustumCulled = false;
  burst.add(sparks);

  const from = new THREE.Vector3();
  const to = new THREE.Vector3();
  const direction = new THREE.Vector3();
  const facing = new THREE.Vector3();
  const velocity = new THREE.Vector3();
  const rotation = new THREE.Quaternion();
  let age = 0;

  function start(origin: THREE.Vector3, destination: THREE.Vector3) {
    from.copy(origin);
    to.copy(destination);
    direction.subVectors(to, from);
    if (direction.lengthSq() < 0.0001) direction.set(0, 0, -0.01);
    facing.copy(direction).normalize().negate();
    age = 0;
    root.visible = true;
    burst.position.copy(to);
    wave.quaternion.setFromUnitVectors(forward, facing);
    rune.position.copy(from).addScaledVector(facing, -0.031);
    rune.quaternion.setFromUnitVectors(forward, facing);
    update(0);
  }

  function update(dt: number) {
    if (!root.visible) return;
    age += dt;
    root.visible = age < spec.flight + 0.42;
    if (!root.visible) return;
    const t = Math.min(1, age / spec.flight);
    rune.visible = age < 0.27;
    launchMat.opacity = Math.max(0, 0.90 * (1 - age / 0.27));
    rune.scale.setScalar(0.75 + age * 2.8);

    projectile.visible = age < spec.flight + (spell === 'lightning' ? 0.11 : 0.02);
    if (spell === 'lightning') {
      projectile.position.copy(from);
      projectile.quaternion.setFromUnitVectors(up, velocity.copy(direction).normalize());
      projectile.scale.y = direction.length();
      hot.opacity = 0.88 * Math.max(0, 1 - age / 0.21);
      tint.opacity = 0.68 * Math.max(0, 1 - age / 0.21);
    } else {
      projectile.position.copy(from).lerp(to, t);
      projectile.position.y += Math.sin(t * Math.PI) * spec.arc;
      velocity.copy(direction);
      velocity.y += Math.cos(t * Math.PI) * spec.arc * Math.PI;
      projectile.quaternion.setFromUnitVectors(up, velocity.normalize());
      projectile.quaternion.multiply(rotation.setFromAxisAngle(up, age * (spell === 'rock' ? 9 : 4)));
      hot.opacity = 0.80;
      tint.opacity = 0.61;
      tail.material.opacity = 0.55;
      if (rockChunks) rockChunks.opacity = 0.96;
    }

    const after = age - spec.flight;
    burst.visible = after >= 0;
    if (!burst.visible) return;
    const fade = Math.max(0, 1 - after / 0.42);
    hitGlow.material.opacity = fade * 0.65;
    hitGlow.scale.setScalar(0.62 + after * 2.3);
    wave.scale.setScalar(0.52 + after * (spell === 'rock' ? 5.1 : 3.4));
    ringMat.opacity = fade * 0.73;
    sparkMat.opacity = fade * 0.89;
    speeds.forEach((speed, i) => coords.setXYZ(i, speed.x * after,
      speed.y * after - after * after * (spell === 'rock' ? 2.6 : 0.65), speed.z * after));
    coords.needsUpdate = true;
  }

  return { start, update, reset: () => { root.visible = false; age = 0; } };
}

export function createEffects(scene: THREE.Scene, art: ArtMaterials) {
  const stage = new THREE.Group();
  stage.name = 'Spell and shield effects';
  scene.add(stage);
  const shield = createShield(stage);
  const bolts = Object.fromEntries(attacks.map((spell) => [spell, {
    index: 0,
    projectiles: Array.from({ length: 2 }, () => createProjectile(stage, spell, art)),
  }])) as Record<Attack, { index: number; projectiles: ReturnType<typeof createProjectile>[] }>;

  const barrierGlow = glow(mint);
  const barrierRune = runeMark(stage, 'barrier', barrierGlow);
  const ringMat = glow(0xe99461);
  const impactWave = placeMesh(stage, new THREE.TorusGeometry(0.52, 0.029, 6, 39), ringMat,
    [-1.13, 0.045, -4.54], [1, 1, 1], [-Math.PI / 2, 0, 0]);
  const dust = createSpot(stage, art.spot, 0xf39a65, 0.52);
  dust.position.set(-1.13, 0.34, -4.50);
  let thump = 0;
  let barrierAge = 1;

  function update(dt: number, active: boolean) {
    shield.update(dt, active);
    for (const spell of attacks) {
      for (const projectile of bolts[spell].projectiles) projectile.update(dt);
    }
    barrierAge += dt;
    barrierRune.visible = barrierAge < 0.29;
    barrierRune.scale.setScalar(0.85 + barrierAge * 2.15);
    barrierGlow.opacity = Math.max(0, 0.94 * (1 - barrierAge / 0.29));
    thump = Math.max(0, thump - dt * 2.17);
    impactWave.visible = dust.visible = thump > 0.02;
    ringMat.opacity = 0.75 * thump;
    impactWave.scale.setScalar(0.85 + (1 - thump) * 2.98);
    dust.scale.setScalar(1.46 + (1 - thump) * 0.98);
    dust.material.opacity = thump * 0.49;
  }

  function cast(spell: Spell, from: THREE.Vector3, to: THREE.Vector3, stance?: Stance) {
    if (spell === 'barrier') {
      shield.place(stance);
      barrierRune.position.copy(from);
      if (stance) barrierRune.quaternion.setFromUnitVectors(forward, stance.forward.clone().normalize().negate());
      else barrierRune.quaternion.identity();
      barrierRune.visible = true;
      barrierAge = 0;
      return;
    }
    const pool = bolts[spell];
    pool.projectiles[pool.index].start(from, to);
    pool.index = (pool.index + 1) % pool.projectiles.length;
  }

  function reset() {
    for (const spell of attacks) {
      const pool = bolts[spell];
      pool.index = 0;
      for (const projectile of pool.projectiles) projectile.reset();
    }
    shield.reset();
    thump = 0;
    barrierAge = 1;
    impactWave.visible = dust.visible = barrierRune.visible = false;
  }

  reset();
  return {
    update,
    cast,
    impact(blocked: boolean) {
      thump = blocked ? 0.55 : 1;
      if (blocked) shield.flash();
    },
    reset,
  };
}
