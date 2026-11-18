# Save Muse! — Competition Submission Draft

**Status:** Local draft only; not submitted. Track and division remain subject to the event rules and the team's dated project provenance. No build invite or public demo URL is available yet.

## Entry copy

**Title:** Save Muse!

**Tagline (87/140 characters):** Trace a spell with a pinch, find Muse's weak point with your gaze, and break the curse.

**Track:** Gaming

**Division:** New — provisional. Confirm the event's New Experience rule against the project's dated provenance and Meta-template exception before choosing it. If any prior experience/code is reused, choose Adapted only if the change meets the event's significant-update requirement and document the change.

**Description (draft):**

Save Muse! is a short boss duel about rescuing Muse from a dark curse. Pinch your thumb and index finger to trace an elemental spell, look at her glowing weak point, and release to attack. Hold your gaze on a target through Muse's raised-fist wind-up to expose it. Time a horizontal barrier to block a punch, or use lightning to interrupt it while a weak point is glowing. Five weak points, spell cooldowns, and a faster enraged phase test attention and timing. Defeat Muse to restore her original form.

Built in Unity 6000.7.0b9 with Meta XR input, the glasses-first experience combines hand-tracked pinch drawing with gaze-based targeting and wind-up timing. It includes a practice phase, a boss duel, and a victory state. The project targets Meta VR Glasses; Quest compatibility is not claimed. The next step is physical-device playtesting to tune comfort and confirm tracking.

**Hand interaction response (optional):** Pinch thumb and index finger to draw a spell from the on-screen guide; release while looking at the intended target to cast. Short pinches activate the title and rematch controls.

**Target launch date if unpublished:** For the representative to confirm; not set in this draft.

**Team:** Four-person team. Representative: `my-name-is-yu` (confirm the public display name). Other contributors and roles: [member 2 name — role], [member 3 name — role], [member 4 name — role]. Enter participant emails only in the official submission form, not in this repository.

## Player instructions for judges

1. On Meta VR Glasses, allow hand and eye tracking. Keep your head comfortably forward and use your eyes to look at the glowing target.
2. At the title screen, follow the visible `Start duel` prompt and short-pinch interaction to begin. The final menu path needs confirmation against the release build.
3. Pinch and trace a spell gesture, then release while looking at Muse's active weak point. During her raised-fist wind-up, hold your gaze on the intended part until the target cue closes. Cast lightning at a glowing weak point to interrupt an attack; draw the horizontal barrier shortly before the punch lands.
4. Follow the cooldown display and target cues. After victory, follow the visible rematch prompt if you want to play again.

## Representative-only submission checklist

- Publish the competition build to the event's required release channel and provide its Invite URL. Do not publish or substitute an Editor-only preview.
- Confirm the official team roster, roles, and public display names; supply emails only in the official form.
- Confirm New vs Adapted from dated source history and the live event rules, including the Meta-template exception.
- Confirm the target launch date if the experience is unpublished.
- Upload the final demo to an authorized user-controlled YouTube/Vimeo channel after its identity is established; then add the public URL. No channel identity or upload authorization has been established here.
- Confirm judge access works through the winner-announcement period.

## Demo video plan (target 2:35, English captions)

Capture the live app view from Meta XR Simulator configured for Meta VR Glasses, or from a physical device. Use one continuous gameplay take where possible; if edits are needed, cut only between genuine recordings of the same build. Burn in short English captions so the demo is understandable without narration. Use only gameplay features that are visibly demonstrated; remove any beat that cannot be captured honestly.

| Time | Genuine gameplay beat | Caption |
|---|---|---|
| 0:00–0:10 | Open on the live scene, then show the title. | `Save Muse! — a duel controlled by pinch and gaze` |
| 0:10–0:35 | Show a pinch-drawn spell, gaze on a glowing weak point, release, and impact. | `Trace. Look. Release to cast.` |
| 0:35–1:00 | Hit a glowing weak point and show the next target cue. | `Read the target. Time each cast.` |
| 1:00–1:25 | During the raised-fist tell, hold gaze until the target is exposed. | `Hold your gaze through the wind-up.` |
| 1:25–1:45 | Interrupt the raised attack with lightning. | `Strike before the slam lands.` |
| 1:45–2:05 | Draw and time the barrier against a punch. | `A brief barrier rewards good timing.` |
| 2:05–2:25 | Show the enraged phase and faster attack tempo. | `Stay focused as Muse enrages.` |
| 2:25–2:35 | Show the real victory state and restored Muse. | `Break the curse. Save Muse.` |

**Capture / edit record:** Pending the reserved Unity Editor/Simulator slot. Keep large raw takes under ignored `Temp/competition-capture/`; deliver only the final MP4 at `Docs/media/SaveMuse_Competition_Demo.mp4`. The available host tooling exposes `metavr xrsim record start/stop/status` for compositor MP4 capture. Capture provenance, device profile, simulator/build version, take ranges, edits, caption file, export duration, and final file hash here after the recording is made. Do not label simulator input as physical-device footage. Make a thumbnail only from a frame in the final verified gameplay footage.

## Audio rights checked

The project already credits three OpenTracks tracks in [`Assets/Runeveil/BGM_CREDITS.md`](../Assets/Runeveil/BGM_CREDITS.md): “ancient ruins” by Maniira, “wait in the deep” by alaki paca, and “栄光のファンファーレ” by t.tam. The official [OpenTracks Audio Source Usage License](https://opentracks.com/en/help/articles/license/) lists video-contest submissions and online video distribution among background-music uses, allows ordinary editing, and says credits are not required. It prohibits standalone audio redistribution, making the audio the primary content, and registering the audio in Content ID/fingerprinting systems. The alaki paca profile adds that adult-content use is allowed; the other two composer profiles defer to the site license. Keep the game's audio subordinate to gameplay and captions, do not publish extracted tracks, and do not register them in Content ID.

Official track pages: [“ancient ruins” — Maniira](https://opentracks.com/en/bgm/detail/19646), [“wait in the deep” — alaki paca](https://opentracks.com/en/bgm/detail/13633), [“栄光のファンファーレ” — t.tam](https://opentracks.com/en/bgm/detail/4668). Checked 2026-11-18.

## Completion gates

- [ ] Verify the Unity menu path and representative gameplay with Sol against the final build.
- [ ] Build and validate the competition release on the intended device; provide its invite URL.
- [ ] Capture and review actual gameplay, produce the captioned export under three minutes, and verify audio and playback.

## References

- [Meta VR Start Developer Competition 2026](https://start-developer-competition-26.devpost.com/)
- [Competition rules](https://start-developer-competition-26.devpost.com/rules)
- [Meta XR Simulator for Unity](https://developers.meta.com/vr/documentation/unity/unity-simulate-xrsim/)
- [Capture a Meta XR Simulator Session](https://developers.meta.com/vr/documentation/unity/xrsim-session-capture/)
