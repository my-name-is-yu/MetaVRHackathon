using UnityEngine;

namespace Runeveil
{
    /// <summary>
    /// Scene music: ruins theme before the duel, boss theme during it, fanfare on victory.
    /// Tracks that do not loop on their own are looped by crossfading the end into the start.
    /// Music is 2D so spatial SFX at the fingertip stay easy to tell apart.
    ///
    /// Tracks (OpenTracks, formerly DOVA-SYNDROME; see BGM_CREDITS.md):
    ///   Resources/Runeveil/BGM/bgm_practice_ancient_ruins  "ancient ruins" by Maniira
    ///   Resources/Runeveil/BGM/bgm_battle_wait_in_the_deep  "wait in the deep" by alaki paca
    ///   Resources/Runeveil/BGM/jingle_victory_fanfare       "栄光のファンファーレ" by t.tam
    /// </summary>
    public sealed class RuneveilMusic : MonoBehaviour
    {
        enum Scene { None, Practice, Battle, Victory, Defeat }

        const string Folder = "Runeveil/BGM/";
        const float LoopCrossfade = 3f;

        AudioClip practiceClip;
        AudioClip battleClip;
        AudioClip victoryClip;
        readonly AudioSource[] decks = new AudioSource[2];
        readonly float[] deckLevel = new float[2];
        readonly float[] deckTarget = new float[2];
        readonly float[] deckFadeRate = new float[2];
        AudioSource jingle;
        int active;
        AudioClip currentClip;
        float sceneLevel = 1f;
        float intensity;
        Scene scene;
        float victoryResumeAt = -1f;

        public void Build()
        {
            if (jingle != null) return;
            practiceClip = Resources.Load<AudioClip>(Folder + "bgm_practice_ancient_ruins");
            battleClip = Resources.Load<AudioClip>(Folder + "bgm_battle_wait_in_the_deep");
            victoryClip = Resources.Load<AudioClip>(Folder + "jingle_victory_fanfare");
            for (int i = 0; i < decks.Length; i++) decks[i] = CreateSource("Music deck " + (i + 1));
            jingle = CreateSource("Music jingle");
        }

        AudioSource CreateSource(string label)
        {
            var child = new GameObject(label);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.priority = 0;
            source.volume = 0f;
            return source;
        }

        /// <summary>Call every frame with the duel state.</summary>
        public void Tick(bool battle, bool won, bool lost, bool fistRaised, RuneveilPinchSettings s, float dt)
        {
            if (jingle == null) return;
            Scene wanted = won ? Scene.Victory : lost ? Scene.Defeat : battle ? Scene.Battle : Scene.Practice;
            if (wanted != scene) Enter(wanted);

            // Victory: the fanfare plays first, then the ruins theme returns quietly.
            if (scene == Scene.Victory && victoryResumeAt >= 0f && Time.unscaledTime >= victoryResumeAt)
            {
                victoryResumeAt = -1f;
                PlayTrack(practiceClip, 2.5f);
            }

            // The boss theme swells while the fist is raised.
            intensity = Mathf.MoveTowards(intensity, scene == Scene.Battle && fistRaised ? 1f : 0f, dt * 2.5f);
            float master = s.musicVolume * sceneLevel * (1f + 0.25f * intensity);

            KeepLooping();
            for (int i = 0; i < decks.Length; i++)
            {
                deckLevel[i] = Mathf.MoveTowards(deckLevel[i], deckTarget[i], deckFadeRate[i] * dt);
                decks[i].volume = deckLevel[i] * master;
                if (deckLevel[i] <= 0f && deckTarget[i] <= 0f && decks[i].isPlaying) decks[i].Stop();
            }
            jingle.volume = s.musicVolume * s.jingleVolume;
        }

        void Enter(Scene next)
        {
            scene = next;
            victoryResumeAt = -1f;
            switch (next)
            {
                case Scene.Practice:
                    sceneLevel = 1f;
                    PlayTrack(practiceClip, 1.5f);
                    break;
                case Scene.Battle:
                    sceneLevel = 1f;
                    if (jingle.isPlaying) jingle.Stop();
                    PlayTrack(battleClip, 1.2f);
                    break;
                case Scene.Victory:
                    sceneLevel = 0.7f;
                    FadeOutAll(0.35f);
                    if (victoryClip != null)
                    {
                        jingle.clip = victoryClip;
                        jingle.Play();
                        victoryResumeAt = Time.unscaledTime + victoryClip.length + 0.6f;
                    }
                    else victoryResumeAt = Time.unscaledTime + 0.5f;
                    break;
                case Scene.Defeat:
                    // Let the boss theme die away, then the ruins theme returns softly.
                    sceneLevel = 0.6f;
                    PlayTrack(practiceClip, 3.5f);
                    break;
            }
        }

        /// <summary>Crossfades from whatever is playing into the given clip.</summary>
        void PlayTrack(AudioClip clip, float fade)
        {
            currentClip = clip;
            if (clip == null)
            {
                FadeOutAll(fade);
                return;
            }
            int next = 1 - active;
            StartDeck(next, clip, 0f, fade);
            Fade(active, 0f, fade);
            active = next;
        }

        void StartDeck(int deck, AudioClip clip, float time, float fade)
        {
            var source = decks[deck];
            source.clip = clip;
            source.time = Mathf.Clamp(time, 0f, Mathf.Max(0f, clip.length - 0.05f));
            source.Play();
            deckLevel[deck] = 0f;
            Fade(deck, 1f, fade);
        }

        void Fade(int deck, float target, float seconds)
        {
            deckTarget[deck] = target;
            deckFadeRate[deck] = seconds <= 0f ? 1000f : 1f / seconds;
        }

        void FadeOutAll(float seconds)
        {
            currentClip = null;
            Fade(0, 0f, seconds);
            Fade(1, 0f, seconds);
        }

        // Tracks are not authored as seamless loops, so crossfade the tail into a fresh start.
        void KeepLooping()
        {
            var source = decks[active];
            if (currentClip == null || source.clip != currentClip || !source.isPlaying) return;
            float fade = Mathf.Min(LoopCrossfade, currentClip.length * 0.25f);
            if (source.time < currentClip.length - fade) return;
            int next = 1 - active;
            StartDeck(next, currentClip, 0f, fade);
            Fade(active, 0f, fade);
            active = next;
        }
    }
}
