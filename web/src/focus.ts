import * as T from 'three';

export function createStroke(scene: T.Scene) {
  const capacity = 1024;
  const ink = new T.InstancedMesh(
    new T.CylinderGeometry(0.0032, 0.0032, 1, 7),
    new T.MeshBasicMaterial({ color: 0xc4ffe9, toneMapped: false }),
    capacity,
  );
  ink.count = 0;
  ink.frustumCulled = false;
  ink.instanceMatrix.setUsage(T.DynamicDrawUsage);
  scene.add(ink);
  const transform = new T.Object3D();
  const up = new T.Vector3(0, 1, 0);
  const direction = new T.Vector3();
  function segment(a: T.Vector3, b: T.Vector3) {
    if (ink.count >= capacity) return;
    direction.subVectors(b, a);
    const length = direction.length();
    if (length < 0.0002) return;
    transform.position.addVectors(a, b).multiplyScalar(0.5);
    transform.quaternion.setFromUnitVectors(up, direction.divideScalar(length));
    transform.scale.set(1, length, 1);
    transform.updateMatrix();
    ink.setMatrixAt(ink.count++, transform.matrix);
    ink.instanceMatrix.needsUpdate = true;
  }
  return {
    segment,
    clear() {
      ink.count = 0;
    },
  };
}

// A first-person casting glove for mouse players. The XR runtime supplies tracked hands.
export function createPreviewGlove(scene: T.Scene) {
  const glove = new T.Group();
  const porcelain = new T.MeshStandardMaterial({ color: 0xffe3cd, roughness: 0.78 });
  const leather = new T.MeshStandardMaterial({ color: 0x65496f, roughness: 0.9 });
  const gold = new T.MeshStandardMaterial({ color: 0xe5bb87, roughness: 0.48 });
  const sphere = new T.SphereGeometry(1, 18, 13);
  function form(
    x: number,
    y: number,
    z: number,
    sx: number,
    sy: number,
    sz: number,
    mat: T.Material,
    angle = 0,
  ) {
    const mesh = new T.Mesh(sphere, mat);
    mesh.position.set(x, y, z);
    mesh.scale.set(sx, sy, sz);
    mesh.rotation.z = angle;
    glove.add(mesh);
  }
  // Sweep a continuous tapered finger along a curved centreline. Closing the
  // tip as part of the same mesh keeps the glove smooth in a near-eye view.
  function finger(points: [number, number, number][], radius: number) {
    const path = new T.CatmullRomCurve3(points.map((p) => new T.Vector3(...p)));
    const rings = 16,
      sides = 12;
    const frames = path.computeFrenetFrames(rings, false);
    const pos: number[] = [],
      indices: number[] = [];
    for (let i = 0; i <= rings; i++) {
      const u = i / rings;
      const center = path.getPointAt(u);
      const tip = Math.max(0, (u - 0.78) / 0.22);
      const r = radius * (1 - 0.16 * u) * Math.sqrt(Math.max(0.0001, 1 - tip * tip));
      for (let j = 0; j < sides; j++) {
        const angle = (j * Math.PI * 2) / sides;
        const vertex = center
          .clone()
          .addScaledVector(frames.normals[i], Math.cos(angle) * r)
          .addScaledVector(frames.binormals[i], Math.sin(angle) * r);
        pos.push(vertex.x, vertex.y, vertex.z);
        if (i < rings) {
          const a = i * sides + j,
            b = i * sides + ((j + 1) % sides);
          indices.push(a, b, a + sides, b, b + sides, a + sides);
        }
      }
    }
    const geometry = new T.BufferGeometry();
    geometry.setAttribute('position', new T.Float32BufferAttribute(pos, 3));
    geometry.setIndex(indices);
    geometry.computeVertexNormals();
    glove.add(new T.Mesh(geometry, porcelain));
  }
  form(0.028, -0.036, 0.002, 0.058, 0.072, 0.037, porcelain, 0.48);
  finger(
    [
      [0.016, -0.036, -0.002],
      [0.004, 0.012, -0.006],
      [-0.018, 0.056, -0.012],
      [-0.043, 0.067, -0.027],
      [-0.068, 0.052, -0.034],
    ],
    0.019,
  );
  finger(
    [
      [-0.005, -0.062, 0.008],
      [-0.028, -0.04, 0.015],
      [-0.049, -0.009, 0.015],
      [-0.065, 0.017, 0.002],
      [-0.067, 0.025, -0.012],
    ],
    0.022,
  );
  finger(
    [
      [0.033, -0.043, -0.02],
      [0.043, 0.009, -0.022],
      [0.02, 0.034, -0.043],
      [-0.003, 0.023, -0.046],
    ],
    0.02,
  );
  finger(
    [
      [0.053, -0.05, -0.018],
      [0.061, -0.001, -0.032],
      [0.044, 0.014, -0.046],
      [0.028, 0.008, -0.045],
    ],
    0.018,
  );
  finger(
    [
      [0.07, -0.052, -0.01],
      [0.077, -0.025, -0.026],
      [0.071, -0.01, -0.04],
      [0.053, -0.008, -0.041],
    ],
    0.017,
  );
  form(0.096, -0.13, 0.024, 0.053, 0.115, 0.046, leather, 0.48);
  form(0.063, -0.086, 0.014, 0.056, 0.026, 0.045, gold, 0.48);
  form(0.062, -0.082, 0.014, 0.056, 0.022, 0.045, porcelain, 0.48);
  glove.scale.setScalar(0.89);
  scene.add(glove);
  return glove;
}
