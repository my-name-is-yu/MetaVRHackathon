using UnityEngine;

namespace Runeveil
{
    public enum PinchPreset
    {
        Strict,
        Standard,
        Loose,
        VeryLoose
    }

    /// <summary>
    /// Pinch detection thresholds. Edit with Runeveil > Pinch Tuner, or select the asset at
    /// Assets/Runeveil/Resources/Runeveil/PinchSettings.asset. Values can be changed during Play.
    /// </summary>
    [CreateAssetMenu(menuName = "Runeveil/Pinch Settings", fileName = "PinchSettings")]
    public sealed class RuneveilPinchSettings : ScriptableObject
    {
        public const string ResourcePath = "Runeveil/PinchSettings";

        [Header("Pinch strength (0-1 from OVRHand)")]
        [Tooltip("Use the analog pinch strength from the hand tracker.")]
        public bool useStrength = true;
        [Tooltip("Strength needed to start a pinch. Lower = easier to start.")]
        [Range(0.05f, 1f)] public float startStrength = 0.6f;
        [Tooltip("Strength below which a pinch begins to end. Lower = harder to drop mid-stroke.")]
        [Range(0f, 1f)] public float releaseStrength = 0.35f;

        [Header("Thumb-to-index distance (meters)")]
        [Tooltip("Also treat the fingertips being close together as a pinch.")]
        public bool useDistance = true;
        [Tooltip("Fingertip distance that starts a pinch. Higher = easier to start.")]
        [Range(0.005f, 0.08f)] public float startDistance = 0.025f;
        [Tooltip("Fingertip distance beyond which a pinch begins to end. Higher = harder to drop.")]
        [Range(0.005f, 0.12f)] public float releaseDistance = 0.045f;

        [Header("System pinch flag")]
        [Tooltip("Also accept the runtime's built-in binary pinch (the original behavior).")]
        public bool acceptSystemPinch = true;

        [Header("Timing (seconds)")]
        [Tooltip("How long the start condition must hold before a pinch begins. 0 = immediate.")]
        [Range(0f, 0.2f)] public float startHoldTime = 0f;
        [Tooltip("How long the fingers must stay apart before the pinch ends. Bridges brief flickers.")]
        [Range(0f, 0.4f)] public float releaseGrace = 0.12f;
        [Tooltip("How long a drawing hand may lose tracking before the stroke is cancelled.")]
        [Range(0f, 0.6f)] public float trackingLossGrace = 0.2f;

        [Header("Short pinch (Start duel / Fight again)")]
        [Tooltip("Longest pinch that still counts as a button press.")]
        [Range(0.2f, 1.2f)] public float tapMaxDuration = 0.6f;
        [Tooltip("Farthest the fingertip may travel during a button press (meters).")]
        [Range(0.01f, 0.1f)] public float tapMaxMovement = 0.04f;

        [Header("Debug")]
        [Tooltip("Show a live pinch meter in the headset HUD.")]
        public bool showDebugMeter;

        static RuneveilPinchSettings current;

        public static RuneveilPinchSettings Current
        {
            get
            {
                if (current != null) return current;
                current = Resources.Load<RuneveilPinchSettings>(ResourcePath);
                if (current == null)
                {
                    current = CreateInstance<RuneveilPinchSettings>();
                    current.hideFlags = HideFlags.DontSave;
                }
                return current;
            }
        }

        public void ApplyPreset(PinchPreset preset)
        {
            switch (preset)
            {
                case PinchPreset.Strict: // Same as before this change: system flag only.
                    useStrength = false; startStrength = 0.95f; releaseStrength = 0.9f;
                    useDistance = false; startDistance = 0.015f; releaseDistance = 0.02f;
                    acceptSystemPinch = true;
                    startHoldTime = 0f; releaseGrace = 0f; trackingLossGrace = 0f;
                    tapMaxDuration = 0.45f; tapMaxMovement = 0.03f;
                    break;
                case PinchPreset.Standard:
                    useStrength = true; startStrength = 0.75f; releaseStrength = 0.5f;
                    useDistance = true; startDistance = 0.02f; releaseDistance = 0.035f;
                    acceptSystemPinch = true;
                    startHoldTime = 0f; releaseGrace = 0.08f; trackingLossGrace = 0.12f;
                    tapMaxDuration = 0.5f; tapMaxMovement = 0.035f;
                    break;
                case PinchPreset.Loose:
                    useStrength = true; startStrength = 0.6f; releaseStrength = 0.35f;
                    useDistance = true; startDistance = 0.025f; releaseDistance = 0.045f;
                    acceptSystemPinch = true;
                    startHoldTime = 0f; releaseGrace = 0.12f; trackingLossGrace = 0.2f;
                    tapMaxDuration = 0.6f; tapMaxMovement = 0.04f;
                    break;
                case PinchPreset.VeryLoose:
                    useStrength = true; startStrength = 0.45f; releaseStrength = 0.25f;
                    useDistance = true; startDistance = 0.035f; releaseDistance = 0.06f;
                    acceptSystemPinch = true;
                    startHoldTime = 0f; releaseGrace = 0.2f; trackingLossGrace = 0.3f;
                    tapMaxDuration = 0.7f; tapMaxMovement = 0.05f;
                    break;
            }
            OnValidate();
        }

        void OnValidate()
        {
            releaseStrength = Mathf.Min(releaseStrength, startStrength);
            releaseDistance = Mathf.Max(releaseDistance, startDistance);
        }
    }
}
