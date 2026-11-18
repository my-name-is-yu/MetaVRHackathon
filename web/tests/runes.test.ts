import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { glyphs, matchRune } from "../src/runes.ts";
import type { Point2, Spell } from "../src/runes.ts";

// ---------------------------------------------------------------------------
// Deterministic helpers (seeded LCG only — no Math.random, no I/O).
// ---------------------------------------------------------------------------

function makeRng(seed: number): () => number {
  let s = seed >>> 0;
  return () => {
    s = (Math.imul(s, 1664525) + 1013904223) >>> 0;
    return s / 4294967296;
  };
}

function spanOf(pts: readonly Point2[]): { sx: number; sy: number; max: number } {
  let minX = Infinity;
  let maxX = -Infinity;
  let minY = Infinity;
  let maxY = -Infinity;
  for (const p of pts) {
    if (p.x < minX) minX = p.x;
    if (p.x > maxX) maxX = p.x;
    if (p.y < minY) minY = p.y;
    if (p.y > maxY) maxY = p.y;
  }
  const sx = maxX - minX;
  const sy = maxY - minY;
  return { sx, sy, max: Math.max(sx, sy) };
}

interface DrawOpts {
  n?: number;
  noise?: number;
  seed?: number;
}

// Nearly-closed circle with a natural small gap (stops at 97% of the loop).
function drawCircle(
  cx: number,
  cy: number,
  r: number,
  o: DrawOpts & { a0?: number; dir?: 1 | -1; aspectX?: number; ease?: boolean } = {},
): Point2[] {
  const { n = 96, noise = 0, seed = 1, a0 = 0.6, dir = 1, aspectX = 1, ease = false } = o;
  const rng = makeRng(seed);
  const pts: Point2[] = [];
  for (let i = 0; i < n; i++) {
    let u = i / (n - 1);
    if (ease) u = u * u; // accelerating draw: non-uniform speed
    const a = a0 + dir * 2 * Math.PI * u * 0.97;
    pts.push({
      x: cx + r * aspectX * Math.cos(a) + (rng() - 0.5) * 2 * noise,
      y: cy + r * Math.sin(a) + (rng() - 0.5) * 2 * noise,
    });
  }
  return pts;
}

// Shared polyline tracer: straight legs through corners, optional jitter.
function traceCorners(corners: readonly Point2[], o: DrawOpts = {}): Point2[] {
  const { n = 90, noise = 0, seed = 1 } = o;
  const rng = makeRng(seed);
  const legs = corners.length - 1;
  const per = Math.max(2, Math.ceil((n - 1) / legs));
  const pts: Point2[] = [];
  for (let k = 0; k < legs; k++) {
    for (let i = 0; i < per; i++) {
      const t = i / per;
      pts.push({
        x: corners[k].x + (corners[k + 1].x - corners[k].x) * t + (rng() - 0.5) * 2 * noise,
        y: corners[k].y + (corners[k + 1].y - corners[k].y) * t + (rng() - 0.5) * 2 * noise,
      });
    }
  }
  pts.push({ ...corners[legs] });
  return pts;
}

function drawZ(cx: number, cy: number, s: number, o: DrawOpts = {}): Point2[] {
  return traceCorners(
    [
      { x: cx - s / 2, y: cy + s / 2 },
      { x: cx + s / 2, y: cy + s / 2 },
      { x: cx - s / 2, y: cy - s / 2 },
      { x: cx + s / 2, y: cy - s / 2 },
    ],
    { n: 90, seed: 12, ...o },
  );
}

function drawV(cx: number, cy: number, w: number, h: number, o: DrawOpts = {}): Point2[] {
  return traceCorners(
    [
      { x: cx - w / 2, y: cy + h / 2 },
      { x: cx, y: cy - h / 2 },
      { x: cx + w / 2, y: cy + h / 2 },
    ],
    { n: 72, seed: 13, ...o },
  );
}

function drawTriangle(cx: number, cy: number, s: number, o: DrawOpts = {}): Point2[] {
  return traceCorners(
    [
      { x: cx, y: cy + s / 2 },
      { x: cx - s / 2, y: cy - s / 2 },
      { x: cx + s / 2, y: cy - s / 2 },
      { x: cx, y: cy + s / 2 },
    ],
    { n: 120, seed: 14, ...o },
  );
}

// Handwritten S, built independently of the template (polyline through a hand
// skeleton, densified by subdivision): top-right launch, over the top, around
// the top-left lobe, diagonal cross through the center, around the
// bottom-right lobe, bottom-left finish.
function drawS(cx: number, cy: number, s: number, o: DrawOpts = {}): Point2[] {
  const { n = 96, noise = 0, seed = 15 } = o;
  const rng = makeRng(seed);
  const skel: Array<[number, number]> = [
    [0.42, 0.32], [0.3, 0.46], [0.04, 0.52], [-0.24, 0.46], [-0.42, 0.32], [-0.46, 0.14],
    [-0.32, 0.02], [-0.08, -0.03], [0.16, -0.08], [0.34, -0.16], [0.42, -0.3],
    [0.32, -0.43], [0.1, -0.5], [-0.16, -0.48], [-0.36, -0.38],
  ];
  const sub = Math.max(1, Math.round(n / (skel.length - 1)));
  const pts: Point2[] = [];
  for (let k = 0; k < skel.length - 1; k++) {
    for (let i = 0; i < sub; i++) {
      const t = i / sub;
      pts.push({
        x: cx + (skel[k][0] + (skel[k + 1][0] - skel[k][0]) * t) * s + (rng() - 0.5) * 2 * noise,
        y: cy + (skel[k][1] + (skel[k + 1][1] - skel[k][1]) * t) * s + (rng() - 0.5) * 2 * noise,
      });
    }
  }
  const last = skel[skel.length - 1];
  pts.push({ x: cx + last[0] * s, y: cy + last[1] * s });
  return pts;
}

// Published reference trace (unit space): top-right launch, top-left lobe,
// center cross, bottom-right lobe, bottom-left finish. Scaled to meters and
// densified into a realistic stroke; tests traverse it in both directions.
const EXAMPLE_S: ReadonlyArray<readonly [number, number]> = [
  [.40, .37], [.17, .50], [-.17, .49], [-.39, .35], [-.44, .20], [-.30, .08],
  [.02, 0], [.30, -.07], [.44, -.22], [.38, -.40], [.15, -.50], [-.16, -.46], [-.38, -.34],
];

function exampleS(cx: number, cy: number, s: number, sub = 8): Point2[] {
  const base = EXAMPLE_S.map(([x, y]) => ({ x: cx + x * s, y: cy + y * s }));
  const pts: Point2[] = [];
  for (let k = 0; k < base.length - 1; k++) {
    for (let i = 0; i < sub; i++) {
      const t = i / sub;
      pts.push({
        x: base[k].x + (base[k + 1].x - base[k].x) * t,
        y: base[k].y + (base[k + 1].y - base[k].y) * t,
      });
    }
  }
  pts.push({ ...base[base.length - 1] });
  return pts;
}

function drawLine(
  cx: number,
  cy: number,
  len: number,
  o: DrawOpts & { tilt?: number } = {},
): Point2[] {
  const { tilt = 0, ...rest } = o;
  return traceCorners(
    [
      { x: cx - len / 2, y: cy - (tilt * len) / 2 },
      { x: cx + len / 2, y: cy + (tilt * len) / 2 },
    ],
    { n: 64, seed: 16, ...rest },
  );
}

// Closed square stroke, optionally rotated (45 degrees reads as a diamond).
function drawSquare(
  cx: number,
  cy: number,
  s: number,
  o: DrawOpts & { rot?: number } = {},
): Point2[] {
  const { rot = 0, ...rest } = o;
  const a = (rot * Math.PI) / 180;
  const spin = (x: number, y: number): Point2 => ({
    x: cx + x * Math.cos(a) - y * Math.sin(a),
    y: cy + x * Math.sin(a) + y * Math.cos(a),
  });
  const c = [
    spin(-s / 2, s / 2),
    spin(s / 2, s / 2),
    spin(s / 2, -s / 2),
    spin(-s / 2, -s / 2),
  ];
  return traceCorners([...c, { ...c[0] }], { n: 120, seed: 17, ...rest });
}

const rev = (pts: readonly Point2[]): Point2[] => pts.slice().reverse();
const thin = (pts: readonly Point2[], k: number): Point2[] => pts.filter((_, i) => i % k === 0);

// Simulate tracking pauses: repeat every `every`-th sample.
function withPauses(pts: readonly Point2[], every = 9, times = 2): Point2[] {
  const out: Point2[] = [];
  pts.forEach((p, i) => {
    out.push({ ...p });
    if (i % every === 0) for (let k = 0; k < times; k++) out.push({ ...p });
  });
  return out;
}

// Arbitrary start point on a closed stroke (cyclic shift, shape unchanged).
function shiftStart(pts: readonly Point2[], k: number): Point2[] {
  return [...pts.slice(k), ...pts.slice(0, k)];
}

const SPELLS: Spell[] = ["fire", "lightning", "ice", "rock", "wind", "barrier"];

// Independent human-like gesture per spell (~30 cm, jittered, offset).
function humanGesture(spell: Spell): Point2[] {
  switch (spell) {
    case "fire":
      return drawCircle(1.0, 0.5, 0.16, { noise: 0.003, seed: 21 });
    case "lightning":
      return drawZ(-0.4, 0.1, 0.32, { noise: 0.003, seed: 22 });
    case "ice":
      return drawV(0.3, -0.2, 0.32, 0.3, { noise: 0.003, seed: 23 });
    case "rock":
      return drawTriangle(0.1, 0.2, 0.34, { noise: 0.003, seed: 24 });
    case "wind":
      return drawS(-0.5, 0.0, 0.32, { noise: 0.003, seed: 25 });
    case "barrier":
      return drawLine(0.4, -0.3, 0.34, { noise: 0.002, seed: 26 });
  }
}

// ---------------------------------------------------------------------------
// Tests.
// ---------------------------------------------------------------------------

describe("glyph templates", () => {
  it("exports all six spells with usable point lists", () => {
    assert.deepEqual(Object.keys(glyphs).sort(), [...SPELLS].sort());
    for (const k of SPELLS) {
      assert.ok(glyphs[k].length >= 8, `${k} needs enough points`);
      for (const p of glyphs[k]) {
        assert.equal(typeof p.x, "number");
        assert.equal(typeof p.y, "number");
        assert.ok(Number.isFinite(p.x) && Number.isFinite(p.y));
      }
    }
  });

  it("glyphs are normalized (max span == 1, centered near origin)", () => {
    for (const k of SPELLS) {
      const { sx, sy, max } = spanOf(glyphs[k]);
      assert.ok(Math.abs(max - 1) < 1e-9, `${k} max span should be 1, got ${max}`);
      assert.ok(sx <= 1 + 1e-9 && sy <= 1 + 1e-9);
      let cx = 0;
      let cy = 0;
      for (const p of glyphs[k]) {
        cx += p.x;
        cy += p.y;
      }
      assert.ok(Math.abs(cx / glyphs[k].length) < 0.2, `${k} centroid x near 0`);
      assert.ok(Math.abs(cy / glyphs[k].length) < 0.2, `${k} centroid y near 0`);
    }
  });

  it("glyph shapes are authentic", () => {
    const gap = (pts: readonly Point2[]) =>
      Math.hypot(pts[pts.length - 1].x - pts[0].x, pts[pts.length - 1].y - pts[0].y);
    // Closed: circle + upright triangle. Open: Z, V, S, line.
    assert.ok(gap(glyphs.fire) < 1e-12, "fire is a closed loop");
    assert.ok(gap(glyphs.rock) < 1e-12, "rock is a closed loop");
    for (const k of ["lightning", "ice", "wind", "barrier"] as const) {
      assert.ok(gap(glyphs[k]) > 0.7, `${k} is an open stroke`);
    }
    // Barrier is a horizontal line (flat, wide).
    const b = spanOf(glyphs.barrier);
    assert.equal(b.sy, 0);
    assert.equal(b.sx, 1);
    // Fire is round (spans match).
    const f = spanOf(glyphs.fire);
    assert.ok(Math.abs(f.sx - f.sy) < 1e-9);
    // Lightning Z: starts top-left, ends bottom-right.
    assert.ok(glyphs.lightning[0].y > 0.4 && glyphs.lightning[0].x < -0.4);
    const zl = glyphs.lightning[glyphs.lightning.length - 1];
    assert.ok(zl.y < -0.4 && zl.x > 0.4);
    // Ice V: apex (lowest point) is interior, ends are high.
    let minI = 0;
    glyphs.ice.forEach((p, i) => {
      if (p.y < glyphs.ice[minI].y) minI = i;
    });
    assert.ok(minI > 0 && minI < glyphs.ice.length - 1, "V apex is interior");
    assert.ok(glyphs.ice[0].y > 0.4 && glyphs.ice[glyphs.ice.length - 1].y > 0.4);
    // Rock: apex up, base down.
    let maxY = -Infinity;
    let minY = Infinity;
    for (const p of glyphs.rock) {
      maxY = Math.max(maxY, p.y);
      minY = Math.min(minY, p.y);
    }
    assert.ok(Math.abs(maxY - 0.5) < 1e-9 && Math.abs(minY + 0.5) < 1e-9);
    // Wind S: launches top-right, finishes bottom-left, with a wide top-left
    // lobe, a wide bottom-right lobe, and a crossing through the center.
    assert.ok(glyphs.wind[0].x > 0.3 && glyphs.wind[0].y > 0.3, "S launches top-right");
    const wEnd = glyphs.wind[glyphs.wind.length - 1];
    assert.ok(wEnd.x < -0.3 && wEnd.y < -0.3, "S finishes bottom-left");
    let topLobeMinX = Infinity;
    let botLobeMaxX = -Infinity;
    let nearCenter = Infinity;
    for (const p of glyphs.wind) {
      if (p.y > 0.1) topLobeMinX = Math.min(topLobeMinX, p.x);
      if (p.y < -0.1) botLobeMaxX = Math.max(botLobeMaxX, p.x);
      nearCenter = Math.min(nearCenter, Math.hypot(p.x, p.y));
    }
    assert.ok(topLobeMinX < -0.3, "S top-left lobe is wide");
    assert.ok(botLobeMaxX > 0.3, "S bottom-right lobe is wide");
    assert.ok(nearCenter < 0.15, "S crosses the center");
  });
});

describe("matchRune positives", () => {
  it("classifies the exact glyphs", () => {
    for (const k of SPELLS) assert.equal(matchRune(glyphs[k]), k);
  });

  it("classifies independent human-like gestures", () => {
    for (const k of SPELLS) assert.equal(matchRune(humanGesture(k)), k, k);
  });

  it("supports both traversal directions", () => {
    for (const k of SPELLS) {
      const fwd = humanGesture(k);
      // Closed loops reversed end near their start; open strokes reverse ends.
      assert.equal(matchRune(rev(fwd)), k, `${k} reversed`);
    }
  });

  it("accepts the reference handwritten S in either traversal direction", () => {
    const s = exampleS(0.5, 0.1, 0.32);
    assert.equal(matchRune(s), "wind");
    assert.equal(matchRune(rev(s)), "wind");
  });

  it("is invariant to scale and translation", () => {
    assert.equal(matchRune(drawCircle(-2, 0.7, 0.09, { seed: 31 })), "fire"); // 18 cm
    assert.equal(matchRune(drawZ(1.5, -1, 0.6, { seed: 32 })), "lightning"); // 60 cm
    assert.equal(matchRune(drawV(3, 2, 0.2, 0.18, { seed: 33 })), "ice");
    assert.equal(matchRune(drawTriangle(-1, -2, 0.5, { seed: 34 })), "rock");
    assert.equal(matchRune(drawS(2, 1, 0.45, { seed: 35 })), "wind");
    assert.equal(matchRune(drawLine(-3, 0.5, 0.5, { seed: 36 })), "barrier");
    // Small but legal: 4 cm circle clears the 3 cm span floor.
    assert.equal(matchRune(drawCircle(0, 0, 0.02, { seed: 37 })), "fire");
  });

  it("tolerates resampling, pauses and speed variation", () => {
    for (const k of SPELLS) {
      const sparse = thin(humanGesture(k), 4);
      assert.ok(sparse.length >= 8, `${k} thinned fixture long enough`);
      assert.equal(matchRune(withPauses(sparse)), k, `${k} sparse + pauses`);
    }
    // Accelerating draw (non-uniform sample spacing) still reads as a circle.
    assert.equal(matchRune(drawCircle(0, 0, 0.16, { ease: true, seed: 38 })), "fire");
  });

  it("tolerates reasonable noise", () => {
    assert.equal(matchRune(drawCircle(0, 0, 0.15, { noise: 0.004, seed: 41 })), "fire");
    assert.equal(matchRune(drawZ(0, 0, 0.3, { noise: 0.004, seed: 42 })), "lightning");
    assert.equal(matchRune(drawV(0, 0, 0.3, 0.28, { noise: 0.004, seed: 43 })), "ice");
    assert.equal(matchRune(drawTriangle(0, 0, 0.32, { noise: 0.004, seed: 44 })), "rock");
    assert.equal(matchRune(drawS(0, 0, 0.3, { noise: 0.004, seed: 45 })), "wind");
    assert.equal(matchRune(drawLine(0, 0, 0.32, { noise: 0.004, seed: 46 })), "barrier");
  });

  it("tolerates reasonable aspect variation", () => {
    assert.equal(matchRune(drawCircle(0, 0, 0.15, { aspectX: 1.25, seed: 51 })), "fire");
    assert.equal(matchRune(drawV(0, 0, 0.39, 0.3, { seed: 52 })), "ice"); // wide V
    assert.equal(matchRune(drawV(0, 0, 0.3, 0.39, { seed: 53 })), "ice"); // tall V
  });

  it("allows arbitrary start point and a natural gap on closed glyphs", () => {
    const circle = humanGesture("fire");
    assert.equal(matchRune(shiftStart(circle, 37)), "fire");
    const tri = humanGesture("rock");
    assert.equal(matchRune(shiftStart(tri, 53)), "rock");
    // Stroke ending near (not exactly at) the start still closes the shape.
    assert.equal(matchRune(circle.slice(0, Math.floor(circle.length * 0.94))), "fire");
    assert.equal(matchRune(tri.slice(0, Math.floor(tri.length * 0.94))), "rock");
  });

  it("is deterministic", () => {
    for (const k of SPELLS) {
      const g = humanGesture(k);
      assert.equal(matchRune(g), matchRune([...g.map((p) => ({ ...p }))]));
    }
  });
});

describe("matchRune rejection", () => {
  it("rejects tiny strokes below the 3 cm span floor", () => {
    assert.equal(matchRune(drawLine(0, 0, 0.029, { n: 30 })), null); // 29 mm
    assert.equal(matchRune(drawCircle(0, 0, 0.01, { n: 60 })), null); // 2 cm span
    // Just above the floor classifies.
    assert.equal(matchRune(drawLine(0, 0, 0.031, { n: 30 })), "barrier");
  });

  it("requires visible, mostly forward travel for even a fast horizontal barrier", () => {
    assert.equal(matchRune(drawLine(0, 0, 0.18, { n: 7 })), "barrier"); // sparse 7-sample line
    assert.equal(matchRune(drawLine(0, 0, 0.18, { n: 3 })), "barrier"); // minimal sparse line
    assert.equal(matchRune(rev(drawLine(0, 0, 0.18, { n: 3 }))), "barrier"); // reversed sparse line
    const twoUnique: Point2[] = [{ x: 0, y: 0 }, { x: 0, y: 0 }, { x: 0, y: 0 }, { x: 0.18, y: 0 }, { x: 0.18, y: 0 }, { x: 0.18, y: 0 }];
    assert.equal(matchRune(twoUnique), null); // endpoint-only padding
    assert.equal(matchRune([...twoUnique, ...twoUnique.slice(-3)]), null);
    const sparseVertical: Point2[] = [0, 1, 2, 3, 4].map((i) => ({ x: 0.2, y: -0.09 + (0.18 * i) / 4 }));
    assert.equal(matchRune(sparseVertical), null); // sparse vertical rejects
    const sparseBacktrack: Point2[] = [{ x: 0, y: 0 }, { x: 0.09, y: 0 }, { x: 0.02, y: 0 }, { x: 0.18, y: 0 }];
    assert.equal(matchRune(sparseBacktrack), null); // sparse backtracking rejects
  });

  it("rejects invalid data", () => {
    assert.equal(matchRune([]), null);
    const nan = drawLine(0, 0, 0.3);
    nan[3] = { x: NaN, y: 0 };
    assert.equal(matchRune(nan), null);
    const inf = drawLine(0, 0, 0.3);
    inf[5] = { x: 0.1, y: Infinity };
    assert.equal(matchRune(inf), null);
    const missing = drawLine(0, 0, 0.3) as unknown[];
    missing[2] = { x: 0.1 } as unknown as Point2;
    assert.equal(matchRune(missing as unknown as readonly Point2[]), null);
    const nulled = drawLine(0, 0, 0.3) as unknown[];
    nulled[4] = null;
    assert.equal(matchRune(nulled as unknown as readonly Point2[]), null);
    assert.equal(matchRune(undefined as unknown as readonly Point2[]), null);
    assert.equal(matchRune("Z" as unknown as readonly Point2[]), null);
  });

  it("preserves orientation: vertical line, flipped V and diagonal reject", () => {
    const vertical: Point2[] = [];
    for (let i = 0; i < 60; i++) vertical.push({ x: 0.2, y: -0.15 + (0.3 * i) / 59 });
    const v = matchRune(vertical);
    assert.equal(v, null);
    assert.notEqual(v, "barrier");

    // Chevron-up is a V rotated 180 degrees: must not read as ice.
    const up: Point2[] = [];
    for (let i = 0; i <= 60; i++) {
      const t = i / 60;
      up.push({ x: -0.15 + 0.3 * t, y: t < 0.5 ? 0.3 * (t * 2) : 0.3 - 0.3 * ((t - 0.5) * 2) });
    }
    const u = matchRune(up);
    assert.equal(u, null);
    assert.notEqual(u, "ice");

    // Diagonal (between horizontal and vertical) matches nothing.
    const diag = drawLine(0, 0, 0.3, { seed: 61 }).map((p) => ({ x: p.x, y: p.x }));
    assert.equal(matchRune(diag), null);
  });

  it("rejects wrong shapes", () => {
    // L shape.
    const ell: Point2[] = [];
    for (let i = 0; i <= 40; i++) ell.push({ x: -0.15, y: 0.15 - 0.3 * (i / 40) });
    for (let i = 1; i <= 40; i++) ell.push({ x: -0.15 + 0.3 * (i / 40), y: -0.15 });
    assert.equal(matchRune(ell), null);

    // X drawn as one continuous stroke (two diagonals).
    const ex: Point2[] = [];
    for (let i = 0; i < 40; i++) {
      const t = i / 39;
      ex.push({ x: -0.15 + 0.3 * t, y: -0.15 + 0.3 * t });
    }
    for (let i = 1; i < 40; i++) {
      const t = i / 39;
      ex.push({ x: 0.15 - 0.3 * t, y: -0.15 + 0.3 * t });
    }
    assert.equal(matchRune(ex), null);

    // Mirrored (backwards) Z is not lightning.
    const mirrored = humanGesture("lightning").map((p) => ({ x: -p.x, y: p.y }));
    const m = matchRune(mirrored);
    assert.equal(m, null);
    assert.notEqual(m, "lightning");
  });

  it("rejects scribbles and extreme backtracking", () => {
    const rng = makeRng(99);
    const scribble: Point2[] = [];
    let sx = 0;
    let sy = 0;
    for (let i = 0; i < 220; i++) {
      sx += (rng() - 0.5) * 0.06;
      sy += (rng() - 0.5) * 0.06;
      scribble.push({ x: sx, y: sy });
    }
    assert.equal(matchRune(scribble), null);

    // Zigzag retracing its path: path length dwarfs gesture span.
    const zig: Point2[] = [];
    const passes = 12;
    for (let i = 0; i < 200; i++) {
      const t = i / 199;
      const tri = Math.abs(((t * passes) % 2) - 0.5) * 2;
      zig.push({ x: -0.15 + 0.3 * t, y: tri * 0.1 });
    }
    assert.equal(matchRune(zig), null);
  });

  it("rejects unequivocal squares, including rotated diamonds", () => {
    for (const rot of [0, 45]) {
      const q = drawSquare(0.3, -0.1, 0.34, { rot, seed: 81 });
      const m = matchRune(q);
      assert.equal(m, null, `square rot ${rot} rejects`);
      assert.notEqual(m, "fire");
    }
    // A noisy square at another angle still rejects (not one clean trace).
    const tilted = drawSquare(-0.2, 0.2, 0.3, { rot: 30, noise: 0.002, seed: 82 });
    assert.equal(matchRune(tilted), null);
    assert.equal(matchRune(rev(tilted)), null);
  });

  it("rejects ambiguous strokes sitting between classes", () => {
    // Shallow dip: too deep to be a clean line, too flat to be a V.
    const shallow: Point2[] = [];
    for (let i = 0; i <= 60; i++) {
      const t = i / 60;
      shallow.push({ x: -0.15 + 0.3 * t, y: 0.1 - 0.05 * (1 - Math.abs(t - 0.5) * 2) });
    }
    assert.equal(matchRune(shallow), null);
  });
});
