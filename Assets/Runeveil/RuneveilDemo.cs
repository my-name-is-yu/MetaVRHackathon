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
        AimSample pendingReleaseAim;
        int focusLocks;

        readonly struct AimSample
        {
            public readonly Ray? Ray;
            public readonly ArenaTarget Target;
            public readonly Vector3 Hit;
            public readonly bool Focused;

            public AimSample(Ray? ray, ArenaTarget target, Vector3 hit, bool focused)
            {
                Ray = ray;
                Target = target;
                Hit = hit;
                Focused = focused;
            }
        }

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
            bool onCore = active && target == ArenaTarget.Core;
            bool wasFocused = duel.FocusReady;
            duel.TrackFocus(onCore && duel.Windup >= 0, deltaTime);
            if (Debug.isDebugBuild && usingXr && !wasFocused && duel.FocusReady) focusLocks++;
            var aim = new AimSample(ray, target, hit, onCore && duel.FocusReady);

            if (usingXr) UpdateHands(aim);
            else if (preview) UpdateMouse(aim);
            else if (drawing || pendingDesktopAction || locked)
            {
                ClearStroke();
                pendingDesktopAction = locked = false;
            }
            if (Debug.isDebugBuild) ReportInputs(ray.HasValue);
            arena.Tick(deltaTime, duel.Windup, duel.Shield, duel.Phase == DuelPhase.Won,
                onCore, duel.FocusProgress, duel.FocusReady);

            string focus = target switch
            {
                ArenaTarget.Core when duel.FocusReady => "Core exposed",
                ArenaTarget.Core when duel.FocusProgress > 0 => "Focusing on core",
                ArenaTarget.Core => locked ? "Core locked" : "Crystal core",
                ArenaTarget.Body => locked ? "Muse locked" : "Corrupted Muse",
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
            hud.SetControlHint(!usingXr && !preview ? "Connect a headset to draw and aim" : null);
            hud.SetActionFocus(usingXr ? ray : mousePointer);
            hud.SetHand(handPosition);
            hud.SetPinchMeter(usingXr && Pinch.showDebugMeter, leftPinch, rightPinch, Pinch);
            if (drawing) hud.ShowStroke(points, DrawingColor);
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
            duel.TrackFocus(false, 0);
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
            if (duel.FocusReady) return "Core exposed! Release an attack rune at the crystal";
            if (duel.Windup >= 0) return "Hold your gaze on the crystal, interrupt with Lightning, or draw a Ward";
            if (duel.Phase == DuelPhase.Battle) return "Aim for the crystal core";
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
            spellLabel = "";
            resultSummary = "";
            Notify("Duel started");
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
        }

        void Release(Vector3 point, AimSample aim)
        {
            Sample(point);
            if (!drawing) return;

            drawing = false;
            strokeHand = null;
            strokeFadeUntil = Time.unscaledTime + 0.28f;
            var spell = RuneRecognizer.MatchRune(samples);
            var settings = Pinch;
            bool shortPinch = usingXr && Time.unscaledTime - strokeStarted <= settings.tapMaxDuration &&
                maxDisplacementSquared <= settings.tapMaxMovement * settings.tapMaxMovement;
            if (!spell.HasValue && shortPinch && aim.Ray.HasValue && hud.ActionHit(aim.Ray.Value))
            {
                StartBattle();
                return;
            }
            if (duel.Finished) return;
            if (!spell.HasValue)
            {
                RecordCast(null, ArenaTarget.Miss, "invalid");
                Notify("Rune unclear. Try the shape again");
                return;
            }
            if (spell != Spell.Barrier && !aim.Ray.HasValue)
            {
                RecordCast(spell, ArenaTarget.Miss, "cancel");
                Notify(usingXr ? gazeHint ?? "Eye gaze unavailable. Look at a target and try again" : "Release inside the Game view");
                return;
            }

            var target = spell == Spell.Barrier ? ArenaTarget.Miss : aim.Target;
            Vector3 hit = spell == Spell.Barrier ? point : aim.Hit;
            var cast = duel.Cast(spell.Value, target, aim.Focused);
            RecordCast(spell, target, cast.ToString());
            arena.Cast(spell.Value, point, hit, target, view.transform.position, view.transform.forward);
            spellLabel = spell == Spell.Barrier ? "Ward" : spell.Value.ToString();
            if (duel.Phase == DuelPhase.Won) ShowResult("Victory");
            else Notify(cast switch
            {
                CastResult.Interrupt => "Lightning interrupted the strike",
                CastResult.FocusHit => "Focused strike! The exposed crystal took extra damage",
                CastResult.Shield => "Ward ready for the next strike",
                CastResult.Miss => "The spell missed",
                _ => target == ArenaTarget.Core ? "Crystal core hit" : "Muse hit"
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
            duel.TrackFocus(false, 0);
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
