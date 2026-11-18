import * as THREE from 'three';
import { createSpot, InstanceBatch, placeMesh, seededRandom, shaded } from './arena-art';
import type { ArtMaterials, Triple } from './arena-art';

export type ArenaPhase = 'practice' | 'battle' | 'won' | 'lost';
export type ArenaPart = 'core' | 'body' | 'miss';

const up = new THREE.Vector3(0, 1, 0);

function sculptedEye(mirror: number) {
  const eye = new THREE.Shape();
  eye.moveTo(-0.22 * mirror, 0.068);
  eye.quadraticCurveTo(-0.17 * mirror, 0.182, -0.10 * mirror, 0.176);
  eye.quadraticCurveTo(0.019 * mirror, 0.133, 0.205 * mirror, 0.034);
  eye.quadraticCurveTo(0.206 * mirror, -0.114, 0.06 * mirror, -0.18);
  eye.quadraticCurveTo(-0.103 * mirror, -0.199, -0.193 * mirror, -0.08);
  eye.closePath();
  return new THREE.ExtrudeGeometry(eye, {
    depth: 0.03, bevelEnabled: true, bevelSegments: 3, bevelSize: 0.029, bevelThickness: 0.036, curveSegments: 10,
  });
}

function chestPlate() {
  const shape = new THREE.Shape();
  shape.moveTo(0, -0.68);
  shape.bezierCurveTo(-0.24, -0.50, -0.73, -0.24, -0.72, 0.02);
  shape.bezierCurveTo(-0.66, 0.30, -0.39, 0.42, -0.19, 0.32);
  shape.quadraticCurveTo(0, 0.24, 0.19, 0.32);
  shape.bezierCurveTo(0.39, 0.42, 0.66, 0.30, 0.72, 0.02);
  shape.bezierCurveTo(0.73, -0.24, 0.24, -0.50, 0, -0.68);
  return new THREE.ExtrudeGeometry(shape, {
    depth: 0.062, bevelEnabled: true, bevelSegments: 3, bevelSize: 0.061, bevelThickness: 0.057, curveSegments: 15,
  });
}

function trail(points: Triple[], thickness: number) {
  const curve = new THREE.CatmullRomCurve3(points.map((point) => new THREE.Vector3(...point)));
  return new THREE.TubeGeometry(curve, points.length * 5, thickness, 5, false);
}

function hornProfile(side: number) {
  return new THREE.CatmullRomCurve3([
    new THREE.Vector3(side * 0.45, 2.91, 0.025),
    new THREE.Vector3(side * 0.72, 3.13, 0.061),
    new THREE.Vector3(side * 0.75, 3.41, 0.065),
    new THREE.Vector3(side * 0.63, 3.59, 0.090),
    new THREE.Vector3(side * 0.44, 3.65, 0.13),
  ]);
}

function hornSection(curve: THREE.CatmullRomCurve3, start: number, end: number) {
  const sides = 8;
  const rings = 5;
  const positions: number[] = [];
  const uvs: number[] = [];
  const indices: number[] = [];
  const forward = new THREE.Vector3(0, 0, 1);
  for (let ring = 0; ring < rings; ring++) {
    const local = ring / (rings - 1);
    const t = THREE.MathUtils.lerp(start, end, local);
    const center = curve.getPoint(t);
    const perpendicular = curve.getTangent(t).cross(forward).normalize();
    const radius = (0.26 * Math.pow(1 - t, 0.65) + 0.015) * (ring === 0 || ring === rings - 1 ? 0.91 : 1);
    for (let angle = 0; angle < sides; angle++) {
      const a = angle / sides * Math.PI * 2;
      const position = center.clone().addScaledVector(perpendicular, Math.cos(a) * radius)
        .addScaledVector(forward, Math.sin(a) * radius);
      positions.push(position.x, position.y, position.z);
      uvs.push(angle / sides, t);
      if (ring === rings - 1) continue;
      const p = ring * sides + angle;
      const q = ring * sides + (angle + 1) % sides;
      indices.push(p, p + sides, q, q, p + sides, q + sides);
    }
  }
  for (let k = 1; k < sides - 1; k++) {
    const edge = (rings - 1) * sides;
    indices.push(0, k, k + 1, edge, edge + k + 1, edge + k);
  }
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
  geometry.setAttribute('uv', new THREE.Float32BufferAttribute(uvs, 2));
  geometry.setIndex(indices);
  geometry.computeVertexNormals();
  return geometry;
}

function shadow(parent: THREE.Object3D, art: ArtMaterials, position: Triple, scale: Triple, opacity: number) {
  return placeMesh(parent, new THREE.PlaneGeometry(1, 1), new THREE.MeshBasicMaterial({
    map: art.spot, color: 0x2a1628, opacity, transparent: true, depthWrite: false, toneMapped: false,
  }), position, scale, [-Math.PI / 2, 0, 0]);
}

export function createGolem(scene: THREE.Scene, art: ArtMaterials) {
  const root = new THREE.Group();
  root.name = 'Amberheart, the ruin guardian';
  root.position.set(0, 0, -5.76);
  scene.add(root);

  shadow(scene, art, [0, 0.024, -5.39], [5.2, 3.45, 1], 0.57);
  shadow(scene, art, [1.66, 0.026, -4.89], [1.8, 1.48, 1], 0.65);
  shadow(scene, art, [-0.85, 0.027, -5.06], [1.66, 1.45, 1], 0.72);
  shadow(scene, art, [0.85, 0.028, -5.08], [1.58, 1.45, 1], 0.68);

  const purple = shaded(0x77506f, art.stone, true);
  const lightStone = shaded(0x946281, art.stone, true);
  const darkStone = shaded(0x5f405d, art.stone, true);
  const knuckleStone = shaded(0x8e6279, art.stone);
  const hornStone = [shaded(0xa57285, art.stone), shaded(0x98667d, art.stone), shaded(0xb88090, art.stone)];
  const sandstone = shaded(0xb17b68, art.stone);
  const copper = shaded(0x784b4b, art.stone);
  const groove = shaded(0x51314f);
  const lichen = shaded(0xffffff);
  const lichenColors = [0x899647, 0x929f4d, 0xa6aa51, 0x7d8742, 0x9ca54e];
  const random = seededRandom(26628);
  const hitBodies: THREE.Mesh[] = [];
  const hitBlocks: THREE.Mesh[] = [];

  function body(parent: THREE.Object3D, material: THREE.Material, at: Triple, size: Triple, rotation?: Triple) {
    const mesh = placeMesh(parent, art.rock, material, at, size, rotation);
    hitBodies.push(mesh);
    return mesh;
  }

  function moss(parent: THREE.Object3D, patches: { at: Triple; width: number; count: number; drop: number }[]) {
    const batch = new InstanceBatch(art.ellipsoid, lichen);
    for (const { at, width, count, drop } of patches) {
      const [x, y, z] = at;
      for (let i = 0; i < count; i++) {
        const across = (i / (count - 1) - 0.5) * width;
        const radius = width * (0.12 + random() * 0.065);
        const length = radius * (0.34 + random() * drop * 0.82);
        const edge = Math.pow(across / (width * 0.50), 2) * 0.185;
        batch.add([x + across, y - (edge + 0.048) * width, z - 0.055 * width + (random() - 0.5) * width * 0.11],
          [radius, length, radius * (0.99 + random() * 0.22)], lichenColors[i % lichenColors.length],
          [0.14 + random() * 0.42, 0, Math.sign(across) * (0.15 + random() * 0.58)]);
      }
    }
    batch.attach(parent, 'Scalloped moss');
  }

  body(root, purple, [0, 1.53, -0.10], [1.12, 1.14, 0.83], [0, 0.09, -0.12]);
  body(root, darkStone, [0, 0.98, -0.13], [0.86, 0.53, 0.70], [0, 0.1, 0.14]);

  for (const side of [-1, 1]) {
    const leg = new THREE.Group();
    root.add(leg);
    leg.position.set(side * 0.72, 0, 0.12);
    leg.rotation.y = side * 0.22;
    body(leg, purple, [side * 0.06, 0.51, -0.03], [0.48, 0.56, 0.57], [0, 0.12, -side * 0.32]);
    body(leg, lightStone, [side * 0.11, 0.19, 0.29], [0.54, 0.30, 0.62], [0, -0.09, side * 0.05]);
    hitBlocks.push(placeMesh(leg, art.block, knuckleStone, [side * 0.02, 0.115, 0.61],
      [0.37, 0.176, 0.27], [0.11, side * 0.07, 0]));
    moss(leg, [{ at: [side * 0.11, 0.445, 0.36], width: 0.56, count: 8, drop: 1.02 }]);
  }

  body(root, purple, [0, 2.57, -0.084], [0.73, 0.67, 0.65], [0, -0.21, 0.17]);
  placeMesh(root, art.ellipsoid, groove, [0, 2.55, 0.477], [0.566, 0.47, 0.152]);
  placeMesh(root, art.ellipsoid, shaded(0x493044), [0, 2.55, 0.525], [0.488, 0.397, 0.139]);
  const eyeMaterial = new THREE.MeshStandardMaterial({ color: 0xffd1a0, emissive: 0xeaa451, emissiveIntensity: 0.38, roughness: 0.62 });
  const pupilMaterial = shaded(0x332333);
  const eyeGlint = new THREE.MeshBasicMaterial({ color: 0xffe5c3, toneMapped: false });
  for (const sign of [-1, 1]) {
    placeMesh(root, sculptedEye(-sign), eyeMaterial, [sign * 0.226, 2.576, 0.662], [0.86, 0.86, 1], [0, sign * 0.12, 0]);
    placeMesh(root, art.ellipsoid, pupilMaterial, [sign * 0.162, 2.56, 0.729], [0.062, 0.087, 0.022], [0, 0, sign * 0.12]);
    placeMesh(root, art.ellipsoid, eyeGlint, [sign * 0.154 - 0.015, 2.592, 0.75], [0.013, 0.017, 0.006]);
    const profile = hornProfile(sign);
    for (let segment = 0; segment < 4; segment++) {
      placeMesh(root, hornSection(profile, segment * 0.25 + 0.006, (segment + 1) * 0.25 - 0.005),
        hornStone[(segment + 1) % hornStone.length], [0, 0, 0]);
    }
  }
  moss(root, [
    { at: [0, 3.08, 0.316], width: 1.14, count: 14, drop: 1.11 },
    { at: [-0.59, 2.80, 0.36], width: 0.42, count: 6, drop: 1.56 },
    { at: [0.56, 2.78, 0.365], width: 0.45, count: 6, drop: 1.57 },
  ]);

  const plate = chestPlate();
  placeMesh(root, plate, copper, [0, 1.835, 0.558], [1.20, 1.125, 1]);
  placeMesh(root, plate, sandstone, [0, 1.84, 0.615], [1.04, 0.95, 0.85]);
  const gemSeat = new THREE.TorusGeometry(0.32, 0.028, 7, 36);
  placeMesh(root, gemSeat, copper, [0, 1.912, 0.705], [0.90, 1.06, 0.7]);
  for (const sign of [-1, 1]) {
    const points: Triple[] = [];
    for (let i = 0; i <= 17; i++) {
      const t = i / 17;
      const radius = 0.19 * (1 - t) + 0.021;
      const angle = -0.5 + t * Math.PI * 2.15;
      points.push([sign * (0.52 + Math.cos(angle) * radius), 1.84 + Math.sin(angle) * radius, 0.735 - 0.10 * Math.cos(angle) * radius]);
    }
    placeMesh(root, trail(points, 0.044), sandstone, [0, 0, 0]);
    placeMesh(root, trail(points, 0.041), copper, [0, -0.045, -0.016]);
    placeMesh(root, trail([[sign * 0.87, 1.70, 0.49], [sign * 0.89, 1.57, 0.501], [sign * 0.82, 1.50, 0.56]], 0.012),
      groove, [0, 0, 0]);
  }

  const idleGold = new THREE.Color(0xffa72c);
  const warningGold = new THREE.Color(0xffd34f);
  const coreMaterial = new THREE.MeshStandardMaterial({ color: idleGold, emissive: 0xff8d22, emissiveIntensity: 0.52,
    roughness: 0.36, flatShading: true });
  const crystal = new THREE.LatheGeometry([
    new THREE.Vector2(0, -0.36), new THREE.Vector2(0.205, -0.177), new THREE.Vector2(0.255, 0.108),
    new THREE.Vector2(0.175, 0.260), new THREE.Vector2(0, 0.38),
  ], 5);
  const core = placeMesh(root, crystal, coreMaterial, [0, 1.913, 0.754], [1, 1, 0.86], [0.08, Math.PI * 0.086, 0.08]);
  const heartlight = createSpot(root, art.spot, 0xffa02d, 0.57);
  heartlight.position.set(0, 1.913, 0.919);
  heartlight.scale.setScalar(1.09);
  const bracket = new THREE.Group();
  bracket.position.set(0, 1.913, 0.987);
  bracket.name = 'Core targeting corners';
  root.add(bracket);
  const bracketMaterial = new THREE.MeshBasicMaterial({ color: 0xffe3a9, toneMapped: false, transparent: true, opacity: 0.94 });
  for (const x of [-1, 1]) {
    for (const y of [-1, 1]) {
      placeMesh(bracket, trail([[0.29 * x, 0.41 * y, 0], [0.375 * x, 0.408 * y, 0.011],
        [0.40 * x, 0.386 * y, 0.011], [0.405 * x, 0.315 * y, 0]], 0.024), bracketMaterial, [0, 0, 0]);
    }
  }
  bracket.visible = false;

  function createArm(side: number) {
    const shoulder = new THREE.Vector3(side * 1.02, 2.20, -0.113);
    const upper = new THREE.Group();
    const lower = new THREE.Group();
    const fist = new THREE.Group();
    root.add(upper, lower, fist);
    body(root, lightStone, [shoulder.x, shoulder.y, shoulder.z], [0.65, 0.605, 0.612], [0, -0.14 * side, 0.17 * side]);
    moss(root, [{ at: [shoulder.x + side * 0.11, shoulder.y + 0.49, shoulder.z + 0.31], width: 0.78, count: 10, drop: 1.18 }]);
    body(upper, purple, [0, 0, 0], [0.480, 0.61, 0.51], [0.11, 0.11 * side, -0.04]);
    body(lower, lightStone, [0, 0, 0], [0.52, 0.67, 0.57], [0, 0.12 * side, side * 0.2]);
    body(fist, darkStone, [0, 0, 0], [0.55, 0.55, 0.52], [0, side * 0.2, 0]);
    for (let knuckle = 0; knuckle < 3; knuckle++) {
      hitBlocks.push(placeMesh(fist, art.block, knuckleStone, [(knuckle - 1) * 0.323, 0.036 + (knuckle === 1 ? 0.054 : 0), 0.32],
        [0.164, 0.295 - Math.abs(knuckle - 1) * 0.04, 0.245], [0.17, (1 - knuckle) * 0.095, (knuckle - 1) * 0.092]));
    }
    hitBlocks.push(placeMesh(fist, art.block, knuckleStone, [-side * 0.445, -0.12, 0.29],
      [0.131, 0.235, 0.196], [0.2, side * 0.24, -side * 0.38]));
    moss(fist, [{ at: [side * 0.062, 0.46, 0.15], width: 0.77, count: 10, drop: 0.97 }]);
    placeMesh(fist, trail([[side * 0.27, 0.33, 0.467], [side * 0.20, 0.262, 0.520], [side * 0.31, 0.185, 0.553]],
      0.009), groove, [0, 0, 0]);
    return { upper, lower, fist, shoulder, elbow: new THREE.Vector3(), wrist: new THREE.Vector3() };
  }

  const attackArm = createArm(-1);
  const anchorArm = createArm(1);
  const aimInverse = new THREE.Matrix4();
  const aimRay = new THREE.Ray();
  const aimLocal = new THREE.Vector3();
  const aimWorld = new THREE.Vector3();
  const partSphere = new THREE.Sphere(new THREE.Vector3(), 0.93);
  const partBlock = new THREE.Box3(new THREE.Vector3(-0.94, -0.94, -0.94), new THREE.Vector3(0.94, 0.94, 0.94));
  const heartSphere = new THREE.Sphere(new THREE.Vector3(), 0.315);
  const ground = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);

  const attackArcs = new THREE.Group();
  attackArcs.name = 'Amber punch telegraph';
  root.add(attackArcs);
  const arcMaterial = new THREE.MeshBasicMaterial({ color: 0xffaf53, toneMapped: false,
    transparent: true, opacity: 0.58, depthWrite: false, blending: THREE.AdditiveBlending });
  for (let i = 0; i < 2; i++) {
    const circle = new THREE.TorusGeometry(0.72 + i * 0.13, 0.016 - i * 0.005, 6, 36, Math.PI * 0.72);
    placeMesh(attackArcs, circle, arcMaterial, [0, 0, 0], [1, 1.16, 1], [0.1, 0.25, Math.PI * 0.59]);
  }
  attackArcs.visible = false;

  const armDirection = new THREE.Vector3();
  let time = 0;
  let lift = 0;
  let slam = 0;
  let shake = 0;
  let defeat = 0;

  function shapeArm(arm: ReturnType<typeof createArm>, fistPitch: number, fistYaw: number, fistRoll: number) {
    arm.upper.position.copy(arm.shoulder).lerp(arm.elbow, 0.55);
    armDirection.copy(arm.elbow).sub(arm.shoulder);
    arm.upper.scale.y = Math.max(0.62, armDirection.length() / 0.84);
    arm.upper.quaternion.setFromUnitVectors(up, armDirection.normalize());
    arm.lower.position.copy(arm.elbow).lerp(arm.wrist, 0.42);
    armDirection.copy(arm.wrist).sub(arm.elbow);
    arm.lower.scale.y = Math.max(0.57, armDirection.length() / 0.76);
    arm.lower.quaternion.setFromUnitVectors(up, armDirection.normalize());
    arm.fist.position.copy(arm.wrist);
    arm.fist.rotation.set(fistPitch, fistYaw, fistRoll);
  }

  function update(dt: number, windup: number, phase: ArenaPhase, coreAimed: boolean) {
    time += dt;
    slam = Math.max(0, slam - dt);
    shake = Math.max(0, shake - dt * 2.25);
    defeat = THREE.MathUtils.damp(defeat, phase === 'won' ? 1 : 0, 3.8, dt);
    const rising = windup >= 0 && (phase === 'battle' || phase === 'practice');
    const targetLift = rising ? THREE.MathUtils.smoothstep(windup, 0, 0.72) : 0;
    lift = THREE.MathUtils.damp(lift, targetLift, rising ? 7.8 : 6.0, dt);
    const slamProgress = 1 - slam / 0.52;
    const thrust = (slam > 0 ? Math.sin(Math.PI * Math.min(0.98, slamProgress * 1.46)) : 0) * (1 - defeat);
    const sway = Math.sin(time * 1.2) * 0.019;
    root.position.y = sway - 0.015 - defeat * 0.59 - thrust * 0.10;
    root.rotation.z = Math.sin(time * 0.91) * 0.015 + shake * Math.sin(time * 26) * 0.033 - defeat * 0.13;
    root.rotation.y = Math.sin(time * 0.67) * 0.025 + thrust * 0.17;

    attackArm.elbow.set(-1.43 - lift * 0.35 + thrust * 0.25,
      1.80 + lift * 0.48 - thrust * 0.51 - defeat * 0.15, 0.05 + lift * 0.21 + thrust * 0.56);
    attackArm.wrist.set(-1.78 - lift * 0.13 + thrust * 0.48,
      2.27 + lift * 0.69 - thrust * 1.64 - defeat * 1.12, 0.63 + thrust * 0.72);
    shapeArm(attackArm, 0.14 - lift * 0.28 + thrust * 0.59, -0.22, -0.24 + lift * 0.38 + thrust * 0.19);

    anchorArm.elbow.set(1.42, 1.38 + lift * 0.03 + defeat * 0.08, 0.15 + lift * 0.15);
    anchorArm.wrist.set(1.64, 0.60 + lift * 0.06 + defeat * 0.59, 0.89 + lift * 0.07);
    shapeArm(anchorArm, 0.10 - lift * 0.17, 0.12 + defeat * 0.29, -0.19);

    attackArcs.visible = rising && defeat < 0.13;
    if (attackArcs.visible) {
      attackArcs.position.copy(attackArm.wrist);
      attackArcs.position.z += 0.032;
      attackArcs.rotation.z = lift * -0.26;
      arcMaterial.opacity = 0.22 + 0.52 * Math.max(0, windup);
    }
    const pulse = 0.5 + 0.5 * Math.sin(time * 3.8);
    const warning = rising ? 0.70 + 0.30 * windup : 0;
    coreMaterial.color.lerpColors(idleGold, warningGold, warning);
    coreMaterial.emissiveIntensity = (0.51 + pulse * 0.12 + warning * 1.18 + (coreAimed ? 0.15 : 0)) * (1 - defeat * 0.86);
    heartlight.material.opacity = (0.35 + pulse * 0.08 + warning * 0.49 + (coreAimed ? 0.08 : 0)) * (1 - defeat * 0.94);
    heartlight.scale.setScalar(0.88 + pulse * 0.10 + warning * 0.42);
    bracket.visible = (rising || coreAimed) && defeat < 0.2 && phase !== 'lost';
    bracketMaterial.opacity = coreAimed ? 0.96 : 0.65 + warning * 0.26;
  }

  function aim(ray: THREE.Ray): { point: THREE.Vector3; part: ArenaPart } {
    root.updateMatrixWorld(true);
    core.getWorldPosition(heartSphere.center);
    const coreHit = ray.intersectSphere(heartSphere, aimWorld);
    const heart = coreHit?.clone();
    const heartDistance = heart ? ray.origin.distanceTo(heart) : Infinity;
    let nearest: THREE.Vector3 | undefined;
    let nearestDistance = Infinity;
    function test(contributor: THREE.Mesh, block: boolean) {
      aimInverse.copy(contributor.matrixWorld).invert();
      aimRay.copy(ray).applyMatrix4(aimInverse);
      const hit = block ? aimRay.intersectBox(partBlock, aimLocal) : aimRay.intersectSphere(partSphere, aimLocal);
      if (!hit) return;
      aimWorld.copy(aimLocal).applyMatrix4(contributor.matrixWorld);
      const distance = ray.origin.distanceTo(aimWorld);
      if (distance < nearestDistance) {
        nearest ??= new THREE.Vector3();
        nearest.copy(aimWorld);
        nearestDistance = distance;
      }
    }
    for (const contributor of hitBodies) test(contributor, false);
    for (const contributor of hitBlocks) test(contributor, true);
    if (heart && heartDistance <= nearestDistance + 0.12) return { point: heart, part: 'core' };
    if (nearest) return { point: nearest, part: 'body' };
    const floorPoint = ray.intersectPlane(ground, aimWorld);
    const point = floorPoint && floorPoint.distanceToSquared(ray.origin) < 81 ? floorPoint.clone() : ray.at(9, new THREE.Vector3());
    return { point, part: 'miss' };
  }

  function reset() {
    time = 0;
    lift = 0;
    slam = 0;
    shake = 0;
    defeat = 0;
    update(0, -1, 'practice', false);
  }

  reset();
  return {
    update,
    aim,
    strike: () => { slam = 0.52; },
    flinch: (strong: boolean) => { shake = strong ? 1 : Math.max(0.33, shake); },
    reset,
  };
}
