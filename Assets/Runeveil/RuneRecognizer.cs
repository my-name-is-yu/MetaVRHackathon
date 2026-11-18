using System;
using System.Collections.Generic;
using UnityEngine;

namespace Runeveil
{
    public enum Spell { Fire, Lightning, Ice, Rock, Wind, Barrier }

    /// <summary>Classifies a single stroke projected onto a stable, Y-up view plane in meters.</summary>
    public static class RuneRecognizer
    {
        private const int SampleCount = 64;
        private const int MinimumPoints = 8;
        private const float MinimumSpan = 0.03f;
        private const float ClosureRatio = 0.35f;
        private const float BacktrackLimit = 8f;
        private const float AmbiguityMargin = 0.03f;

        private static readonly Template[] Templates =
        {
            new Template(Spell.Fire, Circle(), 0.20f, closed: true),
            new Template(Spell.Lightning, Polyline(16,
                new Vector2(-0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f)), 0.24f),
            new Template(Spell.Ice, Polyline(16,
                new Vector2(-0.5f, 0.5f), new Vector2(0f, -0.5f), new Vector2(0.5f, 0.5f)), 0.20f),
            new Template(Spell.Rock, Polyline(21,
                new Vector2(0f, 0.5f), new Vector2(-0.5f, -0.5f),
                new Vector2(0.5f, -0.5f), new Vector2(0f, 0.5f)), 0.20f, closed: true),
            new Template(Spell.Wind, WindCurve(), 0.24f),
            new Template(Spell.Barrier, Polyline(64,
                new Vector2(-0.5f, 0f), new Vector2(0.5f, 0f)), 0.14f)
        };

        public static IReadOnlyList<Vector2> GetGlyph(Spell spell)
        {
            foreach (Template template in Templates)
            {
                if (template.Spell == spell)
                    return template.Glyph;
            }

            throw new ArgumentOutOfRangeException(nameof(spell), spell, "Unknown rune spell.");
        }

        /// <summary>
        /// Lenient circle test used for the Fire charge. Accepts wobbly, oval or slightly open
        /// circles, but rejects shapes with sharp corners (triangles, Z, V).
        /// </summary>
        public static bool IsRoughCircle(IReadOnlyList<Vector2> raw, float minSweepDegrees, float maxRadiusVariation,
            float maxCornerDegrees, float minSpan)
        {
            if (raw == null || raw.Count < MinimumPoints)
                return false;

            GetBounds(raw, out Vector2 min, out Vector2 max);
            Vector2 dimensions = max - min;
            float span = Mathf.Max(dimensions.x, dimensions.y);
            if (span < minSpan || Mathf.Min(dimensions.x, dimensions.y) < span * 0.35f)
                return false;

            const int count = 40;
            var points = Smooth(Resample(raw, count), 2);
            Vector2 center = Vector2.zero;
            foreach (Vector2 point in points) center += point;
            center /= count;

            float meanRadius = 0f;
            foreach (Vector2 point in points) meanRadius += (point - center).magnitude;
            meanRadius /= count;
            if (meanRadius < span * 0.2f)
                return false;

            float variance = 0f;
            foreach (Vector2 point in points)
            {
                float d = (point - center).magnitude - meanRadius;
                variance += d * d;
            }
            if (Mathf.Sqrt(variance / count) / meanRadius > maxRadiusVariation)
                return false;

            float sweep = 0f;
            for (int i = 1; i < count; i++)
                sweep += Vector2.SignedAngle(points[i - 1] - center, points[i] - center);
            if (Mathf.Abs(sweep) < minSweepDegrees)
                return false;

            // Corners: turning measured over three segments so hand jitter does not count as a corner.
            const int reach = 3;
            for (int i = reach; i < count - reach; i++)
            {
                Vector2 before = points[i] - points[i - reach];
                Vector2 after = points[i + reach] - points[i];
                if (before.sqrMagnitude < 1e-10f || after.sqrMagnitude < 1e-10f) continue;
                if (Mathf.Abs(Vector2.SignedAngle(before, after)) > maxCornerDegrees)
                    return false;
            }
            return true;
        }

        private static List<Vector2> Smooth(List<Vector2> points, int radius)
        {
            var result = new List<Vector2>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                int from = Mathf.Max(0, i - radius);
                int to = Mathf.Min(points.Count - 1, i + radius);
                Vector2 sum = Vector2.zero;
                for (int j = from; j <= to; j++) sum += points[j];
                result.Add(sum / (to - from + 1));
            }
            return result;
        }

        private static List<Vector2> Resample(IReadOnlyList<Vector2> raw, int count)
        {
            var result = new List<Vector2>(count);
            float total = (float)PathLength(raw);
            float interval = total / (count - 1);
            float carried = 0f;
            Vector2 previous = raw[0];
            result.Add(previous);
            for (int i = 1; i < raw.Count && result.Count < count; i++)
            {
                Vector2 current = raw[i];
                float segment = Vector2.Distance(previous, current);
                while (segment > 0f && carried + segment >= interval && result.Count < count)
                {
                    float t = (interval - carried) / segment;
                    previous = Vector2.Lerp(previous, current, t);
                    result.Add(previous);
                    segment = Vector2.Distance(previous, current);
                    carried = 0f;
                }
                carried += segment;
                previous = current;
            }
            while (result.Count < count) result.Add(raw[raw.Count - 1]);
            return result;
        }

        public static Spell? MatchRune(IReadOnlyList<Vector2> raw)
        {
            if (raw == null || raw.Count < 3)
                return null;

            for (int i = 0; i < raw.Count; i++)
            {
                Vector2 point = raw[i];
                if (float.IsNaN(point.x) || float.IsInfinity(point.x) ||
                    float.IsNaN(point.y) || float.IsInfinity(point.y))
                    return null;
            }

            GetBounds(raw, out Vector2 min, out Vector2 max);
            Vector2 dimensions = max - min;
            float span = Mathf.Max(dimensions.x, dimensions.y);
            if (span < MinimumSpan)
                return null;

            double length = PathLength(raw);
            if (length <= 0 || double.IsInfinity(length) || length / span > BacktrackLimit)
                return null;

            Vector2 chord = raw[raw.Count - 1] - raw[0];
            float endGap = chord.magnitude;
            float endGapRatio = endGap / span;
            bool closed = endGapRatio < ClosureRatio;

            GetChordDeviations(raw, chord, endGap, out float minDeviation, out float maxDeviation);
            float maxDistanceFromChord = Mathf.Max(-minDeviation, maxDeviation);
            bool barrierShape = dimensions.x >= 3f * dimensions.y && endGapRatio > 0.7f &&
                maxDistanceFromChord / span <= 0.12f && HasLineEvidence(raw, chord / endGap, span, endGap);

            // Fast barrier strokes can span just three frames, including one interior sample.
            if (raw.Count < MinimumPoints)
                return barrierShape ? Spell.Barrier : (Spell?)null;

            Vector2[] sampled = Resample(raw, length);
            bool windShape = endGapRatio > 0.7f && maxDeviation / span > 0.07f &&
                minDeviation / span < -0.07f && !HasSharpTurn(sampled, closed);

            // Four or more strong corners indicate a polygon like a square or diamond.
            if (closed && HasFourCorners(sampled))
                return null;

            Normalize(sampled);
            Template best = null;
            float bestDistance = float.PositiveInfinity;
            float secondDistance = float.PositiveInfinity;
            foreach (Template template in Templates)
            {
                if (template.Closed != closed ||
                    (template.Spell == Spell.Barrier && !barrierShape) ||
                    (template.Spell == Spell.Wind && !windShape))
                    continue;

                float distance = TemplateDistance(sampled, template);
                if (distance < bestDistance)
                {
                    secondDistance = bestDistance;
                    bestDistance = distance;
                    best = template;
                }
                else if (distance < secondDistance)
                {
                    secondDistance = distance;
                }
            }

            if (best == null || bestDistance > best.AcceptDistance || secondDistance - bestDistance < AmbiguityMargin)
                return null;

            return best.Spell;
        }

        private static void GetChordDeviations(IReadOnlyList<Vector2> points, Vector2 chord, float endGap,
            out float minimum, out float maximum)
        {
            minimum = 0f;
            maximum = 0f;
            if (endGap <= 1e-12f)
                return;

            Vector2 perpendicular = new Vector2(-chord.y, chord.x) / endGap;
            Vector2 origin = points[0];
            for (int i = 1; i < points.Count; i++)
            {
                float signedDistance = Vector2.Dot(points[i] - origin, perpendicular);
                minimum = Mathf.Min(minimum, signedDistance);
                maximum = Mathf.Max(maximum, signedDistance);
            }
        }

        private static bool HasLineEvidence(IReadOnlyList<Vector2> points, Vector2 axis, float span, float endGap)
        {
            Vector2 origin = points[0];
            float margin = Mathf.Max(0.005f, span * 0.1f);
            bool hasInterior = false;
            float backtracking = 0f;
            float previous = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                float progress = Vector2.Dot(points[i] - origin, axis);
                hasInterior |= progress > margin && progress < endGap - margin;
                backtracking += Mathf.Max(0f, previous - progress);
                previous = progress;
            }

            return hasInterior && backtracking <= 0.2f * endGap;
        }

        private static void GetBounds(IReadOnlyList<Vector2> points, out Vector2 minimum, out Vector2 maximum)
        {
            minimum = points[0];
            maximum = points[0];
            for (int i = 1; i < points.Count; i++)
            {
                minimum = Vector2.Min(minimum, points[i]);
                maximum = Vector2.Max(maximum, points[i]);
            }
        }

        private static double PathLength(IReadOnlyList<Vector2> points)
        {
            double length = 0;
            for (int i = 1; i < points.Count; i++)
                length += Vector2.Distance(points[i - 1], points[i]);

            return length;
        }

        private static Vector2[] Resample(IReadOnlyList<Vector2> points, double length)
        {
            var samples = new Vector2[SampleCount];
            samples[0] = points[0];
            samples[SampleCount - 1] = points[points.Count - 1];
            double step = length / (SampleCount - 1);
            double traversed = 0;
            int next = 1;

            for (int i = 1; i < points.Count && next < SampleCount - 1; i++)
            {
                Vector2 start = points[i - 1];
                Vector2 end = points[i];
                float segment = Vector2.Distance(start, end);
                if (segment == 0f)
                    continue;

                double segmentEnd = traversed + segment;
                while (next < SampleCount - 1 && next * step <= segmentEnd)
                {
                    float ratio = (float)((next * step - traversed) / segment);
                    samples[next] = Vector2.LerpUnclamped(start, end, ratio);
                    next++;
                }
                traversed = segmentEnd;
            }

            return samples;
        }

        private static void Normalize(Vector2[] points)
        {
            GetBounds(points, out Vector2 min, out Vector2 max);
            Vector2 dimensions = max - min;
            float span = Mathf.Max(dimensions.x, dimensions.y);

            Vector2 center = Vector2.zero;
            foreach (Vector2 point in points)
                center += point;
            center /= points.Length;

            for (int i = 0; i < points.Length; i++)
                points[i] = (points[i] - center) / span;
        }

        private static bool HasSharpTurn(Vector2[] points, bool closed)
        {
            foreach (float angle in TurningAngles(points, closed, 2))
            {
                if (angle >= 60f)
                    return true;
            }

            return false;
        }

        private static bool HasFourCorners(Vector2[] points)
        {
            float[] angles = TurningAngles(points, true, 4);
            var corners = new List<int>(4);
            for (int i = 0; i < angles.Length; i++)
            {
                if (angles[i] < 50f || angles[i] < angles[Wrap(i - 2)] || angles[i] < angles[Wrap(i - 1)] ||
                    angles[i] < angles[Wrap(i + 1)] || angles[i] < angles[Wrap(i + 2)])
                    continue;

                bool separated = true;
                foreach (int corner in corners)
                {
                    int separation = i - corner;
                    separated &= Mathf.Min(separation, SampleCount - separation) >= 8;
                }

                if (separated)
                    corners.Add(i);
                if (corners.Count >= 4)
                    return true;
            }

            return false;
        }

        private static float[] TurningAngles(Vector2[] points, bool closed, int window)
        {
            // Three-point smoothing removes tracking jitter before measuring turns.
            var smooth = new Vector2[SampleCount];
            for (int i = 1; i < SampleCount - 1; i++)
                smooth[i] = (points[i - 1] + points[i] + points[i + 1]) / 3f;

            int last = SampleCount - 1;
            smooth[0] = closed ? (points[last] + points[0] + points[1]) / 3f : (points[0] + points[1]) / 2f;
            smooth[last] = closed ? (points[last - 1] + points[last] + points[0]) / 3f :
                (points[last - 1] + points[last]) / 2f;

            var angles = new float[SampleCount];
            for (int i = 0; i < SampleCount; i++)
            {
                int before = closed ? Wrap(i - window) : Mathf.Max(0, i - window);
                int after = closed ? Wrap(i + window) : Mathf.Min(last, i + window);
                Vector2 incoming = smooth[i] - smooth[before];
                Vector2 outgoing = smooth[after] - smooth[i];
                if (incoming.sqrMagnitude <= 1e-24f || outgoing.sqrMagnitude <= 1e-24f)
                    continue;

                float cross = incoming.x * outgoing.y - incoming.y * outgoing.x;
                angles[i] = Mathf.Abs(Mathf.Atan2(cross, Vector2.Dot(incoming, outgoing))) * Mathf.Rad2Deg;
            }

            return angles;
        }

        private static int Wrap(int index) => (index + SampleCount) % SampleCount;

        private static float TemplateDistance(Vector2[] input, Template template)
        {
            // Closed runes accept any starting point. All six accept either drawing direction.
            int offsets = template.Closed ? SampleCount : 1;
            float best = float.PositiveInfinity;
            for (int offset = 0; offset < offsets; offset++)
            {
                best = Mathf.Min(best, DistanceAtOffset(input, template.Samples, offset, false, best));
                best = Mathf.Min(best, DistanceAtOffset(input, template.Samples, offset, true, best));
            }

            return best / SampleCount;
        }

        private static float DistanceAtOffset(Vector2[] input, Vector2[] reference, int offset, bool reversed, float best)
        {
            float sum = 0f;
            for (int i = 0; i < SampleCount && sum < best; i++)
            {
                int index = Wrap(i + offset);
                if (reversed)
                    index = SampleCount - 1 - index;
                sum += Vector2.Distance(input[i], reference[index]);
            }

            return sum;
        }

        private static Vector2[] Circle()
        {
            var points = new Vector2[65];
            for (int i = 0; i < points.Length - 1; i++)
            {
                float angle = 2f * Mathf.PI * i / (points.Length - 1);
                points[i] = 0.5f * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
            points[points.Length - 1] = points[0];
            return points;
        }

        private static Vector2[] Polyline(int segmentSamples, params Vector2[] corners)
        {
            var points = new Vector2[(corners.Length - 1) * segmentSamples + 1];
            for (int segment = 0; segment < corners.Length - 1; segment++)
            {
                for (int i = 0; i < segmentSamples; i++)
                {
                    points[segment * segmentSamples + i] = Vector2.LerpUnclamped(
                        corners[segment], corners[segment + 1], (float)i / segmentSamples);
                }
            }

            points[points.Length - 1] = corners[corners.Length - 1];
            return points;
        }

        private static Vector2[] WindCurve()
        {
            var points = new List<Vector2>(97);
            var start = new Vector2(0.42f, 0.34f);
            var top = new Vector2(-0.02f, 0.52f);
            var left = new Vector2(-0.34f, 0.10f);
            var right = new Vector2(0.34f, -0.10f);
            var bottom = new Vector2(0.02f, -0.52f);
            points.Add(start);
            AddBezier(points, start, new Vector2(0.33f, 0.52f), new Vector2(0.20f, 0.53f), top, 21);
            AddBezier(points, top, new Vector2(-0.24f, 0.52f), new Vector2(-0.58f, 0.17f), left, 21);
            AddBezier(points, left, new Vector2(-0.10f, 0.03f), new Vector2(0.10f, -0.03f), right, 12);
            AddBezier(points, right, new Vector2(0.58f, -0.17f), new Vector2(0.24f, -0.52f), bottom, 21);
            AddBezier(points, bottom, new Vector2(-0.20f, -0.53f), new Vector2(-0.33f, -0.52f),
                new Vector2(-0.42f, -0.34f), 21);

            GetBounds(points, out Vector2 min, out Vector2 max);
            Vector2 center = (min + max) * 0.5f;
            float span = Mathf.Max(max.x - min.x, max.y - min.y);
            var glyph = new Vector2[points.Count];
            for (int i = 0; i < points.Count; i++)
                glyph[i] = (points[i] - center) / span;

            return glyph;
        }

        private static void AddBezier(List<Vector2> points, Vector2 start, Vector2 firstControl,
            Vector2 secondControl, Vector2 end, int segments)
        {
            for (int i = 1; i <= segments; i++)
            {
                float t = (float)i / segments;
                float u = 1f - t;
                points.Add(u * u * u * start + 3f * u * u * t * firstControl +
                    3f * u * t * t * secondControl + t * t * t * end);
            }
        }

        private sealed class Template
        {
            public readonly Spell Spell;
            public readonly IReadOnlyList<Vector2> Glyph;
            public readonly Vector2[] Samples;
            public readonly float AcceptDistance;
            public readonly bool Closed;

            public Template(Spell spell, Vector2[] glyph, float acceptDistance, bool closed = false)
            {
                Spell = spell;
                Glyph = Array.AsReadOnly(glyph);
                AcceptDistance = acceptDistance;
                Closed = closed;
                Samples = Resample(glyph, PathLength(glyph));
                Normalize(Samples);
            }
        }
    }
}
