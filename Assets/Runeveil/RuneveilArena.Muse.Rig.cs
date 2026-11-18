using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Runeveil
{
    public sealed partial class RuneveilArena
    {
        private const string MuseRigResource = "Runeveil/Muse";
        private const string ChestCrystalRenderer = "MuseWeakCore";
        private static readonly int FrontArtwork = Shader.PropertyToID("_Front");
        private static readonly int BackArtwork = Shader.PropertyToID("_Back");
        private static readonly int SideArtwork = Shader.PropertyToID("_SideArtwork");
        private static readonly int Unpainted = Shader.PropertyToID("_Unpainted");
        private readonly RigWeakPointCue[] rigWeakPointCues = new RigWeakPointCue[WeakPointCount];
        private MuseRig museRig;
        private enum RevealCue { None, Glow, Swap }

        private bool TryBuildMuseRig(Shader arenaShader)
        {
            GameObject modelAsset = Resources.Load<GameObject>(MuseRigResource);
            if (modelAsset == null)
            {
                Debug.LogWarning($"Muse model resource {MuseRigResource} is missing. Using the fallback model.", this);
                return false;
            }

            var clips = new Dictionary<string, AnimationClip>();
            foreach (AnimationClip clip in Resources.LoadAll<AnimationClip>(MuseRigResource))
            {
                if ((clip.hideFlags & HideFlags.HideInHierarchy) != 0) continue;
                string name = clip.name.Substring(clip.name.LastIndexOf('|') + 1);
                clips.TryAdd(name, clip);
            }

            GameObject model = Instantiate(modelAsset, boss, false);
            model.name = "Muse rig";
            try
            {
                SkinnedMeshRenderer[] renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                ConfigureMuseRigMaterials(renderers, arenaShader);
                museRig = new MuseRig(model, renderers, clips);
            }
            catch (Exception error)
            {
                model.SetActive(false);
                Destroy(model);
                Debug.LogWarning($"Muse rig could not be built. Using the fallback model. {error.Message}", this);
                return false;
            }

            MuseEyes = Group(museRig.Head, "Muse eye midpoint");
            MuseEyes.position = boss.TransformPoint(new Vector3(0f, 2.74f, -0.8f));
            for (int i = 0; i < weakAnchors.Length; i++) weakAnchors[i] = museRig.WeakPoint(i);
            BuildRigWeakPointCues(arenaShader);
            Core = weakAnchors[ArenaTarget.Core.WeakPointIndex()];
            return true;
        }

        private void ConfigureMuseRigMaterials(SkinnedMeshRenderer[] renderers, Shader arenaShader)
        {
            Shader artworkShader = Resources.Load<Shader>("Runeveil/MuseSurface");
            bool hasSideArtwork = Array.Exists(renderers, renderer => renderer.sharedMesh != null &&
                renderer.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord2));
            var materials = new Dictionary<Material, Material>();
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                Material[] slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    Material imported = slots[i];
                    if (imported == null)
                        throw new InvalidOperationException($"A material is missing from {renderer.name}.");
                    if (!materials.TryGetValue(imported, out Material material))
                    {
                        material = imported.name switch
                        {
                            "MuseDarkArtwork" => CreateMuseArtwork(artworkShader, imported.name, "MuseFront", "MuseBack"),
                            "MuseFistArtwork" => CreateMuseArtwork(artworkShader, imported.name, "MuseAttack", "MuseBack"),
                            "MuseWhiteArtwork" => CreateMuseArtwork(artworkShader, imported.name, "MuseRestored", "MuseRestored", true),
                            _ => CreateMaterial(arenaShader, imported.name, 0.84f, 0f)
                        };
                        if (material.shader == arenaShader) material.SetColor(BaseColor, imported.color);
                        if (material.shader == artworkShader && hasSideArtwork) material.SetFloat(SideArtwork, 1f);
                        materials.Add(imported, material);
                    }
                    slots[i] = material;
                }
                renderer.sharedMaterials = slots;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                if (renderer.name == ChestCrystalRenderer)
                    Tint(renderer, (Color)Rgb(0xffd383) * 1.16f, 1f);
            }
        }

        private Material CreateMuseArtwork(Shader shader, string label, string front, string back, bool restored = false)
        {
            if (shader == null) throw new InvalidOperationException("The Muse artwork shader is missing.");
            Texture2D frontTexture = Resources.Load<Texture2D>("Runeveil/" + front);
            Texture2D backTexture = front == back ? frontTexture : Resources.Load<Texture2D>("Runeveil/" + back);
            if (frontTexture == null || backTexture == null)
                throw new InvalidOperationException($"Artwork textures are missing for {label}.");

            var material = Track(new Material(shader) { name = label });
            material.SetTexture(FrontArtwork, frontTexture);
            material.SetTexture(BackArtwork, backTexture);
            if (restored) material.SetColor(Unpainted, Rgb(0xe4c899));
            return material;
        }

        private void TickMuseRig(float dt, float windup, bool won, bool staggered, bool stuck,
            bool heavyStrike, bool wardReleaseWindow)
        {
            RevealCue reveal = museRig.Tick(dt, windup, won, staggered, stuck, heavyStrike, strikeContactFrame);
            museRig.TintCorruption(properties, rage);

            bool showWarning = windup >= 0f && !won && (!heavyStrike || lift >= 0.16f);
            warningArcs.gameObject.SetActive(showWarning);
            if (showWarning)
            {
                warningArcs.transform.SetPositionAndRotation(
                    weakAnchors[ArenaTarget.RaisedFist.WeakPointIndex()].position - boss.forward * 0.16f,
                    boss.rotation * Quaternion.Euler(0f, 10f, 17f + 24f * lift));
                Color color = heavyStrike ? Rgb(0xff3932) : Color.white;
                Tint(warningArcs, color * (wardReleaseWindow ? 1.65f : 1f), 1f);
            }

            if (reveal == RevealCue.Glow)
            {
                Flare(museRig.RevealCenter, Rgb(0xffe3b0), 0.26f, 0.18f, 0.92f, 0.62f, false);
            }
            else if (reveal == RevealCue.Swap)
            {
                SparkBurst(museRig.RevealCenter, Spell.Barrier, 18, 1.4f, 1.1f, 0.7f);
            }
        }

        private readonly struct RigWeakPointCue
        {
            public readonly Transform Root;
            public readonly MeshRenderer Outline;
            public readonly MeshRenderer Break;

            public RigWeakPointCue(Transform root, MeshRenderer outline, MeshRenderer fragments)
            {
                Root = root;
                Outline = outline;
                Break = fragments;
            }
        }

        private void BuildRigWeakPointCues(Shader arenaShader)
        {
            Material ink = CreateMaterial(arenaShader, "Weak point cue", 0f, 0f, BlendMode.OneMinusSrcAlpha);
            var outline = new Geometry("Weak point target ring and pointer");
            var fragments = new Geometry("Weak crystal fragments");
            const int keyline = 0x17101e;
            const int targetLight = 0xffffd2;
            Vector3[] circle = Arc(Vector3.zero, 1f, 1f, 0f, 360f, 36);
            for (int i = 0; i < circle.Length - 1; i++)
            {
                Vector3 from = circle[i];
                Vector3 to = circle[i + 1];
                AddWeakCueBand(outline, from, to, 0.282f, 0.314f, keyline);
                AddWeakCueBand(outline, from, to, 0.314f, 0.388f, targetLight);
                AddWeakCueBand(outline, from, to, 0.388f, 0.423f, keyline);
            }
            AddWeakCuePointer(outline, 0.465f, 0.648f, 0.147f, 0f, keyline);
            AddWeakCuePointer(outline, 0.514f, 0.615f, 0.089f, -0.003f, targetLight);

            Vector3[] corners = { Vector3.up, Vector3.right, Vector3.down, Vector3.left };
            foreach (Vector3 corner in corners) AddFocusDiamond(fragments, corner * 0.22f);
            Mesh outlineMesh = Track(outline.CreateMesh());
            Mesh coreOutlineMesh = Track(BuildCoreWeakPointOutline(keyline).CreateMesh());
            Mesh fragmentMesh = Track(fragments.CreateMesh());
            for (int i = 0; i < rigWeakPointCues.Length; i++)
            {
                Transform root = Group(boss, weakAnchors[i].name + " weak point cue");
                Mesh mesh = i == ArenaTarget.Core.WeakPointIndex() ? coreOutlineMesh : outlineMesh;
                MeshRenderer active = Render(root, "Active weak point outline", mesh, ink);
                active.transform.localScale = Vector3.one * RigWeakPointScale(i);
                MeshRenderer broken = Render(root, "Weak point break fragments", fragmentMesh, glow);
                rigWeakPointCues[i] = new RigWeakPointCue(root, active, broken);
            }
        }

        private static Geometry BuildCoreWeakPointOutline(int keyline)
        {
            var outline = new Geometry("Faceted chest crystal outline");
            // Leave the 0.292 m by 0.412 m crystal visible through the frame.
            Vector3[] contour =
            {
                new(0f, 0.295f, 0f), new(-0.225f, 0.115f, 0f), new(-0.225f, -0.125f, 0f),
                new(0f, -0.295f, 0f), new(0.225f, -0.125f, 0f), new(0.225f, 0.115f, 0f)
            };
            const int creamGold = 0xffe4ae;
            for (int i = 0; i < contour.Length; i++)
            {
                Vector3 from = contour[i];
                Vector3 to = contour[(i + 1) % contour.Length];
                AddWeakCueBand(outline, from, to, 0.77f, 0.82f, keyline);
                AddWeakCueBand(outline, from, to, 0.82f, 0.94f, creamGold);
                AddWeakCueBand(outline, from, to, 0.94f, 1f, keyline);
            }
            AddWeakCuePointer(outline, 0.321f, 0.406f, 0.067f, 0f, keyline);
            AddWeakCuePointer(outline, 0.343f, 0.392f, 0.037f, -0.003f, creamGold);
            return outline;
        }

        private static void AddWeakCueBand(Geometry geometry, Vector3 from, Vector3 to, float inner, float outer, int color)
        {
            geometry.Quad(from * inner, to * inner, to * outer, from * outer, color);
        }

        private static void AddWeakCuePointer(Geometry geometry, float tipHeight, float top, float halfWidth, float depth, int color)
        {
            Vector3 tip = new(0f, tipHeight, depth);
            geometry.Quad(tip, new Vector3(-halfWidth, top, depth), new Vector3(halfWidth, top, depth), tip, color);
        }

        private static float RigWeakPointScale(int index) => index == ArenaTarget.Core.WeakPointIndex() ? 1.07f : 0.86f;

        private void TickRigWeakPointCues(int aimed, bool ready, bool won)
        {
            int selected = won ? -1 : (ready ? aimed : activeWeak);
            museRig.ShowWeakPointCrystal(selected);
            for (int i = 0; i < rigWeakPointCues.Length; i++)
            {
                RigWeakPointCue cue = rigWeakPointCues[i];
                float broken = won || i != brokenWeak ? 0f : Mathf.InverseLerp(0.43f, 1f, breakFlash);
                cue.Outline.enabled = i == selected;
                cue.Break.enabled = broken > 0f;
                if (!cue.Outline.enabled && !cue.Break.enabled) continue;

                Vector3 target = weakAnchors[i].position;
                Vector3 direction = target - viewHead;
                Quaternion facing = direction.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(direction, boss.up)
                    : boss.rotation;
                float depth = i == ArenaTarget.Core.WeakPointIndex() ? 0.36f : 0.29f;
                cue.Root.SetPositionAndRotation(target - (facing * Vector3.forward) * depth, facing);
                if (cue.Break.enabled)
                {
                    cue.Break.transform.localScale = Vector3.one * (RigWeakPointScale(i) * Mathf.Lerp(1.82f, 0.83f, broken));
                    Tint(cue.Break, Rgb(0xff9cbd), broken);
                }
            }
        }

        private sealed class MuseRig
        {
            private const float StaggerHoldEnd = 3f;
            private const float RevealTime = 1.15f;
            private const float RevealGlowStart = 1.01f;
            private const float EntryBlend = 0.20f;
            private const float ReactionImpact = 2f / 30f;
            private static readonly string[] TargetBones = { "Core", "PawGem.R", "Horn.L", "Horn.R", "PawGem.L" };
            private static readonly string[] CrystalRenderers =
                { ChestCrystalRenderer, "MuseWeakRightHand", "MuseWeakLeftHorn", "MuseWeakRightHorn", "MuseWeakRaisedFist" };
            private static readonly float[] TargetRadii = { 0.25f, 0.21f, 0.26f, 0.26f, 0.21f };
            private static readonly string[] ReactionNames =
                { "MuseHitBody", "MuseHitCore", "MuseHitPawR", "MuseHitHornL", "MuseHitHornR", "MuseHitPawL" };

            private enum Phase { Idle, Windup, Strike, Stagger, Cleansing, Restored }

            private readonly GameObject model;
            private readonly AnimationClip idle;
            private readonly AnimationClip normal;
            private readonly AnimationClip heavy;
            private readonly AnimationClip lightning;
            private readonly AnimationClip cleanse;
            private readonly AnimationClip[] reactions = new AnimationClip[ReactionNames.Length];
            private readonly Quaternion[][] reactionRest = new Quaternion[ReactionNames.Length][];
            private readonly SkinnedMeshRenderer dark;
            private readonly SkinnedMeshRenderer white;
            private readonly SkinnedMeshRenderer corruption;
            private readonly SkinnedMeshRenderer[] weakPointCrystals = new SkinnedMeshRenderer[WeakPointCount];
            private readonly Color?[] corruptionColors;
            private readonly Transform pelvis;
            private readonly Transform spine;
            private readonly Transform chest;
            private readonly Transform head;
            private readonly Transform strikingShoulder;
            private readonly Transform supportingShoulder;
            private readonly Transform[] markers = new Transform[WeakPointCount];
            private readonly AimVolume[] bodyVolumes;
            private readonly Transform[] joints;
            private readonly Pose[] entry;
            private readonly Pose[] basePose;
            private Phase phase;
            private float time;
            private bool strongStrike;
            private int reactionIndex = -1;
            private float reactionTime;
            private float reactionWeight;
            private float corruptionRage;

            public MuseRig(GameObject model, SkinnedMeshRenderer[] renderers, Dictionary<string, AnimationClip> clips)
            {
                this.model = model;
                idle = clips["MuseIdle"];
                normal = clips["MuseNormal"];
                heavy = clips["MuseHeavy"];
                lightning = clips["MuseLightningDown"];
                cleanse = clips["MuseCleanse"];
                dark = FindRenderer(renderers, "MuseDark");
                white = FindRenderer(renderers, "MuseWhite");
                corruption = FindRenderer(renderers, "MuseCorruption");
                // Older imports have the crystals joined to the corruption mesh.
                for (int i = 0; i < weakPointCrystals.Length; i++)
                    weakPointCrystals[i] = FindRenderer(renderers, CrystalRenderers[i], required: false);
                if (dark.bones.Length < 15 || white.bones.Length < 15)
                    throw new InvalidOperationException("Both Muse forms need a skinned skeleton.");
                Material[] corruptionMaterials = corruption.sharedMaterials;
                corruptionColors = new Color?[corruptionMaterials.Length];
                for (int i = 0; i < corruptionColors.Length; i++)
                {
                    if (corruptionMaterials[i].HasProperty(BaseColor))
                        corruptionColors[i] = corruptionMaterials[i].GetColor(BaseColor);
                }

                var bones = new Dictionary<string, Transform>();
                foreach (Transform node in model.GetComponentsInChildren<Transform>(true))
                    bones.TryAdd(node.name, node);
                pelvis = bones["Pelvis"];
                spine = bones["Spine"];
                chest = bones["Chest"];
                head = bones["Head"];
                strikingShoulder = bones["Clavicle.L"];
                supportingShoulder = bones["Clavicle.R"];
                for (int i = 0; i < markers.Length; i++) markers[i] = bones[TargetBones[i]];
                bodyVolumes = CreateBodyVolumes(model.transform, bones);

                // The horns belong to the corruption mesh and still participate in pose blending.
                joints = bones["Root"].GetComponentsInChildren<Transform>(true);
                entry = new Pose[joints.Length];
                basePose = new Pose[joints.Length];
                idle.SampleAnimation(model, 0f);
                SavePose(basePose);
                for (int i = 0; i < reactions.Length; i++)
                {
                    reactions[i] = clips[ReactionNames[i]];
                    for (int joint = 0; joint < joints.Length; joint++)
                        joints[joint].SetLocalPositionAndRotation(basePose[joint].position, basePose[joint].rotation);
                    reactions[i].SampleAnimation(model, 0f);
                    reactionRest[i] = new Quaternion[joints.Length];
                    for (int joint = 0; joint < joints.Length; joint++)
                        reactionRest[i][joint] = joints[joint].localRotation;
                }
                Reset();
            }

            public Transform Head => head;
            public Vector3 BodyCenter => Vector3.Lerp(pelvis.position, chest.position, 0.5f);
            public Vector3 RevealCenter => markers[ArenaTarget.Core.WeakPointIndex()].position;

            public Transform WeakPoint(int index) => markers[index];

            public void ShowWeakPointCrystal(int selected)
            {
                for (int i = 0; i < weakPointCrystals.Length; i++)
                {
                    SkinnedMeshRenderer crystal = weakPointCrystals[i];
                    bool visible = i == selected;
                    if (crystal != null && crystal.enabled != visible) crystal.enabled = visible;
                }
            }

            public void TintCorruption(MaterialPropertyBlock properties, float rage)
            {
                if (rage == corruptionRage || rage != 0f && Mathf.Abs(corruptionRage - rage) < 0.001f) return;
                corruptionRage = rage;
                Color tint = Color.Lerp(Color.white, Rgb(0xff9c9c), rage);
                for (int i = 0; i < corruptionColors.Length; i++)
                {
                    if (corruptionColors[i] is not Color color) continue;
                    properties.SetColor(BaseColor, color * tint);
                    corruption.SetPropertyBlock(properties, i);
                }
            }

            public void Reset()
            {
                phase = Phase.Idle;
                time = 0f;
                strongStrike = false;
                reactionIndex = -1;
                ShowWhite(false);
                idle.SampleAnimation(model, 0f);
                SavePose(entry);
            }

            public void BeginStrike(bool strong)
            {
                phase = Phase.Strike;
                time = RuneveilDuel.WindupSeconds;
                strongStrike = strong;
            }

            public bool ReachesStrikeContact(float dt) =>
                phase == Phase.Strike && time + dt >= StrikeContactTime(strongStrike);

            public void BeginReaction(ArenaTarget target, bool strong, int level)
            {
                reactionIndex = (int)target - (int)ArenaTarget.Body;
                if (reactionIndex < 0 || reactionIndex >= reactions.Length)
                {
                    reactionIndex = -1;
                    return;
                }
                // The first recoil pose is visible throughout the existing hit stop.
                reactionTime = ReactionImpact;
                reactionWeight = strong ? 1f : Mathf.Min(1f, 0.75f + 0.09f * (level - 1));
            }

            public RevealCue Tick(float dt, float windup, bool won, bool staggered, bool stuck, bool heavyStrike,
                bool contactFrame)
            {
                if (won) return TickCleansing(dt);

                Transform support = null;
                if (staggered || phase == Phase.Stagger && time < lightning.length)
                {
                    if (phase != Phase.Stagger) reactionIndex = -1;
                    Enter(Phase.Stagger);
                    time = staggered ? Mathf.Min(StaggerHoldEnd, time + dt) :
                        Mathf.Min(lightning.length, Mathf.Max(time, StaggerHoldEnd) + dt);
                    Sample(lightning, time, EntryWeight);
                    if (time >= 0.50f && time <= StaggerHoldEnd) support = supportingShoulder;
                }
                else if (windup >= 0f)
                {
                    Enter(Phase.Windup);
                    time = windup * RuneveilDuel.WindupSeconds;
                    Sample(heavyStrike ? heavy : normal, time, EntryWeight);
                }
                else if (phase == Phase.Strike && (stuck || time < StrikeClip.length))
                {
                    float contact = StrikeContactTime(strongStrike);
                    time = contactFrame ? contact : Mathf.Min(stuck ? contact : StrikeClip.length, time + dt);
                    StrikeClip.SampleAnimation(model, time);
                    if ((contactFrame || stuck) && time >= contact) support = strikingShoulder;
                }
                else
                {
                    Enter(Phase.Idle);
                    TickIdle(dt);
                }

                ApplyReaction(dt, support);
                return RevealCue.None;
            }

            public ArenaTarget Aim(Ray ray, ref float distance)
            {
                if (phase is Phase.Cleansing or Phase.Restored) return ArenaTarget.Miss;
                ArenaTarget result = ArenaTarget.Miss;
                foreach (AimVolume volume in bodyVolumes)
                {
                    if (volume.Intersect(ray, ref distance)) result = ArenaTarget.Body;
                }
                for (int i = 0; i < markers.Length; i++)
                {
                    if (CloserSphere(ray, markers[i].position, TargetRadii[i], ref distance))
                        result = (ArenaTarget)((int)ArenaTarget.Core + i);
                }
                return result;
            }

            // Body targets follow the pose without baking the skinned mesh on every gaze sample.
            private static AimVolume[] CreateBodyVolumes(Transform model, Dictionary<string, Transform> bones)
            {
                var volumes = new List<AimVolume>
                {
                    new(model, bones["Spine"], new(0f, 1.49f, 0.07f), new(0.93f, 0.91f, 0.63f)),
                    new(model, bones["Neck"], new(0f, 2.16f, 0.04f), new(0.65f, 0.48f, 0.48f)),
                    new(model, bones["Head"], new(0f, 2.84f, 0.04f), new(0.92f, 0.80f, 0.70f))
                };
                foreach (string suffix in new[] { ".L", ".R" })
                {
                    Transform upperArm = bones["UpperArm" + suffix];
                    // Use the imported joint to find the side because FBX import can reflect X.
                    float side = Mathf.Sign(model.InverseTransformPoint(upperArm.position).x);
                    volumes.Add(new(model, upperArm, new(side * 0.94f, 1.86f, -0.03f),
                        new(0.39f, 0.35f, 0.27f)));
                    volumes.Add(new(model, bones["Forearm" + suffix], new(side * 1.27f, 1.47f, -0.13f),
                        new(0.30f, 0.39f, 0.27f)));
                    volumes.Add(new(model, bones["Paw" + suffix], new(side * 1.39f, 1.16f, -0.22f),
                        new(0.28f, 0.33f, 0.29f)));
                    volumes.Add(new(model, bones["Shin" + suffix], new(side * 0.46f, 0.42f, -0.06f),
                        new(0.27f, 0.37f, 0.29f)));
                    volumes.Add(new(model, bones["Foot" + suffix], new(side * 0.47f, 0.20f, -0.13f),
                        new(0.30f, 0.19f, 0.38f)));
                    volumes.Add(new(model, bones["Horn" + suffix], new(side * 0.72f, 3.59f, -0.14f),
                        new(0.23f, 0.40f, 0.18f)));
                }
                return volumes.ToArray();
            }

            private readonly struct AimVolume
            {
                private readonly Transform bone;
                private readonly Vector3 center;
                private readonly Quaternion orientation;
                private readonly Vector3 inverseRadii;

                public AimVolume(Transform model, Transform bone, Vector3 center, Vector3 radii)
                {
                    this.bone = bone;
                    this.center = bone.InverseTransformPoint(model.TransformPoint(center));
                    orientation = Quaternion.Inverse(bone.rotation) * model.rotation;
                    inverseRadii = new(1f / radii.x, 1f / radii.y, 1f / radii.z);
                }

                public bool Intersect(Ray ray, ref float distance)
                {
                    Quaternion toLocal = Quaternion.Inverse(bone.rotation * orientation);
                    Vector3 origin = Vector3.Scale(toLocal * (ray.origin - bone.TransformPoint(center)), inverseRadii);
                    Vector3 direction = Vector3.Scale(toLocal * ray.direction, inverseRadii);
                    float a = direction.sqrMagnitude;
                    if (a <= 0f) return false;
                    float b = Vector3.Dot(origin, direction);
                    float discriminant = b * b - a * (origin.sqrMagnitude - 1f);
                    if (discriminant < 0f) return false;
                    float root = Mathf.Sqrt(discriminant);
                    float hit = (-b - root) / a;
                    if (hit < 0f) hit = (-b + root) / a;
                    if (hit < 0f || hit >= distance) return false;
                    distance = hit;
                    return true;
                }
            }

            private static bool CloserSphere(Ray ray, Vector3 center, float radius, ref float distance)
            {
                Vector3 toCenter = center - ray.origin;
                float along = Vector3.Dot(toCenter, ray.direction);
                float overlap = radius * radius - (toCenter.sqrMagnitude - along * along);
                if (overlap < 0f) return false;
                float near = along - Mathf.Sqrt(overlap);
                if (near < 0f || near >= distance) return false;
                distance = near;
                return true;
            }

            private static SkinnedMeshRenderer FindRenderer(SkinnedMeshRenderer[] renderers, string name, bool required = true)
            {
                foreach (SkinnedMeshRenderer renderer in renderers)
                {
                    if (renderer.name == name) return renderer;
                }
                if (!required) return null;
                throw new InvalidOperationException($"Skinned renderer \"{name}\" is missing.");
            }

            private void ShowWhite(bool show)
            {
                dark.enabled = !show;
                corruption.enabled = !show;
                white.enabled = show;
                ShowWeakPointCrystal(-1);
            }

            private void Enter(Phase next)
            {
                if (phase == next) return;
                SavePose(entry);
                phase = next;
                time = 0f;
            }

            private AnimationClip StrikeClip => strongStrike ? heavy : normal;
            private float EntryWeight => Mathf.SmoothStep(0f, 1f, time / EntryBlend);

            private void TickIdle(float dt)
            {
                time += dt;
                Sample(idle, time % idle.length, EntryWeight);
            }

            private RevealCue TickCleansing(float dt)
            {
                reactionIndex = -1;
                if (phase == Phase.Restored)
                {
                    TickIdle(dt);
                    return RevealCue.None;
                }

                Enter(Phase.Cleansing);
                float previous = time;
                time = Mathf.Min(cleanse.length, time + dt);
                Sample(cleanse, time, EntryWeight);
                bool swap = previous < RevealTime && time >= RevealTime;
                if (swap) ShowWhite(true);
                bool glow = previous < RevealGlowStart && time >= RevealGlowStart;
                if (time >= cleanse.length) Enter(Phase.Restored);
                return swap ? RevealCue.Swap : glow ? RevealCue.Glow : RevealCue.None;
            }

            private void ApplyReaction(float dt, Transform support)
            {
                if (reactionIndex < 0) return;

                AnimationClip reaction = reactions[reactionIndex];
                Quaternion[] neutral = reactionRest[reactionIndex];
                SavePose(basePose);
                // A clip may omit joints that have no authored recoil.
                for (int i = 0; i < joints.Length; i++) joints[i].localRotation = neutral[i];
                reaction.SampleAnimation(model, reactionTime);
                for (int i = 0; i < joints.Length; i++)
                {
                    Transform joint = joints[i];
                    // A foot or hand that is bearing weight stays on the authored contact pose.
                    bool protect = support != null &&
                        (joint == pelvis || joint == spine || joint == chest || joint == support || joint.IsChildOf(support));
                    Quaternion delta = Quaternion.Inverse(neutral[i]) * joint.localRotation;
                    Quaternion rotation = protect ? basePose[i].rotation :
                        basePose[i].rotation * Quaternion.Slerp(Quaternion.identity, delta, reactionWeight);
                    joint.SetLocalPositionAndRotation(basePose[i].position, rotation);
                }

                reactionTime += dt;
                if (reactionTime >= reaction.length) reactionIndex = -1;
            }

            private void Sample(AnimationClip clip, float sampleTime, float weight)
            {
                clip.SampleAnimation(model, sampleTime);
                if (weight >= 1f) return;
                for (int i = 0; i < joints.Length; i++)
                {
                    Transform joint = joints[i];
                    joint.SetLocalPositionAndRotation(
                        Vector3.Lerp(entry[i].position, joint.localPosition, weight),
                        Quaternion.Slerp(entry[i].rotation, joint.localRotation, weight));
                }
            }

            private void SavePose(Pose[] destination)
            {
                for (int i = 0; i < joints.Length; i++)
                    destination[i] = new Pose(joints[i].localPosition, joints[i].localRotation);
            }
        }
    }
}
