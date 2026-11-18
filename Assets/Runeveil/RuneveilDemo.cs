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
        static readonly Color DrawingColor = new Color32(255, 218, 139, 255);

        [SerializeField] Camera desktopView;
        [SerializeField] OVRCameraRig xrRig;
        [SerializeField] OVRHand leftHand;
        [SerializeField] OVRHand rightHand;
        [SerializeField] RuneveilArena arena;
        [SerializeField] RuneveilHud hud;
#if UNITY_EDITOR
        [SerializeField] bool desktopPreviewInEditor;
#endif

        readonly RuneveilDuel duel = new();
        readonly List<Vector2> samples = new(MaxSamples);
        readonly List<Vector3> points = new(MaxSamples);
        readonly List<XRDisplaySubsystem> displays = new();
        Camera xrView;
        Camera view;
        OVRSkeleton leftSkeleton;
        OVRSkeleton rightSkeleton;
        OVRPlugin.EyeGazeInteractionState eyeState;

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
            arena.Build();
            SelectCamera(HasXrDisplay());
        }

        void LateUpdate()
        {
            bool xrActive = HasXrDisplay();
            if (xrActive != usingXr) SelectCamera(xrActive);

            float deltaTime = Mathf.Min(Time.deltaTime, 0.1f);
            if (!usingXr || OVRManager.hasInputFocus) AdvanceDuel(deltaTime);
            Ray? mousePointer = null;
            Ray? aim = usingXr ? ReadGaze() : MouseAim(out mousePointer);

            if (usingXr) UpdateHands(aim);
            else UpdateMouse(aim);

            Vector3 ignored;
            var target = aim.HasValue ? arena.Aim(aim.Value, out ignored) : ArenaTarget.Miss;
            arena.Tick(deltaTime, duel.Windup, duel.Shield, duel.Phase == DuelPhase.Won,
                target == ArenaTarget.Core);

            string focus = target switch
            {
                ArenaTarget.Core => locked ? "Core locked" : "Crystal core",
                ArenaTarget.Body => locked ? "Golem locked" : "Golem",
                _ => usingXr && !aim.HasValue ? "Eye gaze unavailable" : locked ? "Aim locked" : ""
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
            hud.SetActionFocus(usingXr ? aim : mousePointer);
            hud.SetHand(handPosition);
            if (drawing) hud.ShowStroke(points, DrawingColor);
            else if (strokeFadeUntil > 0)
            {
                float opacity = Mathf.Clamp01((strokeFadeUntil - Time.unscaledTime) / 0.28f);
                hud.ShowStroke(points, DrawingColor, opacity);
                if (opacity <= 0) ClearStroke();
            }
        }

        bool HasXrDisplay()
        {
#if UNITY_EDITOR
            if (desktopPreviewInEditor) return false;
#endif
            SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays)
                if (display.running) return true;
            return false;
        }

        void SelectCamera(bool xr)
        {
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
            else if (duel.Phase == DuelPhase.Battle) Notify("The golem's fist hit you");
        }

        string DefaultStatus()
        {
            if (duel.Finished) return resultSummary;
            if (duel.Windup >= 0) return "Fist rising! Strike the core with Lightning or draw a Ward";
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
            if (!OVRManager.hasInputFocus) return null;

            Transform trackingSpace = xrRig.trackingSpace;
            if (OVRPlugin.eyeGazeInteractionsSupported && OVRPlugin.eyeGazeInteractionsEnabled &&
                OVRPlugin.GetEyeGazeInteractionState(OVRPlugin.Step.Render, -1, ref eyeState) && eyeState.IsValid)
                return new Ray(trackingSpace.TransformPoint(eyeState.Pose.Position.FromFlippedZVector3f()),
                    trackingSpace.TransformDirection(eyeState.Pose.Orientation.FromFlippedZQuatf() * Vector3.forward));

            // The simulator can expose its independent gaze through the OpenXR interaction profile.
            var eyes = InputSystem.GetDevice<EyeGazeInteraction.EyeGazeDevice>();
            if (eyes == null) return null;
            var pose = eyes.pose.ReadValue();
            const InputTrackingState required = InputTrackingState.Position | InputTrackingState.Rotation;
            if (!pose.isTracked || (pose.trackingState & required) != required) return null;
            return new Ray(trackingSpace.TransformPoint(pose.position),
                trackingSpace.TransformDirection(pose.rotation * Vector3.forward));
        }

        void UpdateHands(Ray? aim)
        {
            handPosition = null;
            if (!OVRManager.hasInputFocus)
            {
                if (drawing) ClearStroke();
                leftReady = rightReady = false;
                return;
            }

            ReadHand(leftHand, leftSkeleton, ref leftReady, aim);
            ReadHand(rightHand, rightSkeleton, ref rightReady, aim);
        }

        void ReadHand(OVRHand hand, OVRSkeleton skeleton, ref bool ready, Ray? aim)
        {
            if (!TryGetFingertip(hand, skeleton, out var tip))
            {
                if (strokeHand == hand) ClearStroke();
                ready = false;
                return;
            }

            if (!handPosition.HasValue || strokeHand == hand) handPosition = tip;
            bool pinching = hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
            bool began = pinching && ready;
            ready = !pinching;
            if (strokeHand == hand)
            {
                if (pinching) Sample(tip);
                else Release(tip, aim);
            }
            else if (began && !drawing)
            {
                BeginStroke(tip, hand);
            }
        }

        static bool TryGetFingertip(OVRHand hand, OVRSkeleton skeleton, out Vector3 point)
        {
            point = default;
            if (!hand.IsDataValid || !hand.IsTracked || !hand.IsPointerPoseValid || hand.IsSystemGestureInProgress ||
                !skeleton.IsInitialized || !skeleton.IsDataValid)
                return false;

            var skeletonType = skeleton.GetSkeletonType();
            var tip = skeletonType == OVRSkeleton.SkeletonType.XRHandLeft ||
                skeletonType == OVRSkeleton.SkeletonType.XRHandRight
                ? OVRSkeleton.BoneId.XRHand_IndexTip : OVRSkeleton.BoneId.Hand_IndexTip;
            foreach (var bone in skeleton.Bones)
            {
                if (bone.Id != tip) continue;

                point = bone.Transform.position;
                return true;
            }
            return false;
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

        void UpdateMouse(Ray? aim)
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

        void Release(Vector3 point, Ray? aim)
        {
            Sample(point);
            if (!drawing) return;

            drawing = false;
            strokeHand = null;
            strokeFadeUntil = Time.unscaledTime + 0.28f;
            var spell = RuneRecognizer.MatchRune(samples);
            bool shortPinch = usingXr && Time.unscaledTime - strokeStarted <= 0.45f &&
                maxDisplacementSquared <= 0.0009f;
            if (!spell.HasValue && shortPinch && aim.HasValue && hud.ActionHit(aim.Value))
            {
                StartBattle();
                return;
            }
            if (duel.Finished) return;
            if (!spell.HasValue)
            {
                Notify("Rune unclear. Try the shape again");
                return;
            }
            if (spell != Spell.Barrier && !aim.HasValue)
            {
                Notify(usingXr ? "Eye gaze unavailable. Look at a target and try again" : "Release inside the Game view");
                return;
            }

            Vector3 hit = point;
            var target = spell == Spell.Barrier ? ArenaTarget.Miss : arena.Aim(aim.Value, out hit);
            var cast = duel.Cast(spell.Value, target);
            arena.Cast(spell.Value, point, hit, target, view.transform.position, view.transform.forward);
            spellLabel = spell == Spell.Barrier ? "Ward" : spell.Value.ToString();
            if (duel.Phase == DuelPhase.Won) ShowResult("Victory");
            else Notify(cast switch
            {
                CastResult.Interrupt => "Lightning interrupted the strike",
                CastResult.Shield => "Ward ready for the next strike",
                CastResult.Miss => "The spell missed",
                _ => target == ArenaTarget.Core ? "Crystal core hit" : "Golem hit"
            });
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
