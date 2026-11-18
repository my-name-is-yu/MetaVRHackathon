using UnityEngine;

namespace Runeveil
{
    /// <summary>
    /// Per-hand pinch state with hysteresis (separate start/release thresholds) and a short
    /// release grace period, so brief tracking flickers do not end a stroke.
    /// </summary>
    public sealed class RuneveilPinchDetector
    {
        public const int HistoryLength = 240;

        // Live instances for the Pinch Tuner window. Set by RuneveilDemo.
        public static RuneveilPinchDetector DebugLeft;
        public static RuneveilPinchDetector DebugRight;

        public float Strength { get; private set; }
        /// <summary>Thumb tip to index tip in meters; PositiveInfinity when unknown.</summary>
        public float Distance { get; private set; } = float.PositiveInfinity;
        public bool SystemPinch { get; private set; }
        public bool Tracked { get; private set; }
        public bool Pinching { get; private set; }
        /// <summary>Fingers have separated but the release grace period has not elapsed.</summary>
        public bool InGrace { get; private set; }
        /// <summary>True on the frame the release condition first became true.</summary>
        public bool GraceStarted { get; private set; }
        public float LostTime { get; set; }

        public readonly float[] StrengthHistory = new float[HistoryLength];
        public readonly float[] DistanceHistory = new float[HistoryLength];
        public readonly byte[] StateHistory = new byte[HistoryLength]; // 0 open, 1 grace, 2 pinching, 3 untracked
        public int HistoryHead { get; private set; }

        float startHeld;
        float releaseHeld;

        public void Reset()
        {
            Strength = 0f;
            Distance = float.PositiveInfinity;
            SystemPinch = false;
            Tracked = false;
            Pinching = false;
            InGrace = false;
            GraceStarted = false;
            startHeld = 0f;
            releaseHeld = 0f;
            Record();
        }

        public void Update(float strength, float distance, bool systemPinch, RuneveilPinchSettings settings, float deltaTime)
        {
            Strength = strength;
            Distance = distance;
            SystemPinch = systemPinch;
            Tracked = true;
            GraceStarted = false;

            bool system = settings.acceptSystemPinch && systemPinch;
            bool start = system ||
                (settings.useStrength && strength >= settings.startStrength) ||
                (settings.useDistance && distance <= settings.startDistance);
            bool hold = system ||
                (settings.useStrength && strength >= settings.releaseStrength) ||
                (settings.useDistance && distance <= settings.releaseDistance);

            if (!Pinching)
            {
                InGrace = false;
                startHeld = start ? startHeld + deltaTime : 0f;
                if (start && startHeld >= settings.startHoldTime)
                {
                    Pinching = true;
                    releaseHeld = 0f;
                }
            }
            else if (hold)
            {
                InGrace = false;
                releaseHeld = 0f;
            }
            else
            {
                if (releaseHeld <= 0f) GraceStarted = true;
                releaseHeld += deltaTime;
                InGrace = releaseHeld < settings.releaseGrace;
                if (!InGrace)
                {
                    Pinching = false;
                    startHeld = 0f;
                    releaseHeld = 0f;
                }
            }
            Record();
        }

        void Record()
        {
            HistoryHead = (HistoryHead + 1) % HistoryLength;
            StrengthHistory[HistoryHead] = Strength;
            DistanceHistory[HistoryHead] = float.IsInfinity(Distance) ? -1f : Distance;
            StateHistory[HistoryHead] = (byte)(!Tracked ? 3 : InGrace ? 1 : Pinching ? 2 : 0);
        }
    }
}
