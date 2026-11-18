// Compact, deterministic, offline six-way air-drawing classifier with rejection.
// Single continuous strokes in a stable camera plane in meters (y up).
// Orientation is preserved (no rotation invariance).

export type Spell = "fire" | "lightning" | "ice" | "rock" | "wind" | "barrier";
export type Point2 = { x: number; y: number };

// ---------------------------------------------------------------------------
// Normalized authentic glyph templates (centered at origin, max span == 1).
// ---------------------------------------------------------------------------

function circleGlyph(n = 64): Point2[] {
  const pts: Point2[] = [];
  for (let i = 0; i < n; i++) {
    const a = (2 * Math.PI * i) / n;
    pts.push({ x: 0.5 * Math.cos(a), y: 0.5 * Math.sin(a) });
  }
  pts.push({ ...pts[0] });
  return pts;
}

function polylineGlyph(corners: Point2[], per = 16): Point2[] {
  const pts: Point2[] = [];
  for (let s = 0; s < corners.length - 1; s++) {
    const a = corners[s];
    const b = corners[s + 1];
    for (let i = 0; i < per; i++) {
      const t = i / per;
      pts.push({ x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t });
    }
  }
  pts.push({ ...corners[corners.length - 1] });
  return pts;
}

function bezierGlyph(
  p0: Point2,
  p1: Point2,
  p2: Point2,
  p3: Point2,
  n = 64,
): Point2[] {
  const pts: Point2[] = [];
  for (let i = 0; i < n; i++) {
    const t = i / (n - 1);
    const u = 1 - t;
    pts.push({
      x: u * u * u * p0.x + 3 * u * u * t * p1.x + 3 * u * t * t * p2.x + t * t * t * p3.x,
      y: u * u * u * p0.y + 3 * u * u * t * p1.y + 3 * u * t * t * p2.y + t * t * t * p3.y,
    });
  }
  return pts;
}

// Center bbox at origin, scale max span to 1 (for hand-built glyphs).
function fitGlyph(pts: Point2[]): Point2[] {
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
  const span = Math.max(maxX - minX, maxY - minY) || 1;
  const cx = (minX + maxX) / 2;
  const cy = (minY + maxY) / 2;
  return pts.map((p) => ({ x: (p.x - cx) / span, y: (p.y - cy) / span }));
}

// Conventional Latin S: top-right launch, around the top-left lobe, diagonal
// cross through the center, around the bottom-right lobe, bottom-left finish.
// Five joined cubics with matching joint tangents and 180-degree rotational
// symmetry, so the stroke stays smooth at every joint.
function sGlyph(): Point2[] {
  const j0 = { x: 0.42, y: 0.34 };
  const eTop = { x: -0.02, y: 0.52 };
  const j1 = { x: -0.34, y: 0.1 };
  const j2 = { x: 0.34, y: -0.1 };
  const eBot = { x: 0.02, y: -0.52 };
  const j3 = { x: -0.42, y: -0.34 };
  const t1 = bezierGlyph(j0, { x: 0.33, y: 0.52 }, { x: 0.2, y: 0.53 }, eTop, 22);
  const t2 = bezierGlyph(eTop, { x: -0.24, y: 0.52 }, { x: -0.58, y: 0.17 }, j1, 22);
  const mid = bezierGlyph(j1, { x: -0.1, y: 0.03 }, { x: 0.1, y: -0.03 }, j2, 13);
  const b1 = bezierGlyph(j2, { x: 0.58, y: -0.17 }, { x: 0.24, y: -0.52 }, eBot, 22);
  const b2 = bezierGlyph(eBot, { x: -0.2, y: -0.53 }, { x: -0.33, y: -0.52 }, j3, 22);
  return fitGlyph([...t1, ...t2.slice(1), ...mid.slice(1), ...b1.slice(1), ...b2.slice(1)]);
}

export const glyphs: Record<Spell, readonly Point2[]> = {
  // Circle (closed loop).
  fire: circleGlyph(),
  // Capital Z: top bar left->right, diagonal to bottom-left, bottom bar left->right.
  lightning: polylineGlyph([
    { x: -0.5, y: 0.5 },
    { x: 0.5, y: 0.5 },
    { x: -0.5, y: -0.5 },
    { x: 0.5, y: -0.5 },
  ]),
  // V, apex down.
  ice: polylineGlyph([
    { x: -0.5, y: 0.5 },
    { x: 0, y: -0.5 },
    { x: 0.5, y: 0.5 },
  ]),
  // Closed upright triangle, apex up.
  rock: polylineGlyph(
    [
      { x: 0, y: 0.5 },
      { x: -0.5, y: -0.5 },
      { x: 0.5, y: -0.5 },
      { x: 0, y: 0.5 },
    ],
    21,
  ),
  // Conventional S: starts top-right, ends bottom-left, two wide lobes.
  wind: sGlyph(),
  // Horizontal line.
  barrier: polylineGlyph(
    [
      { x: -0.5, y: 0 },
      { x: 0.5, y: 0 },
    ],
    64,
  ),
};

// ---------------------------------------------------------------------------
// Classifier.
// ---------------------------------------------------------------------------

const RESAMPLE_N = 64;
const MIN_POINTS = 8;
const MIN_SPAN = 0.03; // 3 cm, meters.
const BACKTRACK_LIMIT = 8; // totalLength / maxSpan above this rejects.
const CLOSURE_T = 0.35; // endpoint gap / maxSpan below this counts as closed.
const AMBIGUITY_MARGIN = 0.03; // min gap between best and runner-up.

// Per-class acceptance ceilings on mean normalized point distance.
// Normalization scales max span to 1, so these are fractions of gesture size.
const ACCEPT: Record<Spell, number> = {
  fire: 0.2,
  lightning: 0.24,
  ice: 0.2,
  rock: 0.2,
  wind: 0.24,
  barrier: 0.14,
};

const CLOSED: Record<Spell, boolean> = {
  fire: true,
  lightning: false,
  ice: false,
  rock: true,
  wind: false,
  barrier: false,
};

const ORDER: Spell[] = ["fire", "lightning", "ice", "rock", "wind", "barrier"];

function isValidPoint(p: unknown): p is Point2 {
  if (typeof p !== "object" || p === null) return false;
  const x = (p as Record<string, unknown>).x;
  const y = (p as Record<string, unknown>).y;
  return typeof x === "number" && typeof y === "number" && Number.isFinite(x) && Number.isFinite(y);
}

function pathLength(pts: readonly Point2[]): number {
  let L = 0;
  for (let i = 1; i < pts.length; i++) {
    L += Math.hypot(pts[i].x - pts[i - 1].x, pts[i].y - pts[i - 1].y);
  }
  return L;
}

function bbox(pts: readonly Point2[]): { minX: number; maxX: number; minY: number; maxY: number } {
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
  return { minX, maxX, minY, maxY };
}

// Equidistant arc-length resampling. Deterministic; pauses (duplicate points)
// contribute zero length and are skipped naturally.
function resample(input: readonly Point2[], n: number): Point2[] {
  const pts: Point2[] = input.map((p) => ({ x: p.x, y: p.y }));
  const total = pathLength(pts);
  if (!(total > 0)) return Array.from({ length: n }, () => ({ ...pts[0] }));
  const step = total / (n - 1);
  const out: Point2[] = [{ ...pts[0] }];
  let acc = 0;
  let i = 1;
  let prev: Point2 = { ...pts[0] };
  while (out.length < n && i < pts.length) {
    const seg = Math.hypot(pts[i].x - prev.x, pts[i].y - prev.y);
    if (seg === 0) {
      i++;
      continue;
    }
    if (acc + seg >= step * out.length) {
      const need = step * out.length - acc;
      const t = need / seg;
      const q = { x: prev.x + (pts[i].x - prev.x) * t, y: prev.y + (pts[i].y - prev.y) * t };
      out.push(q);
      prev = q;
      acc += need;
      // Do not advance i: remainder of segment is still pending.
      // acc now equals step * (out.length - 1).
    } else {
      acc += seg;
      prev = { ...pts[i] };
      i++;
    }
  }
  while (out.length < n) out.push({ ...pts[pts.length - 1] });
  return out;
}

// Translate centroid to origin, uniform scale so max span == 1.
function normalizeResampled(pts: Point2[]): Point2[] {
  let cx = 0;
  let cy = 0;
  for (const p of pts) {
    cx += p.x;
    cy += p.y;
  }
  cx /= pts.length;
  cy /= pts.length;
  const b = bbox(pts);
  const span = Math.max(b.maxX - b.minX, b.maxY - b.minY);
  if (!(span > 0)) return pts.map(() => ({ x: 0, y: 0 }));
  return pts.map((p) => ({ x: (p.x - cx) / span, y: (p.y - cy) / span }));
}

function meanDist(a: readonly Point2[], b: readonly Point2[]): number {
  let s = 0;
  for (let i = 0; i < a.length; i++) {
    s += Math.hypot(a[i].x - b[i].x, a[i].y - b[i].y);
  }
  return s / a.length;
}

function reversed(pts: readonly Point2[]): Point2[] {
  return pts.slice().reverse();
}

// Light smoothing (radius 1) to tame per-sample jitter before angle tests.
// Closed inputs wrap; open inputs clamp at the ends.
function smoothedForAngles(pts: readonly Point2[], closed: boolean): Point2[] {
  const n = pts.length;
  const out: Point2[] = new Array(n);
  for (let i = 0; i < n; i++) {
    let sx = 0;
    let sy = 0;
    let c = 0;
    for (let k = -1; k <= 1; k++) {
      let j = i + k;
      if (closed) j = ((j % n) + n) % n;
      else if (j < 0 || j >= n) continue;
      sx += pts[j].x;
      sy += pts[j].y;
      c++;
    }
    out[i] = { x: sx / c, y: sy / c };
  }
  return out;
}

// Windowed turning angle (degrees) at each sample. Closed inputs wrap;
// open inputs clamp at the ends.
function turningAngles(pts: readonly Point2[], closed: boolean, k: number): number[] {
  const s = smoothedForAngles(pts, closed);
  const n = s.length;
  const angs: number[] = new Array(n);
  for (let i = 0; i < n; i++) {
    let a = i - k;
    let b = i + k;
    if (closed) {
      a = ((a % n) + n) % n;
      b = ((b % n) + n) % n;
    } else {
      a = Math.max(0, a);
      b = Math.min(n - 1, b);
    }
    const v1x = s[i].x - s[a].x;
    const v1y = s[i].y - s[a].y;
    const v2x = s[b].x - s[i].x;
    const v2y = s[b].y - s[i].y;
    const n1 = Math.hypot(v1x, v1y);
    const n2 = Math.hypot(v2x, v2y);
    if (!(n1 > 1e-12) || !(n2 > 1e-12)) {
      angs[i] = 0;
      continue;
    }
    const cross = v1x * v2y - v1y * v2x;
    const dot = v1x * v2x + v1y * v2y;
    angs[i] = (Math.abs(Math.atan2(cross, dot)) * 180) / Math.PI;
  }
  return angs;
}

// Largest windowed turning angle (degrees) along the path. Sharp Z-like
// corners read ~100+, smooth S curves stay well under 60.
function maxTurningAngle(pts: readonly Point2[], closed: boolean): number {
  let best = 0;
  for (const ang of turningAngles(pts, closed, 2)) {
    if (ang > best) best = ang;
  }
  return best;
}

// Number of strong corners (local turning-angle maxima above 50 degrees).
// Clean squares/diamonds read 4, triangles 3, circles 0.
function cornerCount(pts: readonly Point2[], closed: boolean): number {
  const angs = turningAngles(pts, closed, 4);
  const n = angs.length;
  const peaks: number[] = [];
  for (let i = 0; i < n; i++) {
    if (angs[i] < 50) continue;
    let isMax = true;
    for (let k = 1; k <= 2; k++) {
      const p = closed ? angs[(((i - k) % n) + n) % n] : i - k >= 0 ? angs[i - k] : -1;
      const q = closed ? angs[(i + k) % n] : i + k < n ? angs[i + k] : -1;
      if (angs[i] < p || angs[i] < q) {
        isMax = false;
        break;
      }
    }
    if (isMax) peaks.push(i);
  }
  const kept: number[] = [];
  for (const p of peaks) {
    if (kept.every((q) => Math.min((p - q + n) % n, (q - p + n) % n) >= 8)) kept.push(p);
  }
  return kept.length;
}

// Best cyclic-shift distance for closed templates (both directions).
function closedDist(input: readonly Point2[], tpl: readonly Point2[]): number {
  const n = input.length;
  let best = Infinity;
  for (let dir = 0; dir < 2; dir++) {
    const t = dir === 0 ? tpl : reversed(tpl);
    for (let s = 0; s < n; s++) {
      let sum = 0;
      for (let i = 0; i < n; i++) {
        const q = t[(i + s) % n];
        sum += Math.hypot(input[i].x - q.x, input[i].y - q.y);
        if (sum / n >= best) break; // early exit
      }
      const d = sum / n;
      if (d < best) best = d;
    }
  }
  return best;
}

function openDist(input: readonly Point2[], tpl: readonly Point2[]): number {
  return Math.min(meanDist(input, tpl), meanDist(input, reversed(tpl)));
}

// Lazily built normalized templates derived from the exported glyphs.
let templateCache: Record<Spell, readonly Point2[]> | null = null;
function templates(): Record<Spell, readonly Point2[]> {
  if (templateCache) return templateCache;
  const t = {} as Record<Spell, readonly Point2[]>;
  for (const k of ORDER) {
    t[k] = normalizeResampled(resample(glyphs[k], RESAMPLE_N));
  }
  templateCache = t;
  return t;
}

// A fast line may only span three XR frames. Its interior sample distinguishes
// observed movement from a tracking jump between two otherwise stationary poses.
function hasLineEvidence(points: readonly Point2[], span: number, endGap: number) {
  const start = points[0];
  const end = points[points.length - 1];
  const axisX = (end.x - start.x) / endGap;
  const axisY = (end.y - start.y) / endGap;
  const margin = Math.max(0.005, span * 0.1);
  let hasInterior = false;
  let backtracking = 0;
  let previous = 0;
  for (const point of points) {
    const progress = (point.x - start.x) * axisX + (point.y - start.y) * axisY;
    if (progress > margin && progress < endGap - margin) hasInterior = true;
    backtracking += Math.max(0, previous - progress);
    previous = progress;
  }
  return hasInterior && backtracking <= 0.2 * endGap;
}

export function matchRune(raw: readonly Point2[]): Spell | null {
  if (!Array.isArray(raw) || raw.length < 3 || !raw.every(isValidPoint)) return null;
  const b = bbox(raw);
  const spanX = b.maxX - b.minX;
  const spanY = b.maxY - b.minY;
  const spanMax = Math.max(spanX, spanY);
  if (!(spanMax >= MIN_SPAN)) return null;

  const total = pathLength(raw);
  if (!(total > 0)) return null;
  // Extreme backtracking / scribble guard (pauses add no length).
  if (total / spanMax > BACKTRACK_LIMIT) return null;

  const endGap = Math.hypot(
    raw[raw.length - 1].x - raw[0].x,
    raw[raw.length - 1].y - raw[0].y,
  );
  const closedInput = endGap / spanMax < CLOSURE_T;

  // Chord deviations of the raw stroke (in meters). Robust to per-sample
  // jitter, unlike path-length ratios which white noise inflates.
  let maxDev = 0;
  let maxSigned = -Infinity;
  let minSigned = Infinity;
  if (endGap > 1e-12) {
    const dx = (raw[raw.length - 1].x - raw[0].x) / endGap;
    const dy = (raw[raw.length - 1].y - raw[0].y) / endGap;
    for (const p of raw) {
      const rx = p.x - raw[0].x;
      const ry = p.y - raw[0].y;
      const signed = rx * -dy + ry * dx; // left-of-chord signed distance
      const dev = Math.abs(signed);
      if (dev > maxDev) maxDev = dev;
      if (signed > maxSigned) maxSigned = signed;
      if (signed < minSigned) minSigned = signed;
    }
  } else {
    maxDev = 0;
    maxSigned = 0;
    minSigned = 0;
  }

  // Barrier orientation/straightness guards on raw meters (preserve orientation).
  // Horizontal line only: wide, flat, endpoints far apart, stays near chord.
  const barrierShapeOk =
    spanX >= 3 * spanY &&
    endGap / spanMax > 0.7 &&
    maxDev / spanMax <= 0.12 &&
    hasLineEvidence(raw, spanMax, endGap);

  if (raw.length < MIN_POINTS) return barrierShapeOk ? "barrier" : null;
  const sampled = resample(raw, RESAMPLE_N);

  // Wind (S) guard: smooth two-lobe curve crossing its chord —
  // significant wiggle on BOTH sides. Rejects straight vertical/diagonal
  // lines and single-sided arcs that template distance alone may forgive.
  // The smoothness cap rejects sharp squared-S zigzags (mirrored Z reads
  // ~100+ degrees here, genuine smooth S curves stay well under 60).
  const windShapeOk =
    endGap / spanMax > 0.7 &&
    maxSigned / spanMax > 0.07 &&
    minSigned / spanMax < -0.07 &&
    maxTurningAngle(sampled, closedInput) < 60;

  // Squareness guard: closed strokes with four or more strong corners
  // (axis-aligned squares, rotated diamonds, other polygons) are not runes.
  // Circles read 0 corners, triangles 3.
  if (closedInput && cornerCount(sampled, true) >= 4) return null;

  const norm = normalizeResampled(sampled);
  const T = templates();

  let best: Spell | null = null;
  let bestD = Infinity;
  let secondD = Infinity;
  for (const k of ORDER) {
    if (CLOSED[k] !== closedInput) continue; // open/closed gate
    if (k === "barrier" && !barrierShapeOk) continue;
    if (k === "wind" && !windShapeOk) continue;
    const d = CLOSED[k] ? closedDist(norm, T[k]) : openDist(norm, T[k]);
    if (d < bestD) {
      secondD = bestD;
      bestD = d;
      best = k;
    } else if (d < secondD) {
      secondD = d;
    }
  }
  if (best === null) return null;
  if (!(bestD <= ACCEPT[best])) return null;
  // Ambiguity rejection: needs a clear winner. Closed fire/rock pairs that
  // sit halfway between shapes have nearly tied distances.
  if (!(secondD - bestD >= AMBIGUITY_MARGIN)) return null;
  return best;
}
