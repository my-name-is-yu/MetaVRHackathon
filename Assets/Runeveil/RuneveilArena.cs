using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Runeveil
{
    public enum ArenaTarget { Miss, Body, Core, RightHand, LeftHorn, RightHorn, RaisedFist }

    internal static class ArenaTargetExtensions
    {
        public static bool IsWeakPoint(this ArenaTarget target) =>
            target is ArenaTarget.Core or ArenaTarget.RightHand or ArenaTarget.LeftHorn or ArenaTarget.RightHorn or ArenaTarget.RaisedFist;

        public static int WeakPointIndex(this ArenaTarget target) =>
            target.IsWeakPoint() ? (int)target - (int)ArenaTarget.Core : -1;

        public static string DisplayName(this ArenaTarget target) => target switch
        {
            ArenaTarget.Core => "Crystal core",
            ArenaTarget.RightHand => "Right hand",
            ArenaTarget.LeftHorn => "Left horn",
            ArenaTarget.RightHorn => "Right horn",
            ArenaTarget.RaisedFist => "Raised fist",
            _ => ""
        };
    }

    public sealed partial class RuneveilArena : MonoBehaviour
    {
        private const float BossDepth = 5.76f;
        private const float CastRange = 9f;
        public const int WeakPointCount = (int)ArenaTarget.RaisedFist - (int)ArenaTarget.Core + 1;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private readonly List<UnityEngine.Object> generatedAssets = new();
        private readonly List<HitSurface> hitSurfaces = new();

        private MaterialPropertyBlock properties;
        private Transform stage;
        private Transform boss;
        private Arm attackArm;
        private Arm anchorArm;
        private MeshRenderer warningArcs;
        private readonly Transform[] weakAnchors = new Transform[WeakPointCount];
        private readonly MeshRenderer[] weakGems = new MeshRenderer[WeakPointCount];
        private readonly MeshRenderer[] weakHalos = new MeshRenderer[WeakPointCount];
        private readonly float[] weakGlow = new float[WeakPointCount];
        private int activeWeak = -1;
        private int brokenWeak = -1;
        private float breakFlash;
        private float weakPop;
        private Material stone;
        private Material vivid;
        private Material glow;
        private Material softGlow;
        private Material shadow;
        private Material ward;
        private Transform wardRoot;
        private MeshRenderer wardShell;
        private MeshRenderer wardEdges;
        private Transform wardRipple;
        private MeshRenderer wardFlash;
        private Projectile[] projectiles;
        private int nextProjectile;
        private Mesh fireShape;
        private Mesh iceShape;
        private Mesh rockShape;
        private Mesh windShape;
        private Mesh softDisc;
        private float age;
        private float lift;
        private float slam;
        private float defeat;
        private float down;
        private float pin;
        private float rage;
        private float hitStop;
        private float wardFlashTime;
        private float wardOpacity;
        private Transform focusIndicator;
        private MeshRenderer focusCorners;
        private MeshRenderer focusBurst;
        private LineRenderer focusArc;
        private Vector3[] focusArcPoints;
        private int focusArcSegments = -1;

        // Charge-complete effect that follows the casting fingertip.
        private const int FlameCount = 5;
        private const int EmberCount = 7;
        private const float ChargeBurstTime = 0.38f;
        private Transform chargeRoot;
        private MeshRenderer chargeHalo;
        private MeshRenderer chargeCore;
        private MeshRenderer chargeBurst;
        private readonly MeshRenderer[] chargeFlames = new MeshRenderer[FlameCount];
        private readonly MeshRenderer[] chargeEmbers = new MeshRenderer[EmberCount];
        private Spell? chargeSpell;
        private const int ArcPoints = 7;
        private readonly LineRenderer[] chargeArcs = new LineRenderer[3];
        private readonly Vector3[,] arcShapes = new Vector3[3, ArcPoints];
        private float nextArcShape;

        // Sparks that trail the drawing fingertip.
        private const int SparkCount = 260;
        private Transform sparkRoot;
        private readonly MeshRenderer[] sparks = new MeshRenderer[SparkCount];
        private readonly Vector3[] sparkVelocity = new Vector3[SparkCount];
        private readonly float[] sparkAge = new float[SparkCount];
        private readonly float[] sparkLife = new float[SparkCount];
        private readonly float[] sparkSize = new float[SparkCount];
        private readonly int[] sparkStyle = new int[SparkCount];
        private int nextSpark;
        private float sparkBudget;
        private Vector3 lastSparkTip;
        private bool hadSparkTip;
        private float chargeAge;
        private float chargeOpacity;
        private int chargeLevel;

        // Pooled flashes and shockwave rings for release and impact effects.
        private const int FlareCount = 40;
        private const int FireballFlames = 9;
        private Transform flareRoot;
        private Mesh ringMesh;
        private Material ringGlow;
        private readonly MeshRenderer[] flares = new MeshRenderer[FlareCount];
        private readonly MeshFilter[] flareFilters = new MeshFilter[FlareCount];
        private readonly float[] flareAge = new float[FlareCount];
        private readonly float[] flareDelay = new float[FlareCount];
        private readonly float[] flareLife = new float[FlareCount];
        private readonly float[] flareFrom = new float[FlareCount];
        private readonly float[] flareTo = new float[FlareCount];
        private readonly float[] flareAlpha = new float[FlareCount];
        private readonly Color[] flareColor = new Color[FlareCount];
        private readonly Vector3[] flarePosition = new Vector3[FlareCount];
        private int nextFlare;
        private Vector3 viewHead;

        public Transform Core { get; private set; }
        public bool HitStopping => hitStop > 0f;

        public Vector3 WeakPointPosition(ArenaTarget target) => weakAnchors[target.WeakPointIndex()].position;

        internal bool CanFocus(ArenaTarget target) =>
            target.IsWeakPoint() && (target != ArenaTarget.RaisedFist || lift >= 0.38f);

        public void Build()
        {
            if (stage != null) return;

            properties = new MaterialPropertyBlock();
            stage = Group(transform, "Moonlit courtyard");
            Shader shader = Resources.Load<Shader>("Runeveil/ArenaSurface");
            stone = CreateMaterial(shader, "Rose and violet stone", 1f, 0.8f);
            vivid = CreateMaterial(shader, "Amber runelight", 0f, 0f);
            glow = CreateMaterial(shader, "Spell light", 0f, 0f, BlendMode.One);
            softGlow = CreateMaterial(shader, "Feathered light", 0f, 0f, BlendMode.One);
            softGlow.SetFloat("_Spot", 1f);
            shadow = CreateMaterial(shader, "Contact shadow", 0f, 0f, BlendMode.OneMinusSrcAlpha);
            shadow.SetFloat("_Spot", 1f);
            ward = CreateMaterial(shader, "Mint ward surface", 0f, 0f, BlendMode.OneMinusSrcAlpha);
            ward.SetFloat("_Cull", (float)CullMode.Off);
            ward.SetFloat("_Rim", 1f);

            var disc = new Geometry("Feathered quad");
            disc.Quad(new(-0.5f, -0.5f, 0f), new(-0.5f, 0.5f, 0f),
                new(0.5f, 0.5f, 0f), new(0.5f, -0.5f, 0f), 0xffffff);
            softDisc = Track(disc.CreateMesh());

            BuildBackdrop(shader);
            BuildCourtyard();
            BuildValleyRidges(shader);
            BuildMuse(shader);
#if UNITY_EDITOR
            BuildMuseRigPrototype();
#endif
            BuildEffects();
            BuildFocusIndicator();
            ResetArena();
        }

        public ArenaTarget Aim(Ray ray, out Vector3 point)
        {
            float distance = CastRange;
            ArenaTarget target = ArenaTarget.Miss;
#if UNITY_EDITOR
            if (museRigPrototype != null)
                target = museRigPrototype.Aim(ray, ref distance);
            else
#endif
            if (boss != null && boss.gameObject.activeSelf)
            {
                foreach (HitSurface surface in hitSurfaces)
                {
                    float hit = surface.Intersect(ray, distance);
                    if (hit >= distance) continue;
                    distance = hit;
                    target = surface.Target;
                }
            }

            if (target != ArenaTarget.Miss)
            {
                point = ray.GetPoint(distance);
                return target;
            }

            var ground = new Plane(transform.up, transform.position);
            point = ray.GetPoint(ground.Raycast(ray, out float floor) && floor < CastRange ? floor : CastRange);
            return ArenaTarget.Miss;
        }

        /// <summary>弱点を狙えていないときの着弾点。プレイヤーから胴体の中心へ向けた線が当たる位置を返す。</summary>
        public Vector3 BodyPoint(Vector3 from)
        {
            Vector3 center = museBody.TransformPoint(MusePoint(new Vector2(626, 900), 0f));
#if UNITY_EDITOR
            if (museRigPrototype != null) center = museRigPrototype.BodyCenter;
#endif
            Aim(new Ray(from, center - from), out Vector3 point);
            return point;
        }

        public void Tick(float dt, float windup, bool shield, bool won, ArenaTarget aimed, bool staggered, bool stuck,
            bool enraged, ArenaTarget weakPoint, ArenaTarget shattered, float focusProgress = 0f, bool focusReady = false,
            bool heavyStrike = false, bool wardReleaseWindow = false)
        {
            dt = Mathf.Clamp(dt, 0f, 0.05f);
            int weakIndex = weakPoint.WeakPointIndex();
            if (weakIndex != activeWeak)
            {
                if (activeWeak >= 0 && weakIndex < 0)
                {
                    brokenWeak = shattered.WeakPointIndex();
                    breakFlash = 1f;
                }
                activeWeak = weakIndex;
                if (activeWeak >= 0) weakPop = 1f;
            }
            breakFlash = Mathf.Max(0f, breakFlash - dt * 2.2f);
            weakPop = Mathf.Max(0f, weakPop - dt * 3f);

            // ヒットストップ中はボスの動きだけを止め、弾やバリアの演出は進める
            float bossDt = hitStop > 0f ? 0f : dt;
            hitStop = Mathf.Max(0f, hitStop - dt);
            age += bossDt;
#if UNITY_EDITOR
            if (museRigPrototype == null)
#endif
            TickMuseCleansing(bossDt, won);
            slam = Mathf.Max(0f, slam - bossDt);
            defeat = Damp(defeat, won ? 1f : 0f, 3.8f, bossDt);
            TickMuseReactions(bossDt, staggered, won);
            pin = Damp(pin, stuck && !won ? 1f : 0f, stuck ? 14f : 5f, bossDt);
            rage = Damp(rage, enraged && !won ? 1f : 0f, 3f, bossDt);
            bool rising = windup >= 0f && !won;
            float charge = Mathf.Clamp01(windup);
            lift = Damp(lift, rising ? Mathf.SmoothStep(0f, 1f, charge / 0.72f) : 0f, rising ? 7.8f : 6f, bossDt);
            float coilTarget = rising && heavyStrike ? Mathf.Sin(Mathf.PI * Mathf.Clamp01(charge / 0.34f)) : 0f;
            coil = Damp(coil, coilTarget, 16f, bossDt);

            float strikeDuration = heavyImpact ? MuseHeavyStrikeSeconds : MuseStrikeSeconds;
            float dropDuration = heavyImpact ? 0.18f : 0.13f;
            float elapsed = strikeDuration - slam;
            float drop = Mathf.SmoothStep(0f, 1f, elapsed / dropDuration);
            float release = Mathf.SmoothStep(1f, 0f, (elapsed - dropDuration) / (strikeDuration - dropDuration));
            // Ward holds the same contact pose until the fist comes free.
            float thrust = Mathf.Max(drop * release, pin) * (1f - defeat);
            bool heavyPose = rising ? heavyStrike : heavyImpact && (slam > 0f || pin > 0.001f);
#if UNITY_EDITOR
            if (museRigPrototype != null)
                TickMuseRigPrototype(bossDt, windup, won, staggered, stuck, heavyStrike, wardReleaseWindow);
            else
#endif
            PoseMuse(rising, thrust, heavyPose, wardReleaseWindow);
            int aimedIndex = aimed.WeakPointIndex();
            bool canFocus = activeWeak >= 0 && rising && CanFocus(aimed);
            focusReady &= canFocus;
            TickWeakPoints(dt, rising, aimedIndex, focusReady, charge);
#if UNITY_EDITOR
            if (museRigPrototype == null)
#endif
            PoseMuseCleansing(museHead);
            TickEffects(dt, shield);
            TickFocusIndicator(rising, staggered, aimedIndex, canFocus, focusProgress, focusReady);
        }

        private void TickWeakPoints(float dt, bool rising, int aimed, bool ready, float charge)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(age * 3.8f);
            Color lit = Color.Lerp(Color.white, Rgb(0xffc1e5),
                0.32f + (rising ? 0.38f * charge : 0.09f * pulse) + down * 0.3f);
            float litHalo = 0.13f + 0.08f * pulse + (rising ? 0.19f : 0f) + down * 0.25f;
            float litScale = 1f + pulse * 0.09f + 0.11f * lift + down * 0.3f;
            for (int i = 0; i < WeakPointCount; i++)
            {
                // 光っていない部位は眠った暗い結晶として残し、弱点になりうる場所を見せておく
                bool highlighted = i == activeWeak || (i == aimed && ready);
                weakGlow[i] = Damp(weakGlow[i], highlighted ? 1f : 0f, highlighted ? 10f : 6f, dt);
                float glowAmount = weakGlow[i];
                float broken = i == brokenWeak ? breakFlash : 0f;
                float popping = i == activeWeak ? weakPop : 0f;
                Vector3 haloSize = i == 0 ? new Vector3(0.64f, 0.81f, 1f) : new Vector3(0.42f, 0.53f, 1f);
                Tint(weakGems[i], Color.Lerp(Rgb(0x5a3c50), lit, glowAmount) * Mathf.Lerp(1f, 0.36f, defeat), 1f);
                weakGems[i].transform.localScale = Vector3.one * (1f - broken * 0.35f + popping * 0.3f);
                Tint(weakHalos[i], Color.Lerp(Rgb(0xff6fb1), Color.white, broken),
                    (glowAmount * (litHalo + (i == aimed ? ready ? 0.20f : 0.09f : 0f)) + broken * 0.9f) * (1f - defeat));
                weakHalos[i].transform.localScale = haloSize * (litScale + broken * 1.4f + popping * 0.5f);
            }
        }

        /// <summary>魔法の見た目を出し、攻撃魔法なら着弾までの秒数を返す（バリアは0）。怯みとヒットストップは着弾時の <see cref="Hit"/> で出す。</summary>
        public float Cast(Spell spell, Vector3 from, Vector3 to, Vector3 head, Vector3 forward, int level = 1)
        {
            viewHead = head;
            level = Mathf.Max(1, level);
            ReleaseBurst(spell, from, level);
            if (spell == Spell.Barrier)
            {
                wardRoot.position = head - Vector3.up * 1.58f;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.01f) wardRoot.rotation = Quaternion.LookRotation(forward);
                wardFlashTime = Mathf.Min(1f, 0.66f + 0.12f * (level - 1));
                Vector3 front = head + (forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward) * 0.9f;
                float power = LevelScale(level);
                Flare(front, Rgb(0xb5ffe8), 0.6f, 0.2f, 2.2f * power, 0.7f, true);
                Flare(front, Rgb(0x9ff2ce), 0.8f, 0.1f, 3f * power, 0.45f, true, 0.12f);
                SparkBurst(front, Spell.Barrier, 20 + 10 * level, 2.2f * power, 2.5f, 1.6f);
                return 0f;
            }

            Projectile shot = projectiles[nextProjectile];
            nextProjectile = (nextProjectile + 1) % projectiles.Length;
            shot.Start = from;
            shot.End = to;
            shot.Age = 0f;
            shot.Arc = 0f;
            shot.Spell = spell;
            shot.Level = level;
            shot.Impacted = false;
            shot.Root.gameObject.SetActive(true);
            shot.Flash.gameObject.SetActive(false);
            shot.Bolt.gameObject.SetActive(spell == Spell.Lightning);
            shot.Body.gameObject.SetActive(spell != Spell.Lightning && spell != Spell.Fire);
            shot.FlameRoot.gameObject.SetActive(spell == Spell.Fire);
            Vector3 direction = to - from;
            shot.Flash.transform.rotation = direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction) : Quaternion.identity;
            switch (spell)
            {
                case Spell.Fire:
                    shot.Color = Rgb(0xff744d);
                    shot.Duration = 0.38f;
                    shot.Arc = 0.29f;
                    shot.Filter.sharedMesh = fireShape;
                    shot.Size = new Vector3(0.13f, 0.13f, 0.26f);
                    break;
                case Spell.Lightning:
                    shot.Color = Rgb(0xffd173);
                    shot.Duration = 0.10f;
                    ShapeBolt(shot.Bolt, from, to);
                    break;
                case Spell.Ice:
                    shot.Color = Rgb(0x80d8ff);
                    shot.Duration = 0.32f;
                    shot.Arc = 0.10f;
                    shot.Filter.sharedMesh = iceShape;
                    shot.Size = new Vector3(0.12f, 0.14f, 0.39f);
                    break;
                case Spell.Rock:
                    shot.Color = Rgb(0xd59f83);
                    shot.Duration = 0.53f;
                    shot.Arc = 0.63f;
                    shot.Filter.sharedMesh = rockShape;
                    shot.Size = new Vector3(0.19f, 0.17f, 0.20f);
                    break;
                default:
                    shot.Color = Rgb(0xc3a0ff);
                    shot.Duration = 0.37f;
                    shot.Arc = 0.07f;
                    shot.Filter.sharedMesh = windShape;
                    shot.Size = Vector3.one;
                    break;
            }
            float levelScale = LevelScale(level);
            shot.Size *= levelScale;
            shot.Body.localScale = shot.Size;
            shot.Trail.widthMultiplier = levelScale;
            shot.Bolt.widthMultiplier = levelScale;
            Color bodyColor = spell == Spell.Rock ? shot.Color : Color.Lerp(shot.Color, Color.white, 0.3f);
            Tint(shot.Renderer, bodyColor, 1f);
            Tint(shot.Bolt, shot.Color, 0.9f);
            Tint(shot.Trail, shot.Color, 0.45f);
            MoveProjectile(shot, 0f);
            return shot.Duration;
        }

        /// <summary>攻撃魔法の着弾時に呼び、ボスを怯ませてヒットストップをかける。</summary>
        public void Hit(Spell spell, ArenaTarget target, bool strong, bool finalBlow, int level = 1)
        {
            if (target == ArenaTarget.Miss) return;
            level = Mathf.Max(1, level);

            // 威力に応じて止める時間を変え、拳を止めた一撃・ダウン中の弱点・止めの一撃は長く止める
            float length = spell switch { Spell.Rock => 0.10f, Spell.Lightning => 0.05f, _ => 0.07f };
            if (target.IsWeakPoint()) length *= 1.3f;
            length *= 1f + 0.15f * (level - 1);
            if (strong) length = Mathf.Max(length, 0.16f);
            if (finalBlow) length = 0.35f;
            hitStop = Mathf.Max(hitStop, length);
            if (!finalBlow)
            {
                RecordMuseReaction(target, strong, level);
#if UNITY_EDITOR
                museRigPrototype?.BeginReaction(target, strong, level);
#endif
            }
        }

        public void Impact(bool blocked, bool heavyStrike)
        {
            heavyImpact = heavyStrike;
            slam = heavyStrike ? MuseHeavyStrikeSeconds : MuseStrikeSeconds;
            if (blocked) wardFlashTime = 1f;
#if UNITY_EDITOR
            museRigPrototype?.BeginStrike(heavyStrike);
#endif
        }

        public void ResetArena()
        {
            age = lift = coil = slam = defeat = pin = rage = hitStop = wardFlashTime = wardOpacity = 0f;
            heavyImpact = false;
            boss.localPosition = Vector3.forward * BossDepth;
            ResetMuseReactions();
#if UNITY_EDITOR
            museRigPrototype?.Reset();
#endif
            breakFlash = weakPop = 0f;
            brokenWeak = -1;
            activeWeak = -1;
            Array.Clear(weakGlow, 0, weakGlow.Length);
            nextProjectile = 0;
            wardRoot.gameObject.SetActive(false);
            foreach (Projectile shot in projectiles)
            {
                shot.Age = -1f;
                shot.Root.gameObject.SetActive(false);
            }
            Tick(0f, -1f, false, false, ArenaTarget.Miss, false, false, false, ArenaTarget.Core, ArenaTarget.Miss);
            breakFlash = weakPop = 0f;
        }

        private void BuildFocusIndicator()
        {
            focusIndicator = Group(stage, "Pink weak point gaze indicator");
            var corners = new Geometry("Weak point focus corners");
            var burst = new Geometry("Exposed weak point diamonds");

            void Bar(float x, float y, float halfWidth, float halfHeight)
            {
                corners.Quad(new Vector3(x - halfWidth, y - halfHeight, 0f), new Vector3(x - halfWidth, y + halfHeight, 0f),
                    new Vector3(x + halfWidth, y + halfHeight, 0f), new Vector3(x + halfWidth, y - halfHeight, 0f), 0xffffff);
            }

            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            {
                Bar(0.353f * x, 0.41f * y, 0.059f, 0.011f);
                Bar(0.408f * x, 0.356f * y, 0.011f, 0.058f);
            }

            for (int sign = -1; sign <= 1; sign += 2)
            {
                AddFocusDiamond(burst, new Vector3(sign * 0.511f, 0f, 0f));
                AddFocusDiamond(burst, new Vector3(0f, sign * 0.511f, 0f));
            }

            focusCorners = Render(focusIndicator, corners, glow);
            Tint(focusCorners, Rgb(0xffb0df), 0.84f);
            focusBurst = Render(focusIndicator, burst, glow);
            Tint(focusBurst, Rgb(0xffc1eb), 0.91f);
            focusArcPoints = Arc(Vector3.zero, 0.394f, 0.394f, 90f, -270f, 48);
            focusArc = Line(focusIndicator, "Gaze charge arc", focusArcPoints.Length, 0.016f);
            focusArc.useWorldSpace = false;
            focusArc.numCapVertices = 3;
        }

        private static void AddFocusDiamond(Geometry geometry, Vector3 at)
        {
            geometry.Quad(at + Vector3.down * 0.046f, at + Vector3.left * 0.038f,
                at + Vector3.up * 0.046f, at + Vector3.right * 0.038f, 0xffffff);
        }

        private void TickFocusIndicator(bool warning, bool staggered, int aimed, bool canFocus, float progress, bool ready)
        {
            bool visible = activeWeak >= 0 && defeat < 0.2f && (warning || staggered || aimed == activeWeak);
            focusIndicator.gameObject.SetActive(visible);
            if (!visible) return;

            int site = canFocus ? aimed : activeWeak;
            var target = (ArenaTarget)((int)ArenaTarget.Core + site);
            bool chest = target == ArenaTarget.Core;
            focusIndicator.SetPositionAndRotation(weakAnchors[site].position - boss.forward * (chest ? 0.32f : 0.14f),
                boss.rotation);
            Vector3 scale = target switch
            {
                ArenaTarget.LeftHorn or ArenaTarget.RightHorn => new(0.78f, 0.9f, 1f),
                ArenaTarget.RaisedFist => new(1.14f, 1f, 1f),
                _ => Vector3.one
            };
            // The ring closes and grows at full focus, with diamonds as a second shape cue.
            focusIndicator.localScale = scale * (ready ? 1.18f : 1f);
            if (!canFocus) progress = 0f;
            focusCorners.enabled = !ready;
            focusBurst.enabled = ready;
            int lastSegment = ready ? focusArcPoints.Length - 1 :
                Mathf.FloorToInt(Mathf.Clamp01(progress) * (focusArcPoints.Length - 1));
            focusArc.enabled = lastSegment > 0;
            if (lastSegment <= 0) return;

            if (lastSegment != focusArcSegments)
            {
                focusArcSegments = lastSegment;
                focusArc.positionCount = lastSegment + 1;
                for (int i = 0; i <= lastSegment; i++) focusArc.SetPosition(i, focusArcPoints[i]);
            }

            focusArc.startWidth = ready ? 0.026f : 0.016f;
            focusArc.endWidth = focusArc.startWidth;
            Tint(focusArc, Rgb(ready ? 0xffd4f1 : 0xff8ad6), ready ? 0.98f : 0.78f);
        }

        private void OnDestroy()
        {
            foreach (UnityEngine.Object asset in generatedAssets)
            {
                if (Application.isPlaying) Destroy(asset);
                else DestroyImmediate(asset);
            }
        }

        private void BuildBackdrop(Shader shader)
        {
            Material sky = CreateMaterial(shader, "Painted twilight valley", 0f, 0f);
            sky.SetTexture("_BaseMap", Resources.Load<Texture2D>("Runeveil/TwilightValley"));
            sky.SetFloat("_Cull", (float)CullMode.Front);
            sky.SetFloat("_ZWrite", 0f);
            sky.renderQueue = (int)RenderQueue.Background;
            var panorama = new Geometry("Valley panorama");
            panorama.Sphere(new Vector3(0f, 1.6f, 0f), Vector3.one * 37f, Quaternion.identity, 0xffffff, 48, 24, false);
            Render(stage, panorama, sky);

            var moon = new Geometry("Amber crescent");
            Vector3 center = new(-10.65f, 9.1f, 27f);
            for (int i = 0; i < 22; i++)
            {
                float t = i * Mathf.PI / 22f;
                float u = (i + 1) * Mathf.PI / 22f;
                Vector3 Edge(float angle, float width) => center +
                    new Vector3(-Mathf.Sin(angle) * width - 0.28f * Mathf.Cos(angle), Mathf.Cos(angle) * 0.99f, 0f);
                moon.Quad(Edge(u, 0.86f), Edge(t, 0.86f), Edge(t, 0.35f), Edge(u, 0.35f), 0xffe1a1);
            }
            Render(stage, moon, vivid);
            var halo = Render(stage, "Moon halo", softDisc, softGlow);
            halo.transform.localPosition = center + Vector3.forward * 0.04f;
            halo.transform.localScale = Vector3.one * 3.2f;
            Tint(halo, Rgb(0xffb679), 0.16f);
        }

        private void BuildCourtyard()
        {
            var random = new System.Random(604426);
            var terrain = new Geometry("Moss bed");
            terrain.Quad(new(-38f, -0.12f, -12f), new(-38f, -0.12f, 42f),
                new(38f, -0.12f, 42f), new(38f, -0.12f, -12f), 0x555053);
            Render(stage, terrain, stone);

            var paving = new Geometry("Staggered flagstones");
            var rubble = new Geometry("Ruined arches and rubble");
            var moss = new Geometry("Moss and courtyard plants");
            var woods = new Geometry("Violet pines");
            int[] stoneColors = { 0x887183, 0xa07f8d, 0x957684, 0xb18b92, 0x927d8a, 0x7d657b };
            int[] mossColors = { 0x777b40, 0x888f4a, 0x9da44d, 0x727542, 0x88924e };
            int[] pineColors = { 0x41474f, 0x465143, 0x50575b, 0x625666 };
            float Random() => (float)random.NextDouble();
            int Pick(int[] palette) => palette[random.Next(palette.Length)];

            for (int row = 0; row < 24; row++)
            {
                float z = -2.8f + row * 0.91f;
                for (int col = -7; col <= 7; col++)
                {
                    float x = col * 0.92f + (row % 2 == 0 ? -0.06f : 0.42f) + (Random() - 0.5f) * 0.08f;
                    if (Mathf.Abs(x) > 5.25f && Random() < 0.17f) continue;
                    paving.Block(new(x, -0.059f + Random() * 0.015f, z),
                        new(0.39f + Random() * 0.057f, 0.062f, 0.38f + Random() * 0.046f),
                        Quaternion.Euler((Random() - 0.5f) * 2f, (Random() - 0.5f) * 10f, (Random() - 0.5f) * 2f),
                        Pick(stoneColors));
                    if (Random() < 0.22f)
                        moss.Sphere(new(x + 0.41f, 0.001f, z + (Random() - 0.5f) * 0.60f),
                            new(0.085f + Random() * 0.12f, 0.022f, 0.15f), Quaternion.identity, Pick(mossColors), 5, 4);
                }
            }

            void MossPatch(Vector3 at, float radius, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    float angle = Random() * Mathf.PI * 2f;
                    float spread = Mathf.Sqrt(Random()) * radius;
                    float size = radius * (0.27f + Random() * 0.37f);
                    Vector3 point = at + new Vector3(Mathf.Cos(angle) * spread, (Random() - 0.5f) * radius * 0.15f,
                        Mathf.Sin(angle) * spread * 0.63f);
                    moss.Sphere(point, new(size, size * 0.30f, size * 0.79f),
                        Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f), Pick(mossColors), 5, 4);
                }
            }

            void Arch(float x, float z, float scale, int missing)
            {
                float radius = 1.02f * scale;
                for (int side = -1; side <= 1; side += 2)
                {
                    float pillar = x + side * radius;
                    rubble.Block(new(pillar, scale * 0.12f, z), new(scale * 0.43f, scale * 0.16f, scale * 0.43f),
                        Quaternion.identity, Pick(stoneColors));
                    for (int level = 0; level < 4; level++)
                    {
                        rubble.Block(new(pillar + (Random() - 0.5f) * 0.06f, (0.39f + 0.31f * level) * scale, z),
                            new(0.29f * scale, 0.153f * scale, 0.30f * scale),
                            Quaternion.Euler(0f, (Random() - 0.5f) * 10f, (Random() - 0.5f) * 4f), Pick(stoneColors));
                    }
                    MossPatch(new(pillar, 0.27f * scale, z - 0.36f * scale), 0.24f * scale, 5);
                }
                for (int segment = 0; segment < 9; segment++)
                {
                    if ((missing & (1 << segment)) != 0) continue;
                    float angle = Mathf.PI * (segment + 0.5f) / 9f;
                    Vector3 radial = new(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                    Vector3 point = new Vector3(x, 1.54f * scale, z) + radial * radius;
                    rubble.Block(point, new(0.19f * scale, 0.30f * scale, 0.31f * scale),
                        Quaternion.Euler(0f, (Random() - 0.5f) * 6f, angle * Mathf.Rad2Deg + 90f), Pick(stoneColors));
                    if (segment % 2 == 0)
                        MossPatch(point + radial * (0.29f * scale) + Vector3.back * (0.19f * scale), 0.28f * scale, 7);
                }
            }

            Arch(-4.55f, 4.80f, 1.25f, 0b001111000);
            Arch(4.48f, 4.70f, 1.23f, 0);
            Arch(-4.21f, 9.40f, 1.01f, 0b000110000);
            Arch(5.00f, 10.10f, 1.05f, 0b100000000);
            Arch(-6.9f, 13.1f, 1.13f, 0b000000011);
            Arch(7.4f, 13.9f, 1f, 0);

            for (int i = 0; i < 74; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float x = side * (2.75f + Random() * 4.1f);
                float z = -1.1f + Random() * 17.4f;
                float size = 0.14f + Random() * (z < 6f ? 0.61f : 0.42f);
                rubble.Sphere(new(x, size * 0.31f, z), new(size, size * 0.72f, size * 0.87f),
                    Quaternion.Euler(Random() * 120f, Random() * 180f, Random() * 40f), Pick(stoneColors), 7, 5, true);
                if (i % 3 != 0) MossPatch(new(x, size * 0.86f, z), size * 0.57f, 6);

                float blade = 0.08f + Random() * 0.12f;
                var tuft = new Vector3(side * (2.40f + Random() * 4.2f), blade * 0.41f, Random() * 14f);
                for (int leaf = -1; leaf <= 1; leaf++)
                {
                    moss.Sphere(tuft + Vector3.right * (leaf * blade * 0.38f), new(blade * 0.18f, blade, blade * 0.16f),
                        Quaternion.Euler(0f, 0f, -leaf * 27f), Pick(mossColors), 5, 4);
                }
            }

            for (int i = 0; i < 36; i++)
            {
                int side = i % 2 == 0 ? -1 : 1;
                float x = side * (3.9f + Random() * 8.7f);
                float z = 8.4f + Random() * 14.8f;
                float height = 1.6f + Random() * 3.25f;
                woods.Block(new(x, height * 0.23f, z), new(0.065f * height, height * 0.31f, 0.075f * height),
                    Quaternion.identity, 0x5a4551);
                int color = Pick(pineColors);
                for (int tier = 0; tier < 4; tier++)
                {
                    float width = (0.24f - 0.041f * tier) * height;
                    woods.Profile(new(x + tier * height * 0.013f * side, height * (0.23f + tier * 0.16f), z),
                        new(width, height * 0.47f, width), Quaternion.Euler(0f, Random() * 170f, -4f * side), color,
                        Geometry.PineProfile, 9);
                }
            }

            Render(stage, paving, stone);
            Render(stage, rubble, stone);
            Render(stage, moss, stone);
            Render(stage, woods, stone);
            BuildLanterns();
        }

        private void BuildValleyRidges(Shader shader)
        {
            var ridges = new Geometry("Distant valley ridges");
            const int segments = 92;
            void Ridge(float distance, float elevation, float phase, int color)
            {
                Vector3 Heel(float a, float radius) => new(Mathf.Sin(a) * radius, -0.108f, Mathf.Cos(a) * radius);
                Vector3 Crest(float a)
                {
                    float height = elevation + 0.67f * Mathf.Sin(a * 5f + phase)
                        + 0.42f * Mathf.Sin(a * 9f + phase * 2f) + 0.23f * Mathf.Cos(a * 17f);
                    return new Vector3(Mathf.Sin(a) * distance, height, Mathf.Cos(a) * distance);
                }

                for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    float b = (i + 1) * Mathf.PI * 2f / segments;
                    Vector3 first = Crest(a);
                    Vector3 second = Crest(b);
                    ridges.Quad(Heel(a, distance - 4f), first, second, Heel(b, distance - 4f), color);
                    ridges.Quad(first, Heel(a, distance + 2.7f), Heel(b, distance + 2.7f), second, color);
                }
            }

            Ridge(18.7f, 1.6f, 2.17f, 0x654961);
            Ridge(24.7f, 2.5f, 4.1f, 0x795671);
            Ridge(30.7f, 3.55f, 1.3f, 0x8a6284);
            Material horizon = CreateMaterial(shader, "Dusk haze", 0.37f, 0.46f);
            Render(stage, ridges, horizon);
        }

        private void BuildLanterns()
        {
            var frames = new Geometry("Iron lantern cages");
            var flames = new Geometry("Courtyard lanterns");
            var halos = new Geometry("Lantern halos");
            var floorLights = new Geometry("Lantern pools");
            void Lantern(float x, float y, float z, float size, int side)
            {
                Vector3 center = new(x, y, z);
                flames.Profile(center, new Vector3(0.105f, 0.166f, 0.105f) * size, Quaternion.identity, 0xffcc70,
                    Geometry.LanternProfile, 8);
                for (int corner = 0; corner < 6; corner++)
                {
                    float a = corner * Mathf.PI / 3f;
                    Vector3 offset = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.108f * size;
                    frames.Block(center + offset, new Vector3(0.012f, 0.172f, 0.012f) * size,
                        Quaternion.identity, 0x57413e);
                }
                frames.Profile(center + Vector3.up * (0.171f * size), new Vector3(0.177f, 0.067f, 0.177f) * size,
                    Quaternion.identity, 0x5c4042, Geometry.LanternProfile, 8);
                frames.Profile(center + Vector3.down * (0.171f * size), new Vector3(0.145f, 0.053f, 0.145f) * size,
                    Quaternion.identity, 0x674344, Geometry.LanternProfile, 8);
                frames.Block(new(x + 0.29f * side, y * 0.50f, z + 0.10f), new(0.028f, y * 0.5f, 0.032f),
                    Quaternion.identity, 0x5c4142);
                frames.Tube(new Vector3[] { center + new Vector3(0.29f * side, 0.17f, 0.10f),
                    center + new Vector3(0.27f * side, 0.32f, 0.05f), center + new Vector3(0.13f * side, 0.39f, 0.03f),
                    center + new Vector3(0f, 0.31f, 0f), center + Vector3.up * 0.21f }, 0.024f, 0x765447);
                halos.Disc(center + Vector3.back * 0.145f, Vector3.right, Vector3.up, 0.93f * size, 0.83f * size, 0xffa256, 145);
                floorLights.Disc(new(x, 0.015f, z - 0.13f), Vector3.right, Vector3.forward, 1.8f * size, 1.48f * size, 0xffa657, 71);
            }

            Lantern(-2.93f, 1.55f, 4.57f, 1f, -1);
            Lantern(2.83f, 1.62f, 4.46f, 1f, 1);
            Lantern(-3.13f, 1.23f, 8.20f, 0.76f, 1);
            Lantern(3.49f, 1.30f, 8.78f, 0.80f, -1);
            Render(stage, frames, stone);
            Render(stage, flames, vivid);
            Render(stage, halos, softGlow);
            Render(stage, floorLights, softGlow);
        }

        private void BuildSparks()
        {
            sparkRoot = Group(stage, "Drawing sparks");
            for (int i = 0; i < SparkCount; i++)
            {
                sparks[i] = Render(sparkRoot, "Spark", softDisc, softGlow);
                sparks[i].gameObject.SetActive(false);
                sparkAge[i] = -1f;
            }
        }

        /// <summary>
        /// Call every frame. While emitting, sparks fly off the fingertip; faster strokes throw more.
        /// Hot sparks (fire charged) burn orange-red.
        /// </summary>
        public void TickSparks(bool emit, Vector3 tip, float speed, Spell? style, float amount, Vector3 head, float dt)
        {
            if (sparkRoot == null) return;
            viewHead = head;
            dt = Mathf.Clamp(dt, 0f, 0.05f);
            if (emit && dt > 0f)
            {
                Vector3 motion = hadSparkTip ? tip - lastSparkTip : Vector3.zero;
                float rate = Mathf.Lerp(12f, 110f, Mathf.Clamp01(speed / 0.7f)) * amount;
                sparkBudget += rate * dt;
                Vector3 inherit = motion / dt * 0.25f;
                while (sparkBudget >= 1f)
                {
                    sparkBudget -= 1f;
                    SpawnSpark(tip - motion * UnityEngine.Random.value, inherit, style);
                }
                lastSparkTip = tip;
                hadSparkTip = true;
            }
            else
            {
                sparkBudget = 0f;
                hadSparkTip = false;
            }

            for (int i = 0; i < SparkCount; i++)
            {
                if (sparkAge[i] < 0f) continue;
                sparkAge[i] += dt;
                float life = sparkAge[i] / sparkLife[i];
                if (life >= 1f)
                {
                    sparkAge[i] = -1f;
                    sparks[i].gameObject.SetActive(false);
                    continue;
                }
                Transform spark = sparks[i].transform;
                sparkVelocity[i] += Vector3.down * ((sparkStyle[i] == (int)Spell.Wind ? 0.25f : 1.4f) * dt);
                sparkVelocity[i] *= 1f - Mathf.Min(1f, 2.2f * dt);
                spark.position += sparkVelocity[i] * dt;
                Vector3 facing = spark.position - head;
                if (facing.sqrMagnitude > 0.0001f && sparkVelocity[i].sqrMagnitude > 1e-6f)
                    spark.rotation = Quaternion.LookRotation(facing, sparkVelocity[i]);
                float size = sparkSize[i] * (1f - life * 0.55f);
                float stretch = 1f + Mathf.Min(3.5f, sparkVelocity[i].magnitude * 9f);
                spark.localScale = new Vector3(size, size * stretch, 1f);
                SparkPalette(sparkStyle[i], out Color hotColor, out Color coolColor);
                Color color = Color.Lerp(hotColor, coolColor, life);
                Tint(sparks[i], color, Mathf.Pow(1f - life, 1.4f) * 0.95f);
            }
        }

        private static void SparkPalette(int style, out Color start, out Color end)
        {
            switch (style)
            {
                case (int)Spell.Fire: start = Rgb(0xffe0a0); end = Rgb(0xff4a1c); break;
                case (int)Spell.Lightning: start = Rgb(0xfffbe0); end = Rgb(0xffd04a); break;
                case (int)Spell.Ice: start = Rgb(0xffffff); end = Rgb(0x7fd0ff); break;
                case (int)Spell.Wind: start = Rgb(0xf6f0ff); end = Rgb(0xb69cff); break;
                case (int)Spell.Barrier: start = Rgb(0xf0fff8); end = Rgb(0x6fe8bd); break;
                default: start = Rgb(0xfff6d8); end = Rgb(0xffa83d); break;
            }
        }

        private void SpawnSpark(Vector3 at, Vector3 inherit, Spell? style, float speedScale = 1f, float sizeScale = 1f,
            float lifeScale = 1f)
        {
            int i = nextSpark;
            nextSpark = (nextSpark + 1) % SparkCount;
            sparkAge[i] = 0f;
            sparkLife[i] = UnityEngine.Random.Range(0.2f, 0.48f) * lifeScale;
            sparkSize[i] = UnityEngine.Random.Range(0.004f, 0.009f) * (style.HasValue ? 1.25f : 1f) * sizeScale;
            sparkStyle[i] = style.HasValue ? (int)style.Value : -1;
            sparkVelocity[i] = UnityEngine.Random.onUnitSphere * (UnityEngine.Random.Range(0.12f, 0.5f) * speedScale) +
                Vector3.up * (0.08f * speedScale) + inherit;
            sparks[i].transform.position = at;
            sparks[i].gameObject.SetActive(true);
        }

        private void BuildFlares()
        {
            flareRoot = Group(stage, "Release and impact flares");
            var ring = new Geometry("Shockwave ring");
            const int segments = 48;
            for (int k = 0; k < segments; k++)
            {
                float a0 = k * Mathf.PI * 2f / segments;
                float a1 = (k + 1) * Mathf.PI * 2f / segments;
                Vector3 i0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * 0.42f;
                Vector3 i1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * 0.42f;
                Vector3 o0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * 0.5f;
                Vector3 o1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * 0.5f;
                ring.Quad(i0, i1, o1, o0, 0xffffff);
            }
            ringMesh = Track(ring.CreateMesh());
            ringGlow = Track(new Material(glow) { name = "Shockwave light" });
            ringGlow.SetFloat("_Cull", (float)CullMode.Off);
            for (int i = 0; i < FlareCount; i++)
            {
                flares[i] = Render(flareRoot, "Flare", softDisc, softGlow);
                flareFilters[i] = flares[i].GetComponent<MeshFilter>();
                flares[i].gameObject.SetActive(false);
                flareAge[i] = -1f;
            }
        }

        /// <summary>Queues a billboarded flash (disc) or expanding shockwave (ring).</summary>
        private void Flare(Vector3 at, Color color, float life, float fromSize, float toSize, float alpha, bool ring,
            float delay = 0f)
        {
            int i = nextFlare;
            nextFlare = (nextFlare + 1) % FlareCount;
            flareAge[i] = 0f;
            flareDelay[i] = delay;
            flareLife[i] = life;
            flareFrom[i] = fromSize;
            flareTo[i] = toSize;
            flareAlpha[i] = alpha;
            flareColor[i] = color;
            flarePosition[i] = at;
            flareFilters[i].sharedMesh = ring ? ringMesh : softDisc;
            flares[i].sharedMaterial = ring ? ringGlow : softGlow;
            flares[i].gameObject.SetActive(false);
        }

        private void TickFlares(float dt)
        {
            for (int i = 0; i < FlareCount; i++)
            {
                if (flareAge[i] < 0f) continue;
                if (flareDelay[i] > 0f)
                {
                    flareDelay[i] -= dt;
                    continue;
                }
                flareAge[i] += dt;
                float life = flareAge[i] / flareLife[i];
                if (life >= 1f)
                {
                    flareAge[i] = -1f;
                    flares[i].gameObject.SetActive(false);
                    continue;
                }
                if (!flares[i].gameObject.activeSelf) flares[i].gameObject.SetActive(true);
                Transform flare = flares[i].transform;
                flare.position = flarePosition[i];
                Vector3 facing = flarePosition[i] - viewHead;
                if (facing.sqrMagnitude > 0.0001f) flare.rotation = Quaternion.LookRotation(facing, Vector3.up);
                float eased = 1f - (1f - life) * (1f - life) * (1f - life);
                flare.localScale = Vector3.one * Mathf.Lerp(flareFrom[i], flareTo[i], eased);
                Tint(flares[i], flareColor[i], flareAlpha[i] * (1f - life) * (1f - life * 0.4f));
            }
        }

        private void SparkBurst(Vector3 at, Spell? style, int count, float speed, float size, float life = 1f)
        {
            for (int k = 0; k < count; k++)
                SpawnSpark(at, Vector3.zero, style, speed * UnityEngine.Random.Range(0.6f, 1.4f), size, life);
        }

        private static Color SpellColor(Spell spell) => spell switch
        {
            Spell.Fire => Rgb(0xff7a3d),
            Spell.Lightning => Rgb(0xffd96a),
            Spell.Ice => Rgb(0x8fdcff),
            Spell.Wind => Rgb(0xc7aaff),
            Spell.Rock => Rgb(0xe0a985),
            _ => Rgb(0x9ff2ce)
        };

        // Burst at the hand the moment the spell is let go.
        private void ReleaseBurst(Spell spell, Vector3 from, int level)
        {
            float power = LevelScale(level);
            Color color = SpellColor(spell);
            Flare(from, Color.white, 0.16f, 0.03f, 0.16f * power, 0.95f, false);
            Flare(from, color, 0.3f, 0.05f, 0.3f * power, 0.85f, false);
            Flare(from, color, 0.42f, 0.03f, 0.42f * power, 0.9f, true);
            if (level >= 3) Flare(from, Color.Lerp(color, Color.white, 0.5f), 0.55f, 0.03f, 0.65f * power, 0.7f, true, 0.06f);
            Spell? style = spell == Spell.Rock ? null : spell;
            SparkBurst(from, style, 18 + 8 * level, 1.6f * power, 1.3f, 1.1f);
        }

        // Explosion where the spell lands: core flash, double shockwave, flying debris sparks.
        private void ImpactBurst(Projectile shot)
        {
            float power = LevelScale(shot.Level);
            Vector3 at = shot.End - (shot.End - shot.Start).normalized * 0.06f;
            Color color = SpellColor(shot.Spell);
            Color bright = Color.Lerp(color, Color.white, 0.55f);
            Flare(at, Color.white, 0.22f, 0.25f, 0.9f * power, 0.95f, false);
            Flare(at, color, 0.5f, 0.4f, 1.9f * power, 0.75f, false);
            Flare(at, bright, 0.55f, 0.2f, 2.6f * power, 0.9f, true);
            Flare(at, color, 0.75f, 0.15f, 3.6f * power, 0.6f, true, 0.08f);
            if (shot.Level >= 2) Flare(at, bright, 0.9f, 0.1f, 4.8f * power, 0.45f, true, 0.16f);
            Spell? style = shot.Spell == Spell.Rock ? null : shot.Spell;
            int count = 26 + 14 * shot.Level;
            switch (shot.Spell)
            {
                case Spell.Fire:
                    SparkBurst(at, Spell.Fire, count, 7f * power, 5f, 1.6f);
                    SparkBurst(at, Spell.Fire, count / 2, 3f, 7f, 2.2f); // slow ember shower
                    break;
                case Spell.Ice:
                    SparkBurst(at, Spell.Ice, count, 6f * power, 6.5f, 1.3f);
                    break;
                case Spell.Lightning:
                    SparkBurst(at, Spell.Lightning, count + 10, 9f * power, 3.5f, 0.8f);
                    break;
                case Spell.Wind:
                    SparkBurst(at, Spell.Wind, count, 5.5f * power, 4.5f, 1.8f);
                    break;
                default:
                    SparkBurst(at, style, count, 5f * power, 6f, 1.5f);
                    break;
            }
        }

        private void BuildChargeEffect()
        {
            chargeRoot = Group(stage, "Fire charge");
            chargeHalo = Render(chargeRoot, "Charge halo", softDisc, softGlow);
            for (int i = 0; i < FlameCount; i++)
                chargeFlames[i] = Render(chargeRoot, "Flame tongue", softDisc, softGlow);
            chargeCore = Render(chargeRoot, "Charge core", softDisc, softGlow);
            for (int i = 0; i < EmberCount; i++)
                chargeEmbers[i] = Render(chargeRoot, "Ember", softDisc, softGlow);
            chargeBurst = Render(chargeRoot, "Charge burst", softDisc, softGlow);
            for (int i = 0; i < chargeArcs.Length; i++)
            {
                chargeArcs[i] = Line(chargeRoot, "Charge arc", ArcPoints, 0.0045f);
                chargeArcs[i].gameObject.SetActive(false);
            }
            chargeRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// Shows the charged-spell effect at the fingertip, styled per element. Pass null to let it fade.
        /// Call every frame; a change of spell plays the charge-complete burst.
        /// </summary>
        public void SetCharge(Spell? spell, int level, Vector3 position, Vector3 head, float dt)
        {
            if (chargeRoot == null) return;
            viewHead = head;
            dt = Mathf.Clamp(dt, 0f, 0.05f);
            if (spell.HasValue && (spell != chargeSpell || level != chargeLevel)) chargeAge = 0f;
            if (spell.HasValue) chargeLevel = Mathf.Max(1, level);
            if (spell.HasValue) chargeSpell = spell;
            chargeOpacity = spell.HasValue ? 1f : Mathf.Max(0f, chargeOpacity - dt * 9f);
            if (chargeOpacity <= 0f)
            {
                chargeSpell = null;
                if (chargeRoot.gameObject.activeSelf) chargeRoot.gameObject.SetActive(false);
                return;
            }

            chargeAge += dt;
            if (!chargeRoot.gameObject.activeSelf) chargeRoot.gameObject.SetActive(true);
            if (spell.HasValue) chargeRoot.position = position;
            Vector3 facing = chargeRoot.position - head;
            if (facing.sqrMagnitude > 0.0001f) chargeRoot.rotation = Quaternion.LookRotation(facing, Vector3.up);
            // Each power level makes the charge visibly bigger.
            chargeRoot.localScale = Vector3.one * LevelScale(chargeLevel);

            Spell style = chargeSpell ?? Spell.Fire;
            float a = chargeOpacity;
            float t = chargeAge;
            // Pop: everything swells briefly when the charge completes.
            float pop = 1f + 0.55f * Mathf.Exp(-t * 9f);
            float breathe = 1f + 0.08f * Mathf.Sin(t * 7.3f) + 0.05f * Mathf.Sin(t * 13.1f);
            ChargePalette(style, out Color halo, out Color core, out Color burstColor);

            float haloAlpha = style == Spell.Lightning ? 0.3f + 0.25f * Hash(Mathf.Floor(t * 24f)) : 0.42f;
            chargeHalo.transform.localPosition = new Vector3(0f, style == Spell.Fire ? 0.008f : 0f, 0.004f);
            chargeHalo.transform.localScale = Vector3.one * 0.15f * pop * breathe;
            Tint(chargeHalo, halo, haloAlpha * a);

            chargeCore.transform.localPosition = new Vector3(0f, style == Spell.Fire ? 0.004f : 0f, -0.002f);
            chargeCore.transform.localScale = Vector3.one * 0.042f * pop * breathe;
            Tint(chargeCore, core, 0.95f * a);

            switch (style)
            {
                case Spell.Fire: FireCharge(t, a, pop); break;
                case Spell.Lightning: LightningCharge(t, a, pop); break;
                case Spell.Ice: IceCharge(t, a, pop); break;
                case Spell.Wind: WindCharge(t, a, pop); break;
                default: WardCharge(t, a, pop); break;
            }
            foreach (LineRenderer arc in chargeArcs)
                if (arc.gameObject.activeSelf != (style == Spell.Lightning)) arc.gameObject.SetActive(style == Spell.Lightning);

            float burst = Mathf.Clamp01(t / ChargeBurstTime);
            bool bursting = burst < 1f;
            if (chargeBurst.gameObject.activeSelf != bursting) chargeBurst.gameObject.SetActive(bursting);
            if (bursting)
            {
                chargeBurst.transform.localPosition = new Vector3(0f, 0f, -0.004f);
                chargeBurst.transform.localScale = Vector3.one * Mathf.Lerp(0.05f, 0.36f, 1f - (1f - burst) * (1f - burst));
                Tint(chargeBurst, burstColor, (1f - burst) * 0.9f);
            }
        }

        private static float LevelScale(int level) =>
            1f + (Mathf.Clamp(level, 1, RuneveilDuel.MaxChargeLevel) - 1f) / (RuneveilDuel.MaxChargeLevel - 1f);

        private static void ChargePalette(Spell spell, out Color halo, out Color core, out Color burst)
        {
            switch (spell)
            {
                case Spell.Lightning: halo = Rgb(0xffc23a); core = Rgb(0xfffbe0); burst = Rgb(0xffe27a); break;
                case Spell.Ice: halo = Rgb(0x4fb8ff); core = Rgb(0xf0fbff); burst = Rgb(0xa3e4ff); break;
                case Spell.Wind: halo = Rgb(0x9b7cff); core = Rgb(0xf4efff); burst = Rgb(0xcfb6ff); break;
                case Spell.Barrier: halo = Rgb(0x5ee8b5); core = Rgb(0xeafff6); burst = Rgb(0x9ff2ce); break;
                default: halo = Rgb(0xff5a2a); core = Rgb(0xfff1b8); burst = Rgb(0xffb347); break;
            }
        }

        private static float Hash(float x) => Mathf.Repeat(Mathf.Sin(x * 12.9898f) * 43758.5453f, 1f);

        private void Part(MeshRenderer part, Vector3 position, float angle, Vector2 size, Color color, float alpha)
        {
            Transform item = part.transform;
            item.localPosition = position;
            item.localRotation = Quaternion.Euler(0f, 0f, angle);
            item.localScale = new Vector3(size.x, size.y, 1f);
            Tint(part, color, alpha);
        }

        // Fire: flickering tongues with rising embers.
        private void FireCharge(float t, float a, float pop)
        {
            for (int i = 0; i < FlameCount; i++)
            {
                float phase = i * 1.37f;
                float flicker = 0.75f + 0.25f * Mathf.Sin(t * (11f + i * 2.3f) + phase) +
                    0.12f * Mathf.Sin(t * (23f + i * 3.1f) + phase * 2f);
                float x = (i - (FlameCount - 1) * 0.5f) * 0.0105f + 0.003f * Mathf.Sin(t * 6f + phase);
                float height = (i == FlameCount / 2 ? 0.085f : 0.06f - Mathf.Abs(i - FlameCount / 2) * 0.008f) * flicker * pop;
                Part(chargeFlames[i], new Vector3(x, height * 0.42f, 0.001f * i), -x * 380f + 6f * Mathf.Sin(t * 9f + phase),
                    new Vector2(0.032f * (0.9f + 0.1f * flicker), height),
                    Color.Lerp(Rgb(0xff7a2e), Rgb(0xffc04d), 0.5f + 0.5f * Mathf.Sin(phase)), 0.85f * a);
            }
            for (int i = 0; i < EmberCount; i++)
            {
                float speed = 0.55f + 0.17f * (i % 3);
                float rise = Mathf.Repeat(t * speed + i / (float)EmberCount, 1f);
                float sway = Mathf.Sin(t * 3.1f + i * 2.2f) * 0.012f + (i - EmberCount * 0.5f) * 0.004f;
                float size = Mathf.Lerp(0.014f, 0.005f, rise);
                Part(chargeEmbers[i], new Vector3(sway, 0.012f + rise * 0.11f, -0.003f), 0f, new Vector2(size, size),
                    Color.Lerp(Rgb(0xffd27a), Rgb(0xff4d1f), rise), (1f - rise) * 0.9f * a);
            }
        }

        // Lightning: jagged arcs that re-strike constantly, with crackling sparks.
        private void LightningCharge(float t, float a, float pop)
        {
            if (t >= nextArcShape || t < 0.02f)
            {
                nextArcShape = t + 0.055f;
                for (int j = 0; j < chargeArcs.Length; j++)
                {
                    float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                    float length = UnityEngine.Random.Range(0.045f, 0.08f) * pop;
                    Vector3 end = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * length;
                    Vector3 side = new Vector3(-end.y, end.x, 0f).normalized;
                    for (int k = 0; k < ArcPoints; k++)
                    {
                        float p = k / (ArcPoints - 1f);
                        float jitter = k == 0 || k == ArcPoints - 1 ? 0f : UnityEngine.Random.Range(-0.011f, 0.011f);
                        arcShapes[j, k] = end * p + side * jitter;
                    }
                }
            }
            for (int j = 0; j < chargeArcs.Length; j++)
            {
                LineRenderer arc = chargeArcs[j];
                for (int k = 0; k < ArcPoints; k++)
                    arc.SetPosition(k, chargeRoot.TransformPoint(arcShapes[j, k]));
                Tint(arc, Color.Lerp(Rgb(0xfff3a8), Color.white, Hash(j + Mathf.Floor(t * 30f))),
                    (0.55f + 0.45f * Hash(j * 7f + Mathf.Floor(t * 40f))) * a);
            }
            for (int i = 0; i < FlameCount; i++)
            {
                float seed = Mathf.Floor(t * 20f) * 3.1f + i * 17.3f;
                float angle = Hash(seed) * Mathf.PI * 2f;
                float r = 0.025f + Hash(seed + 1f) * 0.03f;
                float size = 0.006f + Hash(seed + 2f) * 0.008f;
                Part(chargeFlames[i], new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, -0.002f), 0f,
                    new Vector2(size, size), Rgb(0xfffbe0), Hash(seed + 3f) * 0.95f * a);
            }
            for (int i = 0; i < EmberCount; i++)
            {
                float seed = Mathf.Floor(t * 32f) * 5.7f + i * 9.1f;
                float angle = Hash(seed) * Mathf.PI * 2f;
                float r = 0.01f + Hash(seed + 4f) * 0.05f;
                Part(chargeEmbers[i], new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, -0.003f), angle * Mathf.Rad2Deg,
                    new Vector2(0.003f, 0.012f), Rgb(0xffe27a), (Hash(seed + 5f) > 0.4f ? 0.9f : 0f) * a);
            }
        }

        // Ice: crystal shards slowly orbiting the core, with drifting snow.
        private void IceCharge(float t, float a, float pop)
        {
            for (int i = 0; i < FlameCount; i++)
            {
                float angle = t * 1.1f + i * Mathf.PI * 2f / FlameCount;
                float r = (0.045f + 0.004f * Mathf.Sin(t * 2f + i)) * pop;
                Part(chargeFlames[i], new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, 0.001f * i),
                    angle * Mathf.Rad2Deg - 90f, new Vector2(0.012f, 0.05f * pop),
                    Color.Lerp(Rgb(0xc9f1ff), Color.white, 0.5f + 0.5f * Mathf.Sin(t * 3f + i)), 0.9f * a);
            }
            for (int i = 0; i < EmberCount; i++)
            {
                float fall = Mathf.Repeat(t * 0.35f + i / (float)EmberCount, 1f);
                float x = Mathf.Sin(i * 2.3f + t * 1.3f) * 0.045f;
                Part(chargeEmbers[i], new Vector3(x, 0.06f - fall * 0.13f, -0.003f), 0f, new Vector2(0.008f, 0.008f),
                    Rgb(0xf0fbff), Mathf.Sin(Mathf.PI * fall) * 0.9f * a);
            }
        }

        // Wind: fast swirling wisps and motes spiraling outward.
        private void WindCharge(float t, float a, float pop)
        {
            for (int i = 0; i < FlameCount; i++)
            {
                float angle = t * 6.5f + i * Mathf.PI * 2f / FlameCount;
                float r = (0.04f + 0.008f * Mathf.Sin(t * 3f + i)) * pop;
                Part(chargeFlames[i], new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, 0.001f * i),
                    angle * Mathf.Rad2Deg, new Vector2(0.01f, 0.05f), Rgb(0xe2d6ff), 0.75f * a);
            }
            for (int i = 0; i < EmberCount; i++)
            {
                float phase = Mathf.Repeat(t * 0.8f + i / (float)EmberCount, 1f);
                float angle = t * 5f + i * 0.9f + phase * 4f;
                float r = 0.015f + phase * 0.07f;
                float size = Mathf.Lerp(0.009f, 0.004f, phase);
                Part(chargeEmbers[i], new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, -0.003f), 0f,
                    new Vector2(size, size), Rgb(0xf6f0ff), (1f - phase) * 0.8f * a);
            }
        }

        // Ward: a slowly turning ring of pulsing nodes with rising motes.
        private void WardCharge(float t, float a, float pop)
        {
            for (int i = 0; i < FlameCount; i++)
            {
                float angle = t * 0.9f + i * Mathf.PI * 2f / FlameCount;
                float r = 0.05f * pop;
                float size = 0.017f * (1f + 0.25f * Mathf.Sin(t * 4f + i * 1.3f));
                Part(chargeFlames[i], new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, 0.001f * i), 0f,
                    new Vector2(size, size), Rgb(0xb5ffe8), 0.85f * a);
            }
            for (int i = 0; i < EmberCount; i++)
            {
                float rise = Mathf.Repeat(t * 0.4f + i / (float)EmberCount, 1f);
                float x = Mathf.Sin(i * 2.7f) * 0.04f + Mathf.Sin(t * 1.7f + i) * 0.006f;
                Part(chargeEmbers[i], new Vector3(x, -0.03f + rise * 0.1f, -0.003f), 0f, new Vector2(0.007f, 0.007f),
                    Rgb(0x9ff2ce), Mathf.Sin(Mathf.PI * rise) * 0.8f * a);
            }
        }

        private void BuildEffects()
        {
            BuildChargeEffect();
            BuildSparks();
            BuildFlares();
            wardRoot = Group(stage, "Mint ward");
            var shell = new Geometry("Ward hemisphere");
            shell.Ward(new(0f, 0.13f, 0.29f), new(1.80f, 1.29f, 1.43f));
            wardShell = Render(wardRoot, shell, ward);
            var edge = new Geometry("Ward crest");
            var crest = new Vector3[33];
            for (int i = 0; i < crest.Length; i++)
            {
                float x = i / (crest.Length - 1f) * 1.94f - 0.97f;
                float radius = Mathf.Sqrt(1f - x * x);
                float angle = Mathf.Acos(0.76f * radius) - 0.155f;
                crest[i] = new Vector3(x * 1.80f, 0.13f + Mathf.Cos(angle) * 1.29f * radius,
                    0.29f + Mathf.Sin(angle) * 1.43f * radius);
            }
            edge.Tube(crest, 0.012f, 0xffffff, 7);
            wardEdges = Render(wardRoot, edge, glow);
            var ripples = new Geometry("Ward impact ring");
            ripples.Tube(Arc(Vector3.zero, 0.26f, 0.26f, 0f, 360f, 26), 0.008f, 0xffffff, 6);
            wardFlash = Render(wardRoot, ripples, glow);
            wardRipple = wardFlash.transform;
            wardRipple.localPosition = new Vector3(0f, 0.85f, 1.51f);

            var fire = new Geometry("Flame projectile");
            fire.Sphere(Vector3.zero, Vector3.one, Quaternion.identity, 0xffb06e, 9, 7, true);
            fireShape = Track(fire.CreateMesh());
            var ice = new Geometry("Ice spear");
            ice.Profile(Vector3.zero, Vector3.one, Quaternion.Euler(90f, 0f, 0f), 0xe8f7ff, Geometry.GemProfile, 5);
            iceShape = Track(ice.CreateMesh());
            var rock = new Geometry("Hewn rock projectile");
            rock.Sphere(Vector3.zero, Vector3.one, Quaternion.identity, 0xf5c4a4, 8, 6, true);
            rockShape = Track(rock.CreateMesh());
            var wind = new Geometry("Wind blade");
            wind.Tube(Arc(Vector3.zero, 0.36f, 0.75f, -52f, 62f, 14), 0.025f, 0xe9d5ff, 6, 0.2f);
            wind.Tube(Arc(Vector3.back * 0.075f, 0.30f, 0.63f, -31f, 46f, 12), 0.012f, 0xc5abff, 5, 0.25f);
            windShape = Track(wind.CreateMesh());

            projectiles = new Projectile[8];
            for (int i = 0; i < projectiles.Length; i++)
            {
                Transform root = Group(stage, "Spell effect");
                MeshRenderer projectileRenderer = Render(root, "Projectile", fireShape, vivid);
                var bolt = Line(root, "Lightning bolt", 10, 0.044f);
                var trail = Line(root, "Spell trail", 8, 0.038f);
                MeshRenderer flash = Render(root, "Impact flash", softDisc, softGlow);
                Transform flameRoot = Group(root, "Fireball flames");
                var flames = new MeshRenderer[FireballFlames];
                for (int k = 0; k < flames.Length; k++)
                    flames[k] = Render(flameRoot, "Fireball flame", softDisc, softGlow);
                projectiles[i] = new Projectile(root, projectileRenderer, bolt, trail, flash)
                {
                    Glow = Render(root, "Projectile glow", softDisc, softGlow),
                    FlameRoot = flameRoot,
                    Flames = flames
                };
            }
        }

        private void TickEffects(float dt, bool shield)
        {
            wardOpacity = Damp(wardOpacity, shield ? 1f : 0f, 19f, dt);
            wardFlashTime = Mathf.Max(0f, wardFlashTime - dt * 2.65f);
            float visibility = Mathf.Max(wardOpacity, wardFlashTime);
            wardRoot.gameObject.SetActive(visibility > 0.015f);
            if (wardRoot.gameObject.activeSelf)
            {
                Tint(wardShell, Rgb(0xa5ffe0), visibility * 0.26f + wardFlashTime * 0.11f);
                Tint(wardEdges, Rgb(0x8ff8d3), visibility * 0.72f);
                wardRipple.localScale = Vector3.one * (0.46f + (1f - wardFlashTime) * 3.4f);
                wardRipple.gameObject.SetActive(wardFlashTime > 0.12f);
                Tint(wardFlash, Rgb(0xb5ffe8), wardFlashTime * 0.77f);
            }

            TickFlares(dt);
            foreach (Projectile shot in projectiles)
            {
                if (shot.Age < 0f) continue;
                shot.Age += dt;
                float progress = shot.Age / shot.Duration;
                float after = shot.Age - shot.Duration;
                if (progress < 1f)
                {
                    MoveProjectile(shot, progress);
                    EmitTrailSparks(shot, progress, dt);
                    continue;
                }
                if (!shot.Impacted)
                {
                    shot.Impacted = true;
                    ImpactBurst(shot);
                }
                if (after > 0.24f)
                {
                    shot.Age = -1f;
                    shot.Root.gameObject.SetActive(false);
                    continue;
                }
                shot.Body.gameObject.SetActive(false);
                shot.Bolt.gameObject.SetActive(false);
                shot.Trail.gameObject.SetActive(false);
                shot.Glow.gameObject.SetActive(false);
                shot.FlameRoot.gameObject.SetActive(false);
                shot.Flash.gameObject.SetActive(true);
                shot.Flash.transform.position = shot.End - (shot.End - shot.Start).normalized * 0.045f;
                shot.Flash.transform.localScale = Vector3.one * (0.37f + after * 5f) * LevelScale(shot.Level);
                Tint(shot.Flash, shot.Color, 0.88f * (1f - after / 0.24f));
            }
        }

        private void EmitTrailSparks(Projectile shot, float progress, float dt)
        {
            shot.SparkBudget += dt * (55f + 30f * shot.Level);
            Spell? style = shot.Spell == Spell.Rock ? null : shot.Spell;
            while (shot.SparkBudget >= 1f)
            {
                shot.SparkBudget -= 1f;
                Vector3 at = shot.Spell == Spell.Lightning
                    ? shot.Bolt.GetPosition(UnityEngine.Random.Range(0, shot.Bolt.positionCount))
                    : ShotPosition(shot, Mathf.Max(0f, progress - UnityEngine.Random.value * 0.04f));
                float distance = Vector3.Distance(at, viewHead);
                SpawnSpark(at, Vector3.zero, style, 1.2f + distance * 0.25f, 1.2f + distance * 0.55f, 0.9f);
            }
        }

        // A fireball is drawn as flickering tongues of flame that stream back from the direction of travel.
        private void MoveFireball(Projectile shot, float progress, Vector3 at)
        {
            shot.Body.gameObject.SetActive(false);
            Vector3 facing = at - viewHead;
            Vector3 travel = ShotPosition(shot, Mathf.Min(1f, progress + 0.03f)) - at;
            Vector3 back = facing.sqrMagnitude > 0.0001f ? Vector3.ProjectOnPlane(-travel, facing.normalized) : -travel;
            // Flames lick backwards and a little upwards, like a real fire in motion.
            back = back.sqrMagnitude > 1e-8f ? back.normalized : Vector3.up;
            back = (back + Vector3.up * 0.35f).normalized;
            if (facing.sqrMagnitude > 0.0001f) shot.FlameRoot.rotation = Quaternion.LookRotation(facing, back);
            shot.FlameRoot.position = at;

            float size = shot.Size.x;
            float t = shot.Age;
            for (int k = 0; k < shot.Flames.Length; k++)
            {
                float phase = k * 1.91f;
                float flicker = 0.7f + 0.3f * Mathf.Sin(t * (19f + k * 3.7f) + phase) +
                    0.15f * Mathf.Sin(t * (37f + k * 5.3f) + phase * 1.7f);
                bool inner = k < 3;
                float spread = inner ? (k - 1) * 0.18f : ((k - 3) - 2.5f) * 0.3f;
                float length = size * (inner ? 2.1f : 3.1f - Mathf.Abs(spread) * 1.6f) * flicker;
                float width = size * (inner ? 0.85f : 1.05f) * (0.85f + 0.15f * flicker);
                float sway = Mathf.Sin(t * 13f + phase) * 0.12f;
                Transform flame = shot.Flames[k].transform;
                flame.localPosition = new Vector3((spread + sway * 0.5f) * size, length * 0.38f, inner ? -0.01f : 0.01f * k);
                flame.localRotation = Quaternion.Euler(0f, 0f, -(spread + sway) * 40f);
                flame.localScale = new Vector3(width, length, 1f);
                Color color = inner
                    ? Color.Lerp(Rgb(0xfff0b0), Rgb(0xffc04d), k * 0.4f)
                    : Color.Lerp(Rgb(0xff8a2e), Rgb(0xff3a14), 0.5f + 0.5f * Mathf.Sin(phase + t * 6f));
                Tint(shot.Flames[k], color, (inner ? 0.95f : 0.8f) * (0.8f + 0.2f * flicker));
            }
        }

        private void MoveProjectile(Projectile shot, float progress)
        {
            bool bolts = shot.Spell == Spell.Lightning;
            shot.Trail.gameObject.SetActive(!bolts && progress > 0.01f);
            shot.Glow.gameObject.SetActive(!bolts);
            if (bolts)
            {
                shot.FlameRoot.gameObject.SetActive(false);
                // Re-strike the bolt every few frames so it crackles in flight.
                if (UnityEngine.Random.value < 0.5f) ShapeBolt(shot.Bolt, shot.Start, shot.End, UnityEngine.Random.value * 10f);
                Tint(shot.Bolt, Color.Lerp(shot.Color, Color.white, 0.57f), 0.90f - progress * 0.55f);
                return;
            }
            Vector3 at = ShotPosition(shot, progress);
            bool fire = shot.Spell == Spell.Fire;
            shot.FlameRoot.gameObject.SetActive(fire);
            if (fire) MoveFireball(shot, progress, at);
            Transform glowing = shot.Glow.transform;
            glowing.position = at;
            Vector3 facing = at - viewHead;
            if (facing.sqrMagnitude > 0.0001f) glowing.rotation = Quaternion.LookRotation(facing, Vector3.up);
            float pulse = 1f + 0.12f * Mathf.Sin(shot.Age * 40f);
            // Keep the fireball's aura faint so the flame shapes, not a round disc, read first.
            glowing.localScale = Vector3.one * (shot.Size.x * (fire ? 2.8f : 4.2f) * pulse);
            Tint(shot.Glow, shot.Color, fire ? 0.35f : 0.7f);
            Vector3 ahead = ShotPosition(shot, progress + 0.025f);
            shot.Body.position = at;
            if ((ahead - at).sqrMagnitude > 0.0001f) shot.Body.rotation = Quaternion.LookRotation(ahead - at);
            for (int i = 0; i < shot.Trail.positionCount; i++)
                shot.Trail.SetPosition(i, ShotPosition(shot, Mathf.Max(0f, progress - 0.053f * i)));
        }

        private static Vector3 ShotPosition(Projectile shot, float t)
        {
            return Vector3.LerpUnclamped(shot.Start, shot.End, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * shot.Arc);
        }

        private static void ShapeBolt(LineRenderer line, Vector3 start, Vector3 end, float seed = 0f)
        {
            Vector3 cross = Vector3.Cross(end - start, Vector3.up).normalized;
            for (int i = 0; i < line.positionCount; i++)
            {
                float t = i / (line.positionCount - 1f);
                float noise = Mathf.Sin(t * 29f + seed) * Mathf.Sin(t * Mathf.PI);
                line.SetPosition(i, Vector3.Lerp(start, end, t) + cross * (noise * 0.18f) +
                    Vector3.up * (Mathf.Cos(t * 42f + seed * 1.7f) * Mathf.Sin(t * Mathf.PI) * 0.15f));
            }
        }

        private LineRenderer Line(Transform parent, string label, int count, float width)
        {
            LineRenderer line = Group(parent, label).gameObject.AddComponent<LineRenderer>();
            line.sharedMaterial = glow;
            line.positionCount = count;
            line.useWorldSpace = true;
            line.startWidth = width;
            line.endWidth = width * 0.21f;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private Material CreateMaterial(Shader shader, string label, float shading, float fog, BlendMode blend = BlendMode.Zero)
        {
            var material = Track(new Material(shader) { name = label });
            material.SetFloat("_Shading", shading);
            material.SetFloat("_Fog", fog);
            if (blend != BlendMode.Zero)
            {
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)blend);
                material.SetFloat("_ZWrite", 0f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            return material;
        }

        private MeshRenderer Render(Transform parent, Geometry geometry, Material material)
        {
            return Render(parent, geometry.Name, Track(geometry.CreateMesh()), material);
        }

        private static MeshRenderer Render(Transform parent, string label, Mesh mesh, Material material)
        {
            GameObject part = Group(parent, label).gameObject;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return renderer;
        }

        private void Tint(Renderer renderer, Color color, float opacity)
        {
            color.a = opacity;
            properties.SetColor(BaseColor, color);
            renderer.SetPropertyBlock(properties);
        }

        private T Track<T>(T asset) where T : UnityEngine.Object
        {
            generatedAssets.Add(asset);
            return asset;
        }

        private static Transform Group(Transform parent, string label)
        {
            Transform group = new GameObject(label).transform;
            group.SetParent(parent, false);
            return group;
        }

        private static Vector3[] Arc(Vector3 center, float xRadius, float yRadius, float from, float to, int segments)
        {
            var positions = new Vector3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Lerp(from, to, i / (float)segments) * Mathf.Deg2Rad;
                positions[i] = center + new Vector3(Mathf.Cos(angle) * xRadius, Mathf.Sin(angle) * yRadius, 0f);
            }
            return positions;
        }

        private static float Damp(float from, float to, float rate, float dt)
        {
            return Mathf.Lerp(from, to, 1f - Mathf.Exp(-rate * dt));
        }

        private static Color32 Rgb(int hex, byte alpha = 255)
        {
            return new Color32((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex, alpha);
        }

        private sealed class Arm
        {
            private readonly Vector3 shoulder;
            public readonly Transform Upper;
            public readonly Transform Lower;
            public readonly Transform Fist;
            public Vector3 Elbow { get; private set; }

            public Arm(Vector3 shoulder, Transform upper, Transform lower, Transform fist)
            {
                this.shoulder = shoulder;
                Upper = upper;
                Lower = lower;
                Fist = fist;
            }

            public void Pose(Vector3 elbow, Vector3 wrist, Vector3 fistEuler)
            {
                Elbow = elbow;
                PlaceSegment(Upper, shoulder, elbow, 0.55f, 0.51f);
                PlaceSegment(Lower, elbow, wrist, 0.43f, 0.47f);
                Fist.SetLocalPositionAndRotation(wrist, Quaternion.Euler(fistEuler));
            }

            private static void PlaceSegment(Transform part, Vector3 start, Vector3 end, float pivot, float length)
            {
                Vector3 direction = end - start;
                Vector3 facing = Vector3.ProjectOnPlane(Vector3.forward, direction);
                part.SetLocalPositionAndRotation(Vector3.Lerp(start, end, pivot), Quaternion.LookRotation(facing, direction));
                part.localScale = new Vector3(1f, Mathf.Max(0.6f, direction.magnitude / length), 1f);
            }
        }

        private sealed class Projectile
        {
            public readonly Transform Root;
            public readonly Transform Body;
            public readonly MeshRenderer Renderer;
            public readonly MeshFilter Filter;
            public readonly LineRenderer Bolt;
            public readonly LineRenderer Trail;
            public readonly MeshRenderer Flash;
            public MeshRenderer Glow;
            public Transform FlameRoot;
            public MeshRenderer[] Flames;
            public int Level = 1;
            public bool Impacted;
            public float SparkBudget;
            public Vector3 Start;
            public Vector3 End;
            public Vector3 Size;
            public float Age;
            public float Duration;
            public float Arc;
            public Color Color;
            public Spell Spell;

            public Projectile(Transform root, MeshRenderer renderer, LineRenderer bolt, LineRenderer trail, MeshRenderer flash)
            {
                Root = root;
                Renderer = renderer;
                Body = renderer.transform;
                Filter = Body.GetComponent<MeshFilter>();
                Bolt = bolt;
                Trail = trail;
                Flash = flash;
            }
        }

        private sealed class HitSurface
        {
            private const int LeafSize = 8;
            private static readonly IComparer<Triangle>[] CenterOrder = {
                Comparer<Triangle>.Create((a, b) => a.Center.x.CompareTo(b.Center.x)),
                Comparer<Triangle>.Create((a, b) => a.Center.y.CompareTo(b.Center.y)),
                Comparer<Triangle>.Create((a, b) => a.Center.z.CompareTo(b.Center.z))
            };
            private readonly Transform part;
            private readonly Triangle[] triangles;
            private readonly Node[] nodes;
            public readonly ArenaTarget Target;

            public HitSurface(Transform part, Vector3[] vertices, IReadOnlyList<int> indices, ArenaTarget target)
            {
                this.part = part;
                Target = target;
                var surface = new List<Triangle>(indices.Count / 3);
                for (int i = 0; i < indices.Count; i += 3)
                {
                    var triangle = new Triangle(vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]]);
                    if (Vector3.Cross(triangle.AB, triangle.AC).sqrMagnitude > 1e-16f) surface.Add(triangle);
                }
                triangles = surface.ToArray();
                var partitions = new List<Node>(Mathf.Max(1, triangles.Length / 4));
                if (triangles.Length > 0) Partition(partitions, 0, triangles.Length);
                nodes = partitions.ToArray();
            }

            private int Partition(List<Node> partitions, int first, int count)
            {
                Bounds bounds = new Bounds(triangles[first].A, Vector3.zero);
                for (int i = first; i < first + count; i++)
                {
                    Triangle triangle = triangles[i];
                    bounds.Encapsulate(triangle.A);
                    bounds.Encapsulate(triangle.A + triangle.AB);
                    bounds.Encapsulate(triangle.A + triangle.AC);
                }
                bounds.Expand(0.0001f);

                int index = partitions.Count;
                partitions.Add(default);
                if (count <= LeafSize)
                {
                    partitions[index] = new Node(bounds, first, count, 0);
                    return index;
                }

                Vector3 size = bounds.size;
                int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                Array.Sort(triangles, first, count, CenterOrder[axis]);
                int half = count / 2;
                int left = Partition(partitions, first, half);
                int right = Partition(partitions, first + half, count - half);
                partitions[index] = new Node(bounds, left, 0, right);
                return index;
            }

            public float Intersect(Ray ray, float limit)
            {
                if (nodes.Length == 0) return limit;
                Matrix4x4 inverse = part.worldToLocalMatrix;
                Vector3 origin = inverse.MultiplyPoint3x4(ray.origin);
                // Keep the original world-ray parameter when a moving limb is scaled.
                Vector3 direction = inverse.MultiplyVector(ray.direction);
                return IntersectNode(0, origin, direction, limit);
            }

            private float IntersectNode(int index, Vector3 origin, Vector3 direction, float limit)
            {
                Node node = nodes[index];
                if (!Crosses(node.Bounds, origin, direction, limit)) return limit;
                if (node.Count == 0)
                {
                    limit = IntersectNode(node.First, origin, direction, limit);
                    return IntersectNode(node.Right, origin, direction, limit);
                }

                for (int i = node.First; i < node.First + node.Count; i++)
                    limit = triangles[i].Intersect(origin, direction, limit);
                return limit;
            }

            private static bool Crosses(Bounds bounds, Vector3 origin, Vector3 direction, float limit)
            {
                Vector3 min = bounds.min;
                Vector3 max = bounds.max;
                float near = 0f;
                float far = limit;
                for (int axis = 0; axis < 3; axis++)
                {
                    float step = direction[axis];
                    if (Mathf.Abs(step) < 1e-7f)
                    {
                        if (origin[axis] < min[axis] || origin[axis] > max[axis]) return false;
                        continue;
                    }
                    float a = (min[axis] - origin[axis]) / step;
                    float b = (max[axis] - origin[axis]) / step;
                    near = Mathf.Max(near, Mathf.Min(a, b));
                    far = Mathf.Min(far, Mathf.Max(a, b));
                    if (near > far) return false;
                }
                return true;
            }

            private readonly struct Node
            {
                public readonly Bounds Bounds;
                public readonly int First;
                public readonly int Count;
                public readonly int Right;

                public Node(Bounds bounds, int first, int count, int right)
                {
                    Bounds = bounds;
                    First = first;
                    Count = count;
                    Right = right;
                }
            }

            private readonly struct Triangle
            {
                public readonly Vector3 A;
                public readonly Vector3 AB;
                public readonly Vector3 AC;
                public Vector3 Center => A + (AB + AC) / 3f;

                public Triangle(Vector3 a, Vector3 b, Vector3 c)
                {
                    A = a;
                    AB = b - a;
                    AC = c - a;
                }

                public float Intersect(Vector3 origin, Vector3 direction, float limit)
                {
                    Vector3 cross = Vector3.Cross(direction, AC);
                    float determinant = Vector3.Dot(AB, cross);
                    if (determinant <= 1e-8f) return limit;

                    Vector3 relative = origin - A;
                    float u = Vector3.Dot(relative, cross);
                    float tolerance = 1e-5f * determinant;
                    if (u < -tolerance || u > determinant + tolerance) return limit;
                    Vector3 perpendicular = Vector3.Cross(relative, AB);
                    float v = Vector3.Dot(direction, perpendicular);
                    if (v < -tolerance || u + v > determinant + tolerance) return limit;
                    float distance = Vector3.Dot(AC, perpendicular) / determinant;
                    return distance >= 0f ? Mathf.Min(distance, limit) : limit;
                }
            }
        }

        private sealed class Geometry
        {
            public static readonly Vector2[] PineProfile = {
                new(0f, -0.34f), new(0.84f, -0.34f), new(1f, -0.24f), new(0.78f, -0.02f),
                new(0.45f, 0.23f), new(0.04f, 0.62f), new(0f, 0.67f)
            };
            public static readonly Vector2[] LanternProfile = {
                new(0f, -1f), new(0.67f, -1f), new(1f, 0.82f), new(0.92f, 1f), new(0f, 1f)
            };
            public static readonly Vector2[] GemProfile = {
                new(0f, -1f), new(0.73f, -0.47f), new(1f, 0.29f), new(0.67f, 0.7f), new(0f, 1f)
            };
            private static readonly Vector2[] BlockProfile = {
                new(0f, -1f), new(0.87f, -1f), new(1f, -0.76f), new(1f, 0.76f), new(0.87f, 1f), new(0f, 1f)
            };
            private static readonly Vector2[] BeveledOutline = {
                new(-1f, -0.78f), new(-0.78f, -1f), new(0.78f, -1f), new(1f, -0.78f),
                new(1f, 0.78f), new(0.78f, 1f), new(-0.78f, 1f), new(-1f, 0.78f)
            };
            private static readonly Vector2[] QuadUVs = { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            private readonly List<Vector3> vertices = new();
            private readonly List<Vector3> normals = new();
            private readonly List<Vector2> uv = new();
            private readonly List<Color32> colors = new();
            private readonly List<int> indices = new();
            public readonly string Name;

            public Geometry(string name) => Name = name;

            public Mesh CreateMesh(bool keepReadable = false)
            {
                var mesh = new Mesh { name = Name };
                if (vertices.Count > ushort.MaxValue) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uv);
                mesh.SetColors(colors);
                mesh.SetTriangles(indices, 0);
                if (!keepReadable) mesh.UploadMeshData(true);
                return mesh;
            }

            public void Disc(Vector3 center, Vector3 across, Vector3 up, float width, float height, int color, byte alpha)
            {
                Vector3 x = across * (width * 0.5f);
                Vector3 y = up * (height * 0.5f);
                Quad(center - x - y, center - x + y, center + x + y, center + x - y, color, alpha);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int color, byte alpha = 255)
            {
                var normal = Vector3.Cross(b - a, c - a).normalized;
                Append(a, b, c, d, normal, Rgb(color, alpha), QuadUVs[0], QuadUVs[1], QuadUVs[2], QuadUVs[3], false);
            }

            public void Sphere(Vector3 center, Vector3 radii, Quaternion rotation, int color, int sides, int rows, bool rocky = false)
            {
                Surface(center, radii, rotation, color, sides, rows, (u, v) =>
                {
                    float longitude = (u - 0.55f) * Mathf.PI * 2f;
                    float latitude = (v - 0.5f) * Mathf.PI;
                    float ring = Mathf.Cos(latitude);
                    Vector3 point = new(Mathf.Sin(longitude) * ring, Mathf.Sin(latitude), Mathf.Cos(longitude) * ring);
                    if (rocky)
                    {
                        float shift = 1f + Mathf.Sin(point.x * 8f + point.z * 6f) * Mathf.Cos(point.y * 9f - point.z * 4f) * 0.073f;
                        point *= shift;
                    }
                    return point;
                }, rocky);
            }

            public void Ward(Vector3 center, Vector3 radii)
            {
                Surface(center, radii, Quaternion.identity, 0xffffff, 24, 12, (u, v) =>
                {
                    float longitude = (u - 0.5f) * Mathf.PI;
                    float latitude = v * Mathf.PI * 0.5f;
                    float ring = Mathf.Cos(latitude);
                    return new Vector3(Mathf.Sin(longitude) * ring, Mathf.Sin(latitude), Mathf.Cos(longitude) * ring);
                }, false);
            }

            private void Surface(Vector3 center, Vector3 radii, Quaternion rotation, int color, int sides, int rows,
                Func<float, float, Vector3> unitPoint, bool varied)
            {
                for (int row = 0; row < rows; row++)
                {
                    float v = row / (float)rows;
                    float vv = (row + 1f) / rows;
                    for (int segment = 0; segment < sides; segment++)
                    {
                        float u = segment / (float)sides;
                        float uu = (segment + 1f) / sides;
                        Vector3 a = unitPoint(u, v);
                        Vector3 b = unitPoint(u, vv);
                        Vector3 c = unitPoint(uu, vv);
                        Vector3 d = unitPoint(uu, v);
                        Vector3 outside = rotation * (a + b + c + d);
                        Face(center, radii, rotation, color, a, b, c, d, outside,
                            new Vector2(u, v), new Vector2(u, vv), new Vector2(uu, vv), new Vector2(uu, v),
                            varied ? 0.88f + ((row * 13 + segment * 5) % 9) * 0.024f : 1f);
                    }
                }
            }

            public void Block(Vector3 center, Vector3 radii, Quaternion rotation, int color)
            {
                Profile(center, radii, rotation, color, BlockProfile, BeveledOutline.Length, BeveledOutline);
            }

            public void Profile(Vector3 center, Vector3 radii, Quaternion rotation, int color, Vector2[] profile,
                int sides, Vector2[] outline = null)
            {
                var ring = new Vector2[sides];
                for (int side = 0; side < sides; side++)
                {
                    float angle = side * Mathf.PI * 2f / sides;
                    ring[side] = outline != null ? outline[side] : new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                }

                for (int row = 0; row < profile.Length - 1; row++)
                {
                    Vector2 low = profile[row];
                    Vector2 high = profile[row + 1];
                    for (int side = 0; side < sides; side++)
                    {
                        Vector2 radial = ring[side];
                        Vector2 next = ring[(side + 1) % sides];
                        Vector3 a = new(radial.x * low.x, low.y, radial.y * low.x);
                        Vector3 b = new(radial.x * high.x, high.y, radial.y * high.x);
                        Vector3 c = new(next.x * high.x, high.y, next.y * high.x);
                        Vector3 d = new(next.x * low.x, low.y, next.y * low.x);
                        Vector3 outside = rotation * (Mathf.Approximately(low.y, high.y)
                            ? new Vector3(0f, Mathf.Sign(low.y), 0f) : new Vector3(radial.x + next.x, 0f, radial.y + next.y));
                        float variation = Mathf.Approximately(low.y, high.y) ? 1f
                            : 0.93f + ((row * 13 + side * 5) % 7) * 0.025f;
                        Face(center, radii, rotation, color, a, b, c, d, outside,
                            QuadUVs[0], QuadUVs[1], QuadUVs[2], QuadUVs[3], variation);
                    }
                }
            }

            public void Tube(Vector3[] points, float radius, int color, int sides = 6, float endWidth = 1f)
            {
                var rings = new Vector3[points.Length, sides];
                for (int i = 0; i < points.Length; i++)
                {
                    Vector3 direction = (points[Mathf.Min(i + 1, points.Length - 1)] - points[Mathf.Max(0, i - 1)]).normalized;
                    Vector3 right = Vector3.Cross(direction, Vector3.forward).normalized;
                    if (right.sqrMagnitude < 0.1f) right = Vector3.Cross(direction, Vector3.up).normalized;
                    Vector3 up = Vector3.Cross(right, direction);
                    float t = i / (points.Length - 1f);
                    float width = radius * Mathf.Lerp(1f, endWidth, t);
                    for (int side = 0; side < sides; side++)
                    {
                        float a = side * Mathf.PI * 2f / sides;
                        rings[i, side] = points[i] + (Mathf.Cos(a) * right + Mathf.Sin(a) * up) * width;
                    }
                }
                for (int i = 0; i < points.Length - 1; i++)
                {
                    for (int side = 0; side < sides; side++)
                    {
                        int next = (side + 1) % sides;
                        Vector3 a = rings[i, side];
                        Vector3 b = rings[i + 1, side];
                        Vector3 c = rings[i + 1, next];
                        Vector3 d = rings[i, next];
                        Vector3 outside = a + d - points[i] * 2f;
                        Face(Vector3.zero, Vector3.one, Quaternion.identity, color, a, b, c, d, outside,
                            QuadUVs[0], QuadUVs[1], QuadUVs[2], QuadUVs[3], 1f);
                    }
                }
            }

            private void Face(Vector3 center, Vector3 radii, Quaternion rotation, int color,
                Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outside,
                Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD, float tone)
            {
                Vector3 Place(Vector3 unit) => center + rotation * Vector3.Scale(unit, radii);
                a = Place(a);
                b = Place(b);
                c = Place(c);
                d = Place(d);
                Vector3 normal = Vector3.Cross(b - a, c - a) + Vector3.Cross(c - a, d - a);
                bool reverse = Vector3.Dot(normal, outside) < 0f;
                Color32 tint = Rgb(color);
                tint.r = (byte)Mathf.Clamp(tint.r * tone, 0f, 255f);
                tint.g = (byte)Mathf.Clamp(tint.g * tone, 0f, 255f);
                tint.b = (byte)Mathf.Clamp(tint.b * tone, 0f, 255f);
                Append(a, b, c, d, (reverse ? -normal : normal).normalized, tint, uvA, uvB, uvC, uvD, reverse);
            }

            private void Append(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Color32 tint,
                Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD, bool reverse)
            {
                int offset = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                vertices.Add(d);
                uv.Add(uvA);
                uv.Add(uvB);
                uv.Add(uvC);
                uv.Add(uvD);
                for (int i = 0; i < 4; i++)
                {
                    normals.Add(normal);
                    colors.Add(tint);
                }
                indices.Add(offset);
                indices.Add(offset + (reverse ? 2 : 1));
                indices.Add(offset + (reverse ? 1 : 2));
                indices.Add(offset);
                indices.Add(offset + (reverse ? 3 : 2));
                indices.Add(offset + (reverse ? 2 : 3));
            }
        }
    }
}
