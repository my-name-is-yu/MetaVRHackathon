using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Runeveil
{
    public sealed partial class RuneveilArena
    {
        private const float MuseAtlasSize = 1254f;
        private const float MuseScale = 0.00340f;
        private const float MuseStrikeSeconds = 0.48f;
        private const float MuseHeavyStrikeSeconds = 0.78f;
        private Transform museBody;
        private Transform museHead;
        private float coil;
        private bool heavyImpact;
        private Transform thornWind;
        private MeshRenderer thornRenderer;
        public Transform MuseEyes { get; private set; }

        private static Vector3 MusePoint(Vector2 pixel, float depth = 0f) =>
            new((pixel.x - 627f) * MuseScale, (1200f - pixel.y) * MuseScale, depth);

        private static Vector2 OtherSide(Vector2 pixel) => new(MuseAtlasSize - pixel.x, pixel.y);

        private void BuildMuse(Shader arenaShader)
        {
            var contact = new Geometry("Muse contact shadows");
            contact.Disc(new(0f, 0.02f, BossDepth), Vector3.right, Vector3.forward, 4.55f, 3.12f, 0x311d31, 165);
            for (int side = -1; side <= 1; side += 2)
                contact.Disc(new(side * 0.51f, 0.024f, BossDepth - 0.12f), Vector3.right, Vector3.forward,
                    1.36f, 1.31f, 0x291628, 195);
            Render(stage, contact, shadow);

            boss = Group(stage, "Dark Muse");
            if (!TryBuildMuseRig(arenaShader))
                BuildFallbackMuse(arenaShader);

            var warning = new Geometry("Warning arcs on Muse's raised paw");
            warning.Tube(Arc(Vector3.zero, 0.53f, 0.55f, 105f, 246f, 23), 0.018f, 0xffb767);
            warning.Tube(Arc(Vector3.back * 0.022f, 0.62f, 0.64f, 113f, 254f, 24), 0.009f, 0xffebac);
            warningArcs = Render(museBody != null ? museBody : boss, warning, glow);
        }

        private void BuildFallbackMuse(Shader arenaShader)
        {
            museBody = Group(boss, "Muse upper body");
            museHead = Group(museBody, "Muse head pivot");
            museHead.localPosition = new Vector3(0f, 2.13f, 0f);
            Transform headArtwork = Group(museHead, "Muse head artwork");
            headArtwork.localPosition = -museHead.localPosition;
            Shader surface = Resources.Load<Shader>("Runeveil/MuseSurface");
            Texture2D back = Resources.Load<Texture2D>("Runeveil/MuseBack");
            Material Plush(string label, string front)
            {
                var paint = Track(new Material(surface) { name = label });
                paint.SetTexture("_Front", Resources.Load<Texture2D>(front));
                paint.SetTexture("_Back", back);
                return paint;
            }
            Material plush = Plush("Muse front and back study", "Runeveil/MuseFront");
            Material paw = Plush("Muse clenched paws", "Runeveil/MuseAttack");
            Material face = Track(new Material(plush) { name = "Muse face" });
            ConfigureMuseCleansing(plush, paw, face);

            MuseForm(museBody, plush, "Round plum plush torso", new(627, 853), new Vector2[] {
                new(625, 548), new(742, 560), new(824, 578), new(881, 602), new(914, 653), new(922, 722),
                new(936, 772), new(952, 827), new(950, 910), new(936, 970), new(908, 1026), new(853, 1067),
                new(779, 1086), new(698, 1090), new(631, 1095), new(553, 1093), new(467, 1089), new(397, 1069),
                new(345, 1030), new(310, 972), new(294, 910), new(293, 840), new(306, 783), new(327, 730),
                new(335, 661), new(351, 609), new(423, 572), new(512, 549)
            }, p => MusePoint(p, 0.043f), new(0.865f, 0.777f), faces: 24);

            MuseForm(headArtwork, face, "Cream face and swept plum head", new(625, 417), new Vector2[] {
                new(617, 166), new(677, 166), new(734, 178), new(786, 216), new(835, 256), new(878, 306),
                new(908, 349), new(933, 391), new(934, 432), new(951, 477), new(940, 509), new(931, 557),
                new(908, 596), new(874, 620), new(808, 637), new(740, 647), new(669, 652), new(616, 658),
                new(551, 655), new(489, 643), new(424, 633), new(374, 617), new(335, 590), new(313, 549),
                new(300, 509), new(299, 476), new(313, 425), new(306, 395), new(330, 352), new(356, 316),
                new(389, 281), new(431, 240), new(488, 196), new(543, 176)
            }, p => MusePoint(p, -0.045f), new(0.88f, 0.76f),
                relief: (p, front) => front
                    ? 0.067f * MuseBump(p, new(638, 284), new(77, 61)) + 0.022f * MuseBump(p, new(629, 515), new(183, 70))
                    : 0.103f * MuseBump(p, new(625, 322), new(81, 69)),
                faces: 28);
            MuseEyes = Group(headArtwork, "Muse eye midpoint");
            MuseEyes.localPosition = MusePoint(new(628, 418), -0.93f);
            for (int side = -1; side <= 1; side += 2)
            {
                Transform eye = Group(MuseEyes, side < 0 ? "Left glowing eye" : "Right glowing eye");
                eye.localPosition = MusePoint(new(628 + side * 96, 425), -0.887f) - MuseEyes.localPosition;
                BuildMuseLeg(plush, side);
                BuildMuseHorn(headArtwork, plush, side);
            }

            BuildMuseGem();
            attackArm = BuildMuseArm(plush, paw, 1);
            anchorArm = BuildMuseArm(plush, paw, -1);
            AddWeakGem(ArenaTarget.RightHand, anchorArm.Fist, "Right hand crystal", new Vector3(0f, 0f, -0.39f));
            AddWeakGem(ArenaTarget.RaisedFist, attackArm.Fist, "Raised fist crystal", new Vector3(0f, 0.02f, -0.47f));
            BuildThornWind(arenaShader);
        }

        private void PoseMuse(bool rising, float thrust, bool heavyStrike, bool wardReleaseWindow)
        {
            float raised = lift * (1f - thrust);
            float heavyRaise = heavyStrike ? raised : 0f;
            float heavyThrust = heavyStrike ? thrust : 0f;
            float idle = 1f - down;
            var bodyOffset = new Vector3(-0.13f * coil - 0.07f * raised + 0.10f * thrust,
                Mathf.Sin(age * 1.2f) * 0.019f * idle - 0.23f * coil - 0.07f * thrust - 0.13f * heavyThrust,
                -0.12f * thrust);
            var bodyEuler = new Vector3(4f * raised - 7f * thrust - 11f * heavyThrust,
                1.45f * Mathf.Sin(age * 0.67f) * idle - 6f * raised - 6f * heavyRaise + 12f * thrust,
                2.8f + 0.86f * Mathf.Sin(age * 0.91f) * idle + 8f * coil + 3f * raised - 6f * thrust);
            var bodyScale = new Vector3(1f + 0.06f * coil, 1f - 0.055f * coil - 0.05f * heavyThrust, 1f);
            var headEuler = new Vector3(-6f * coil - 3f * raised + 5f * thrust, 3f * raised, -3f * raised);
            ApplyMuseBodyReaction(ref bodyOffset, ref bodyEuler, ref bodyScale, ref headEuler);
            museBody.SetLocalPositionAndRotation(bodyOffset, Quaternion.Euler(bodyEuler));
            museBody.localScale = bodyScale;
            museHead.localRotation = Quaternion.Euler(headEuler);

            Vector3 attackElbow = new(1.24f + 0.19f * raised - 0.08f * thrust,
                1.98f + 0.57f * raised + 0.18f * heavyRaise - 0.19f * coil - 0.40f * thrust,
                -0.05f + 0.12f * raised - 0.46f * thrust);
            Vector3 attackWrist = new(1.45f + 0.26f * raised - 0.26f * thrust,
                2.39f + 0.94f * raised + 0.39f * heavyRaise - 0.61f * coil - 0.91f * thrust - 0.33f * heavyThrust,
                -0.42f + 0.15f * raised - 0.72f * thrust + 0.13f * heavyThrust);
            Vector3 attackFist = new(6f + 11f * raised - 42f * thrust - 12f * heavyThrust,
                -8.5f - 14f * thrust, -12f + 10f * raised + 30f * thrust);
            ApplyMuseArmReaction(true, ref attackElbow, ref attackWrist, ref attackFist);
            attackArm.Pose(attackElbow, attackWrist, attackFist);

            Vector3 anchorElbow = new(-1.11f - 0.05f * coil, 1.39f + 0.11f * raised, -0.12f + 0.12f * thrust);
            Vector3 anchorWrist = new(-1.34f - 0.13f * coil, 1.19f + 0.14f * raised - 0.16f * heavyThrust,
                -0.61f - 0.08f * raised + 0.21f * thrust);
            Vector3 anchorFist = new(6f * raised, 11f + 8f * thrust, -12f);
            ApplyMuseArmReaction(false, ref anchorElbow, ref anchorWrist, ref anchorFist);
            anchorArm.Pose(anchorElbow, anchorWrist, anchorFist);

            bool showWarning = rising && defeat < 0.13f && (!heavyStrike || raised >= 0.16f);
            warningArcs.gameObject.SetActive(showWarning);
            if (showWarning)
            {
                Transform arcs = warningArcs.transform;
                arcs.localPosition = attackArm.Fist.localPosition + Vector3.back * 0.30f;
                arcs.localRotation = Quaternion.Euler(0f, 10f, 17f + 24f * lift);
                Color strikeColor = heavyStrike ? Rgb(0xff3932) : Color.white;
                Tint(warningArcs, strikeColor * (wardReleaseWindow ? 1.65f : 1f), 1f);
            }
            // 怒り状態では棘の帯を赤く染め、大きく揺らす
            thornWind.localRotation = Quaternion.Euler(0f, (11f + 10f * rage) * Mathf.Sin(age * 0.72f) + lift * 13f,
                (2f + 3f * rage) * Mathf.Sin(age));
            thornWind.localPosition = Vector3.up * (0.061f * Mathf.Sin(age * 1.7f) + lift * 0.07f);
            Tint(thornRenderer, Color.Lerp(Color.white, Rgb(0xff4a4a), rage), 1f);
        }

        private void BuildMuseLeg(Material plush, int side)
        {
            Vector2 Mirror(Vector2 p) => side > 0 ? p : OtherSide(p);
            var outline = new Vector2[] {
                new(704, 990), new(800, 984), new(880, 1000), new(879, 1086), new(868, 1154), new(854, 1187),
                new(809, 1198), new(744, 1199), new(697, 1191), new(674, 1177), new(663, 1140), new(652, 1086),
                new(649, 1035)
            };
            MuseForm(boss, plush, side > 0 ? "Right stuffed foot" : "Left stuffed foot", new(768, 1104), outline,
                p => MusePoint(Mirror(p), 0.025f), new(0.62f, 0.52f), frontPixel: Mirror, backPixel: p => OtherSide(Mirror(p)));
        }

        private void BuildMuseHorn(Transform headArtwork, Material plush, int side)
        {
            Vector2 Mirror(Vector2 p) => side < 0 ? p : OtherSide(p);
            var outline = new Vector2[] {
                new(317, 232), new(330, 184), new(351, 142), new(383, 106), new(423, 79), new(456, 70),
                new(489, 73), new(507, 85), new(503, 101), new(483, 122), new(475, 148), new(477, 170),
                new(493, 191), new(515, 208), new(496, 231), new(459, 263), new(424, 301), new(393, 321),
                new(367, 310), new(341, 280)
            };
            // Left and right are named from the Muse's perspective.
            ArenaTarget target = side < 0 ? ArenaTarget.RightHorn : ArenaTarget.LeftHorn;
            MuseForm(headArtwork, plush, target.DisplayName() + " (cracked crystal horn)",
                new(407, 209), outline, p => MusePoint(Mirror(p), -0.07f), new(0.47f, 0.43f),
                frontPixel: Mirror, backPixel: p => OtherSide(Mirror(p)),
                relief: (p, _) => 0.035f * MuseBump(p, new(386, 206), new(37, 75)), faces: 12, outlineSteps: 1, target: target);
            AddWeakGem(target, headArtwork, target.DisplayName() + " crystal", MusePoint(Mirror(new(407, 209)), -0.56f));
        }

        private void BuildMuseGem()
        {
            Core = Group(museBody, "Pink chest crystal");
            Core.localPosition = MusePoint(new(632, 783), -0.822f);
            BuildWeakGem(ArenaTarget.Core, Core, "Faceted rose crystal", 1f);
        }

        private void AddWeakGem(ArenaTarget target, Transform parent, string label, Vector3 localPosition)
        {
            Transform anchor = Group(parent, label);
            anchor.localPosition = localPosition;
            BuildWeakGem(target, anchor, label, 0.7f);
        }

        private void BuildWeakGem(ArenaTarget target, Transform anchor, string label, float scale)
        {
            int index = target.WeakPointIndex();
            weakAnchors[index] = anchor;
            Mesh mesh = Track(MuseGemGeometry(label, scale).CreateMesh(keepReadable: true));
            weakGems[index] = Render(anchor, label, mesh, vivid);
            hitSurfaces.Add(new HitSurface(weakGems[index].transform, mesh.vertices, mesh.triangles, target));
            mesh.UploadMeshData(true);
            weakHalos[index] = Render(anchor, label + " radiance", softDisc, softGlow);
            weakHalos[index].transform.localPosition = Vector3.back * 0.1f;
        }

        private static Geometry MuseGemGeometry(string label, float scale)
        {
            var gem = new Geometry(label);
            var boundary = new Vector3[] {
                new(0, 0.251f, 0.02f), new(0.128f, 0.081f, 0.012f), new(0.128f, -0.106f, 0.013f),
                new(0.013f, -0.243f, 0.018f), new(-0.127f, -0.1f, 0.013f), new(-0.127f, 0.075f, 0.012f)
            };
            var crown = new Vector3[] {
                new(0, 0.095f, -0.107f), new(0.062f, 0.048f, -0.116f), new(0.058f, -0.063f, -0.117f),
                new(0.002f, -0.134f, -0.112f), new(-0.068f, -0.065f, -0.102f), new(-0.068f, 0.048f, -0.097f)
            };
            int[] facetColors = { 0xffa2d6, 0xef4e9b, 0xff8fc4, 0xf37cbb, 0xff61a0, 0xffe1f3 };
            Vector3 tip = new Vector3(0f, -0.009f, -0.122f) * scale;
            for (int i = 0; i < boundary.Length; i++)
            {
                int next = (i + 1) % boundary.Length;
                gem.Quad(boundary[i] * scale, boundary[next] * scale, crown[next] * scale, crown[i] * scale, facetColors[i]);
                gem.Quad(crown[i] * scale, crown[next] * scale, tip, tip, 0xf72b85);
                gem.Tube(new[] { (boundary[i] + Vector3.back * 0.002f) * scale, (boundary[next] + Vector3.back * 0.002f) * scale },
                    0.003f * scale, 0xffc5e5, 4);
            }
            return gem;
        }

        private Arm BuildMuseArm(Material plush, Material paw, int side)
        {
            bool raised = side > 0;
            var arm = new Arm(new Vector3(side * 0.92f, 1.72f, 0.04f),
                Group(museBody, raised ? "Raised upper arm" : "Resting upper arm"),
                Group(museBody, raised ? "Raised forearm" : "Resting forearm"),
                Group(museBody, raised ? "Raised plush fist" : "Resting plush fist"));
            void Segment(Transform part, string name, Vector2 center, Vector2[] outline)
            {
                MuseForm(part, plush, name, center, outline, p =>
                    new Vector3((p.y - center.y) * MuseScale * side, (p.x - center.x) * MuseScale, 0f),
                    new(0.335f, 0.285f), frontPixel: raised ? null : OtherSide,
                    backPixel: raised ? OtherSide : p => p);
            }
            Segment(arm.Upper, "Plush shoulder with curled stone trim", new(919, 680), new Vector2[] {
                new(809, 678), new(829, 630), new(867, 603), new(939, 600), new(997, 610),
                new(1017, 655), new(1017, 709), new(987, 754), new(925, 774), new(853, 759), new(821, 729)
            });
            Segment(arm.Lower, "Plush forearm with bright pink fracture", new(1031, 685), new Vector2[] {
                new(938, 682), new(958, 629), new(996, 604), new(1051, 601), new(1106, 612),
                new(1124, 660), new(1118, 716), new(1093, 760), new(1036, 768), new(984, 758), new(950, 723)
            });
            Vector2 middle = raised ? new(989, 278) : new(193, 803);
            Vector2[] outline = raised ? new Vector2[] {
                new(966, 155), new(1017, 160), new(1063, 180), new(1100, 214), new(1126, 245), new(1138, 291),
                new(1121, 339), new(1090, 380), new(1041, 396), new(1003, 391), new(951, 368), new(903, 345),
                new(868, 326), new(853, 289), new(864, 244), new(893, 197), new(923, 168)
            } : new Vector2[] {
                new(184, 681), new(228, 685), new(266, 711), new(282, 748), new(296, 788), new(290, 837),
                new(268, 879), new(240, 914), new(198, 925), new(149, 910), new(116, 875), new(96, 833),
                new(89, 789), new(100, 741), new(136, 703)
            };
            float scale = raised ? 0.00348f : 0.00344f;
            MuseForm(arm.Fist, paw, raised ? "Raised clenched knuckles" : "Resting clenched knuckles", middle, outline,
                p => new Vector3((p.x - middle.x) * scale, (middle.y - p.y) * scale, 0f),
                raised ? new(0.455f, 0.365f) : new(0.366f, 0.285f),
                backPixel: p => new Vector2((raised ? 101f : 1158f) - (p.x - middle.x) * 0.42f,
                    683f + (p.y - middle.y) * 0.54f),
                relief: (p, front) => front && raised
                    ? 0.038f * Mathf.Cos((p.x + p.y * 0.44f) * 0.088f) * MuseBump(p, new(1023, 252), new(95, 82))
                    : 0f,
                faces: 20, target: raised ? ArenaTarget.RaisedFist : ArenaTarget.RightHand);
            return arm;
        }

        private static float MuseBump(Vector2 pixel, Vector2 center, Vector2 extent)
        {
            Vector2 delta = pixel - center;
            return Mathf.Exp(-(delta.x * delta.x / (extent.x * extent.x) + delta.y * delta.y / (extent.y * extent.y)));
        }

        private void MuseForm(Transform parent, Material material, string label, Vector2 center, Vector2[] outline,
            Func<Vector2, Vector3> plane, Vector2 depth, Func<Vector2, Vector2> frontPixel = null,
            Func<Vector2, Vector2> backPixel = null, Func<Vector2, bool, float> relief = null, int faces = 14,
            int outlineSteps = 2, ArenaTarget target = ArenaTarget.Body)
        {
            int sides = outline.Length * outlineSteps;
            var rim = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                int last = i / outlineSteps;
                rim[i] = Catmull(outline[(last + outline.Length - 1) % outline.Length], outline[last],
                    outline[(last + 1) % outline.Length], outline[(last + 2) % outline.Length], (i % outlineSteps) / (float)outlineSteps);
            }
            var points = new Vector3[(faces + 1) * sides];
            var frontUvs = new Vector2[points.Length];
            var backUvs = new Vector2[points.Length];
            var weights = new Color32[points.Length];
            static Vector2 Uv(Vector2 p) => new(p.x / MuseAtlasSize, 1f - p.y / MuseAtlasSize);
            for (int latitude = 0; latitude <= faces; latitude++)
            {
                float angle = Mathf.PI * latitude / faces;
                float radial = Mathf.Sin(angle);
                float height = Mathf.Cos(angle);
                bool front = height >= 0f;
                float backWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.33f, -0.33f, height));
                for (int side = 0; side < sides; side++)
                {
                    int i = latitude * sides + side;
                    Vector2 pixel = Vector2.LerpUnclamped(center, rim[side], radial);
                    float sculpt = relief?.Invoke(pixel, front) ?? 0f;
                    points[i] = plane(pixel) + Vector3.back * (height * ((front ? depth.x : depth.y) + sculpt));
                    frontUvs[i] = Uv(frontPixel?.Invoke(pixel) ?? pixel);
                    backUvs[i] = Uv(backPixel?.Invoke(pixel) ?? OtherSide(pixel));
                    weights[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * backWeight));
                }
            }

            Vector3 normal = Vector3.Cross(points[sides] - points[0], points[sides + 1] - points[0]);
            bool reverse = Vector3.Dot(normal, Vector3.back) < 0f;
            var triangles = new List<int>(faces * sides * 6);
            for (int latitude = 0; latitude < faces; latitude++)
            {
                for (int side = 0; side < sides; side++)
                {
                    int a = latitude * sides + side;
                    int b = (latitude + 1) * sides + side;
                    int d = latitude * sides + (side + 1) % sides;
                    int c = d + sides;
                    triangles.Add(a);
                    triangles.Add(reverse ? c : b);
                    triangles.Add(reverse ? b : c);
                    triangles.Add(a);
                    triangles.Add(reverse ? d : c);
                    triangles.Add(reverse ? c : d);
                }
            }
            var mesh = Track(new Mesh { name = label, vertices = points, uv = frontUvs, uv2 = backUvs, colors32 = weights });
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            var renderer = Render(parent, label, mesh, material);
            hitSurfaces.Add(new HitSurface(renderer.transform, points, triangles, target));
            mesh.UploadMeshData(true);
        }

        private static Vector2 Catmull(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t) =>
            0.5f * ((2f * b) + (-a + c) * t + (2f * a - 5f * b + 4f * c - d) * (t * t) +
                (-a + 3f * b - 3f * c + d) * (t * t * t));

        private void BuildThornWind(Shader shader)
        {
            thornWind = Group(museBody, "Magenta thorn wisps");
            Material streak = CreateMaterial(shader, "Magenta wisp surface", 0.12f, 0f);
            streak.SetFloat("_Cull", (float)CullMode.Off);
            var ribbons = new Geometry("Pointed magenta wisps and floating fragments");
            void Curl(float from, float sweep, float width, float baseY, float rise, float horizontal, float depth)
            {
                const int steps = 35;
                Vector3 Vertex(int i, float edge)
                {
                    float t = i / (float)steps;
                    float a = (from + t * sweep) * Mathf.Deg2Rad;
                    float fan = Mathf.Sin(t * Mathf.PI) * Mathf.Lerp(0.42f, 1.2f, t);
                    float barbs = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI * 7f)), 3f) * 0.78f;
                    float y = baseY + t * rise + 0.135f * Mathf.Sin(a * 2f + 0.22f);
                    Vector3 center = new(horizontal * Mathf.Sin(a), y, depth * Mathf.Cos(a));
                    Vector3 flare = new(0.27f * Mathf.Sin(a), 0.92f, 0.27f * Mathf.Cos(a));
                    return center + flare * (edge * width * fan * (1f + (edge > 0f ? barbs : 0f)));
                }
                for (int i = 0; i < steps; i++)
                {
                    float t = (i + 0.5f) / steps;
                    float light = Mathf.Max(0f, Mathf.Sin(t * Mathf.PI * 3.4f + 0.6f));
                    int rim = ColorInt(Color.Lerp(Rgb(0x8d245b), Rgb(0xf564ad), light));
                    ribbons.Quad(Vertex(i, -1f), Vertex(i, -0.55f), Vertex(i + 1, -0.55f), Vertex(i + 1, -1f), rim);
                    ribbons.Quad(Vertex(i, -0.55f), Vertex(i, 1f), Vertex(i + 1, 1f), Vertex(i + 1, -0.55f),
                        ColorInt(Color.Lerp(Rgb(0x491d3d), Rgb(0x9f336f), light)));
                }
            }
            Curl(219f, 109f, 0.19f, 0.66f, 0.90f, 1.52f, 1.01f);
            Curl(161f, -107f, 0.16f, 0.69f, 0.91f, 1.54f, 1.00f);
            Curl(26f, -129f, 0.19f, 3.38f, -0.91f, 1.42f, 0.78f);
            for (int i = 0; i < 8; i++)
            {
                float side = i % 2 == 0 ? -1 : 1;
                Vector3 center = new(side * (1.41f + 0.35f * Mathf.Sin(i * 1.24f)),
                    0.47f + 0.405f * i, 0.22f - 0.50f * Mathf.Cos(i * 3.2f));
                ribbons.Profile(center, new(0.082f, 0.141f, 0.069f), Quaternion.Euler(i * 21f, i * 29f, -16f * i),
                    i % 3 == 0 ? 0xb43a7e : 0x7e3263, Geometry.GemProfile, 5);
            }
            thornRenderer = Render(thornWind, ribbons, streak);
        }

        private static int ColorInt(Color color)
        {
            Color32 bytes = color;
            return (bytes.r << 16) | (bytes.g << 8) | bytes.b;
        }
    }
}
