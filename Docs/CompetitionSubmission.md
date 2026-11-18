# Save Muse! — Competition Submission Draft

**Status:** Local draft only; not submitted. Track and division remain subject to the event rules and the team's dated project provenance. The build invite and public demo URL are still to be added.

## Entry copy

**Title:** Save Muse!

**Tagline (87/140 characters):** Trace a spell with a pinch, find Muse's weak point with your gaze, and break the curse.

**Track:** Gaming

**Division:** New — provisional. Confirm the event's New Experience rule against the project's dated provenance and Meta-template exception before choosing it. If any prior experience or code is reused, choose Adapted only if the change meets the event's significant-update requirement and document the change.

**Description (draft):**

Save Muse! is a short boss duel about rescuing Muse from a dark curse. Pinch your thumb and index finger to trace an elemental spell, look at her glowing weak point, and release to attack. Hold your gaze on a target through Muse's raised-fist wind-up to expose it. Time a horizontal barrier to block a punch, or use lightning to interrupt it while a weak point is glowing. Five weak points, spell cooldowns, and a faster enraged phase test attention and timing. Win the duel to break the curse and save Muse. The experience includes a practice phase, a boss duel, and a victory state.

**Hand interaction response (optional):** Pinch thumb and index finger to draw a spell from the on-screen guide; release while looking at the intended target to cast. Short pinches activate the title and rematch controls.

**Target launch date if unpublished:** For the representative to confirm; not set in this draft.

**Team:** Four-person team: `my-name-is-yu` (representative; input implementation), `Yasei-no-otoko` (eye gaze and boss combat), `hattori8` (spell effects and music implementation), and `lockdef` (combat tuning). Enter participant emails only in the official submission form, not in this repository. Music composition remains credited to the composers listed below.

## Player instructions for judges

1. At the title screen, follow the visible `Start duel` prompt and use the indicated short-pinch interaction to begin.
2. Pinch and trace a spell gesture from the on-screen guide, then release while looking at Muse's active weak point.
3. During her raised-fist wind-up, hold your gaze on the intended part until the target cue closes. Cast lightning at a glowing weak point to interrupt an attack, or draw a horizontal barrier shortly before the punch lands.
4. Follow the cooldown display and target cues. After victory, follow the visible rematch prompt if you want to play again.

## Representative-only submission checklist

- Use the exact signed competition build from the private prerelease. Create or select Save Muse!'s dedicated Competition channel, enable its invite, and verify judge access with the invite URL.
- Confirm the official team roster, roles, and public display names; supply emails only in the official form.
- Confirm New vs Adapted from dated source history and the live event rules, including the Meta-template exception.
- Confirm the target launch date if the experience is unpublished.
- Add the final demo URL from an authorized user-controlled YouTube or Vimeo channel. Check that it plays publicly without sign-in.
- Submit the exact entry using the representative's authenticated Devpost account and save the submission receipt. Confirm judge access through the winner-announcement period.

## Demo video plan (English captions; keep under three minutes)

Build a concise cut from actual gameplay, with gameplay filling the frame throughout. Use the short gameplay span that opens on Muse's attack cue and ends on the recorded Victory state; omit the old in-game title/menu sequence. Burn in short English captions in a clear safe area so the demo works without narration. Keep each caption tied to the visible action, avoid claims about a restored-form shot, and stop at the real victory screen. Do not pad the video to a target duration.

| Order | Gameplay beat | Caption |
|---|---|---|
| 1 | Open on the actual incoming attack cue. | `Save Muse!` / `Read the cue. Find the weak point.` |
| 2 | Show the traced fire spell and its release. | `Trace a spell, then release.` |
| 3 | Show the weak-point hit and the on-screen shatter message. | `Strike the exposed weak point.` |
| 4 | End on the real Victory result. | `Victory. Muse is saved.` |

Keep source provenance, exact take ranges, and technical QA notes in the ignored private verification receipt under `Recordings/Verification/`, outside this submission draft. Attach only the reviewed final MP4 to the private release or hand-off. Make a thumbnail only from a frame in the final reviewed gameplay footage.

## Audio rights checked

The project credits three OpenTracks works in [`Assets/Runeveil/BGM_CREDITS.md`](../Assets/Runeveil/BGM_CREDITS.md): “ancient ruins” by Maniira, “wait in the deep” by alaki paca, and “栄光のファンファーレ” by t.tam. The official [OpenTracks Audio Source Usage License](https://opentracks.com/en/help/articles/license/) lists video-contest submissions and online video distribution among background-music uses, allows ordinary editing, and says credits are not required. It prohibits standalone audio redistribution, making the audio the primary content, and registering the audio in Content ID or fingerprinting systems. The alaki paca profile adds that adult-content use is allowed; the other two composer profiles defer to the site license. Both provided source recordings have AAC tracks that decode as digital silence, so use English captions without adding a soundtrack. The in-game music remains credited in the project; do not publish extracted tracks or register them in Content ID.

Official track pages: [“ancient ruins” — Maniira](https://opentracks.com/en/bgm/detail/19646), [“wait in the deep” — alaki paca](https://opentracks.com/en/bgm/detail/13633), [“栄光のファンファーレ” — t.tam](https://opentracks.com/en/bgm/detail/4668). License checked 2026-11-18.

## Completion gates

- [ ] Verify the title and rematch interaction against the final build.
- [ ] Confirm the competition release, judge invite, and public demo URL.
- [ ] Review the actual gameplay edit, keep it under three minutes, and verify captions, audio rights, and playback.

## References

- [Meta VR Start Developer Competition 2026](https://start-developer-competition-26.devpost.com/)
- [Competition rules](https://start-developer-competition-26.devpost.com/rules)
- [OpenTracks Audio Source Usage License](https://opentracks.com/en/help/articles/license/)
