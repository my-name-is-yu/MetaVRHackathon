using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
using UnityEngine.XR;
#endif

namespace Runeveil
{
    public enum DuelStage { Practice, Battle, Won, Lost }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(350)] // Apply the current frame's native hand and gaze events before presenting.
    public sealed class RuneveilDuel : MonoBehaviour
    {
        public const int MaxBossHP = 120;
        private const float RestSeconds = 2f;
        private const float WindupSeconds = 2.4f;
        private const int MaxSamples = 1024;

        public DuelStage Phase { get; private set; } = DuelStage.Practice;
        public int BossHP { get; private set; } = MaxBossHP;
        public int Hearts { get; private set; } = 3;
        public bool Shield { get; private set; }
        public float Windup => IsPlaying && attackIn <= 0f ? Mathf.Clamp01(-attackIn / WindupSeconds) : -1f;
        public int Interrupts { get; private set; }
        public int Blocks { get; private set; }
        public RuneSpell? LastSpell { get; private set; }
        public RuneveilArena.Target LastTarget { get; private set; }
        public string LastResult { get; private set; } = "";
        private bool IsPlaying => Phase == DuelStage.Practice || Phase == DuelStage.Battle;

        private readonly List<Vector2> samples = new List<Vector2>(128);
        private RuneveilArena arena;
        private RuneveilInput input;
        private Transform view;
        private Ray? currentGaze;
        private Vector3 origin, lastTip, drawRight, drawUp;
        private bool drawing;
        private float strokeSpan, attackIn = RestSeconds, noticeUntil;
        private string notice = "";
        private string trackingHint = "";
        private bool actionAimed;
        private System.Text.StringBuilder castDiagnostics;
        private float reportCastsAt;
        private bool hudDirty = true;
        private LineRenderer trail;
        private Material trailMaterial;

        private void Awake()
        {
            var path = new GameObject("Glowing fingertip trail");
            path.transform.SetParent(transform, false);
            trail = path.AddComponent<LineRenderer>();
            trail.useWorldSpace = true;
            trail.widthMultiplier = 0.009f;
            trail.numCapVertices = 4;
            trail.numCornerVertices = 3;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.positionCount = 0;
            trail.enabled = false;
            trailMaterial = new Material(Shader.Find("Sprites/Default"));
            trail.sharedMaterial = trailMaterial;
            trail.startColor = new Color(0.60f, 0.99f, 0.91f);
            trail.endColor = Color.white;
        }

        private void OnEnable() => Connect();
        private void Start() => Connect();

        // Start also covers components added to a live scene in different orders.
        private void Connect()
        {
            if (!arena) arena = GetComponent<RuneveilArena>();
            if (input) return;
            input = GetComponent<RuneveilInput>();
            if (!input) return;
            input.StrokeBegan += OnPinch;
            input.StrokePoint += SampleStroke;
            input.StrokeEnded += ReleaseStroke;
            input.StrokeCancelled += CancelStroke;
            input.TrackingUpdated += SetTracking;
            hudDirty = true;
        }

        private void OnDisable()
        {
            if (input)
            {
                input.StrokeBegan -= OnPinch;
                input.StrokePoint -= SampleStroke;
                input.StrokeEnded -= ReleaseStroke;
                input.StrokeCancelled -= CancelStroke;
                input.TrackingUpdated -= SetTracking;
                input = null;
            }
            CancelStroke();
            currentGaze = null;
            FlushCastDiagnostics();
        }

        private void OnDestroy()
        {
            if (trailMaterial) Destroy(trailMaterial);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) { CancelStroke(); currentGaze = null; }
        }

        public void SetTracking(Transform eyeView, Vector3? tip, Ray? gaze)
        {
            view = eyeView;
            currentGaze = gaze;
            string hint = !gaze.HasValue && input ? input.GazeHint : "";
            if (string.IsNullOrEmpty(hint) && !tip.HasValue) hint = "Bring a hand into view, then pinch.";
            if (hint != trackingHint) { trackingHint = hint; hudDirty = true; }
            if (arena && view) arena.SetHand(view, tip);
        }

        private void OnPinch(Vector3 tip) => BeginStroke(tip, input.Head);

        public void BeginStroke(Vector3 tip, Transform eyeView)
        {
            if (!eyeView || !arena) return;
            ClearStroke();
            view = eyeView;
            origin = lastTip = tip;
            drawRight = view.right;
            drawUp = view.up;
            drawing = true;
            samples.Add(Vector2.zero);
            trail.positionCount = 1;
            trail.SetPosition(0, tip);
            trail.enabled = true;
        }

        public void SampleStroke(Vector3 tip)
        {
            if (!drawing || (tip - lastTip).sqrMagnitude < 0.000004f) return;
            if (samples.Count >= MaxSamples) { CancelStroke(); return; }
            var offset = tip - origin;
            var projected = new Vector2(Vector3.Dot(offset, drawRight), Vector3.Dot(offset, drawUp));
            samples.Add(projected);
            strokeSpan = Mathf.Max(strokeSpan, projected.magnitude);
            trail.positionCount = samples.Count;
            trail.SetPosition(samples.Count - 1, tip);
            lastTip = tip;
        }

        public void CancelStroke()
        {
            if (drawing) LastResult = "cancel";
            ClearStroke();
        }

        private void ClearStroke()
        {
            drawing = false;
            strokeSpan = 0f;
            samples.Clear();
            if (trail) { trail.enabled = false; trail.positionCount = 0; }
        }

        public void ReleaseStroke(Vector3 tip, Ray? gaze)
        {
            if (!drawing || !arena) return;
            SampleStroke(tip);
            if (!drawing) return;
            bool tap = strokeSpan < 0.03f;
            RuneSpell? spell = RuneveilRunes.Match(samples);
            ClearStroke();

            if (Phase != DuelStage.Battle && tap && gaze.HasValue && arena.AimAction(gaze.Value))
            {
                StartBattle();
                return;
            }
            if (!IsPlaying) return;
            if (!spell.HasValue)
            {
                LastResult = "invalid";
                RecordCast(null, RuneveilArena.Target.Miss, LastResult);
                Notify("Try drawing that rune again");
                return;
            }
            if (spell != RuneSpell.Barrier && !gaze.HasValue)
            {
                LastResult = "cancel";
                RecordCast(spell, RuneveilArena.Target.Miss, LastResult);
                Notify("No eye aim at release. Check eye tracking.");
                return;
            }

            LastSpell = spell;
            LastTarget = RuneveilArena.Target.Miss;
            var destination = tip;
            if (spell == RuneSpell.Barrier)
            {
                Shield = true;
                LastResult = "shield";
                Notify("Barrier ready");
            }
            else
            {
                // Hit-test once at release; the cast animation retains this destination.
                LastTarget = arena.Aim(gaze.Value, out destination);
                bool core = LastTarget == RuneveilArena.Target.Core;
                bool hit = LastTarget != RuneveilArena.Target.Miss;
                bool interrupted = core && spell == RuneSpell.Lightning && Windup >= 0f;
                if (interrupted) { Interrupts++; attackIn = RestSeconds; }
                LastResult = interrupted ? "interrupt" : hit ? "hit" : "miss";
                if (hit && Phase == DuelStage.Battle)
                    BossHP = Mathf.Max(0, BossHP - Damage(spell.Value) - (core ? 4 : 0));
                Notify(interrupted ? "Lightning interrupted the slam!" : spell.Value.ToString());
            }
            arena.Cast(spell.Value.ToString(), tip, destination, view.position, view.forward, LastTarget);
            RecordCast(spell, LastTarget, LastResult);
            if (BossHP == 0) Finish(DuelStage.Won);
        }

        private static int Damage(RuneSpell spell)
        {
            switch (spell)
            {
                case RuneSpell.Fire: return 16;
                case RuneSpell.Lightning: return 12;
                case RuneSpell.Rock: return 22;
                default: return 14;
            }
        }

        public void StartBattle()
        {
            if (!arena || Phase == DuelStage.Battle) return;
            ClearStroke();
            Phase = DuelStage.Battle;
            Hearts = 3; BossHP = MaxBossHP; Shield = false;
            Interrupts = Blocks = 0;
            LastSpell = null; LastTarget = RuneveilArena.Target.Miss; LastResult = "";
            attackIn = RestSeconds;
            arena.ResetView();
            Notify("Duel started");
        }

        private void Finish(DuelStage outcome)
        {
            Phase = outcome;
            Shield = false;
            ClearStroke();
            Notify(outcome == DuelStage.Won ? "Victory!" : "The golem wins");
        }

        private void LateUpdate()
        {
            if (!arena) return;
#if UNITY_EDITOR
            if (!XRSettings.isDeviceActive) UpdateMousePreview();
#endif
            float dt = Time.deltaTime;
            if (IsPlaying)
            {
                attackIn -= dt;
                if (attackIn <= -WindupSeconds)
                {
                    attackIn = RestSeconds;
                    bool blocked = Shield;
                    Shield = false;
                    arena.Impact(blocked);
                    if (blocked) Blocks++;
                    else if (Phase == DuelStage.Battle) Hearts--;
                    if (Hearts == 0) Finish(DuelStage.Lost);
                    else Notify(blocked ? "Slam blocked" : Phase == DuelStage.Practice ? "Practice slam" : "Golem hit");
                }
            }

            bool aimed = currentGaze.HasValue && arena.Aim(currentGaze.Value, out _) == RuneveilArena.Target.Core;
            arena.Present(dt, Windup, Shield, aimed, Phase == DuelStage.Won, Phase == DuelStage.Lost);
            bool hoveringAction = Phase != DuelStage.Battle && currentGaze.HasValue && arena.AimAction(currentGaze.Value);
            if (hoveringAction != actionAimed) { actionAimed = hoveringAction; hudDirty = true; }
            if (notice.Length > 0 && Time.time >= noticeUntil) { notice = ""; hudDirty = true; }
            if (hudDirty)
            {
                string action = Phase == DuelStage.Practice ? "START DUEL" : IsPlaying ? null : "PLAY AGAIN";
                arena.SetHud(BossHP, MaxBossHP, Hearts, HudStatus(), action);
                hudDirty = false;
            }
            if (castDiagnostics != null && castDiagnostics.Length > 0 && Time.unscaledTime >= reportCastsAt)
                FlushCastDiagnostics();
        }

        private string HudStatus()
        {
            if (!string.IsNullOrEmpty(trackingHint)) return trackingHint;
            if (actionAimed) return "Pinch and release here to " + (Phase == DuelStage.Practice ? "start." : "play again.");
            if (notice.Length > 0) return notice;
            if (Phase == DuelStage.Practice)
                return "Pinch to draw. Look at START DUEL and tap to fight.\nO fire   Z bolt   V ice   triangle rock   S wind   line shield";
            if (Phase == DuelStage.Won) return "Victory! Look at PLAY AGAIN and pinch.";
            if (Phase == DuelStage.Lost) return "Defeated. Look at PLAY AGAIN and pinch.";
            return "";
        }

        // Development builds batch completed attempts in five-second windows.
        // Captures no gaze rays, finger positions, trajectories, or device IDs.
        private void RecordCast(RuneSpell? spell, RuneveilArena.Target target, string result)
        {
            if (!Debug.isDebugBuild) return;
            castDiagnostics ??= new System.Text.StringBuilder();
            if (castDiagnostics.Length == 0) reportCastsAt = Time.unscaledTime + 5f;
            else castDiagnostics.AppendLine();
            castDiagnostics.Append("[RuneveilCast] spell=").Append(spell?.ToString() ?? "None")
                .Append(" part=").Append(target).Append(" result=").Append(result);
        }

        private void FlushCastDiagnostics()
        {
            if (castDiagnostics == null || castDiagnostics.Length == 0) return;
            Debug.Log(castDiagnostics.ToString());
            castDiagnostics.Clear();
        }

        private void Notify(string text)
        {
            notice = text;
            noticeUntil = Time.time + 1.6f;
            hudDirty = true;
        }

#if UNITY_EDITOR
        private Ray? lockedMouseAim;

        private void UpdateMousePreview()
        {
            var mouse = Mouse.current;
            var preview = Camera.main;
            if (mouse == null || !preview) return;
            var screen = mouse.position.ReadValue();
            var aim = preview.ScreenPointToRay(screen);
            if (mouse.rightButton.wasPressedThisFrame)
                lockedMouseAim = lockedMouseAim.HasValue ? (Ray?)null : aim;
            var tip = preview.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0.5f));
            var gaze = lockedMouseAim ?? aim;
            SetTracking(preview.transform, tip, gaze);
            if (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame) StartBattle();
            if (mouse.leftButton.wasPressedThisFrame) BeginStroke(tip, preview.transform);
            if (mouse.leftButton.isPressed) SampleStroke(tip);
            if (mouse.leftButton.wasReleasedThisFrame) ReleaseStroke(tip, gaze);
        }
#endif
    }
}
