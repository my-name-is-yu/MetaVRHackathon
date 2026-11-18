using UnityEditor;
using UnityEngine;

namespace Runeveil.EditorTools
{
    /// <summary>
    /// Runeveil > Pinch Tuner. Edits the pinch settings asset and, during Play
    /// (Meta XR Simulator or Link), graphs each hand's live pinch strength and fingertip distance.
    /// </summary>
    public sealed class RuneveilPinchTuner : EditorWindow
    {
        const string DefaultAssetPath = "Assets/Runeveil/Resources/Runeveil/PinchSettings.asset";
        const float DistanceScale = 0.1f; // graph top = 10 cm

        static readonly Color StrengthColor = new Color(0.62f, 0.95f, 0.81f);
        static readonly Color DistanceColor = new Color(0.64f, 0.8f, 1f);
        static readonly Color StartColor = new Color(0.99f, 0.78f, 0.49f);
        static readonly Color ReleaseColor = new Color(0.75f, 0.55f, 0.6f);

        RuneveilPinchSettings settings;
        SerializedObject serialized;
        Vector2 scroll;
        readonly Vector3[] line = new Vector3[RuneveilPinchDetector.HistoryLength];

        [MenuItem("Runeveil/Pinch Tuner")]
        static void Open() => GetWindow<RuneveilPinchTuner>("Pinch Tuner");

        void OnEnable()
        {
            if (settings == null) settings = AssetDatabase.LoadAssetAtPath<RuneveilPinchSettings>(DefaultAssetPath);
            EditorApplication.update += RepaintWhilePlaying;
        }

        void OnDisable() => EditorApplication.update -= RepaintWhilePlaying;

        void RepaintWhilePlaying()
        {
            if (EditorApplication.isPlaying) Repaint();
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            var picked = (RuneveilPinchSettings)EditorGUILayout.ObjectField("Settings", settings,
                typeof(RuneveilPinchSettings), false);
            if (picked != settings)
            {
                settings = picked;
                serialized = null;
            }

            if (settings == null)
            {
                EditorGUILayout.HelpBox("Pinch settings asset not found.", MessageType.Warning);
                if (GUILayout.Button("Create " + DefaultAssetPath)) CreateAsset();
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawPresets();
            DrawFields();
            EditorGUILayout.Space(8);
            DrawLive();
            EditorGUILayout.EndScrollView();
        }

        void CreateAsset()
        {
            settings = CreateInstance<RuneveilPinchSettings>();
            AssetDatabase.CreateAsset(settings, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            serialized = null;
        }

        void DrawPresets()
        {
            EditorGUILayout.LabelField("Presets", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                Preset("Strict (original)", PinchPreset.Strict);
                Preset("Standard", PinchPreset.Standard);
                Preset("Loose", PinchPreset.Loose);
                Preset("Very loose", PinchPreset.VeryLoose);
            }
        }

        void Preset(string label, PinchPreset preset)
        {
            if (!GUILayout.Button(label)) return;
            Undo.RecordObject(settings, "Pinch preset");
            bool meter = settings.showDebugMeter;
            settings.ApplyPreset(preset);
            settings.showDebugMeter = meter;
            EditorUtility.SetDirty(settings);
            serialized = null;
        }

        void DrawFields()
        {
            if (serialized == null || serialized.targetObject != settings) serialized = new SerializedObject(settings);
            serialized.Update();
            var property = serialized.GetIterator();
            property.NextVisible(true); // skip m_Script
            while (property.NextVisible(false))
                EditorGUILayout.PropertyField(property, true);
            if (serialized.ApplyModifiedProperties()) EditorUtility.SetDirty(settings);

            if (EditorApplication.isPlaying)
                EditorGUILayout.HelpBox("Changes made during Play are saved to the asset and kept after you stop.",
                    MessageType.None);
        }

        void DrawLive()
        {
            EditorGUILayout.LabelField("Live hands", EditorStyles.boldLabel);
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play with Meta XR Simulator or Link to see live pinch values.\n" +
                    "Green line: strength. Blue line: fingertip distance (top = 10 cm).\n" +
                    "Shading: green = pinching, yellow = release grace, grey = no hand.", MessageType.Info);
                return;
            }
            DrawHand("Left", RuneveilPinchDetector.DebugLeft);
            DrawHand("Right", RuneveilPinchDetector.DebugRight);
        }

        void DrawHand(string name, RuneveilPinchDetector pinch)
        {
            if (pinch == null)
            {
                EditorGUILayout.LabelField(name, "no data (RuneveilDemo not running in XR)");
                return;
            }

            string state = !pinch.Tracked ? "no hand" : pinch.InGrace ? "GRACE" : pinch.Pinching ? "PINCH" : "open";
            string distance = float.IsInfinity(pinch.Distance) ? "--" : $"{pinch.Distance * 100f:0.0} cm";
            EditorGUILayout.LabelField(name,
                $"{state}   strength {pinch.Strength:0.00}   distance {distance}   system {(pinch.SystemPinch ? "on" : "off")}");

            Rect rect = GUILayoutUtility.GetRect(10, 110, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint) return;

            EditorGUI.DrawRect(rect, new Color(0.12f, 0.1f, 0.14f));
            int count = RuneveilPinchDetector.HistoryLength;
            float step = rect.width / (count - 1);

            // State shading.
            for (int i = 0; i < count; i++)
            {
                int index = (pinch.HistoryHead + 1 + i) % count;
                byte s = pinch.StateHistory[index];
                if (s == 0) continue;
                Color shade = s == 2 ? new Color(0.3f, 0.8f, 0.6f, 0.18f)
                    : s == 1 ? new Color(1f, 0.8f, 0.3f, 0.3f) : new Color(0.5f, 0.5f, 0.5f, 0.22f);
                EditorGUI.DrawRect(new Rect(rect.x + i * step, rect.y, Mathf.Ceil(step), rect.height), shade);
            }

            if (settings.useStrength)
            {
                Threshold(rect, settings.startStrength, StartColor);
                Threshold(rect, settings.releaseStrength, ReleaseColor);
            }
            if (settings.useDistance)
            {
                Threshold(rect, settings.startDistance / DistanceScale, new Color(DistanceColor.r, DistanceColor.g, DistanceColor.b, 0.9f));
                Threshold(rect, settings.releaseDistance / DistanceScale, new Color(DistanceColor.r, DistanceColor.g, DistanceColor.b, 0.45f));
            }

            Plot(rect, pinch.StrengthHistory, pinch.HistoryHead, 1f, StrengthColor);
            Plot(rect, pinch.DistanceHistory, pinch.HistoryHead, 1f / DistanceScale, DistanceColor);
        }

        static void Threshold(Rect rect, float value, Color color)
        {
            float y = rect.yMax - Mathf.Clamp01(value) * rect.height;
            EditorGUI.DrawRect(new Rect(rect.x, y, rect.width, 1), color);
        }

        void Plot(Rect rect, float[] values, int head, float scale, Color color)
        {
            int count = values.Length;
            float step = rect.width / (count - 1);
            int used = 0;
            for (int i = 0; i < count; i++)
            {
                float v = values[(head + 1 + i) % count];
                if (v < 0f) // unknown sample: flush segment
                {
                    DrawLine(used, color);
                    used = 0;
                    continue;
                }
                line[used++] = new Vector3(rect.x + i * step, rect.yMax - Mathf.Clamp01(v * scale) * rect.height);
            }
            DrawLine(used, color);
        }

        void DrawLine(int used, Color color)
        {
            if (used < 2) return;
            Handles.color = color;
            var points = new Vector3[used];
            System.Array.Copy(line, points, used);
            Handles.DrawAAPolyLine(2f, points);
        }
    }
}
