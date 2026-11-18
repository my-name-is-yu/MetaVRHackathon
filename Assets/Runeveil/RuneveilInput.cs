using System;
using UnityEngine;

/// <summary>Meta XR fingertip strokes and combined interaction gaze in Unity world space.</summary>
[DefaultExecutionOrder(300)]
public sealed class RuneveilInput : MonoBehaviour
{
    public event Action<Vector3> StrokeBegan;
    public event Action<Vector3> StrokePoint;
    public event Action<Vector3, Ray?> StrokeEnded;
    public event Action StrokeCancelled;
    public event Action<Transform, Vector3?, Ray?> TrackingUpdated;

    [SerializeField] private OVRCameraRig cameraRig;

    public Transform Head => cameraRig != null ? cameraRig.centerEyeAnchor : null;
    public string GazeStatus { get; private set; } = "視線を準備中";
    public string GazeHint { get; private set; } = "Preparing eye tracking";

    private readonly DrawingHand left = new DrawingHand();
    private readonly DrawingHand right = new DrawingHand();
    private DrawingHand drawing;
    private DrawingHand preferred;
    private Camera eyeCamera;
    private float nextRigSearch;
    private Vector3? currentTip;
    private Ray? currentGaze;
    private int sampledFrame = -1;
    private bool hasInitialDirection;
    private bool independentGaze;
    private Vector3 initialHeadLocalDirection;
    private double lastGazeTime;
    private double lastTimeChange;
    private double nextDiagnostic;
    private int diagnosticFrames;
    private int diagnosticHandFrames;
    private int diagnosticEyeFrames;
    private int diagnosticIndependentFrames;

    public bool TryGetGaze(out Ray ray)
    {
        ray = currentGaze.GetValueOrDefault();
        return sampledFrame == Time.frameCount && currentGaze.HasValue;
    }

    public bool TryGetHandPoint(out Vector3 point)
    {
        point = currentTip.GetValueOrDefault();
        return sampledFrame == Time.frameCount && currentTip.HasValue;
    }

    private void LateUpdate()
    {
        if ((cameraRig == null || !left.IsBound || !right.IsBound) && Time.unscaledTime >= nextRigSearch)
        {
            BindRig();
            nextRigSearch = Time.unscaledTime + 1f;
        }

        if (eyeCamera == null && Head != null)
            eyeCamera = Head.GetComponent<Camera>();

        if (!OVRManager.hasInputFocus)
        {
            ResetInput();
            GazeStatus = "VRアプリに戻ってください";
            GazeHint = "Return to the game to continue";
            TrackingUpdated?.Invoke(Head, null, null);
            return;
        }

        sampledFrame = Time.frameCount;
        currentGaze = ReadGaze();
        left.Sample();
        right.Sample();
        var hudHand = drawing ?? preferred;
        if (hudHand == null || !hudHand.Tracked)
            hudHand = right.Tracked ? right : left;
        currentTip = hudHand.Tracked ? hudHand.Tip : (Vector3?)null;
        TrackingUpdated?.Invoke(Head, currentTip, currentGaze);

        if (drawing != null)
        {
            if (!drawing.Tracked)
            {
                CancelStroke();
            }
            else if (drawing.Pinching)
            {
                StrokePoint?.Invoke(drawing.Tip);
            }
            else
            {
                var release = drawing.Tip;
                drawing = null;
                StrokeEnded?.Invoke(release, currentGaze);
            }
        }
        else
        {
            var started = right.Began ? right : left.Began ? left : null;
            if (started != null)
            {
                drawing = preferred = started;
                StrokeBegan?.Invoke(started.Tip);
            }
        }
        ReportTracking();
    }

    private void BindRig()
    {
        if (cameraRig == null)
            cameraRig = FindAnyObjectByType<OVRCameraRig>();
        foreach (var skeleton in FindObjectsByType<OVRSkeleton>())
        {
            switch (skeleton.GetSkeletonType())
            {
                case OVRSkeleton.SkeletonType.XRHandLeft:
                    left.Bind(skeleton, OVRSkeleton.BoneId.XRHand_IndexTip);
                    break;
                case OVRSkeleton.SkeletonType.XRHandRight:
                    right.Bind(skeleton, OVRSkeleton.BoneId.XRHand_IndexTip);
                    break;
                case OVRSkeleton.SkeletonType.HandLeft:
                    left.Bind(skeleton, OVRSkeleton.BoneId.Hand_IndexTip);
                    break;
                case OVRSkeleton.SkeletonType.HandRight:
                    right.Bind(skeleton, OVRSkeleton.BoneId.Hand_IndexTip);
                    break;
            }
        }
    }

    private Ray? ReadGaze()
    {
        if (eyeCamera == null)
            return GazeUnavailable("視線カメラを準備中", "Preparing the eye camera");
        if (!OVRPlugin.eyeGazeInteractionsSupported)
            return GazeUnavailable("この環境は視線入力に未対応です", "Eye gaze is unavailable on this XR runtime");
        if (!OVRPlugin.eyeGazeInteractionsEnabled)
            return GazeUnavailable("システム設定で視線入力を許可してください", "Enable eye tracking and allow eye access for this game");

        var state = default(OVRPlugin.EyeGazeInteractionState);
        if (!OVRPlugin.GetEyeGazeInteractionState(OVRPlugin.Step.Render, -1, ref state) || !state.IsValid)
        {
            GazeStatus = "視線を追跡できません";
            GazeHint = "Adjust your headset until your eyes are tracked";
            return null;
        }

        // OpenXR and Unity clocks need not share an epoch. Detect a frozen timestamp
        // against local elapsed time, without comparing their absolute values.
        double now = Time.realtimeSinceStartupAsDouble;
        if (state.Time > 0d)
        {
            if (state.Time != lastGazeTime)
            {
                lastGazeTime = state.Time;
                lastTimeChange = now;
            }
            else if (now - lastTimeChange > 0.2d)
            {
                GazeStatus = "視線の更新を待っています";
                GazeHint = "Waiting for a fresh eye sample";
                return null;
            }
        }

        var worldPose = state.Pose.ToOVRPose().ToWorldSpacePose(eyeCamera);
        var direction = worldPose.orientation * Vector3.forward;
        var localDirection = Head.InverseTransformDirection(direction);
        if (!hasInitialDirection)
        {
            initialHeadLocalDirection = localDirection;
            hasInitialDirection = true;
        }
        if (!independentGaze && Vector3.Angle(initialHeadLocalDirection, localDirection) > 1.5f)
            independentGaze = true;

        diagnosticEyeFrames++;
        GazeStatus = independentGaze ? "視線追跡中" : "頭を止めたまま、目だけで左右を見てください";
        GazeHint = independentGaze ? string.Empty : "Hold your head still and look left and right with your eyes";
        if (!independentGaze) return null;
        return new Ray(worldPose.position, direction);
    }

    private Ray? GazeUnavailable(string reason, string hint)
    {
        GazeStatus = reason;
        GazeHint = hint;
        hasInitialDirection = independentGaze = false;
        return null;
    }

    private void CancelStroke()
    {
        if (drawing == null) return;
        drawing = null;
        StrokeCancelled?.Invoke();
    }

    private void ResetInput()
    {
        CancelStroke();
        left.ResetPinch();
        right.ResetPinch();
        currentGaze = null;
        currentTip = null;
        sampledFrame = -1;
        hasInitialDirection = independentGaze = false;
        lastGazeTime = 0d;
    }

    private void OnDisable() => ResetInput();

    private void OnApplicationPause(bool paused)
    {
        if (paused) ResetInput();
    }

    private void ReportTracking()
    {
        if (!Debug.isDebugBuild) return;
        diagnosticFrames++;
        if (currentTip.HasValue) diagnosticHandFrames++;
        if (currentGaze.HasValue) diagnosticIndependentFrames++;
        double now = Time.realtimeSinceStartupAsDouble;
        if (now < nextDiagnostic) return;
        Debug.Log($"[RuneveilInput] frames={diagnosticFrames} hand={diagnosticHandFrames} "
            + $"validEye={diagnosticEyeFrames} independentEye={diagnosticIndependentFrames} status={GazeStatus}");
        diagnosticFrames = diagnosticHandFrames = diagnosticEyeFrames = diagnosticIndependentFrames = 0;
        nextDiagnostic = now + 5d;
    }

    private sealed class DrawingHand
    {
        private OVRSkeleton skeleton;
        private OVRHand hand;
        private OVRSkeleton.BoneId tipId;

        public bool IsBound => skeleton != null && hand != null;
        public bool Tracked { get; private set; }
        public bool Pinching { get; private set; }
        public bool Began { get; private set; }
        public Vector3 Tip { get; private set; }

        public void Bind(OVRSkeleton candidate, OVRSkeleton.BoneId boneId)
        {
            if (IsBound || !candidate.TryGetComponent<OVRHand>(out var trackedHand)) return;
            hand = trackedHand;
            skeleton = candidate;
            tipId = boneId;
        }

        public void Sample()
        {
            bool wasTracked = Tracked;
            bool wasPinching = Pinching;
            ResetPinch();
            if (!IsBound || !hand.isActiveAndEnabled || !skeleton.isActiveAndEnabled ||
                !hand.IsTracked || !hand.IsDataValid || hand.IsSystemGestureInProgress ||
                !skeleton.IsInitialized || !skeleton.IsDataValid) return;

            // The ordinary Meta hand skeleton keeps this list in bone-ID order.
            // Check the ID as well so a different rig cannot supply the wrong tip.
            int i = (int)tipId;
            var bones = skeleton.Bones;
            if (bones == null || i >= bones.Count || bones[i].Id != tipId || bones[i].Transform == null) return;
            Tip = bones[i].Transform.position;
            Tracked = true;
            Pinching = hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
            Began = wasTracked && !wasPinching && Pinching;
        }

        public void ResetPinch()
        {
            Tracked = Pinching = Began = false;
        }
    }
}
