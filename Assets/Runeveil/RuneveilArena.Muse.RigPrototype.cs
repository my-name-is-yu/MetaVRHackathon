#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Runeveil
{
    public sealed partial class RuneveilArena
    {
        private const string MuseRigPath = "Assets/Runeveil/Prototype/MusePrototype.fbx";
        private const string MuseRigSessionKey = "Runeveil.MuseRigPrototype";
        private const string MuseRigMenu = "Runeveil/Muse Rig Prototype/Use in Play Mode";
        private MuseRigPreview museRigPrototype;
        private enum RevealCue { None, Glow, Swap }

        [MenuItem(MuseRigMenu)]
        private static void ToggleMuseRigPrototype() =>
            SessionState.SetBool(MuseRigSessionKey, !SessionState.GetBool(MuseRigSessionKey, false));

        [MenuItem(MuseRigMenu, true)]
        private static bool ValidateMuseRigPrototype()
        {
            Menu.SetChecked(MuseRigMenu, SessionState.GetBool(MuseRigSessionKey, false));
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private void BuildMuseRigPrototype()
        {
            if (!SessionState.GetBool(MuseRigSessionKey, false)) return;
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(MuseRigPath);
            if (modelAsset == null)
            {
                Debug.LogError($"Muse rig prototype could not load {MuseRigPath}.");
                return;
            }

            var clips = new Dictionary<string, AnimationClip>();
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(MuseRigPath))
            {
                if (asset is not AnimationClip clip || (clip.hideFlags & HideFlags.HideInHierarchy) != 0) continue;
                string name = clip.name.Substring(clip.name.LastIndexOf('|') + 1);
                clips.TryAdd(name, clip);
            }

            var previousRenderers = boss.GetComponentsInChildren<MeshRenderer>(true);
            Transform mount = Group(boss, "Muse Rig Prototype");
            GameObject model = Instantiate(modelAsset, mount, false);
            try
            {
                var preview = new MuseRigPreview(model, clips);
                ConfigureMuseRigMaterials(model.GetComponentsInChildren<SkinnedMeshRenderer>(true));
                museRigPrototype = preview;
            }
            catch (Exception error)
            {
                mount.gameObject.SetActive(false);
                Destroy(mount.gameObject);
                Debug.LogError($"Muse rig prototype could not be built. {error.Message}");
                return;
            }

            foreach (MeshRenderer renderer in previousRenderers) renderer.enabled = false;
            foreach (MeshRenderer halo in weakHalos) halo.enabled = true;
            warningArcs.enabled = true;
        }

        private void ConfigureMuseRigMaterials(SkinnedMeshRenderer[] renderers)
        {
            Shader shader = Resources.Load<Shader>("Runeveil/ArenaSurface");
            var materials = new Dictionary<Material, Material>();
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                Material[] source = renderer.sharedMaterials;
                for (int i = 0; i < source.Length; i++)
                {
                    Material imported = source[i];
                    if (imported == null) continue;
                    if (!materials.TryGetValue(imported, out Material preview))
                    {
                        preview = CreateMaterial(shader, imported.name, 0.84f, 0f);
                        preview.SetColor(BaseColor, imported.color);
                        materials.Add(imported, preview);
                    }
                    source[i] = preview;
                }
                renderer.sharedMaterials = source;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        private void TickMuseRigPrototype(float dt, float windup, bool won, bool staggered, bool stuck,
            bool heavyStrike, bool wardReleaseWindow)
        {
            RevealCue reveal = museRigPrototype.Tick(dt, windup, won, staggered, stuck, heavyStrike);
            for (int i = 0; i < weakAnchors.Length; i++)
            {
                weakAnchors[i].position = museRigPrototype.WeakPoint(i);
                weakHalos[i].enabled = !won;
            }

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
                Flare(museRigPrototype.RevealCenter, Rgb(0xffe3b0), 0.26f, 0.18f, 0.92f, 0.62f, false);
            }
            else if (reveal == RevealCue.Swap)
            {
                Vector3 crystal = museRigPrototype.RevealCenter;
                SparkBurst(crystal, Spell.Barrier, 18, 1.4f, 1.1f, 0.7f);
            }
        }

        private sealed class MuseRigPreview
        {
            private const float WindupEnd = 2.4f;
            private const float NormalContact = 2.533333f;
            private const float HeavyContact = 2.59f;
            private const float StaggerHoldEnd = 3f;
            private const float RevealTime = 1.15f;
            private const float RevealGlowStart = 1.01f;
            private const float EntryBlend = 0.20f;
            private const float ReactionImpact = 2f / 30f;
            private static readonly string[] TargetBones = { "Core", "PawGem.R", "Horn.L", "Horn.R", "PawGem.L" };
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
            private readonly Transform pelvis;
            private readonly Transform spine;
            private readonly Transform chest;
            private readonly Transform head;
            private readonly Transform strikingShoulder;
            private readonly Transform supportingShoulder;
            private readonly Transform[] markers = new Transform[WeakPointCount];
            private readonly Transform[] joints;
            private readonly Pose[] entry;
            private readonly Pose[] basePose;
            private Phase phase;
            private float time;
            private bool strongStrike;
            private int reactionIndex = -1;
            private float reactionTime;
            private float reactionWeight;

            public MuseRigPreview(GameObject model, Dictionary<string, AnimationClip> clips)
            {
                this.model = model;
                idle = clips["MuseIdle"];
                normal = clips["MuseNormal"];
                heavy = clips["MuseHeavy"];
                lightning = clips["MuseLightningDown"];
                cleanse = clips["MuseCleanse"];
                dark = FindRenderer("MuseDark");
                white = FindRenderer("MuseWhite");
                corruption = FindRenderer("MuseCorruption");
                joints = dark.bones;
                if (joints.Length < 15 || white.bones.Length < 15)
                    throw new InvalidOperationException("Both Muse forms need a skinned skeleton.");

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

            public Vector3 BodyCenter => Vector3.Lerp(pelvis.position, chest.position, 0.5f);
            public Vector3 RevealCenter => chest.position - model.transform.forward * 0.55f;

            public Vector3 WeakPoint(int index) => markers[index].position;

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
                time = WindupEnd;
                strongStrike = strong;
            }

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

            public RevealCue Tick(float dt, float windup, bool won, bool staggered, bool stuck, bool heavyStrike)
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
                    time = windup * WindupEnd;
                    Sample(heavyStrike ? heavy : normal, time, EntryWeight);
                }
                else if (phase == Phase.Strike && (stuck || time < StrikeClip.length))
                {
                    float contact = strongStrike ? HeavyContact : NormalContact;
                    time = Mathf.Min(stuck ? contact : StrikeClip.length, time + dt);
                    StrikeClip.SampleAnimation(model, time);
                    if (stuck && time >= contact) support = strikingShoulder;
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
                if (CloserSphere(ray, BodyCenter, 0.83f, ref distance)) result = ArenaTarget.Body;
                if (CloserSphere(ray, head.position + Vector3.up * 0.40f, 0.83f, ref distance)) result = ArenaTarget.Body;
                for (int i = 0; i < markers.Length; i++)
                {
                    if (CloserSphere(ray, markers[i].position, TargetRadii[i], ref distance))
                        result = (ArenaTarget)((int)ArenaTarget.Core + i);
                }
                return result;
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

            private SkinnedMeshRenderer FindRenderer(string name)
            {
                foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer.name == name) return renderer;
                }
                throw new InvalidOperationException($"Skinned renderer \"{name}\" is missing.");
            }

            private void ShowWhite(bool show)
            {
                dark.enabled = !show;
                corruption.enabled = !show;
                white.enabled = show;
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
#endif
