using UnityEngine;

namespace Runeveil
{
    /// <summary>
    /// Procedurally synthesized sound effects (no audio assets needed):
    /// fingertip touch, drawing crackle loop, charge complete, and spell release.
    /// </summary>
    public sealed class RuneveilAudio : MonoBehaviour
    {
        const int Rate = 44100;

        AudioSource oneShot;
        AudioSource drawLoop;
        AudioClip touchClip;
        AudioClip drawClip;
        AudioClip chargeClip;
        AudioClip castClip;
        AudioClip wardClip;
        float drawLevel;
        float drawSpeed;

        public void Build()
        {
            if (oneShot != null) return;
            oneShot = CreateSource("SFX one-shots");
            drawLoop = CreateSource("SFX drawing loop");
            drawLoop.loop = true;
            drawLoop.volume = 0f;

            touchClip = Clip("Touch", 0.18f, Touch);
            drawClip = LoopClip("Drawing crackle", 1.6f, 0.12f, Crackle);
            chargeClip = Clip("Charge complete", 1.25f, Charge);
            castClip = Clip("Flame release", 0.8f, Cast);
            wardClip = Clip("Ward release", 0.7f, Ward);
            drawLoop.clip = drawClip;
        }

        AudioSource CreateSource(string label)
        {
            var child = new GameObject(label);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0.65f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 0.5f;
            source.maxDistance = 12f;
            source.dopplerLevel = 0f;
            return source;
        }

        public void PlayTouch(Vector3 at, RuneveilPinchSettings s) =>
            Play(touchClip, at, s.sfxVolume * s.touchVolume, Random.Range(0.95f, 1.06f));

        public void PlayCharge(Vector3 at, RuneveilPinchSettings s) =>
            Play(chargeClip, at, s.sfxVolume * s.chargeVolume, 1f);

        public void PlayCast(Spell spell, Vector3 at, RuneveilPinchSettings s)
        {
            if (spell == Spell.Barrier)
            {
                Play(wardClip, at, s.sfxVolume * s.castVolume, 1f);
                return;
            }
            float pitch = spell switch
            {
                Spell.Lightning => 1.45f,
                Spell.Ice => 1.25f,
                Spell.Rock => 0.75f,
                Spell.Wind => 1.12f,
                _ => 1f
            };
            Play(castClip, at, s.sfxVolume * s.castVolume, pitch * Random.Range(0.97f, 1.03f));
        }

        /// <summary>Call every frame. Speed is the fingertip speed in meters per second.</summary>
        public void SetDrawing(bool active, Vector3 at, float speed, RuneveilPinchSettings s, float dt)
        {
            if (drawLoop == null) return;
            drawSpeed = Mathf.Lerp(drawSpeed, active ? speed : 0f, 1f - Mathf.Exp(-dt * 14f));
            float motion = Mathf.Clamp01(drawSpeed / 0.6f);
            float target = active ? s.sfxVolume * s.drawVolume * (0.22f + 0.78f * motion) : 0f;
            drawLevel = Mathf.MoveTowards(drawLevel, target, dt * (active ? 6f : 9f));
            drawLoop.transform.position = at;
            drawLoop.volume = drawLevel;
            drawLoop.pitch = 0.85f + 0.45f * motion;
            if (drawLevel > 0.001f && !drawLoop.isPlaying)
            {
                drawLoop.timeSamples = Random.Range(0, drawClip.samples);
                drawLoop.Play();
            }
            else if (drawLevel <= 0.001f && drawLoop.isPlaying)
            {
                drawLoop.Stop();
            }
        }

        void Play(AudioClip clip, Vector3 at, float volume, float pitch)
        {
            if (clip == null || volume <= 0f) return;
            // A fresh source per sound lets overlapping effects keep their own position and pitch.
            var source = Instantiate(oneShot, transform);
            source.transform.position = at;
            source.pitch = pitch;
            source.volume = volume;
            source.clip = clip;
            source.Play();
            Destroy(source.gameObject, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
        }

        // ---------- synthesis ----------

        delegate void Synth(float[] data, System.Random random);

        static AudioClip Clip(string name, float seconds, Synth synth)
        {
            var data = new float[Mathf.CeilToInt(seconds * Rate)];
            synth(data, new System.Random(name.GetHashCode()));
            Normalize(data, 0.9f);
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip LoopClip(string name, float seconds, float crossfade, Synth synth)
        {
            int length = Mathf.CeilToInt(seconds * Rate);
            int fade = Mathf.CeilToInt(crossfade * Rate);
            var raw = new float[length + fade];
            synth(raw, new System.Random(name.GetHashCode()));
            var data = new float[length];
            for (int i = 0; i < length; i++)
            {
                if (i < fade)
                {
                    float w = i / (float)fade;
                    data[i] = raw[i] * w + raw[length + i] * (1f - w);
                }
                else data[i] = raw[i];
            }
            Normalize(data, 0.9f);
            var clip = AudioClip.Create(name, length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static void Normalize(float[] data, float peak)
        {
            float max = 0f;
            foreach (float v in data) max = Mathf.Max(max, Mathf.Abs(v));
            if (max < 1e-5f) return;
            float gain = peak / max;
            for (int i = 0; i < data.Length; i++) data[i] *= gain;
        }

        static float Noise(System.Random random) => (float)(random.NextDouble() * 2.0 - 1.0);
        static float Coefficient(float cutoff) => 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate);
        const float Tau = Mathf.PI * 2f;

        // Fingertips meet: crisp tick plus a small glassy chime.
        static void Touch(float[] d, System.Random r)
        {
            float high = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float n = Noise(r);
                high += Coefficient(5000f) * (n - high);
                float click = (n - high) * Mathf.Exp(-t * 420f) * 0.7f;
                float tick = Mathf.Sin(Tau * 2650f * t) * Mathf.Exp(-t * 95f) * 0.35f;
                float chime = Mathf.Sin(Tau * 1760f * t) * Mathf.Exp(-t * 24f) * 0.2f +
                    Mathf.Sin(Tau * 2637f * t) * Mathf.Exp(-t * 32f) * 0.12f;
                d[i] = click + tick + chime;
            }
        }

        // Sparkler: soft hiss with random crackles.
        static void Crackle(float[] d, System.Random r)
        {
            float lowA = 0f, lowB = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float n = Noise(r);
                lowA += Coefficient(7000f) * (n - lowA);
                lowB += Coefficient(1800f) * (n - lowB);
                d[i] = (lowA - lowB) * 0.18f;
            }
            int pops = Mathf.RoundToInt(d.Length / (float)Rate * 75f);
            for (int p = 0; p < pops; p++)
            {
                int start = r.Next(d.Length);
                int length = r.Next(Rate / 600, Rate / 180);
                float amplitude = 0.25f + (float)r.NextDouble() * 0.75f;
                float hp = 0f;
                for (int k = 0; k < length && start + k < d.Length; k++)
                {
                    float n = Noise(r);
                    hp += Coefficient(3500f) * (n - hp);
                    d[start + k] += (n - hp) * amplitude * Mathf.Exp(-k / (length * 0.25f));
                }
            }
        }

        // Ignition: deep whump, bright burst, shimmering chord and a few embers.
        static void Charge(float[] d, System.Random r)
        {
            float low = 0f, phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float n = Noise(r);
                float cutoff = Mathf.Lerp(700f, 6500f, Mathf.Exp(-t * 7f));
                low += Coefficient(cutoff) * (n - low);
                float burst = low * Mathf.Exp(-t * 5.5f) * Mathf.Clamp01(t * 200f) * 0.8f;
                float freq = 50f + 75f * Mathf.Exp(-t * 14f);
                phase += Tau * freq / Rate;
                float whump = Mathf.Sin(phase) * Mathf.Exp(-t * 8f) * 0.85f;
                float vibrato = 1f + 0.004f * Mathf.Sin(Tau * 5.5f * t);
                float shimmer = (Mathf.Sin(Tau * 659f * vibrato * t) + Mathf.Sin(Tau * 988f * vibrato * t) * 0.8f +
                    Mathf.Sin(Tau * 1319f * vibrato * t) * 0.6f) * Mathf.Exp(-t * 2.8f) * 0.11f *
                    Mathf.Clamp01(t * 30f);
                d[i] = burst + whump + shimmer;
            }
            for (int p = 0; p < 18; p++)
            {
                int start = r.Next(Rate / 20, d.Length * 2 / 3);
                int length = Rate / 400;
                float amplitude = (0.15f + (float)r.NextDouble() * 0.3f) * (1f - start / (float)d.Length);
                for (int k = 0; k < length && start + k < d.Length; k++)
                    d[start + k] += Noise(r) * amplitude * Mathf.Exp(-k / (length * 0.3f));
            }
        }

        // Flame release: falling band-passed roar with a punchy thump.
        static void Cast(float[] d, System.Random r)
        {
            float a = 0f, b = 0f, phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float n = Noise(r);
                float center = Mathf.Lerp(350f, 3800f, Mathf.Exp(-t * 6f));
                a += Coefficient(center * 1.8f) * (n - a);
                b += Coefficient(center * 0.45f) * (a - b);
                float env = Mathf.Clamp01(t * 60f) * Mathf.Exp(-t * 4.6f);
                float roar = (a - b) * env * 1.3f + b * env * 0.5f;
                phase += Tau * (45f + 90f * Mathf.Exp(-t * 18f)) / Rate;
                float thump = Mathf.Sin(phase) * Mathf.Exp(-t * 13f) * 0.7f;
                d[i] = roar + thump;
            }
        }

        // Ward: airy rising sweep with a soft bell.
        static void Ward(float[] d, System.Random r)
        {
            float low = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                low += Coefficient(Mathf.Lerp(600f, 4000f, t / 0.7f)) * (Noise(r) - low);
                float air = low * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.7f)) * 0.5f;
                float bell = (Mathf.Sin(Tau * 784f * t) + Mathf.Sin(Tau * 1175f * t) * 0.6f) * Mathf.Exp(-t * 4f) * 0.25f;
                d[i] = air + bell;
            }
        }
    }
}
