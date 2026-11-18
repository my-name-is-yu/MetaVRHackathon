import * as THREE from 'three';
import { RoundedBoxGeometry } from 'three/addons/geometries/RoundedBoxGeometry.js';
import { createSpot, InstanceBatch, placeMesh, seededRandom, shaded } from './arena-art';
import type { ArtMaterials } from './arena-art';

function createSky(scene: THREE.Scene, root: THREE.Group) {
  const sky = new THREE.Mesh(new THREE.SphereGeometry(70, 28, 16), new THREE.ShaderMaterial({
    side: THREE.BackSide,
    depthWrite: false,
    toneMapped: false,
    uniforms: {
      highSky: { value: new THREE.Color('#724895') },
      horizon: { value: new THREE.Color('#ee8ba6') },
      sunset: { value: new THREE.Color('#ffc08e') },
      cloudColor: { value: new THREE.Color('#f4a0b0') },
    },
    vertexShader: `
      varying vec3 vDirection;
      void main() {
        vDirection = position;
        gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
      }
    `,
    fragmentShader: `
      uniform vec3 highSky;
      uniform vec3 horizon;
      uniform vec3 sunset;
      uniform vec3 cloudColor;
      varying vec3 vDirection;

      float cloud(vec2 p, vec2 center, vec2 scale) {
        return 1.0 - smoothstep(0.70, 1.08, length((p - center) / scale));
      }

      void main() {
        vec3 d = normalize(vDirection);
        vec3 color = mix(horizon, highSky, smoothstep(-0.025, 0.55, d.y));
        float warm = (1.0 - smoothstep(-0.11, 0.15, d.y)) *
          smoothstep(0.10, 0.94, dot(d, normalize(vec3(-0.50, 0.04, -1.0))));
        color = mix(color, sunset, 0.77 * warm);

        vec2 p = vec2(atan(d.x, -d.z), d.y);
        float nearCloud = max(cloud(p, vec2(0.48, 0.30), vec2(0.34, 0.035)),
          max(cloud(p, vec2(0.43, 0.325), vec2(0.13, 0.057)),
              cloud(p, vec2(0.62, 0.352), vec2(0.16, 0.076))));
        float farCloud = max(cloud(p, vec2(-0.68, 0.22), vec2(0.24, 0.031)),
          cloud(p, vec2(-0.74, 0.245), vec2(0.14, 0.06)));
        float lowCloud = cloud(p, vec2(0.09, 0.16), vec2(0.26, 0.018));
        color = mix(color, cloudColor, 0.42 * max(nearCloud, max(farCloud, 0.40 * lowCloud)));
        gl_FragColor = vec4(color, 1.0);
        #include <colorspace_fragment>
      }
    `,
  }));
  sky.frustumCulled = false;
  sky.renderOrder = -100;
  sky.name = 'Procedural dusk backdrop';
  root.add(sky);

  const moon = placeMesh(root, new THREE.PlaneGeometry(1, 1), new THREE.ShaderMaterial({
    transparent: true,
    depthWrite: false,
    toneMapped: false,
    uniforms: { color: { value: new THREE.Color('#ffe1a1') } },
    vertexShader: `
      varying vec2 vUv;
      void main() {
        vUv = uv;
        gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
      }
    `,
    fragmentShader: `
      uniform vec3 color;
      varying vec2 vUv;
      void main() {
        vec2 p = vUv - 0.5;
        float d = length(p);
        float outside = 1.0 - smoothstep(0.240, 0.247, d);
        float inside = smoothstep(0.209, 0.218, length(p - vec2(0.100, 0.055)));
        float glow = (1.0 - smoothstep(0.14, 0.48, d)) * 0.23;
        gl_FragColor = vec4(color, max(outside * inside, glow));
        #include <colorspace_fragment>
      }
    `,
  }), [-10.65, 10.0, -28], [5.0, 5.0, 1]);
  moon.name = 'Amber crescent';

  return () => { sky.visible = !(scene.background instanceof THREE.Texture); };
}

function createRidges(root: THREE.Group) {
  const distant = new THREE.Group();
  root.add(distant);
  const layers = [
    { depth: -41, rise: 5.1, color: 0xb17f9f, seed: 894 },
    { depth: -31, rise: 3.7, color: 0x9b6c94, seed: 415 },
    { depth: -23, rise: 2.6, color: 0x815b87, seed: 281 },
  ];
  for (const layer of layers) {
    const random = seededRandom(layer.seed);
    const shape = new THREE.Shape();
    shape.moveTo(-53, -5);
    for (let x = -53; x <= 53; x += 0.7) {
      const rolling = Math.sin(x * 0.18 + layer.seed) * 0.95 + Math.cos(x * 0.59) * 0.30;
      shape.lineTo(x, layer.rise + rolling + random() * 0.42);
    }
    shape.lineTo(53, -5);
    shape.closePath();
    placeMesh(distant, new THREE.ShapeGeometry(shape), new THREE.MeshBasicMaterial({ color: layer.color, fog: false }),
      [0, 0, layer.depth]);
  }
  const towers = new THREE.Shape();
  towers.moveTo(-1.95, 0);
  towers.lineTo(-1.95, 2.6);
  towers.lineTo(-1.53, 2.6);
  towers.lineTo(-1.44, 3.18);
  towers.lineTo(-0.81, 3.31);
  towers.lineTo(-0.56, 3.12);
  towers.lineTo(-0.27, 3.12);
  towers.lineTo(-0.27, 2.72);
  towers.lineTo(0.22, 2.72);
  towers.lineTo(0.35, 3.01);
  towers.lineTo(1.25, 3.10);
  towers.lineTo(1.43, 2.81);
  towers.lineTo(1.71, 2.81);
  towers.lineTo(1.78, 1.62);
  towers.lineTo(2.15, 1.50);
  towers.lineTo(2.24, 0);
  towers.closePath();
  for (const x of [-1.12, -0.25, 0.74]) {
    const window = new THREE.Path();
    window.moveTo(x - 0.18, 1.18);
    window.lineTo(x - 0.18, 2.11);
    window.quadraticCurveTo(x, 2.53, x + 0.18, 2.11);
    window.lineTo(x + 0.18, 1.18);
    window.closePath();
    towers.holes.push(window);
  }
  const ruin = new THREE.ShapeGeometry(towers);
  const silhouette = new THREE.MeshBasicMaterial({ color: 0x87618c, fog: false });
  placeMesh(root, ruin, silhouette, [8.4, 2.37, -20.5], [1.25, 1.15, 1]);
  placeMesh(root, ruin, silhouette, [-6.6, 1.9, -18.5], [0.84, 0.93, 1]);
  return distant;
}

export function createScenery(scene: THREE.Scene, art: ArtMaterials) {
  const root = new THREE.Group();
  root.name = 'Moonlit courtyard';
  scene.add(root);
  scene.fog = new THREE.FogExp2(0xb488a2, 0.050);
  const skyUpdate = createSky(scene, root);
  const distantRidges = createRidges(root);

  scene.add(new THREE.HemisphereLight(0xf9d5ed, 0x73606c, 2.04));
  const key = new THREE.DirectionalLight(0xffc294, 2.50);
  key.position.set(-4.5, 7, 1.6);
  scene.add(key);
  const rim = new THREE.DirectionalLight(0xbd8eff, 0.96);
  rim.position.set(5, 4, -8);
  scene.add(rim);

  const random = seededRandom(604426);
  const groundMat = shaded(0x696247, art.stone);
  placeMesh(root, new THREE.PlaneGeometry(85, 86), groundMat, [0, -0.115, -25], [1, 1, 1], [-Math.PI / 2, 0, 0]);
  const brick = new InstanceBatch(new RoundedBoxGeometry(2, 2, 2, 1, 0.18), shaded(0xffffff, art.stone));
  const stones = new InstanceBatch(art.rock, shaded(0xffffff, art.stone, true));
  const leaves = new InstanceBatch(new THREE.SphereGeometry(1, 8, 5), shaded(0xffffff));
  const rockColors = [0x887183, 0xa07f8d, 0x957684, 0xb18b92, 0x927d8a, 0x7d657b];
  const mossColors = [0x7c7e40, 0x929549, 0xa7a750, 0x74783e, 0x8a8e50];
  const pick = (colors: number[]) => colors[Math.floor(random() * colors.length)];

  // Stagger the pavers and let the moss-colored bed show through the grout.
  for (let row = 0; row < 22; row++) {
    const z = 2.8 - row * 0.91;
    for (let col = -7; col <= 7; col++) {
      const x = col * 0.92 + (row % 2 ? 0.42 : -0.06) + (random() - 0.5) * 0.08;
      if (Math.abs(x) > 5.25 && random() < 0.17) continue;
      const depth = 0.38 + random() * 0.045;
      brick.add([x, -0.068 + random() * 0.014, z], [0.39 + random() * 0.057, 0.062, depth], pick(rockColors),
        [(random() - 0.5) * 0.032, (random() - 0.5) * 0.17, (random() - 0.5) * 0.038]);
      if (random() < 0.24) {
        leaves.add([x + 0.39, 0.003, z + (random() - 0.5) * 0.59], [0.08 + random() * 0.12, 0.025, 0.14], pick(mossColors));
      }
    }
  }

  function mossClump(x: number, y: number, z: number, radius: number, count: number) {
    for (let i = 0; i < count; i++) {
      const angle = random() * Math.PI * 2;
      const spread = Math.sqrt(random()) * radius;
      const size = radius * (0.26 + 0.37 * random());
      leaves.add([x + Math.cos(angle) * spread, y - radius * 0.055 + (random() - 0.5) * radius * 0.15, z + Math.sin(angle) * spread * 0.63],
        [size, size * 0.29, size * 0.80], pick(mossColors), [0, angle, 0]);
    }
  }

  function arch(x: number, z: number, size: number, missing: number[] = []) {
    const radius = 1.02 * size;
    const spring = 1.54 * size;
    for (const side of [-1, 1]) {
      const pillar = x + side * radius;
      brick.add([pillar, 0.12 * size, z], [0.43 * size, 0.16 * size, 0.43 * size], pick(rockColors));
      for (let level = 0; level < 4; level++) {
        const dy = (0.39 + 0.31 * level) * size;
        brick.add([pillar + (random() - 0.5) * 0.06, dy, z],
          [(level === 0 ? 0.31 : 0.28) * size, 0.154 * size, 0.30 * size], pick(rockColors),
          [0, (random() - 0.5) * 0.16, (random() - 0.5) * 0.07]);
      }
      mossClump(pillar + 0.05, 0.28 * size, z + 0.37 * size, size * 0.27, 5);
    }
    for (let segment = 0; segment < 9; segment++) {
      if (missing.includes(segment)) continue;
      const angle = Math.PI * (segment + 0.5) / 9;
      const ax = x + Math.cos(angle) * radius;
      const ay = spring + Math.sin(angle) * radius;
      brick.add([ax, ay, z], [0.19 * size, 0.29 * size, 0.32 * size], pick(rockColors),
        [0, (random() - 0.5) * 0.10, angle + Math.PI / 2]);
      if (segment % 2 === 0 || segment === 3) {
        const outsideX = ax + Math.cos(angle) * 0.27 * size;
        const outsideY = ay + Math.sin(angle) * 0.27 * size;
        mossClump(outsideX, outsideY, z + 0.21 * size, 0.23 * size, 6);
        for (let leaf = 0; leaf < 4; leaf++) {
          leaves.add([outsideX + (random() - 0.5) * 0.27 * size, outsideY - leaf * 0.085 * size, z + 0.303 * size],
            [0.075 * size, (0.095 + random() * 0.045) * size, 0.032 * size], pick(mossColors), [0, 0, (random() - 0.5) * 0.88]);
        }
      }
    }
  }

  arch(-4.55, -4.8, 1.25, [3, 4, 5, 6]);
  arch(4.48, -4.7, 1.23);
  arch(-4.21, -9.4, 1.01, [4, 5]);
  arch(5.00, -10.1, 1.05, [8]);
  arch(-6.9, -13.1, 1.13, [0, 1]);
  arch(7.4, -13.9, 1.00);

  for (let i = 0; i < 57; i++) {
    const side = (i % 2 === 0) ? -1 : 1;
    const z = 1.1 - random() * 16.4;
    const x = side * (2.85 + random() * 3.8);
    const size = 0.16 + random() * (z > -6 ? 0.63 : 0.43);
    stones.add([x, size * 0.31, z], [size, size * 0.72, size * (0.65 + random() * 0.35)], pick(rockColors),
      [random() * Math.PI, random() * Math.PI, random() * 0.6]);
    if (i % 3 !== 0) mossClump(x, size * 0.93, z, size * 0.55, 5);
  }

  for (let i = 0; i < 83; i++) {
    const side = i % 2 === 0 ? -1 : 1;
    const x = side * (2.25 + random() * 4.65);
    const z = 0.5 - random() * 15.5;
    const scale = 0.08 + random() * 0.1;
    for (let blade = 0; blade < 3; blade++) {
      leaves.add([x + (blade - 1) * scale * 0.44, scale * 0.41, z + random() * 0.06],
        [scale * 0.19, scale * (0.60 + random() * 0.54), scale * 0.15], pick(mossColors), [0, random() * 0.8, (1 - blade) * 0.45]);
    }
  }

  const conifer = new THREE.LatheGeometry([
    new THREE.Vector2(0, -0.3), new THREE.Vector2(0.84, -0.3), new THREE.Vector2(1, -0.17),
    new THREE.Vector2(0.74, 0.08), new THREE.Vector2(0.42, 0.39), new THREE.Vector2(0.03, 0.90), new THREE.Vector2(0, 0.94),
  ], 10);
  const trees = new InstanceBatch(conifer, shaded(0xffffff));
  const trunks = new InstanceBatch(new THREE.CylinderGeometry(0.53, 1, 2, 7), shaded(0xffffff, art.stone));
  for (let i = 0; i < 33; i++) {
    const side = i % 2 === 0 ? -1 : 1;
    const x = side * (3.9 + random() * 8.5);
    const z = -7.5 - random() * 14;
    const height = 1.5 + random() * 3.3;
    const color = pick([0x4d5659, 0x546050, 0x50575b, 0x625866]);
    trunks.add([x, height * 0.218 - 0.10, z], [0.069 * height, height * 0.28, 0.076 * height], 0x5e4a53);
    stones.add([x, -0.045, z], [0.15 * height, 0.041 * height, 0.115 * height], 0x6a595c);
    for (let tier = 0; tier < 4; tier++) {
      const width = (0.24 - tier * 0.042) * height;
      trees.add([x + tier * height * 0.014 * side, height * (0.38 + tier * 0.15), z], [width, height * 0.23, width], color,
        [0, random() * 3, -0.09 * side]);
    }
  }

  const metal = shaded(0x542e30);
  const rimMetal = shaded(0x93704e);
  const light = new THREE.MeshBasicMaterial({ color: 0xffcc70, toneMapped: false });
  const fewSidedCylinder = new THREE.CylinderGeometry(1, 0.68, 2, 6);
  const ring = new THREE.TorusGeometry(1, 0.15, 6, 16);
  const floorGlow = new THREE.MeshBasicMaterial({ color: 0xffad57, map: art.spot, transparent: true,
    opacity: 0.30, blending: THREE.AdditiveBlending, depthWrite: false, toneMapped: false });
  const glowPlane = new THREE.PlaneGeometry(1, 1);
  const goldBars = new InstanceBatch(art.block, rimMetal);
  for (const [x, z, y, size, side] of [[-2.93, -4.57, 1.55, 1, -1], [2.83, -4.46, 1.62, 1, 1],
    [-3.13, -8.2, 1.23, 0.76, 1], [3.49, -8.78, 1.30, 0.80, -1]]) {
    const glow = createSpot(root, art.spot, 0xff9850, 0.54);
    glow.position.set(x, y, z + 0.02);
    glow.scale.setScalar(0.92 * size);
    placeMesh(root, glowPlane, floorGlow, [x, 0.018, z + 0.18], [2.3 * size, 1.65 * size, 1], [-Math.PI / 2, 0, 0]);
    placeMesh(root, fewSidedCylinder, light, [x, y, z], [0.127 * size, 0.159 * size, 0.127 * size]);
    placeMesh(root, fewSidedCylinder, metal, [x, y + 0.179 * size, z], [0.155 * size, 0.038 * size, 0.155 * size], [0, 0, Math.PI]);
    placeMesh(root, fewSidedCylinder, metal, [x, y - 0.177 * size, z], [0.131 * size, 0.037 * size, 0.131 * size]);
    placeMesh(root, ring, metal, [x, y + 0.240 * size, z], [0.052 * size, 0.056 * size, 0.055 * size]);
    for (let corner = 0; corner < 6; corner++) {
      const a = corner * Math.PI / 3;
      goldBars.add([x + Math.sin(a) * 0.104 * size, y, z + Math.cos(a) * 0.104 * size],
        [0.013 * size, 0.164 * size, 0.014 * size], 0xffffff, [Math.cos(a) * 0.13, 0, -Math.sin(a) * 0.13]);
    }
    const curl = new THREE.CatmullRomCurve3([
      new THREE.Vector3(x + 0.31 * size * side, y + 0.19 * size, z - 0.06),
      new THREE.Vector3(x + 0.29 * size * side, y + 0.37 * size, z),
      new THREE.Vector3(x + 0.13 * size * side, y + 0.42 * size, z),
      new THREE.Vector3(x, y + 0.34 * size, z),
      new THREE.Vector3(x, y + 0.265 * size, z),
    ]);
    placeMesh(root, new THREE.TubeGeometry(curl, 13, 0.025 * size, 6, false), metal, [0, 0, 0]);
  }

  brick.attach(root, 'Courtyard blocks');
  stones.attach(root, 'Rubble and tree trunks');
  leaves.attach(root, 'Moss and courtyard plants');
  trunks.attach(root, 'Evergreen trunks');
  trees.attach(root, 'Dark evergreens');
  goldBars.attach(root, 'Lantern frames');
  return () => {
    skyUpdate();
    distantRidges.visible = !(scene.background instanceof THREE.Texture);
  };
}
