# Save Muse! — Competition Submission Packet

**Private working packet.** Representative `my-name-is-yu` owns video publication and Devpost submission. The authenticated submission status and timestamp are recorded below; the judging video URL is present in the submitted entry and is Unlisted. Meta Competition channel/build verification remains pending. Google Drive is the representative's video delivery destination.

## Public Devpost copy

### Title

Save Muse!

### Tagline

Trace a spell with a pinch, find Muse's weak point with your gaze, and break the curse.

### Description

Save Muse! is a short boss duel about rescuing Muse from a dark curse. Pinch your thumb and index finger to trace an elemental spell, look at her glowing weak point, and release to attack. Hold your gaze through Muse's raised-fist wind-up to expose a target. Time a horizontal barrier to block a punch, or cast lightning to interrupt it while a weak point is glowing. Five weak points, spell cooldowns, and a faster enraged phase test attention and timing. The idea was to make drawing create the spell and looking choose its target. Built in Unity, the game compares each pinch trace with rune shapes and locks the target when the spell releases. Next, we will test whether new players learn their first cast quickly and can read Muse's attack cues under pressure.

### Optional hand interaction response

Pinch thumb and index finger to trace a spell from the on-screen guide; release while looking at the intended target to cast. Short pinches activate the title and rematch controls.

### Team

`my-name-is-yu` (representative; input implementation), `Yasei-no-otoko` (eye gaze and boss combat), `hattori8` (spell effects and music implementation), and `lockdef` (combat tuning). Enter participant emails only in the official form.

## Public entry fields for the representative to confirm

- **Track:** Gaming.
- **Division (provisional): New Experience.** Repository history places the Unity starter and its template notes on November 2, 2026 ([`cbf0175`](https://github.com/my-name-is-yu/MetaVRHackathon/commit/cbf0175ee9d018facced589022a2e7a25285d9e2), [`60cba74`](https://github.com/my-name-is-yu/MetaVRHackathon/commit/60cba740959a13684ec3053bf50ac036fc0ef244)). The first game-specific Runeveil WebXR prototype is dated November 18 at 13:00 JST ([`d48d15d`](https://github.com/my-name-is-yu/MetaVRHackathon/commit/d48d15da79ccf95256ef834c3b6f2e0dfe70d15d)); the Unity port followed at 13:52 ([`7ddf5aa`](https://github.com/my-name-is-yu/MetaVRHackathon/commit/7ddf5aa6f9c17b91756ae8f40172aebfc2a5cfdc)) and the Muse boss at 14:52 ([`db59d9c`](https://github.com/my-name-is-yu/MetaVRHackathon/commit/db59d9c900eb2a645c74135aa0175e8ae7842bb3)). These commits support New as a candidate but cannot rule out off-repository work before September 24; the representative should confirm that no earlier project design, code, build, or prototype existed. Official [§4 of the rules](https://start-developer-competition-26.devpost.com/rules) says New must be created during the competition window and excludes prior codebases, shipped titles, and repurposed early-access builds. Adapted applies when a build, prototype, or published app existed before September 24 and a meaningful feature, mode, or platform integration shipped during the window; the sponsor decides significance and may reassign entries. The published rules state no blanket template exception. If the starter's status affects this classification, obtain the organizer's decision. The November 18 WebXR-to-Unity port alone does not make the project Adapted because both versions are within the competition window.
- **Target launch date, if unpublished:** Representative to confirm.
- **Demo video URL:** Present in the submitted entry's description and embed; see the representative-only handoff below for the link and playback status.

## Judge instructions

1. At the title screen, follow the visible `Start duel` prompt and use the indicated short-pinch interaction.
2. Pinch and trace a spell from the on-screen guide, then release while looking at Muse's active weak point.
3. During her raised-fist wind-up, hold your gaze on the intended target until its cue closes. Cast lightning at a glowing weak point to interrupt an attack, or draw a horizontal barrier shortly before the punch lands.
4. Follow the cooldown display and target cues. After victory, follow the visible rematch prompt to play again.

## Representative-only handoff checklist

**Do not paste this section into public Devpost fields.**

- **Owner/status:** `my-name-is-yu` is the designated representative for the Competition channel/invite, public video publication, and Devpost submission. Around 2026-11-19 14:49 JST, a team member directly observed in an authenticated `My projects` view that the four-person Save Muse! card showed **SUBMITTED**. An authenticated team-member project response reported `state=published`, project ID `1555851`, and `submitted_at=2026-11-19 02:06:19.115 JST` (2026-11-18 17:06:19.115 UTC), 2 h 53 min 40.885 s before the 05:00 JST deadline. The earlier ~00:30 JST **Draft** report is retained as pre-submission history; Devpost Help defines Draft as not submitted.
- **Devpost URL observation (2026-11-19):** The representative shared the [Save Muse! project page](https://devpost.com/software/save-muse-unct9z). An unauthenticated GET was observed to return HTTP 302 to Devpost sign-in at 00:20:28 JST (2026-11-18 15:20:28 UTC). Independent requests returned HTTP 403 Forbidden at 00:20:42.874 JST (15:20:42.874 UTC) and 00:22:32.963 JST (15:22:32.963 UTC), with no readable page body. These anonymous requests did not expose submission time, event association, video URL, or judging build; later evidence is recorded below.
- **Competition-entry linkage (2026-11-19):** At 00:27:52 JST (2026-11-18 15:27:52 UTC), an unauthenticated request to the entry management page returned HTTP 302 to Devpost sign-in. The [official competition home](https://start-developer-competition-26.devpost.com/) showed a `My projects` link with event ID `31024` and slug `meta-vr-start-developer-competition-2026`, matching management-entry ID `1311003`. This management-entry ID is distinct from project ID `1555851`. The authenticated project response identifies the event as Meta VR Start Developer Competition 2026; the earlier anonymous request did not expose team or submission details.
- **Verified candidate APK:** [Private prerelease page](https://github.com/my-name-is-yu/MetaVRHackathon/releases/tag/save-muse-competition-candidate-0.2.3-2026-11-18) · [APK asset](https://github.com/my-name-is-yu/MetaVRHackathon/releases/download/save-muse-competition-candidate-0.2.3-2026-11-18/Save-Muse-0.2.3-release.apk). Asset: `Save-Muse-0.2.3-release.apk`; verified SHA-256: `397b1244bcde2ea4b5741b406c9d1fde0c46beba30722186f62cb4dd47da9073`. This GitHub prerelease is the APK handoff, not the competition judge invite.
- **Competition judge access and Meta build:** The required Competition release-channel URL answer is not exposed by the project response, and its `website_url` points to the original GitHub repository rather than a Meta build. Save Muse! was absent from the available Meta organization's `My apps`; no Save Muse! Meta app, channel, build, or code was verified. Do not use another app as evidence; judge access remains unverified.
- **Video delivery:** `Save Muse! - Competition Demo.mp4` is delivered to Google Drive for the representative. The local final file receipt records 32.000 s, 36,479,770 bytes, SHA-256 `b0378f14364e6d3fd597017851a2175c80f11091c54ac85249db6e8e7303f95d` (H.264, 1920×1080, 30 fps; AAC stereo, 48 kHz); the file's current bytes and hash match that receipt. The submitted entry's body and embed use the [same YouTube video](https://www.youtube.com/watch?v=OHTZYbgOI3o). Anonymous normal-player checks at 14:46–14:48 JST returned HTTP 200 / `playabilityStatus=OK`; oEmbed and player metadata identify `Save Muse!   Competition Demo`, 32 s, non-private and **Unlisted** (playable by link without sign-in, not searchable as Public). The duration matches the local final; no frame-by-frame content comparison was performed.
- **Published-copy difference:** The published Devpost tagline, Inspiration, and How we built it fields contain hardware model names, unlike the device-neutral prepared copy in this packet. The model names are not repeated here; this is not evidence of a Meta app build or hardware validation.
- **Submission art:** [Devpost cover](SubmissionArt/Save-Muse-Devpost-Cover.jpg) and [video thumbnail](SubmissionArt/Save-Muse-Video-Thumbnail.jpg). Use each image in its corresponding representative-controlled entry field.
- Confirm the roster and public display names. Supply emails only in the official form.
- Confirm the New vs Adapted division and target launch date if the experience is unpublished.
- Retain the Devpost submission receipt and verify judge access through the winner-announcement period.

## Private video and music record

The final cut uses both user-supplied gameplay recordings, ends on the actual Victory result, and carries English on-screen captions. Source provenance and exact edit ranges are in the ignored local receipt `Recordings/Verification/FinalVideoReceipt.md`; keep that provenance record out of public Devpost fields.

The final soundtrack is integrated into the gameplay video: `wait in the deep` by alaki paca and `栄光のファンファーレ` by t.tam, from OpenTracks. `hattori8` implemented music playback; composition remains credited to the composers. The official [OpenTracks Audio License](https://opentracks.com/help/articles/license/) permits background music in videos and games and ordinary editing, while prohibiting standalone audio distribution and audio fingerprint/Content ID registration. Keep the tracks embedded in the video and do not publish them separately.

## Official references

- [Meta VR Start Developer Competition 2026](https://start-developer-competition-26.devpost.com/)
- [Competition rules](https://start-developer-competition-26.devpost.com/rules)
- [Judging details](https://start-developer-competition-26.devpost.com/details/judging)
- [OpenTracks track: `wait in the deep`](https://opentracks.com/en/bgm/detail/13633)
- [OpenTracks track: `栄光のファンファーレ`](https://opentracks.com/en/bgm/detail/4668)
