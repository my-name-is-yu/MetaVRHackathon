using System.Collections.Generic;
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
#endif

        readonly RuneveilDuel duel = new();
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
        // Latest eye ray even when it has not been verified as independent eye movement.
        Ray? unverifiedGaze;
        bool freshEye;
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
        int focusLocks;

        readonly struct AimSample
        {
            public readonly Ray? Ray;
            public readonly ArenaTarget Target;
            public readonly Vector3 Hit;
            public readonly ArenaTarget ExposedTarget;

            public AimSample(Ray? ray, ArenaTarget target, Vector3 hit, ArenaTarget exposedTarget)
            {
                Ray = ray;
                Target = target;
                Hit = hit;
                ExposedTarget = exposedTarget;
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

        void LateUpdate()
        {
            bool xrActive = HasXrDisplay();
            if (xrActive != usingXr) SelectCamera(xrActive);

            float deltaTime = Mathf.Min(Time.deltaTime, 0.1f);
            bool preview = !usingXr && DesktopPreviewEnabled;
            bool active = usingXr ? OVRManager.hasInputFocus : preview && Application.isFocused;
            if (active) AdvanceDuel(deltaTime);
            Ray? mousePointer = null;
            Ray? ray = null;
            if (usingXr) ray = ReadGaze();
            else if (preview) ray = MouseAim(out mousePointer);
            Vector3 hit = default;
            var target = ray.HasValue ? arena.Aim(ray.Value, out hit) : ArenaTarget.Miss;
            var aimed = active ? target : ArenaTarget.Miss;
            bool wasFocused = duel.FocusReady;
            duel.TrackFocus(arena.IsWeakPointExposed(aimed, duel.Windup) ? aimed : ArenaTarget.Miss, deltaTime);
            if (Debug.isDebugBuild && usingXr && !wasFocused && duel.FocusReady)
            {
                focusLocks++;
                Debug.Log($"[RuneveilFocus] part={duel.ExposedTarget}");
            }
            var aim = new AimSample(ray, target, hit, duel.ExposedTarget);

            if (usingXr) UpdateHands(aim);
            else if (preview) UpdateMouse(aim);
            else if (drawing || pendingDesktopAction || locked)
            {
                ClearStroke();
                pendingDesktopAction = locked = false;
            }
            if (Debug.isDebugBuild) ReportInputs(ray.HasValue);
            arena.Tick(deltaTime, duel.Windup, duel.Shield, duel.Phase == DuelPhase.Won,
                aimed, duel.FocusProgress, duel.FocusReady);
            arena.SetCharge(chargedSpell, chargeLevel, chargeAnchor, view.transform.position, deltaTime);
            UpdateDrawingFeedback(deltaTime);
            music.Tick(duel.Phase == DuelPhase.Battle, duel.Phase == DuelPhase.Won, duel.Phase == DuelPhase.Lost,
                duel.Windup >= 0f, Pinch, Time.unscaledDeltaTime);

            string focus = target switch
            {
                ArenaTarget.Body => locked ? "Muse locked" : "Corrupted Muse",
                _ when target.IsWeakPoint() => target.DisplayName(),
                _ => usingXr && !ray.HasValue ? "Eye gaze unavailable" : locked ? "Aim locked" : ""
            };
            string status = Time.unscaledTime < noticeUntil ? notice : DefaultStatus();
            string action = duel.Phase switch
            {
                DuelPhase.Practice => "Start duel",
                DuelPhase.Won or DuelPhase.Lost => "Fight again",
                _ => ""
            };
            hud.Render(duel.BossHealth, duel.Hearts, duel.Shield, focus, spellLabel, status,
                duel.Phase == DuelPhase.Practice, action, usingXr);
            hud.SetVictoryIllustration(duel.Phase == DuelPhase.Won, Time.unscaledDeltaTime);
            hud.SetControlHint(!usingXr && !preview ? "Connect a headset to draw and aim" : null);
            hud.SetActionFocus(usingXr ? ray : mousePointer);
            hud.SetHand(handPosition);
            hud.SetPinchMeter(usingXr && Pinch.showDebugMeter, leftPinch, rightPinch, Pinch);
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
            duel.TrackFocus(ArenaTarget.Miss, 0);
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
            var impact = duel.Step(deltaTime);
            if (impact == AttackResult.None) return;

            bool blocked = impact == AttackResult.Blocked;
            arena.Impact(blocked);
            if (blocked) Notify("Your ward blocked the strike");
            else if (duel.Phase == DuelPhase.Lost) ShowResult("Defeated");
            else if (duel.Phase == DuelPhase.Battle) Notify("The Muse's strike hit you");
        }

        string DefaultStatus()
        {
            if (!usingXr && !DesktopPreviewEnabled) return "";
            if (duel.Finished) return resultSummary;
            if (usingXr && !string.IsNullOrEmpty(gazeHint)) return gazeHint;
            if (usingXr && !handPosition.HasValue) return "Bring a hand into view, then pinch";
            if (duel.FocusReady) return "Weak point exposed! Release an attack rune";
            if (chargedSpell.HasValue && !drawing)
                return chargedSpell == Spell.Barrier
                    ? (usingXr ? "Ward charged — pinch and release to raise it" : "Ward charged — click to raise it")
                    : $"{ChargeName(chargedSpell.Value)} charged — " +
                      (usingXr ? "look at a target, pinch and release" : "click to cast");
            if (duel.Windup >= 0) return "Hold your gaze on the crystal, a horn, the right hand or the raised fist";
            if (duel.Phase == DuelPhase.Battle) return "Watch for Muse to raise a fist";
            return "";
        }

        void ShowResult(string outcome)
        {
            noticeUntil = 0;
            resultSummary = $"{outcome}  |  Interrupts {duel.Interruptions}  |  Blocks {duel.Blocks}";
        }

        void Notify(string message)
        {
            notice = message;
            noticeUntil = Time.unscaledTime + 1.65f;
        }

        void StartBattle()
        {
            ClearStroke();
            duel.StartBattle();
            arena.ResetArena();
            hud.SetVictoryIllustration(false, 0f);
            spellLabel = "";
            resultSummary = "";
            Notify("Duel started");
        }

        Ray? ReadGaze()
        {
            freshEye = false;
            unverifiedGaze = null;
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
                unverifiedGaze = ray;
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
                unverifiedGaze ??= ray;
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
                }
                if (pinch.InGrace) return;
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

            if (duel.Phase != DuelPhase.Battle && Keyboard.current?.enterKey.wasPressedThisFrame == true)
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
                pendingDesktopAction = duel.Phase != DuelPhase.Battle && hud.ActionHit(cursorRay);
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
                if (!match.HasValue && chargeLevel < settings.maxChargeLevel &&
                    RepeatsSince(chargeSegmentStart, chargedSpell.Value))
                {
                    match = chargedSpell;
                    levelUp = true;
                }
            }

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
                Notify(chargeLevel >= settings.maxChargeLevel ? $"{name} Lv{chargeLevel} — MAX POWER!" : $"{name} Lv{chargeLevel}!");
            else
                Notify(match == Spell.Barrier
                    ? "Ward charged! Release to raise it"
                    : usingXr ? $"{name} charged! Look at a target and release" : $"{name} charged! Release to cast");
        }

        void Release(Vector3 point, AimSample aim)
        {
            Sample(point);
            if (!drawing) return;

            drawing = false;
            strokeHand = null;
            strokeFadeUntil = Time.unscaledTime + 0.28f;
            var recognized = RuneRecognizer.MatchRune(samples);
            if (!recognized.HasValue && LooksLikeFire(null)) recognized = Spell.Fire;
            var settings = Pinch;
            bool shortPinch = usingXr && Time.unscaledTime - strokeStarted <= settings.tapMaxDuration &&
                maxDisplacementSquared <= settings.tapMaxMovement * settings.tapMaxMovement;
            if (!recognized.HasValue && shortPinch && aim.Ray.HasValue && hud.ActionHit(aim.Ray.Value))
            {
                StartBattle();
                return;
            }
            // A held charge is spent only when it is actually cast; otherwise it is kept.
            // A charge made during this stroke is what gets cast. A charge held from an earlier stroke is
            // kept if this stroke draws a different rune, and cast otherwise.
            var spell = chargedThisStroke ? chargedSpell
                : recognized.HasValue && recognized != chargedSpell ? recognized : chargedSpell ?? recognized;
            chargedThisStroke = false;
            bool releasingCharge = chargedSpell.HasValue && spell == chargedSpell;
            if (duel.Finished && !releasingCharge) return;
            if (!spell.HasValue)
            {
                RecordCast(null, ArenaTarget.Miss, "invalid");
                Notify("Rune unclear. Try the shape again");
                return;
            }
            if (spell != Spell.Barrier && !aim.Ray.HasValue)
            {
                // No verified eye gaze: still cast. Aim along the raw eye ray if there is one, otherwise
                // where the head faces. Focus bonuses still need verified gaze, so none applies here.
                Ray fallback = (usingXr ? unverifiedGaze : null) ?? new Ray(view.transform.position, view.transform.forward);
                var fallbackTarget = arena.Aim(fallback, out Vector3 fallbackHit);
                aim = new AimSample(fallback, fallbackTarget, fallbackHit, ArenaTarget.Miss);
                RecordCast(spell, fallbackTarget, "fallback-aim");
            }

            int level = releasingCharge ? Mathf.Max(1, chargeLevel) : 1;
            if (releasingCharge)
            {
                chargedSpell = null;
                chargeHand = null;
                chargeLevel = 0;
            }
            var target = spell == Spell.Barrier ? ArenaTarget.Miss : aim.Target;
            Vector3 hit = spell == Spell.Barrier ? point : aim.Hit;
            if (duel.Finished)
            {
                float flight = arena.Cast(spell.Value, point, hit, ArenaTarget.Miss, view.transform.position,
                    view.transform.forward, level);
                sfx.PlayCast(spell.Value, point, Pinch, level);
                sfx.PlayImpact(spell.Value, level, hit, flight, Pinch);
                return;
            }
            var cast = duel.Cast(spell.Value, target, aim.ExposedTarget, level);
            RecordCast(spell, target, cast.ToString());
            float flightTime = arena.Cast(spell.Value, point, hit, target, view.transform.position,
                view.transform.forward, level);
            sfx.PlayCast(spell.Value, point, Pinch, level);
            sfx.PlayImpact(spell.Value, level, hit, flightTime, Pinch);
            string spellName = spell == Spell.Barrier ? "Ward" : spell.Value.ToString();
            spellLabel = spellName + (level > 1 ? $" Lv{level}" : "");
            string power = level > 1 ? $"Lv{level} {spellName}! " : "";
            if (duel.Phase == DuelPhase.Won) ShowResult("Victory");
            else Notify(cast switch
            {
                CastResult.Interrupt => power + "Lightning interrupted the strike",
                CastResult.FocusHit => $"{power}{target.DisplayName()} exposed! Extra damage",
                CastResult.Shield => power + "Ward ready for the next strike",
                CastResult.Miss => power + "The spell missed",
                _ => power + (target.IsWeakPoint() ? $"{target.DisplayName()} hit" : "Muse hit")
            });
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
            duel.TrackFocus(ArenaTarget.Miss, 0);
            handPosition = null;
            ResetEyeEvidence();
        }

        void RecordCast(Spell? spell, ArenaTarget target, string result)
        {
            if (!Debug.isDebugBuild) return;
            castReport ??= new System.Text.StringBuilder();
            if (castReport.Length != 0) castReport.AppendLine();
            castReport.Append("[RuneveilCast] spell=").Append(spell?.ToString() ?? "None")
                .Append(" part=").Append(target).Append(" result=").Append(result);
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

        void ClearStroke()
        {
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
