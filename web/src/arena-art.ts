import * as THREE from 'three';
import { RoundedBoxGeometry } from 'three/addons/geometries/RoundedBoxGeometry.js';

export type Triple = readonly [number, number, number];

export function seededRandom(seed: number) {
  return () => {
    seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0;
    return seed / 4294967296;
  };
}

function texture(size: number, pixel: (x: number, y: number) => readonly number[]) {
  const pixels = new Uint8Array(size * size * 4);
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      pixels.set(pixel(x, y), (x + y * size) * 4);
    }
  }
  const map = new THREE.DataTexture(pixels, size, size);
  map.colorSpace = THREE.SRGBColorSpace;
  map.magFilter = THREE.LinearFilter;
  map.minFilter = THREE.LinearMipmapLinearFilter;
  map.generateMipmaps = true;
  map.needsUpdate = true;
  return map;
}

function mottledStone() {
  const random = seededRandom(12856);
  return texture(128, (x, y) => {
    const column = x * Math.PI / 64;
    const row = y * Math.PI / 64;
    const fields = Math.sin(column * 2 + Math.cos(row * 3)) * Math.cos(row * 2 - column);
    const grain = Math.sin(column * 9 + row * 7) * Math.cos(row * 6 + column * 3);
    const pitting = random() < 0.027 ? 0.07 : 0;
    const value = Math.min(255, Math.round(255 * (0.95 + fields * 0.035 + grain * 0.024 - pitting - random() * 0.03)));
    return [value, value, value, 255];
  });
}

function carvedRock() {
  const shape = new THREE.IcosahedronGeometry(1, 2);
  const vertices = shape.getAttribute('position');
  const colors = new Float32Array(vertices.count * 3);
  for (let i = 0; i < vertices.count; i++) {
    const x = vertices.getX(i);
    const y = vertices.getY(i);
    const z = vertices.getZ(i);
    const radial = 1 + Math.sin(x * 8 + z * 6) * Math.cos(y * 9 - z * 4) * 0.065;
    vertices.setXYZ(i, x * radial, y * radial, z * radial);
  }
  const random = seededRandom(53128);
  for (let i = 0; i < vertices.count; i += 3) {
    const tone = (Math.floor(random() * 6) * 0.022) + 0.88;
    colors.fill(tone, i * 3, (i + 3) * 3);
  }
  shape.setAttribute('color', new THREE.BufferAttribute(colors, 3));
  return shape;
}

export function createArtMaterials() {
  const spot = texture(64, (x, y) => {
    const radius = Math.hypot((x - 31.5) / 31.5, (y - 31.5) / 31.5);
    const alpha = Math.max(0, 1 - radius * radius);
    return [255, 255, 255, Math.round(alpha * alpha * alpha * 255)];
  });
  const stone = mottledStone();
  stone.wrapS = THREE.RepeatWrapping;
  stone.wrapT = THREE.RepeatWrapping;
  return {
    spot,
    stone,
    rock: carvedRock(),
    block: new RoundedBoxGeometry(2, 2, 2, 2, 0.18),
    ellipsoid: new THREE.SphereGeometry(1, 12, 8),
    crystal: new THREE.OctahedronGeometry(1),
  };
}

export type ArtMaterials = ReturnType<typeof createArtMaterials>;

export function shaded(color: THREE.ColorRepresentation, map?: THREE.Texture, vertexColors = false) {
  return new THREE.MeshStandardMaterial({ color, map: map ?? null, roughness: 0.92, vertexColors });
}

export function placeMesh<G extends THREE.BufferGeometry, M extends THREE.Material>(
  parent: THREE.Object3D,
  geometry: G,
  material: M,
  position: Triple,
  scale: Triple = [1, 1, 1],
  rotation?: Triple,
) {
  const mesh = new THREE.Mesh(geometry, material);
  mesh.position.set(...position);
  mesh.scale.set(...scale);
  if (rotation) mesh.rotation.set(...rotation);
  parent.add(mesh);
  return mesh;
}

export class InstanceBatch {
  private readonly pose = new THREE.Object3D();
  private readonly stamps: { matrix: THREE.Matrix4; color: THREE.Color }[] = [];

  constructor(private readonly geometry: THREE.BufferGeometry, private readonly material: THREE.Material) {}

  add(position: Triple, scale: Triple, color: THREE.ColorRepresentation, rotation: Triple = [0, 0, 0]) {
    this.pose.position.set(...position);
    this.pose.scale.set(...scale);
    this.pose.rotation.set(...rotation);
    this.pose.updateMatrix();
    this.stamps.push({ matrix: this.pose.matrix.clone(), color: new THREE.Color(color) });
  }

  attach(parent: THREE.Object3D, name: string) {
    const mesh = new THREE.InstancedMesh(this.geometry, this.material, this.stamps.length);
    this.stamps.forEach(({ matrix, color }, index) => {
      mesh.setMatrixAt(index, matrix);
      mesh.setColorAt(index, color);
    });
    mesh.computeBoundingSphere();
    mesh.name = name;
    parent.add(mesh);
  }
}

export function createSpot(parent: THREE.Object3D, map: THREE.Texture, color: number, opacity: number) {
  const spot = new THREE.Sprite(new THREE.SpriteMaterial({
    map,
    color,
    opacity,
    transparent: true,
    depthWrite: false,
    blending: THREE.AdditiveBlending,
    toneMapped: false,
  }));
  parent.add(spot);
  return spot;
}
