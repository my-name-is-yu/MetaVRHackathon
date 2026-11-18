using System.Collections.Generic;
using Runeveil.Contracts;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace Runeveil
{
    [DefaultExecutionOrder(100)]
    public sealed class RuneveilDemo : MonoBehaviour
    {
        const float DrawingDepth = 0.57f;
        const float MinimumSampleDistanceSquared = 0.000004f;
        const int MaxSamples = 1024;
        const double MaxEyeSampleAge = 0.2;
        const float IndependentEyeAngle = 1.5f;
        static readonly Color DrawingColor = new Color32(255, 218, 139, 255);
        static readonly Color ChargedFireColor = new Color32(255, 128, 66, 255);
        static readonly Color ChargedLightningColor = new Color32(255, 213, 106, 255);
        static readonly Color ChargedIceColor = new Color32(163, 228, 255, 255);
        static readonly Color ChargedWindColor = new Color32(207, 182, 255, 255);
        static readonly Color ChargedWardColor = new Color32(159, 242, 206, 255);
        static readonly Color HandLabelColor = new Color32(159, 242, 206, 255);
        const float ChargeCheckInterval = 0.05f;
        const float WardCueSeconds = 1.3f;

        [SerializeField] Camera desktopView;
        [SerializeField] OVRCameraRig xrRig;
        [SerializeField] OVRHand leftHand;
        [SerializeField] OVRHand rightHand;
        [SerializeField] RuneveilArena arena;
        [SerializeField] RuneveilHud hud;
        [Tooltip("Optional. Defaults to Resources/Runeveil/PinchSettings.")]
        [SerializeField] RuneveilPinchSettings pinchSettings;
#if UNITY_EDITOR
        [SerializeField] bool desktopPreviewInEditor;
        [Header("Debug (Editor only)")]
        [Tooltip("XR only. Treat the eye gaze as always on the marked weak point, and accept a short pinch as " +
            "Start duel / Fight again without looking at the button.")]
        [SerializeField] bool assumeGazeOnTarget;
#endif

        struct PendingHit
        {
            public Spell Spell;
            public SpellTarget Target;
            public float Delay;
            public bool AssumedGaze;
            public int Level;
            public int? InterruptWindup;
        }

        readonly RuneveilDuel duel = new();
        readonly List<PendingHit> pendingHits = new();
        readonly RuneveilPinchDetector leftPinch = new();
        readonly RuneveilPinchDetector rightPinch = new();
        RuneveilAudio sfx;
        RuneveilMusic music;
        Vector3 lastDrawTip;
        bool hadDrawTip;
        readonly List<Vector2> samples = new(MaxSamples);
        readonly List<Vector3> points = new(MaxSamples);
        readonly List<XRDisplaySubsystem> displays = new();
        Camera xrView;
        Camera view;
        OVRSkeleton leftSkeleton;
        OVRSkeleton rightSkeleton;
        OVRPlugin.EyeGazeInteractionState eyeState;
        EyeEvidence nativeEye;
        EyeEvidence openXrEye;
        string gazeHint;
        bool freshEye;
        bool wasAssumingGaze;
        int frameCount, handFrames, freshEyeFrames, independentEyeFrames, cancelledStrokes;
        double reportAt;
        System.Text.StringBuilder castReport;

        OVRHand strokeHand;
        bool leftReady;
        bool rightReady;
        bool drawing;
        bool usingXr;
        bool locked;
        bool pendingDesktopAction;
        bool menuAvailableAtStrokeStart;
        Vector2 lockedViewportPoint;
        Vector3 origin;
        Vector3 strokeRight;
        Vector3 strokeUp;
        Vector3? handPosition;
        Vector3 pendingReleasePoint;
        Spell? chargedSpell;
        OVRHand chargeHand;
        Vector3 chargeAnchor;
        float nextChargeCheck;
        AimSample pendingReleaseAim;
        int? lightningReleaseWindup;
        double lightningReleaseDeadline;
        int focusLocks;

        readonly struct AimSample
        {
            public readonly Ray? SpellRay;
            public readonly Ray? MenuActionRay;
            public readonly SpellTarget Target;
            public readonly Vector3 Hit;
            public readonly bool AssumedGaze;
            public readonly int? Windup;

            public AimSample(Ray? spellRay, Ray? menuActionRay, SpellTarget target, Vector3 hit, bool assumedGaze,
                int? windup)
            {
                SpellRay = spellRay;
                MenuActionRay = menuActionRay;
                Target = target;
                Hit = hit;
                AssumedGaze = assumedGaze;
                Windup = windup;
            }
        }

        float chargeMatchedFor;
        int pendingKey = -1;
        bool chargedThisStroke;
        int chargeLevel;
        int chargeSegmentStart;
        readonly List<Vector2> segment = new(MaxSamples);

        static bool Chargeable(Spell spell) => spell != Spell.Rock;

        static Color ChargeColor(Spell spell) => spell switch
        {
            Spell.Fire => ChargedFireColor,
            Spell.Lightning => ChargedLightningColor,
            Spell.Ice => ChargedIceColor,
            Spell.Wind => ChargedWindColor,
            _ => ChargedWardColor
        };

        static string ChargeName(Spell spell) => spell switch
        {
            Spell.Barrier => "Ward",
            _ => spell.ToString()
        };
        float strokeStarted;
        float maxDisplacementSquared;
        float strokeFadeUntil;
        string notice;
        float noticeUntil;
        bool wasEnraged;
        string spellLabel = "";
        string resultSummary = "";

        void Awake()
        {
            xrView = xrRig.centerEyeAnchor.GetComponent<Camera>();
            leftSkeleton = leftHand.GetComponent<OVRSkeleton>();
            rightSkeleton = rightHand.GetComponent<OVRSkeleton>();
            RuneveilPinchDetector.DebugLeft = leftPinch;
            RuneveilPinchDetector.DebugRight = rightPinch;
            sfx = gameObject.AddComponent<RuneveilAudio>();
            sfx.Build();
            music = gameObject.AddComponent<RuneveilMusic>();
            music.Build();
            arena.Build();
            SelectCamera(HasXrDisplay());
        }

        RuneveilPinchSettings Pinch => pinchSettings != null ? pinchSettings : RuneveilPinchSettings.Current;

        bool AssumeGaze
        {
            get
            {
#if UNITY_EDITOR
                return usingXr && assumeGazeOnTarget;
#else
                return false;
#endif
            }
        }

        string MenuAction => duel.Phase switch
        {
            DuelPhase.Practice => "Start duel",
            DuelPhase.Won when arena.MuseCleansingComplete => "Fight again",
            DuelPhase.Lost => "Fight again",
            _ => ""
        };

        void LateUpdate()
        {
            bool xrActive = HasXrDisplay();
            if (xrActive != usingXr) SelectCamera(xrActive);

            float deltaTime = Mathf.Min(Time.deltaTime, 0.1f);
            bool preview = !usingXr && DesktopPreviewEnabled;
            bool active = usingXr ? OVRManager.hasInputFocus : preview && Application.isFocused;
            bool advance = active && !arena.HitStopping;
            float strikeIn = duel.WindupSecondsLeft;
            bool closingWindup = advance && strikeIn > 0f && strikeIn <= deltaTime;
            // ヒットストップ中は拳の溜めやクールタイムも止め、ボスの見た目とずれないようにする
            if (advance && !closingWindup) AdvanceDuel(deltaTime);
            LandHits(deltaTime);
            if (duel.Enraged && !wasEnraged) Notify("The Muse is enraged! Red strikes cost 2 hearts");
            wasEnraged = duel.Enraged;
            bool assumed = AssumeGaze;
            if (assumed != wasAssumingGaze)
            {
                wasAssumingGaze = assumed;
                duel.TrackFocus(ArenaTarget.Miss, 0f);
                pendingReleaseAim = default;
                lightningReleaseWindup = null;
                ResetEyeEvidence();
            }

            Ray? mousePointer = null;
            Ray? spellRay = null;
            Ray? menuActionRay = null;
            bool headMenuAim = false;
            if (usingXr && !assumed)
            {
                Ray? eyeRay = ReadGaze();
                bool menuActionAvailable = active && MenuAction.Length != 0;
                headMenuAim = menuActionAvailable && !eyeRay.HasValue;
                Transform head = xrRig.centerEyeAnchor;
                Ray headRay = headMenuAim ? new Ray(head.position, head.forward) : default;
                var rays = RuneveilInputPolicy.Resolve(eyeRay, headRay, menuActionAvailable);
                spellRay = rays.SpellRay;
                menuActionRay = rays.MenuActionRay;
            }
            else if (assumed) spellRay = AssumedGaze();
            else if (preview) spellRay = MouseAim(out mousePointer);
            Vector3 hit = default;
            ArenaTarget target = ArenaTarget.Miss;
            if (active && spellRay.HasValue)
            {
                if (assumed)
                {
                    target = duel.WeakPoint;
                    hit = arena.WeakPointPosition(target);
                }
                else target = arena.Aim(spellRay.Value, out hit);
            }
            bool wasFocused = duel.FocusReady;
            duel.TrackFocus(arena.CanFocus(target) ? target : ArenaTarget.Miss, deltaTime);
            if (Debug.isDebugBuild && usingXr && !assumed && !wasFocused && duel.FocusReady) focusLocks++;
            int? windup = duel.WindupSecondsLeft > 0f ? duel.WindupRevision : null;
            var aim = new AimSample(spellRay, menuActionRay, duel.CaptureTarget(target), hit, assumed, windup);

            if (usingXr) UpdateHands(aim);
            else if (preview) UpdateMouse(aim);
            else if (drawing || pendingDesktopAction || locked)
            {
                ClearStroke();
                pendingDesktopAction = locked = false;
            }
            // Read this frame's release before a punch that crosses its deadline this frame.
            if (closingWindup && !duel.Finished)
                AdvanceDuel(deltaTime);
            else if (advance && duel.WindupSecondsLeft == 0f)
                AdvanceDuel(0f);
            bool heavyContact = duel.HeavyStrike;
            bool wardBeforeContact = duel.WardVisible;
            AttackResult contact = ResolveMuseStrike(deltaTime);
            float releaseGrace = usingXr ? Pinch.releaseGrace : 0f;
            bool wardReleaseWindow = duel.WindupSecondsLeft > releaseGrace &&
                duel.WindupSecondsLeft <= WardCueSeconds;
            if (Debug.isDebugBuild) ReportInputs(!assumed && spellRay.HasValue);
            ArenaTarget activeWeakPoint = duel.Finished ? ArenaTarget.Miss : duel.WeakPoint;
            ArenaTarget shatteredPoint = duel.Finished ? ArenaTarget.Miss : duel.ShatteredPoint;
            arena.Tick(deltaTime, duel.Windup, wardBeforeContact && duel.WardVisible, duel.Phase == DuelPhase.Won, target,
                duel.Stagger > 0, duel.Stuck > 0, duel.Enraged, activeWeakPoint, shatteredPoint,
                duel.FocusProgress, duel.FocusReady, duel.HeavyStrike, wardReleaseWindow);
            if (contact != AttackResult.None)
                sfx.PlayMuseStrike(contact == AttackResult.Blocked, heavyContact,
                    arena.WeakPointPosition(ArenaTarget.RaisedFist), Pinch);
            if (duel.Phase == DuelPhase.Won && arena.MuseCleansingComplete && resultSummary.Length == 0)
                ShowResult("Victory");
            arena.SetCharge(chargedSpell, chargeLevel, chargeAnchor, view.transform.position, deltaTime);
            UpdateDrawingFeedback(deltaTime);
            music.Tick(duel.Phase == DuelPhase.Battle, duel.Phase == DuelPhase.Won, duel.Phase == DuelPhase.Lost,
                duel.Windup >= 0f, Pinch, Time.unscaledDeltaTime);

            string focus = target switch
            {
                _ when duel.Finished => "",
                _ when duel.ExposedTarget == target && target.IsWeakPoint() => $"{target.DisplayName()} exposed",
                _ when target.IsWeakPoint() && duel.FocusProgress > 0 => $"Focusing on {target.DisplayName().ToLowerInvariant()}",
                _ when target.IsWeakPoint() && activeWeakPoint.IsWeakPoint() => target.DisplayName(),
                ArenaTarget.Body => locked ? "Muse locked" : "Corrupted Muse",
                _ => usingXr && !spellRay.HasValue ? "Eye gaze unavailable" : locked ? "Aim locked" : ""
            };
            if (assumed && !duel.Finished) focus = $"Debug gaze · {focus}";
            string action = MenuAction;
            string status = Time.unscaledTime < noticeUntil ? notice : DefaultStatus(spellRay.HasValue);
            if (headMenuAim)
            {
                string heading = duel.Finished ? resultSummary : "HEAD AIM · MENU ONLY";
                status = heading + "\nPoint your head at the button and pinch.";
            }
            ArenaTarget markedWeakPoint = duel.ExposedTarget.IsWeakPoint() ? duel.ExposedTarget : activeWeakPoint;
            hud.Render(duel.BossHealth, duel.Hearts, duel.WardVisible, markedWeakPoint.DisplayName(), focus, spellLabel, status,
                duel.Phase == DuelPhase.Practice, action, usingXr,
                duel.Phase != DuelPhase.Won || !arena.MuseRestoredFormVisible);
            hud.SetVictoryIllustration(arena.MuseVictoryPortraitBlend);
            hud.SetControlHint(!usingXr && !preview ? "Connect a headset to draw and aim" : null);
            hud.SetActionFocus(usingXr ? menuActionRay : mousePointer);
            hud.SetHand(handPosition);
            hud.SetPinchMeter(usingXr && Pinch.showDebugMeter, leftPinch, rightPinch, Pinch);
            for (var spell = Spell.Fire; spell <= Spell.Barrier; spell++)
                hud.SetCooldown(spell, duel.CooldownLeft(spell), RuneveilDuel.CooldownSeconds(spell));
            if (drawing) hud.ShowStroke(points, chargedSpell.HasValue ? ChargeColor(chargedSpell.Value) : DrawingColor);
            else if (strokeFadeUntil > 0)
            {
                float opacity = Mathf.Clamp01((strokeFadeUntil - Time.unscaledTime) / 0.28f);
                hud.ShowStroke(points, DrawingColor, opacity);
                if (opacity <= 0) ClearStroke();
            }
        }

        bool DesktopPreviewEnabled
        {
            get
            {
#if UNITY_EDITOR
                return desktopPreviewInEditor;
#else
                return false;
#endif
            }
        }

        // Drawing crackle and sparks follow the fingertip; both scale with its speed.
        void UpdateDrawingFeedback(float deltaTime)
        {
            bool active = drawing && points.Count > 0;
            Vector3 tip = active ? points[points.Count - 1] : lastDrawTip;
            float speed = active && hadDrawTip && deltaTime > 0f ? (tip - lastDrawTip).magnitude / deltaTime : 0f;
            lastDrawTip = tip;
            hadDrawTip = active;

            var settings = Pinch;
            sfx.SetDrawing(active, chargedSpell, tip, speed, settings, deltaTime);
            arena.TickSparks(active && settings.sparks, tip, speed, chargedSpell,
                settings.sparkAmount, view.transform.position, deltaTime);
        }

        bool HasXrDisplay()
        {
            if (DesktopPreviewEnabled) return false;
            SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays)
                if (display.running) return true;
            return false;
        }

        void SelectCamera(bool xr)
        {
            duel.TrackFocus(ArenaTarget.Miss, 0f);
            ResetEyeEvidence();
            leftReady = rightReady = false;
            locked = false;
            pendingDesktopAction = false;
            usingXr = xr;
            xrRig.disableEyeAnchorCameras = !xr;
            leftHand.gameObject.SetActive(xr);
            rightHand.gameObject.SetActive(xr);
            xrView.enabled = xr;
            desktopView.enabled = !xr;
            // The rig camera retains its listener, so there is one listener in either mode.
            xrView.GetComponent<AudioListener>().enabled = xr;
            desktopView.GetComponent<AudioListener>().enabled = !xr;
            view = xr ? xrView : desktopView;
            hud.Initialize(view, desktopView.transform);
            ClearStroke();
        }

        void AdvanceDuel(float deltaTime)
        {
            if (duel.Step(deltaTime, MustWaitForLightning()))
                arena.BeginStrike(duel.HeavyStrike);
        }

        AttackResult ResolveMuseStrike(float deltaTime)
        {
            if (!duel.StrikeCommitted || !arena.ReachesStrikeContact(deltaTime)) return AttackResult.None;

            int heartsBefore = duel.Hearts;
            AttackResult impact = duel.ResolveStrike();
            bool blocked = impact == AttackResult.Blocked;
            arena.Impact(blocked);
            if (blocked) Notify("Ward blocked the strike. Counterattack now");
            else if (duel.Phase == DuelPhase.Lost) ShowResult("Defeated");
            else if (heartsBefore - duel.Hearts == 2) Notify("Heavy strike! You lost 2 hearts");
            else if (duel.Phase == DuelPhase.Battle) Notify("The Muse's strike hit you");
            return impact;
        }

        bool MustWaitForLightning()
        {
            if (duel.Windup < 0f) return false;
            int windup = duel.WindupRevision;
            // Allow the frame that completes the grace period to deliver its release.
            if (lightningReleaseWindup == windup &&
                Time.unscaledTimeAsDouble - Time.unscaledDeltaTime < lightningReleaseDeadline)
                return true;
            foreach (var hit in pendingHits)
                if (hit.InterruptWindup == windup) return true;
            return false;
        }

        string DefaultStatus(bool canAim)
        {
            if (!usingXr && !DesktopPreviewEnabled) return "";
            if (duel.Finished) return resultSummary;
            if (usingXr && duel.Phase != DuelPhase.Battle && !string.IsNullOrEmpty(gazeHint)) return gazeHint;
            if (usingXr && !handPosition.HasValue) return "Bring a hand into view, then pinch";
            if (duel.Stagger > 0) return "The Muse is down! Strike the marked weak point";
            if (duel.WardCounterActive)
                return canAim ? "Counterattack the marked fist" : "Cast while the paw is stuck";
            if (duel.FocusReady) return "Release an attack rune at the exposed part";
            if (duel.Windup >= 0)
                return canAim ? "Release Ward when the arcs flare, or hit a weak point with Lightning"
                    : "Draw Ward now. Release when the arcs flare";
            if (chargedSpell.HasValue && !drawing)
                return chargedSpell == Spell.Barrier
                    ? (usingXr ? "Ward charged — pinch and release to raise it" : "Ward charged — click to raise it")
                    : $"{ChargeName(chargedSpell.Value)} charged — " +
                      (usingXr ? "look at a target, pinch and release" : "click to cast");
            if (duel.Phase == DuelPhase.Battle)
                return !canAim && usingXr ? "Spells hit the body. Draw Ward when the Muse raises her paw"
                    : duel.WeakPoint == ArenaTarget.Miss
                    ? "The weak point shattered. Watch for the next marker"
                    : $"Aim for the marked {duel.WeakPoint.DisplayName().ToLowerInvariant()}";
            return "";
        }

        void ShowResult(string outcome)
        {
            noticeUntil = 0;
            if (duel.Phase == DuelPhase.Won)
            {
                string rank = duel.Hearts == RuneveilDuel.MaxHearts ? "S" : duel.Hearts >= 3 ? "A" : "B";
                outcome += $"  |  Rank {rank}";
            }
            resultSummary = $"{outcome}  |  Interrupts {duel.Interruptions}  |  Blocks {duel.Blocks}";
        }

        void Notify(string message)
        {
            notice = message;
            noticeUntil = Time.unscaledTime + 1.65f;
        }

        void StartBattle()
        {
            if (MenuAction.Length == 0) return;

            ClearStroke();
            pendingHits.Clear();
            pendingReleaseAim = default;
            ClearCharge();
            duel.StartBattle();
            arena.ResetArena();
            hud.SetVictoryIllustration(0f);
            spellLabel = "";
            resultSummary = "";
            Notify("Duel started");
        }

        /// <summary>エディター専用のデバッグ用。視線が取れなくても、頭の位置から目印の付いた弱点を見ている扱いにする。</summary>
        Ray? AssumedGaze()
        {
            gazeHint = null;
            if (!OVRManager.hasInputFocus || !duel.WeakPoint.IsWeakPoint()) return null;

            Vector3 eye = view.transform.position;
            return new Ray(eye, arena.WeakPointPosition(duel.WeakPoint) - eye);
        }

        Ray? ReadGaze()
        {
            freshEye = false;
            if (!OVRManager.hasInputFocus)
            {
                ResetEyeEvidence();
                gazeHint = "Return to the game to continue";
                return null;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            Transform space = xrRig.trackingSpace;
            bool supported = OVRPlugin.eyeGazeInteractionsSupported;
            bool enabled = supported && OVRPlugin.eyeGazeInteractionsEnabled;
            bool nativeValid = enabled &&
                OVRPlugin.GetEyeGazeInteractionState(OVRPlugin.Step.Render, -1, ref eyeState) && eyeState.IsValid;
            if (nativeValid)
            {
                var ray = new Ray(space.TransformPoint(eyeState.Pose.Position.FromFlippedZVector3f()),
                    space.TransformDirection(eyeState.Pose.Orientation.FromFlippedZQuatf() * Vector3.forward));
                if (Verified(ray, ref nativeEye, eyeState.Time, now))
                {
                    gazeHint = null;
                    return ray;
                }
            }
            else nativeEye = default;

            // Some simulator runtimes publish gaze only through the OpenXR interaction profile.
            var eyes = InputSystem.GetDevice<EyeGazeInteraction.EyeGazeDevice>();
            var pose = eyes != null ? eyes.pose.ReadValue() : default;
            const InputTrackingState required = InputTrackingState.Position | InputTrackingState.Rotation;
            bool openXrValid = eyes != null && pose.isTracked && (pose.trackingState & required) == required;
            double age = eyes == null ? double.PositiveInfinity
                : UnityEngine.InputSystem.LowLevel.InputState.currentTime - eyes.lastUpdateTime;
            if (openXrValid && age >= -MaxEyeSampleAge && age <= MaxEyeSampleAge)
            {
                var ray = new Ray(space.TransformPoint(pose.position), space.TransformDirection(pose.rotation * Vector3.forward));
                if (Verified(ray, ref openXrEye, eyes.lastUpdateTime, now))
                {
                    gazeHint = null;
                    return ray;
                }
            }
            else openXrEye = default;

            gazeHint = freshEye ? "Keep your head still and look left and right with your eyes"
                : nativeValid || openXrValid ? "Waiting for a fresh eye sample"
                : supported && !enabled ? "Enable eye tracking and allow eye access for this game"
                : !supported && eyes == null ? "Eye gaze is unavailable on this XR runtime"
                : "Adjust your headset until your eyes are tracked";
            return null;
        }

        bool Verified(Ray ray, ref EyeEvidence evidence, double stamp, double now)
        {
            return evidence.Accept(view.transform.InverseTransformDirection(ray.direction),
                view.transform.rotation, stamp, now, ref freshEye);
        }

        // Deltas stay within each provider's clock. A stationary or replayed sample never
        // proves independent movement, even when the player turns their head.
        struct EyeEvidence
        {
            double lastStamp, lastAdvance;
            Quaternion referenceHead;
            Vector3 referenceDirection;
            bool independent;

            public bool Accept(Vector3 direction, Quaternion head, double stamp, double now, ref bool fresh)
            {
                if (!(stamp > 0) || double.IsInfinity(stamp) || !(direction.sqrMagnitude > 0.9f))
                    return false;
                if (stamp == lastStamp)
                {
                    if (now - lastAdvance > MaxEyeSampleAge) return false;
                    fresh = true;
                    return independent;
                }

                if (lastStamp == 0 || stamp < lastStamp || now - lastAdvance > MaxEyeSampleAge)
                {
                    independent = false;
                    referenceHead = head;
                    referenceDirection = direction;
                }
                if (!independent)
                {
                    if (Quaternion.Angle(referenceHead, head) > 1f)
                    {
                        referenceHead = head;
                        referenceDirection = direction;
                    }
                    else independent = Vector3.Angle(referenceDirection, direction) >= IndependentEyeAngle;
                }
                lastStamp = stamp;
                lastAdvance = now;
                fresh = true;
                return independent;
            }
        }

        void ResetEyeEvidence()
        {
            nativeEye = default;
            openXrEye = default;
            freshEye = false;
            gazeHint = null;
        }

        void UpdateHands(AimSample aim)
        {
            handPosition = null;
            if (!OVRManager.hasInputFocus)
            {
                if (drawing) CancelTrackingStroke();
                leftReady = rightReady = false;
                leftPinch.Reset();
                rightPinch.Reset();
                return;
            }

            float deltaTime = Time.unscaledDeltaTime;
            ReadHand(leftHand, leftSkeleton, leftPinch, ref leftReady, aim, deltaTime);
            ReadHand(rightHand, rightSkeleton, rightPinch, ref rightReady, aim, deltaTime);
        }

        void ReadHand(OVRHand hand, OVRSkeleton skeleton, RuneveilPinchDetector pinch, ref bool ready, AimSample aim,
            float deltaTime)
        {
            var settings = Pinch;
            if (!TryGetFingertips(hand, skeleton, out var tip, out var thumb, out bool hasThumb))
            {
                pinch.LostTime += deltaTime;
                // Keep the stroke alive through a brief tracking dropout.
                if (strokeHand == hand && pinch.LostTime <= settings.trackingLossGrace) return;
                if (strokeHand == hand) CancelTrackingStroke();
                pinch.Reset();
                ready = false;
                return;
            }

            pinch.LostTime = 0f;
            // The charge stays on the hand that made it, even between strokes.
            if (chargedSpell.HasValue && (chargeHand == hand || chargeHand == null)) chargeAnchor = tip;
            if (!handPosition.HasValue || strokeHand == hand) handPosition = tip;
            float distance = hasThumb ? Vector3.Distance(tip, thumb) : float.PositiveInfinity;
            pinch.Update(hand.GetFingerPinchStrength(OVRHand.HandFinger.Index), distance,
                hand.GetFingerIsPinching(OVRHand.HandFinger.Index), settings, deltaTime);

            bool pinching = pinch.Pinching;
            bool began = pinching && ready;
            ready = !pinching;
            if (strokeHand == hand)
            {
                // Remember where and what the player looked at the moment the fingers parted.
                if (pinch.GraceStarted)
                {
                    pendingReleasePoint = tip;
                    pendingReleaseAim = aim;
                    if (pinch.InGrace && aim.Windup.HasValue && aim.Target.Part.IsWeakPoint() &&
                        duel.CooldownLeft(Spell.Lightning) <= 0f && PreviewReleaseSpell(tip) == Spell.Lightning)
                    {
                        lightningReleaseWindup = aim.Windup;
                        lightningReleaseDeadline = Time.unscaledTimeAsDouble + settings.releaseGrace;
                    }
                }
                if (pinch.InGrace) return;
                lightningReleaseWindup = null;
                if (pinching) Sample(tip);
                else Release(pendingReleasePoint, pendingReleaseAim);
            }
            else if (began && !drawing)
            {
                BeginStroke(tip, hand);
            }
        }

        static bool TryGetFingertips(OVRHand hand, OVRSkeleton skeleton, out Vector3 index, out Vector3 thumb,
            out bool hasThumb)
        {
            index = default;
            thumb = default;
            hasThumb = false;
            if (!hand.IsDataValid || !hand.IsTracked || hand.IsSystemGestureInProgress ||
                !skeleton.IsInitialized || !skeleton.IsDataValid)
                return false;

            var skeletonType = skeleton.GetSkeletonType();
            bool openXrHand = skeletonType == OVRSkeleton.SkeletonType.XRHandLeft ||
                skeletonType == OVRSkeleton.SkeletonType.XRHandRight;
            var indexId = openXrHand ? OVRSkeleton.BoneId.XRHand_IndexTip : OVRSkeleton.BoneId.Hand_IndexTip;
            var thumbId = openXrHand ? OVRSkeleton.BoneId.XRHand_ThumbTip : OVRSkeleton.BoneId.Hand_ThumbTip;
            bool hasIndex = false;
            foreach (var bone in skeleton.Bones)
            {
                if (bone.Id == indexId)
                {
                    index = bone.Transform.position;
                    hasIndex = true;
                }
                else if (bone.Id == thumbId)
                {
                    thumb = bone.Transform.position;
                    hasThumb = true;
                }
            }
            return hasIndex;
        }

        Ray? MouseAim(out Ray? pointer)
        {
            pointer = null;
            Mouse mouse = Mouse.current;
            if (mouse == null || !Application.isFocused) return null;

            Vector2 position = mouse.position.ReadValue();
            if (view.pixelRect.Contains(position)) pointer = view.ScreenPointToRay(position);
            if (mouse.rightButton.wasPressedThisFrame && pointer.HasValue)
            {
                locked = !locked;
                if (locked) lockedViewportPoint = view.ScreenToViewportPoint(position);
                Notify(locked ? "Aim locked" : "Aim unlocked");
            }

            if (locked) return view.ViewportPointToRay(lockedViewportPoint);
            return pointer;
        }

        void UpdateMouse(AimSample aim)
        {
            handPosition = null;
            var mouse = Mouse.current;
            if (mouse == null || !Application.isFocused)
            {
                if (drawing) ClearStroke();
                pendingDesktopAction = false;
                return;
            }

            if (MenuAction.Length != 0 && Keyboard.current?.enterKey.wasPressedThisFrame == true)
            {
                StartBattle();
                return;
            }

            Vector2 screenPoint = mouse.position.ReadValue();
            bool inside = view.pixelRect.Contains(screenPoint);
            var cursorRay = view.ScreenPointToRay(screenPoint);
            var drawingPlane = new Plane(view.transform.forward, view.transform.position +
                view.transform.forward * DrawingDepth);
            if (!drawingPlane.Raycast(cursorRay, out float distance)) return;
            Vector3 point = cursorRay.GetPoint(distance);
            if (chargedSpell.HasValue && inside) chargeAnchor = point;

            if (mouse.leftButton.wasPressedThisFrame && inside)
            {
                pendingDesktopAction = MenuAction.Length != 0 && hud.ActionHit(cursorRay);
                if (!pendingDesktopAction) BeginStroke(point, null);
            }
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                if (pendingDesktopAction && inside && hud.ActionHit(cursorRay)) StartBattle();
                else if (drawing) Release(point, aim);
                pendingDesktopAction = false;
            }
            else if (drawing && mouse.leftButton.isPressed) Sample(point);
            else if (drawing) ClearStroke();
        }

        void BeginStroke(Vector3 point, OVRHand hand)
        {
            ClearStroke();
            drawing = true;
            menuAvailableAtStrokeStart = hand != null && MenuAction.Length != 0;
            strokeHand = hand;
            strokeStarted = Time.unscaledTime;
            origin = point;
            strokeRight = view.transform.right;
            strokeUp = view.transform.up;
            samples.Add(Vector2.zero);
            points.Add(point);
            if (chargedSpell.HasValue)
            {
                chargeHand = hand;
                chargeAnchor = point;
            }
            RefreshHandLabel();
            sfx.PlayTouch(point, Pinch);
        }

        void RefreshHandLabel()
        {
            if (chargedSpell.HasValue)
                hud.SetHandLabel(ChargeName(chargedSpell.Value).ToUpperInvariant() +
                    (chargeLevel > 1 ? $" Lv{chargeLevel}" : " READY"), ChargeColor(chargedSpell.Value));
            else hud.SetHandLabel("Drawing rune", HandLabelColor);
        }

        bool LooksLikeFire(Spell? strictMatch) => LooksLikeFire(samples, strictMatch);

        bool LooksLikeFire(IReadOnlyList<Vector2> stroke, Spell? strictMatch)
        {
            if (strictMatch == Spell.Fire) return true;
            if (strictMatch.HasValue) return false; // a confident other rune wins
            var s = Pinch;
            return RuneRecognizer.IsRoughCircle(stroke, s.circleMinSweep, s.circleMaxRadiusVariation,
                s.circleMaxCorner, s.circleMinSize);
        }

        Spell? ClassifyChargeable(IReadOnlyList<Vector2> stroke)
        {
            var strict = RuneRecognizer.MatchRune(stroke);
            Spell? match = LooksLikeFire(stroke, strict) ? (Spell?)Spell.Fire : strict;
            return match.HasValue && Chargeable(match.Value) ? match : null;
        }

        // True when some tail of the stroke drawn since the last charge is the given rune again.
        // Searching tails lets the connecting move back to a shape's start be ignored.
        bool RepeatsSince(int start, Spell spell)
        {
            int end = samples.Count;
            if (end - start < 6) return false;
            int step = Mathf.Max(1, (end - start) / 20);
            for (int from = start; from <= end - 6; from += step)
            {
                segment.Clear();
                Vector2 offset = samples[from];
                for (int i = from; i < end; i++) segment.Add(samples[i] - offset);
                if (ClassifyChargeable(segment) == spell) return true;
            }
            return false;
        }

        void Sample(Vector3 point)
        {
            if ((point - points[points.Count - 1]).sqrMagnitude < MinimumSampleDistanceSquared) return;
            if (samples.Count == MaxSamples)
            {
                ClearStroke();
                return;
            }

            Vector3 offset = point - origin;
            maxDisplacementSquared = Mathf.Max(maxDisplacementSquared, offset.sqrMagnitude);
            samples.Add(new Vector2(Vector3.Dot(offset, strokeRight), Vector3.Dot(offset, strokeUp)));
            points.Add(point);
            if (chargedSpell.HasValue) chargeAnchor = point;
            CheckCharge();
        }

        // Recognize the rune while the stroke is still being drawn. Once a shape has matched for a
        // moment, that spell is charged; releasing then casts it. Drawing the same shape again after
        // charging raises its power level. A fresh Lv1 charge can still change while the stroke goes on
        // (a horizontal line charges Ward, then turns into Lightning as the Z is finished).
        void CheckCharge()
        {
            if (!drawing || Time.unscaledTime < nextChargeCheck) return;
            float elapsed = nextChargeCheck <= 0f ? 0f : ChargeCheckInterval;
            nextChargeCheck = Time.unscaledTime + ChargeCheckInterval;
            var settings = Pinch;
            int maxChargeLevel = Mathf.Clamp(settings.maxChargeLevel, 1, RuneveilDuel.MaxChargeLevel);

            Spell? match = null;
            bool levelUp = false;
            if (!chargedSpell.HasValue)
            {
                match = ClassifyChargeable(samples);
            }
            else
            {
                // As on the remote: a fresh charge can still turn into another rune while the stroke goes on
                // (a horizontal line charges Ward, then becomes Lightning as the Z is finished).
                // Once powered up past Lv1 it stays that rune.
                if (chargedThisStroke && chargeLevel == 1)
                {
                    var whole = ClassifyChargeable(samples);
                    if (whole.HasValue && whole != chargedSpell) match = whole;
                }
                if (!match.HasValue && chargedSpell != Spell.Barrier && chargeLevel < maxChargeLevel &&
                    RepeatsSince(chargeSegmentStart, chargedSpell.Value))
                {
                    match = chargedSpell;
                    levelUp = true;
                }
            }
            // クールタイム中の魔法は新しく溜めない（溜まった表示なのに撃てない状態を作らない）
            if (match.HasValue && !levelUp && duel.CooldownLeft(match.Value) > 0) match = null;

            int key = match.HasValue ? (int)match.Value * 2 + (levelUp ? 1 : 0) : -1;
            if (key != pendingKey)
            {
                pendingKey = key;
                chargeMatchedFor = 0f;
                return;
            }
            if (!match.HasValue) return;

            chargeMatchedFor += elapsed;
            float needed = match == Spell.Barrier ? settings.wardConfirmTime : settings.chargeConfirmTime;
            if (chargeMatchedFor < needed) return;

            chargeLevel = levelUp ? chargeLevel + 1 : 1;
            chargedSpell = match;
            chargedThisStroke = true;
            chargeSegmentStart = samples.Count - 1;
            pendingKey = -1;
            chargeMatchedFor = 0f;
            chargeHand = strokeHand;
            chargeAnchor = points[points.Count - 1];
            RefreshHandLabel();
            sfx.PlayCharge(match.Value, chargeLevel, chargeAnchor, settings);
            string name = ChargeName(match.Value);
            if (levelUp)
                Notify(chargeLevel >= maxChargeLevel ? $"{name} Lv{chargeLevel} — MAX POWER!" : $"{name} Lv{chargeLevel}!");
            else
                Notify(match == Spell.Barrier
                    ? "Ward charged! Release to raise it"
                    : usingXr ? $"{name} charged! Look at a target and release" : $"{name} charged! Release to cast");
        }

        Spell? RecognizeStroke() => RuneRecognizer.MatchRune(samples) ?? (LooksLikeFire(null) ? Spell.Fire : (Spell?)null);

        Spell? SpellToRelease(Spell? recognized)
        {
            // A finished Z can resolve the earlier horizontal Ward prefix at release.
            if (chargedThisStroke && !(chargedSpell == Spell.Barrier && recognized == Spell.Lightning))
                return chargedSpell;
            return recognized ?? chargedSpell;
        }

        Spell? PreviewReleaseSpell(Vector3 point)
        {
            if ((point - points[points.Count - 1]).sqrMagnitude < MinimumSampleDistanceSquared)
                return SpellToRelease(RecognizeStroke());
            if (samples.Count == MaxSamples) return null;

            // Include the same fingertip sample as Release without modifying the live stroke.
            Vector3 offset = point - origin;
            samples.Add(new Vector2(Vector3.Dot(offset, strokeRight), Vector3.Dot(offset, strokeUp)));
            var spell = SpellToRelease(RecognizeStroke());
            samples.RemoveAt(samples.Count - 1);
            return spell;
        }

        void Release(Vector3 point, AimSample aim)
        {
            Sample(point);
            if (!drawing) return;

            drawing = false;
            strokeHand = null;
            strokeFadeUntil = Time.unscaledTime + 0.28f;
            var recognized = RecognizeStroke();
            var settings = Pinch;
            bool shortPinch = usingXr && Time.unscaledTime - strokeStarted <= settings.tapMaxDuration &&
                maxDisplacementSquared <= settings.tapMaxMovement * settings.tapMaxMovement;
            // デバッグ時はボタンが出ていれば、見ていなくても押したことにする
            bool onAction = menuAvailableAtStrokeStart && MenuAction.Length != 0 &&
                (aim.AssumedGaze || (aim.MenuActionRay.HasValue && hud.ActionHit(aim.MenuActionRay.Value)));
            if (!recognized.HasValue && shortPinch && onAction)
            {
                StartBattle();
                return;
            }
            var spell = SpellToRelease(recognized);
            bool replaceWardPrefix = chargedThisStroke && chargedSpell == Spell.Barrier && spell == Spell.Lightning;
            chargedThisStroke = false;
            bool releasingCharge = chargedSpell.HasValue && spell == chargedSpell;
            if (duel.Finished && !releasingCharge) return;
            if (!spell.HasValue)
            {
                RecordCast(null, ArenaTarget.Miss, "invalid", aim.AssumedGaze);
                Notify("Rune unclear. Try the shape again");
                return;
            }
            string label = spell == Spell.Barrier ? "Ward" : spell.Value.ToString();
            // 決着後に残った溜めを放つときは見た目だけ出すので、クールタイムは見ない
            if (!duel.Finished && !duel.TryCast(spell.Value))
            {
                RecordCast(spell, ArenaTarget.Miss, "recharging", aim.AssumedGaze);
                Notify($"{label} is recharging ({duel.CooldownLeft(spell.Value):0.0}s)");
                return;
            }
            int level = releasingCharge ? Mathf.Max(1, chargeLevel) : 1;
            if (releasingCharge || replaceWardPrefix) ClearCharge();

            SpellTarget target = aim.Target;
            Vector3 hit = spell == Spell.Barrier ? point : aim.Hit;
            if (spell != Spell.Barrier && target.Part == ArenaTarget.Miss)
            {
                target = duel.CaptureTarget(ArenaTarget.Body);
                hit = arena.BodyPoint(view.transform.position);
                if (!aim.SpellRay.HasValue) RecordCast(spell, target.Part, "fallback-aim", aim.AssumedGaze);
            }
            float travel = arena.Cast(spell.Value, point, hit, view.transform.position, view.transform.forward,
                level, showWardPulse: duel.WardVisible);
            sfx.PlayCast(spell.Value, point, Pinch, level);
            sfx.PlayImpact(spell.Value, level, hit, travel, Pinch);
            if (duel.Finished) return;
            spellLabel = label + (level > 1 ? $" Lv{level}" : "");
            if (spell == Spell.Barrier)
            {
                RecordCast(spell, ArenaTarget.Miss, "Shield", aim.AssumedGaze);
                Notify($"Ward up for {RuneveilDuel.ShieldSeconds:0.#}s");
                return;
            }
            pendingHits.Add(new PendingHit
            {
                Spell = spell.Value,
                Target = target,
                Delay = travel,
                AssumedGaze = aim.AssumedGaze,
                Level = level,
                InterruptWindup = spell == Spell.Lightning && target.Part.IsWeakPoint() ? aim.Windup : null
            });
        }

        /// <summary>飛んでいる攻撃魔法の着弾を待ち、着弾した瞬間にダメージと手応えを出す。</summary>
        void LandHits(float deltaTime)
        {
            // 弾の移動と同じ上限で時間を進め、見た目の着弾と判定をそろえる
            float step = Mathf.Min(deltaTime, 0.05f);
            for (int i = pendingHits.Count - 1; i >= 0; i--)
            {
                var pending = pendingHits[i];
                pending.Delay -= step;
                if (pending.Delay > 0)
                {
                    pendingHits[i] = pending;
                    continue;
                }

                pendingHits.RemoveAt(i);
                ArenaTarget part = duel.DamageTarget(pending.Target);
                var result = duel.Hit(pending.Spell, pending.Target, pending.Level);
                if (result == CastResult.None) continue;

                RecordCast(pending.Spell, part, result.ToString(), pending.AssumedGaze);
                bool won = duel.Phase == DuelPhase.Won;
                arena.Hit(pending.Spell, part,
                    result == CastResult.Interrupt || result == CastResult.Exposed || result == CastResult.FocusHit, won,
                    pending.Level);
                string power = pending.Level > 1 ? $"Lv{pending.Level} {pending.Spell}! " : "";
                if (won) noticeUntil = 0;
                else Notify(result switch
                {
                    CastResult.Interrupt => power + "Lightning stopped her strike. The Muse is down",
                    CastResult.Exposed => power + "Exposed weak point hit!",
                    CastResult.FocusHit => power + $"{part.DisplayName()} hit! Extra focus damage",
                    CastResult.Miss => "The spell missed",
                    _ => power + (part.IsWeakPoint()
                        ? $"{part.DisplayName()} hit!"
                        : "Glancing hit. Aim at the marker or focus on a weak point")
                });
            }
        }

        void CancelTrackingStroke()
        {
            if (!drawing) return;
            cancelledStrokes++;
            ClearStroke();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) ResetTracking();
        }

        void OnDisable()
        {
            ResetTracking();
            FlushDiagnostics();
        }

        void ResetTracking()
        {
            CancelTrackingStroke();
            leftReady = rightReady = false;
            leftPinch.Reset();
            rightPinch.Reset();
            pendingReleaseAim = default;
            duel.TrackFocus(ArenaTarget.Miss, 0f);
            handPosition = null;
            ResetEyeEvidence();
        }

        void RecordCast(Spell? spell, ArenaTarget target, string result, bool assumedGaze = false)
        {
            if (!Debug.isDebugBuild) return;
            castReport ??= new System.Text.StringBuilder();
            if (castReport.Length != 0) castReport.AppendLine();
            castReport.Append("[RuneveilCast] spell=").Append(spell?.ToString() ?? "None")
                .Append(" part=").Append(target).Append(" result=").Append(result);
            if (assumedGaze) castReport.Append(" source=debug-gaze");
        }

        void ReportInputs(bool aimed)
        {
            if (usingXr)
            {
                frameCount++;
                if (handPosition.HasValue) handFrames++;
                if (freshEye) freshEyeFrames++;
                if (aimed) independentEyeFrames++;
            }
            double now = Time.realtimeSinceStartupAsDouble;
            if (reportAt == 0) reportAt = now + 5d;
            if (now < reportAt) return;
            FlushDiagnostics();
            reportAt = now + 5d;
        }

        void FlushDiagnostics()
        {
            if (frameCount > 0)
                Debug.Log($"[RuneveilInput] frames={frameCount} hand={handFrames} validEye={freshEyeFrames} " +
                    $"independentEye={independentEyeFrames} focusLocks={focusLocks} cancelled={cancelledStrokes}");
            if (castReport != null && castReport.Length > 0)
            {
                Debug.Log(castReport.ToString());
                castReport.Clear();
            }
            frameCount = handFrames = freshEyeFrames = independentEyeFrames = focusLocks = cancelledStrokes = 0;
        }

        void ClearCharge()
        {
            chargedSpell = null;
            chargeHand = null;
            chargeLevel = 0;
        }

        void ClearStroke()
        {
            lightningReleaseWindup = null;
            chargeMatchedFor = 0f;
            pendingKey = -1;
            chargedThisStroke = false;
            chargeSegmentStart = 0;
            nextChargeCheck = 0f;
            drawing = false;
            strokeHand = null;
            strokeFadeUntil = 0;
            maxDisplacementSquared = 0;
            samples.Clear();
            points.Clear();
            hud.ShowStroke(points, DrawingColor, 0);
        }
    }
}
