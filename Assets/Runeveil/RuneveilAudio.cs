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
        // Indexed by (int)Spell. Rock has no charge, so its slots stay null.
        readonly AudioSource[] chargedLoops = new AudioSource[6];
        readonly AudioClip[] chargedLoopClips = new AudioClip[6];
        readonly AudioClip[] chargeClips = new AudioClip[6];
        readonly float[] chargedLevels = new float[6];
        AudioClip touchClip;
        AudioClip drawClip;
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
            chargeClips[(int)Spell.Fire] = Clip("Fire charge", 1.25f, Charge);
            chargeClips[(int)Spell.Lightning] = Clip("Lightning charge", 1.1f, LightningCharge);
            chargeClips[(int)Spell.Ice] = Clip("Ice charge", 1.4f, IceCharge);
            chargeClips[(int)Spell.Wind] = Clip("Wind charge", 1.1f, WindCharge);
            chargeClips[(int)Spell.Barrier] = Clip("Ward charge", 1.4f, WardCharge);
            castClip = Clip("Flame release", 0.8f, Cast);
            wardClip = Clip("Ward release", 0.7f, Ward);
            drawLoop.clip = drawClip;
            AddChargedLoop(Spell.Fire, LoopClip("Charged flame roar", 2.4f, 0.2f, FlameRoar));
            AddChargedLoop(Spell.Lightning, LoopClip("Charged electric buzz", 2f, 0.15f, ElectricBuzz));
            AddChargedLoop(Spell.Ice, LoopClip("Charged frost shimmer", 2.4f, 0.2f, FrostShimmer));
            AddChargedLoop(Spell.Wind, LoopClip("Charged gust", 2.4f, 0.3f, Gust));
            AddChargedLoop(Spell.Barrier, LoopClip("Charged ward hum", 2f, 0.2f, WardHum));
        }

        void AddChargedLoop(Spell spell, AudioClip clip)
        {
            var source = CreateSource("SFX charged loop " + spell);
            source.loop = true;
            source.volume = 0f;
            source.clip = clip;
            chargedLoops[(int)spell] = source;
            chargedLoopClips[(int)spell] = clip;
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

        public void PlayCharge(Spell spell, Vector3 at, RuneveilPinchSettings s) =>
            Play(chargeClips[(int)spell], at, s.sfxVolume * s.chargeVolume, 1f);

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

        /// <summary>
        /// Call every frame. Speed is the fingertip speed in meters per second.
        /// While charged, the sparkler crackle crossfades into a roaring flame.
        /// </summary>
        public void SetDrawing(bool active, Spell? charged, Vector3 at, float speed, RuneveilPinchSettings s, float dt)
        {
            if (drawLoop == null) return;
            drawSpeed = Mathf.Lerp(drawSpeed, active ? speed : 0f, 1f - Mathf.Exp(-dt * 14f));
            float motion = Mathf.Clamp01(drawSpeed / 0.6f);
            float level = s.sfxVolume * s.drawVolume * (0.22f + 0.78f * motion);

            drawLevel = Mathf.MoveTowards(drawLevel, active && !charged.HasValue ? level : 0f, dt * (active ? 6f : 9f));
            UpdateLoop(drawLoop, drawClip, drawLevel, 0.85f + 0.45f * motion, at);

            // Each charged element has its own moving sound; it swells with speed, like swinging a torch.
            for (int i = 0; i < chargedLoops.Length; i++)
            {
                if (chargedLoops[i] == null) continue;
                bool on = active && charged.HasValue && (int)charged.Value == i;
                float target = on ? s.sfxVolume * s.chargedDrawVolume * (0.35f + 0.65f * motion) : 0f;
                chargedLevels[i] = Mathf.MoveTowards(chargedLevels[i], target, dt * (active ? 5f : 8f));
                UpdateLoop(chargedLoops[i], chargedLoopClips[i], chargedLevels[i], 0.8f + 0.35f * motion, at);
            }
        }

        static void UpdateLoop(AudioSource source, AudioClip clip, float volume, float pitch, Vector3 at)
        {
            source.transform.position = at;
            source.volume = volume;
            source.pitch = pitch;
            if (volume > 0.001f && !source.isPlaying)
            {
                source.timeSamples = Random.Range(0, clip.samples);
                source.Play();
            }
            else if (volume <= 0.001f && source.isPlaying)
            {
                source.Stop();
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

        // Charged flame: low rumbling roar with a flickering mid-band whoosh and soft pops.
        static void FlameRoar(float[] d, System.Random r)
        {
            float rumble = 0f, rumble2 = 0f, midA = 0f, midB = 0f;
            float flickerPhase = 0f, swirlPhase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float n = Noise(r);
                rumble += Coefficient(220f) * (n - rumble);
                rumble2 += Coefficient(220f) * (rumble - rumble2);
                midA += Coefficient(2600f) * (n - midA);
                midB += Coefficient(700f) * (n - midB);
                // Irregular flicker: two slow sines at unrelated rates.
                flickerPhase += Tau * (3.1f + 1.3f * Mathf.Sin(Tau * 0.41f * t)) / Rate;
                swirlPhase += Tau * 1.7f / Rate;
                float flicker = 0.65f + 0.35f * Mathf.Sin(flickerPhase) * Mathf.Sin(swirlPhase + 0.8f);
                d[i] = rumble2 * 5.5f + (midA - midB) * 0.5f * flicker;
            }
            int pops = Mathf.RoundToInt(d.Length / (float)Rate * 9f);
            for (int p = 0; p < pops; p++)
            {
                int start = r.Next(d.Length);
                int length = r.Next(Rate / 120, Rate / 50);
                float amplitude = 0.2f + (float)r.NextDouble() * 0.35f;
                float low = 0f;
                for (int k = 0; k < length && start + k < d.Length; k++)
                {
                    low += Coefficient(900f) * (Noise(r) - low);
                    d[start + k] += low * amplitude * 3f * Mathf.Exp(-k / (length * 0.3f));
                }
            }
        }

        static void Ping(float[] d, int start, float freq, float decay, float amplitude)
        {
            for (int k = 0; start + k < d.Length; k++)
            {
                float t = k / (float)Rate;
                float env = Mathf.Exp(-t * decay);
                if (env < 0.001f) break;
                d[start + k] += Mathf.Sin(Tau * freq * t) * env * amplitude * Mathf.Clamp01(t * 2000f);
            }
        }

        static void Zap(float[] d, System.Random r, int start, int length, float amplitude)
        {
            float low = 0f;
            for (int k = 0; k < length && start + k < d.Length; k++)
            {
                float n = Noise(r);
                low += Coefficient(2500f) * (n - low);
                d[start + k] += (n - low) * amplitude * Mathf.Exp(-k / (length * 0.35f));
            }
        }

        // Lightning charge: sharp crack, electric buzz, scattered snaps.
        static void LightningCharge(float[] d, System.Random r)
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float freq = 98f * (1f + 0.15f * Mathf.Exp(-t * 10f));
                float buzz = 0f;
                for (int k = 1; k <= 9; k++) buzz += Mathf.Sin(Tau * freq * k * t) / k;
                d[i] = buzz * Mathf.Exp(-t * 3.5f) * 0.28f * (0.7f + 0.3f * Mathf.Sin(Tau * 23f * t));
            }
            Zap(d, r, 0, Rate / 25, 1.1f);
            for (int p = 0; p < 26; p++)
            {
                int start = r.Next(Rate / 30, d.Length * 3 / 4);
                Zap(d, r, start, r.Next(Rate / 600, Rate / 200), (0.3f + (float)r.NextDouble() * 0.5f) * (1f - start / (float)d.Length));
            }
        }

        // Lightning moving loop: humming buzz with random snaps.
        static void ElectricBuzz(float[] d, System.Random r)
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float buzz = 0f;
                for (int k = 1; k <= 10; k++) buzz += Mathf.Sin(Tau * 110f * k * t) / k;
                d[i] = buzz * 0.22f * (0.65f + 0.35f * Mathf.Sin(Tau * 7f * t) * Mathf.Sin(Tau * 2.5f * t));
            }
            int zaps = Mathf.RoundToInt(d.Length / (float)Rate * 38f);
            for (int p = 0; p < zaps; p++)
                Zap(d, r, r.Next(d.Length), r.Next(Rate / 500, Rate / 160), 0.3f + (float)r.NextDouble() * 0.6f);
        }

        // Ice charge: frosty hiss and a cascade of crystal chimes.
        static void IceCharge(float[] d, System.Random r)
        {
            float low = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float n = Noise(r);
                low += Coefficient(4500f) * (n - low);
                float hiss = (n - low) * Mathf.Exp(-t * 6f) * Mathf.Clamp01(t * 100f) * 0.35f;
                float whump = Mathf.Sin(Tau * 180f * t) * Mathf.Exp(-t * 12f) * 0.3f;
                d[i] = hiss + whump;
            }
            float[] notes = { 2093f, 2637f, 3136f, 4186f, 3520f };
            for (int k = 0; k < notes.Length; k++)
                Ping(d, Mathf.RoundToInt((0.01f + k * 0.045f) * Rate), notes[k], 4.5f, 0.24f);
            for (int p = 0; p < 10; p++)
                Ping(d, r.Next(Rate / 10, d.Length / 2), 3000f + (float)r.NextDouble() * 2500f, 30f, 0.08f);
        }

        // Ice moving loop: airy high shimmer with random tinkles.
        static void FrostShimmer(float[] d, System.Random r)
        {
            float low = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float n = Noise(r);
                low += Coefficient(5000f) * (n - low);
                d[i] = (n - low) * 0.14f * (0.75f + 0.25f * Mathf.Sin(Tau * 0.8f * t));
            }
            int tinkles = Mathf.RoundToInt(d.Length / (float)Rate * 14f);
            for (int p = 0; p < tinkles; p++)
                Ping(d, r.Next(d.Length), 2500f + (float)r.NextDouble() * 2800f, 35f, 0.12f + (float)r.NextDouble() * 0.2f);
        }

        static float Bandpass(ref float a, ref float b, float n, float center)
        {
            a += Coefficient(center * 1.6f) * (n - a);
            b += Coefficient(center * 0.6f) * (a - b);
            return a - b;
        }

        // Wind charge: swelling whoosh with a rising whistle.
        static void WindCharge(float[] d, System.Random r)
        {
            float a = 0f, b = 0f, phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float center = Mathf.Lerp(400f, 2400f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.9f)));
                float swell = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 1.05f));
                float whoosh = Bandpass(ref a, ref b, Noise(r), center) * swell * 1.2f;
                phase += Tau * Mathf.Lerp(700f, 1400f, Mathf.Clamp01(t / 0.5f)) / Rate;
                float whistle = Mathf.Sin(phase) * Mathf.Exp(-t * 3f) * Mathf.Clamp01(t * 20f) * 0.12f;
                d[i] = whoosh + whistle;
            }
        }

        // Wind moving loop: gusting band-passed air.
        static void Gust(float[] d, System.Random r)
        {
            float a = 0f, b = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float center = 650f + 380f * Mathf.Sin(Tau * 0.7f * t) + 200f * Mathf.Sin(Tau * 1.9f * t + 1f);
                float gust = 0.6f + 0.4f * Mathf.Sin(Tau * 0.55f * t + 0.4f);
                d[i] = Bandpass(ref a, ref b, Noise(r), center) * gust;
            }
        }

        // Ward charge: warm bell chord with a soft shimmer.
        static void WardCharge(float[] d, System.Random r)
        {
            float low = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                low += Coefficient(3000f) * (Noise(r) - low);
                float shimmer = low * Mathf.Exp(-t * 5f) * 0.25f;
                float hum = Mathf.Sin(Tau * 130.8f * t) * Mathf.Exp(-t * 3f) * 0.2f;
                d[i] = shimmer + hum;
            }
            float[] chord = { 523.25f, 659.25f, 783.99f, 1046.5f };
            for (int k = 0; k < chord.Length; k++)
                Ping(d, Mathf.RoundToInt(k * 0.03f * Rate), chord[k], 2.4f, 0.2f);
        }

        // Ward moving loop: steady, gently pulsing hum.
        static void WardHum(float[] d, System.Random r)
        {
            float low = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                low += Coefficient(2000f) * (Noise(r) - low);
                float tone = Mathf.Sin(Tau * 220f * t) + 0.6f * Mathf.Sin(Tau * 330f * t) + 0.3f * Mathf.Sin(Tau * 440f * t);
                d[i] = tone * 0.25f * (0.7f + 0.3f * Mathf.Sin(Tau * 3f * t)) + low * 0.15f;
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
