// Compact, deterministic, offline six-way air-drawing classifier with rejection.
// Single continuous strokes in a stable camera plane in meters (y up).
// Orientation is preserved (no rotation invariance).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Runeveil
{
    public enum RuneSpell
    {
        Fire,
        Lightning,
        Ice,
        Rock,
        Wind,
        Barrier,
    }

    public static class RuneveilRunes
    {
        const int ResampleCount = 64;
        const int MinPoints = 8;
        const float MinSpan = 0.03f; // 3 cm, meters.
        const float BacktrackLimit = 8f; // totalLength / maxSpan above this rejects.
        const float ClosureT = 0.35f; // endpoint gap / maxSpan below this counts as closed.
        const float AmbiguityMargin = 0.03f; // min gap between best and runner-up.

        static readonly RuneSpell[] Order =
        {
            RuneSpell.Fire,
            RuneSpell.Lightning,
            RuneSpell.Ice,
            RuneSpell.Rock,
            RuneSpell.Wind,
            RuneSpell.Barrier,
        };

        // Per-class acceptance ceilings on mean normalized point distance.
        // Normalization scales max span to 1, so these are fractions of gesture size.
        static float AcceptCeiling(RuneSpell spell)
        {
            switch (spell)
            {
                case RuneSpell.Fire: return 0.20f;
                case RuneSpell.Lightning: return 0.24f;
                case RuneSpell.Ice: return 0.20f;
                case RuneSpell.Rock: return 0.20f;
                case RuneSpell.Wind: return 0.24f;
                case RuneSpell.Barrier: return 0.14f;
                default: return 0.20f;
            }
        }

        static bool IsClosedShape(RuneSpell spell)
        {
            return spell == RuneSpell.Fire || spell == RuneSpell.Rock;
        }

        static readonly Dictionary<RuneSpell, Vector2[]> Templates = BuildTemplates();

        static Dictionary<RuneSpell, Vector2[]> BuildTemplates()
        {
            var glyphs = new Dictionary<RuneSpell, List<Vector2>>();
            glyphs[RuneSpell.Fire] = CircleGlyph();
            glyphs[RuneSpell.Lightning] = PolylineGlyph(new[]
            {
                new Vector2(-0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(-0.5f, -0.5f),
                new Vector2(0.5f, -0.5f),
            });
            glyphs[RuneSpell.Ice] = PolylineGlyph(new[]
            {
                new Vector2(-0.5f, 0.5f),
                new Vector2(0f, -0.5f),
                new Vector2(0.5f, 0.5f),
            });
            glyphs[RuneSpell.Rock] = PolylineGlyph(new[]
            {
                new Vector2(0f, 0.5f),
                new Vector2(-0.5f, -0.5f),
                new Vector2(0.5f, -0.5f),
                new Vector2(0f, 0.5f),
            }, 21);
            glyphs[RuneSpell.Wind] = BuildSGlyph();
            glyphs[RuneSpell.Barrier] = PolylineGlyph(new[]
            {
                new Vector2(-0.5f, 0f),
                new Vector2(0.5f, 0f),
            }, 64);

            var templates = new Dictionary<RuneSpell, Vector2[]>();
            foreach (RuneSpell spell in Order)
                templates[spell] = NormalizeResampled(Resample(glyphs[spell], ResampleCount));
            return templates;
        }

        static List<Vector2> CircleGlyph(int n = 64)
        {
            var pts = new List<Vector2>(n + 1);
            for (int i = 0; i < n; i++)
            {
                float a = 2f * Mathf.PI * i / n;
                pts.Add(new Vector2(0.5f * Mathf.Cos(a), 0.5f * Mathf.Sin(a)));
            }
            pts.Add(pts[0]);
            return pts;
        }

        static List<Vector2> PolylineGlyph(IList<Vector2> corners, int per = 16)
        {
            var pts = new List<Vector2>((corners.Count - 1) * per + 1);
            for (int s = 0; s < corners.Count - 1; s++)
            {
                Vector2 a = corners[s];
                Vector2 b = corners[s + 1];
                for (int i = 0; i < per; i++)
                {
                    float t = (float)i / per;
                    pts.Add(new Vector2(
                        a.x + (b.x - a.x) * t,
                        a.y + (b.y - a.y) * t));
                }
            }
            pts.Add(corners[corners.Count - 1]);
            return pts;
        }

        static List<Vector2> BezierGlyph(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, int n)
        {
            var pts = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / (n - 1);
                float u = 1f - t;
                pts.Add(new Vector2(
                    u * u * u * p0.x + 3f * u * u * t * p1.x + 3f * u * t * t * p2.x + t * t * t * p3.x,
                    u * u * u * p0.y + 3f * u * u * t * p1.y + 3f * u * t * t * p2.y + t * t * t * p3.y));
            }
            return pts;
        }

        // Center bbox at origin, scale max span to 1 (for hand-built glyphs).
        static List<Vector2> FitGlyph(List<Vector2> pts)
        {
            BBox(pts, out float minX, out float maxX, out float minY, out float maxY);
            float span = Mathf.Max(maxX - minX, maxY - minY);
            if (span <= 0f) span = 1f;
            float cx = (minX + maxX) * 0.5f;
            float cy = (minY + maxY) * 0.5f;
            var outPts = new List<Vector2>(pts.Count);
            foreach (Vector2 p in pts)
                outPts.Add(new Vector2((p.x - cx) / span, (p.y - cy) / span));
            return outPts;
        }

        // Conventional Latin S: top-right launch, around the top-left lobe, diagonal
        // cross through the center, around the bottom-right lobe, bottom-left finish.
        // Five joined cubics with matching joint tangents and 180-degree rotational
        // symmetry, so the stroke stays smooth at every joint.
        static List<Vector2> BuildSGlyph()
        {
            var j0 = new Vector2(0.42f, 0.34f);
            var eTop = new Vector2(-0.02f, 0.52f);
            var j1 = new Vector2(-0.34f, 0.10f);
            var j2 = new Vector2(0.34f, -0.10f);
            var eBot = new Vector2(0.02f, -0.52f);
            var j3 = new Vector2(-0.42f, -0.34f);
            var parts = new List<Vector2>();
            AppendSkipFirst(parts, BezierGlyph(j0, new Vector2(0.33f, 0.52f), new Vector2(0.20f, 0.53f), eTop, 22));
            AppendSkipFirst(parts, BezierGlyph(eTop, new Vector2(-0.24f, 0.52f), new Vector2(-0.58f, 0.17f), j1, 22));
            AppendSkipFirst(parts, BezierGlyph(j1, new Vector2(-0.10f, 0.03f), new Vector2(0.10f, -0.03f), j2, 13));
            AppendSkipFirst(parts, BezierGlyph(j2, new Vector2(0.58f, -0.17f), new Vector2(0.24f, -0.52f), eBot, 22));
            AppendSkipFirst(parts, BezierGlyph(eBot, new Vector2(-0.20f, -0.53f), new Vector2(-0.33f, -0.52f), j3, 22));
            return FitGlyph(parts);
        }

        static void AppendSkipFirst(List<Vector2> dst, List<Vector2> src)
        {
            for (int i = 0; i < src.Count; i++)
            {
                if (dst.Count > 0 && i == 0) continue;
                dst.Add(src[i]);
            }
        }

        public static RuneSpell? Match(IReadOnlyList<Vector2> points)
        {
            if (points == null || points.Count < 3) return null;
            for (int i = 0; i < points.Count; i++)
            {
                if (!IsFinite(points[i].x) || !IsFinite(points[i].y)) return null;
            }

            BBox(points, out float minX, out float maxX, out float minY, out float maxY);
            float spanX = maxX - minX;
            float spanY = maxY - minY;
            float spanMax = Mathf.Max(spanX, spanY);
            if (!(spanMax >= MinSpan)) return null;

            float total = PathLength(points);
            if (!(total > 0f)) return null;
            // Extreme backtracking / scribble guard (pauses add no length).
            if (total / spanMax > BacktrackLimit) return null;

            Vector2 start = points[0];
            Vector2 end = points[points.Count - 1];
            float endGap = Mathf.Sqrt(
                (end.x - start.x) * (end.x - start.x) +
                (end.y - start.y) * (end.y - start.y));
            bool closedInput = endGap / spanMax < ClosureT;

            // Chord deviations of the raw stroke (in meters). Robust to per-sample
            // jitter, unlike path-length ratios which white noise inflates.
            float maxDev = 0f;
            float maxSigned = 0f;
            float minSigned = 0f;
            if (endGap > 1e-12f)
            {
                float dx = (end.x - start.x) / endGap;
                float dy = (end.y - start.y) / endGap;
                maxSigned = float.NegativeInfinity;
                minSigned = float.PositiveInfinity;
                for (int i = 0; i < points.Count; i++)
                {
                    float rx = points[i].x - start.x;
                    float ry = points[i].y - start.y;
                    float signed = rx * -dy + ry * dx; // left-of-chord signed distance
                    float dev = Mathf.Abs(signed);
                    if (dev > maxDev) maxDev = dev;
                    if (signed > maxSigned) maxSigned = signed;
                    if (signed < minSigned) minSigned = signed;
                }
            }

            // Barrier orientation/straightness guards on raw meters (preserve orientation).
            // Horizontal line only: wide, flat, endpoints far apart, stays near chord.
            bool barrierShapeOk =
                spanX >= 3f * spanY &&
                endGap / spanMax > 0.7f &&
                maxDev / spanMax <= 0.12f &&
                HasLineEvidence(points, spanMax, endGap);

            if (points.Count < MinPoints) return barrierShapeOk ? RuneSpell.Barrier : (RuneSpell?)null;

            Vector2[] sampled = Resample(points, ResampleCount);

            // Wind (S) guard: smooth two-lobe curve crossing its chord —
            // significant wiggle on BOTH sides. Rejects straight vertical/diagonal
            // lines and single-sided arcs that template distance alone may forgive.
            // The smoothness cap rejects sharp squared-S zigzags (mirrored Z reads
            // ~100+ degrees here, genuine smooth S curves stay well under 60).
            bool windShapeOk =
                endGap / spanMax > 0.7f &&
                maxSigned / spanMax > 0.07f &&
                minSigned / spanMax < -0.07f &&
                MaxTurningAngle(sampled, closedInput) < 60f;

            // Squareness guard: closed strokes with four or more strong corners
            // (axis-aligned squares, rotated diamonds, other polygons) are not runes.
            // Circles read 0 corners, triangles 3.
            if (closedInput && CornerCount(sampled, true) >= 4) return null;

            Vector2[] norm = NormalizeResampled(sampled);

            RuneSpell? best = null;
            float bestD = float.PositiveInfinity;
            float secondD = float.PositiveInfinity;
            foreach (RuneSpell spell in Order)
            {
                if (IsClosedShape(spell) != closedInput) continue; // open/closed gate
                if (spell == RuneSpell.Barrier && !barrierShapeOk) continue;
                if (spell == RuneSpell.Wind && !windShapeOk) continue;
                float d = IsClosedShape(spell)
                    ? ClosedDist(norm, Templates[spell])
                    : OpenDist(norm, Templates[spell]);
                if (d < bestD)
                {
                    secondD = bestD;
                    bestD = d;
                    best = spell;
                }
                else if (d < secondD)
                {
                    secondD = d;
                }
            }
            if (best == null) return null;
            if (!(bestD <= AcceptCeiling(best.Value))) return null;
            // Ambiguity rejection: needs a clear winner. Closed fire/rock pairs that
            // sit halfway between shapes have nearly tied distances.
            if (!(secondD - bestD >= AmbiguityMargin)) return null;
            return best;
        }

        static bool IsFinite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }

        static void BBox(IReadOnlyList<Vector2> pts, out float minX, out float maxX, out float minY, out float maxY)
        {
            minX = float.PositiveInfinity;
            maxX = float.NegativeInfinity;
            minY = float.PositiveInfinity;
            maxY = float.NegativeInfinity;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 p = pts[i];
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }
        }

        static float PathLength(IReadOnlyList<Vector2> pts)
        {
            float length = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                float dx = pts[i].x - pts[i - 1].x;
                float dy = pts[i].y - pts[i - 1].y;
                length += Mathf.Sqrt(dx * dx + dy * dy);
            }
            return length;
        }

        // Equidistant arc-length resampling. Deterministic; pauses (duplicate points)
        // contribute zero length and are skipped naturally.
        static Vector2[] Resample(IReadOnlyList<Vector2> input, int n)
        {
            var outPts = new Vector2[n];
            float total = PathLength(input);
            if (!(total > 0f))
            {
                for (int i = 0; i < n; i++) outPts[i] = input[0];
                return outPts;
            }
            float step = total / (n - 1);
            outPts[0] = input[0];
            int filled = 1;
            float acc = 0f;
            int iIdx = 1;
            Vector2 prev = input[0];
            while (filled < n && iIdx < input.Count)
            {
                float dx = input[iIdx].x - prev.x;
                float dy = input[iIdx].y - prev.y;
                float seg = Mathf.Sqrt(dx * dx + dy * dy);
                if (seg == 0f)
                {
                    iIdx++;
                    continue;
                }
                if (acc + seg >= step * filled)
                {
                    float need = step * filled - acc;
                    float t = need / seg;
                    var q = new Vector2(prev.x + (input[iIdx].x - prev.x) * t,
                        prev.y + (input[iIdx].y - prev.y) * t);
                    outPts[filled++] = q;
                    prev = q;
                    acc += need;
                    // Do not advance iIdx: remainder of segment is still pending.
                    // acc now equals step * (filled - 1).
                }
                else
                {
                    acc += seg;
                    prev = input[iIdx];
                    iIdx++;
                }
            }
            Vector2 last = input[input.Count - 1];
            while (filled < n) outPts[filled++] = last;
            return outPts;
        }

        // Translate centroid to origin, uniform scale so max span == 1.
        static Vector2[] NormalizeResampled(Vector2[] pts)
        {
            float cx = 0f;
            float cy = 0f;
            for (int i = 0; i < pts.Length; i++)
            {
                cx += pts[i].x;
                cy += pts[i].y;
            }
            cx /= pts.Length;
            cy /= pts.Length;
            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            for (int i = 0; i < pts.Length; i++)
            {
                if (pts[i].x < minX) minX = pts[i].x;
                if (pts[i].x > maxX) maxX = pts[i].x;
                if (pts[i].y < minY) minY = pts[i].y;
                if (pts[i].y > maxY) maxY = pts[i].y;
            }
            float span = Mathf.Max(maxX - minX, maxY - minY);
            var outPts = new Vector2[pts.Length];
            if (!(span > 0f)) return outPts; // all zeros
            for (int i = 0; i < pts.Length; i++)
                outPts[i] = new Vector2((pts[i].x - cx) / span, (pts[i].y - cy) / span);
            return outPts;
        }

        static float MeanDist(Vector2[] a, Vector2[] b)
        {
            float s = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                float dx = a[i].x - b[i].x;
                float dy = a[i].y - b[i].y;
                s += Mathf.Sqrt(dx * dx + dy * dy);
            }
            return s / a.Length;
        }

        static Vector2[] Reversed(Vector2[] pts)
        {
            var outPts = new Vector2[pts.Length];
            for (int i = 0; i < pts.Length; i++)
                outPts[i] = pts[pts.Length - 1 - i];
            return outPts;
        }

        // Light smoothing (radius 1) to tame per-sample jitter before angle tests.
        // Closed inputs wrap; open inputs clamp at the ends.
        static Vector2[] SmoothedForAngles(Vector2[] pts, bool closed)
        {
            int n = pts.Length;
            var outPts = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float sx = 0f;
                float sy = 0f;
                int c = 0;
                for (int k = -1; k <= 1; k++)
                {
                    int j = i + k;
                    if (closed) j = ((j % n) + n) % n;
                    else if (j < 0 || j >= n) continue;
                    sx += pts[j].x;
                    sy += pts[j].y;
                    c++;
                }
                outPts[i] = new Vector2(sx / c, sy / c);
            }
            return outPts;
        }

        // Windowed turning angle (degrees) at each sample. Closed inputs wrap;
        // open inputs clamp at the ends.
        static float[] TurningAngles(Vector2[] pts, bool closed, int k)
        {
            Vector2[] s = SmoothedForAngles(pts, closed);
            int n = s.Length;
            var angs = new float[n];
            for (int i = 0; i < n; i++)
            {
                int a = i - k;
                int b = i + k;
                if (closed)
                {
                    a = ((a % n) + n) % n;
                    b = ((b % n) + n) % n;
                }
                else
                {
                    a = Math.Max(0, a);
                    b = Math.Min(n - 1, b);
                }
                float v1x = s[i].x - s[a].x;
                float v1y = s[i].y - s[a].y;
                float v2x = s[b].x - s[i].x;
                float v2y = s[b].y - s[i].y;
                float n1 = Mathf.Sqrt(v1x * v1x + v1y * v1y);
                float n2 = Mathf.Sqrt(v2x * v2x + v2y * v2y);
                if (!(n1 > 1e-12f) || !(n2 > 1e-12f))
                {
                    angs[i] = 0f;
                    continue;
                }
                float cross = v1x * v2y - v1y * v2x;
                float dot = v1x * v2x + v1y * v2y;
                angs[i] = Mathf.Abs(Mathf.Atan2(cross, dot)) * Mathf.Rad2Deg;
            }
            return angs;
        }

        // Largest windowed turning angle (degrees) along the path. Sharp Z-like
        // corners read ~100+, smooth S curves stay well under 60.
        static float MaxTurningAngle(Vector2[] pts, bool closed)
        {
            float best = 0f;
            foreach (float ang in TurningAngles(pts, closed, 2))
                if (ang > best) best = ang;
            return best;
        }

        // Number of strong corners (local turning-angle maxima above 50 degrees).
        // Clean squares/diamonds read 4, triangles 3, circles 0.
        static int CornerCount(Vector2[] pts, bool closed)
        {
            float[] angs = TurningAngles(pts, closed, 4);
            int n = angs.Length;
            var peaks = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (angs[i] < 50f) continue;
                bool isMax = true;
                for (int k = 1; k <= 2; k++)
                {
                    float p = closed ? angs[(((i - k) % n) + n) % n] : (i - k >= 0 ? angs[i - k] : -1f);
                    float q = closed ? angs[(i + k) % n] : (i + k < n ? angs[i + k] : -1f);
                    if (angs[i] < p || angs[i] < q)
                    {
                        isMax = false;
                        break;
                    }
                }
                if (isMax) peaks.Add(i);
            }
            var kept = new List<int>();
            foreach (int p in peaks)
            {
                bool far = true;
                foreach (int q in kept)
                {
                    int d = Math.Min((p - q + n) % n, (q - p + n) % n);
                    if (d < 8) { far = false; break; }
                }
                if (far) kept.Add(p);
            }
            return kept.Count;
        }

        // Best cyclic-shift distance for closed templates (both directions).
        static float ClosedDist(Vector2[] input, Vector2[] tpl)
        {
            int n = input.Length;
            Vector2[] rev = Reversed(tpl);
            float best = float.PositiveInfinity;
            for (int dir = 0; dir < 2; dir++)
            {
                Vector2[] t = dir == 0 ? tpl : rev;
                for (int s = 0; s < n; s++)
                {
                    float sum = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        Vector2 q = t[(i + s) % n];
                        float dx = input[i].x - q.x;
                        float dy = input[i].y - q.y;
                        sum += Mathf.Sqrt(dx * dx + dy * dy);
                        if (sum / n >= best) break; // early exit
                    }
                    float d = sum / n;
                    if (d < best) best = d;
                }
            }
            return best;
        }

        static float OpenDist(Vector2[] input, Vector2[] tpl)
        {
            return Mathf.Min(MeanDist(input, tpl), MeanDist(input, Reversed(tpl)));
        }

        // A fast line may only span three XR frames. Its interior sample distinguishes
        // observed movement from a tracking jump between two otherwise stationary poses.
        static bool HasLineEvidence(IReadOnlyList<Vector2> points, float span, float endGap)
        {
            Vector2 start = points[0];
            Vector2 end = points[points.Count - 1];
            float axisX = (end.x - start.x) / endGap;
            float axisY = (end.y - start.y) / endGap;
            float margin = Mathf.Max(0.005f, span * 0.1f);
            bool hasInterior = false;
            float backtracking = 0f;
            float previous = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                float progress = (points[i].x - start.x) * axisX + (points[i].y - start.y) * axisY;
                if (progress > margin && progress < endGap - margin) hasInterior = true;
                backtracking += Mathf.Max(0f, previous - progress);
                previous = progress;
            }
            return hasInterior && backtracking <= 0.2f * endGap;
        }
    }
}
