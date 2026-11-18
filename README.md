# Save Muse!

This Unity game is based on Meta's VR Glasses template and uses Meta XR SDKs for **Meta VR Glasses**.

## Unity instructions

### Play the duel

Open [`Assets/Scenes/Runeveil.unity`](Assets/Scenes/Runeveil.unity) in Unity and press Play. This is the first scene that loads in a standard build.

- **On Meta VR Glasses**, pinch your thumb and index finger together, trace a shape from the spell guide, and release to cast. Look at a weak point to aim. If the glasses can't track your gaze, your spell hits Muse's torso. To select `Start duel` or `Fight again` in a menu, face the button and pinch briefly.
- **To play with a mouse in the Unity Editor**, enable `Desktop Preview In Editor` in the `Runeveil Duel` Inspector. Hold the left mouse button and drag to draw. Release to fire at the cursor. Right-click to lock or unlock your aim. Click `Start duel` or press Enter to begin.
- **To test without eye tracking in the Unity Editor**, enable `Assume Gaze On Target` in the `Runeveil Duel` Inspector. When playing in XR with Meta XR Simulator or Link, the game treats your gaze as fixed on the glowing weak point. You can select `Start duel` or `Fight again` with a brief pinch wherever you're looking. This setting only applies in the Editor.

You start the duel with five hearts. Muse starts with 120 health.

- **Weak points.** Muse's five weak points are the chest gem, the right hand, the left and right horns, and the raised fist. Normally, you can target whichever one is glowing. Your aim locks in as soon as you start releasing the pinch. The spell deals damage on impact. When a hit shatters a weak point, a different one lights up 0.8 seconds later.
- **Body hits.** If you aren't aiming at a weak point, or if eye tracking is unavailable, the spell hits the torso for 55% of the sum of its base damage and charge bonus.
- **Charging spells.** Keep the pinch held and trace the same shape again to charge Fireball, Lightning, Ice spear, or Wind blade up to level 3. The charge bonus is 0 damage at level 1, 6 at level 2, and 12 at level 3. On a weak-point hit, the game adds this bonus after applying any gaze or knockdown modifiers.
- **Exposing a weak point.** When corrupted Muse raises a fist, hold your gaze on any of the five weak points for 0.35 seconds. When the targeting ring closes and four diamonds appear, that spot is exposed. A hit there deals 8 extra damage. Looking at another spot restarts the gaze timer there.
- **Interrupting with Lightning.** Trace a Z, aim at a weak point, and start releasing your pinch during the windup. Lightning interrupts the punch when it hits. Even if you start releasing just before the windup ends, the game allows time to confirm the release (0.12 seconds in XR) and for the bolt to land (about 0.10 seconds). Muse drops to one knee for 3 seconds. During that time, weak-point hits deal double damage before the charge bonus is added.
- **Blocking with Ward.** Draw a horizontal line to cast Ward, a barrier that lasts 1.5 seconds and blocks one attack. Start drawing when Muse raises a fist. Release when the arc glows brightly, about 1.3 seconds before the punch lands. A successful block stops the fist for 2.2 seconds and makes the crystal glow. The first spell to land during that opening deals weak-point damage.
- **Cooldowns.** Fireball, Ice spear, and Wind blade each take 1.5 seconds to recharge. Lightning takes 3 seconds, Stone shot takes 4 seconds, and Ward takes 3.5 seconds. The remaining time appears at the bottom center of the screen.
- **Heavy attacks.** Once Muse reaches half health or less, Muse switches to red heavy attacks starting with the next windup. Normal hits cost you one heart and heavy hits cost you two. Both attacks have a 2.4-second windup. The delay before the next windup is 2 seconds normally and 1.5 seconds when Muse is enraged.
- **Victory.** Defeat Muse to hear a fanfare and see an illustration of Muse restored to the original white form. You earn an S rank with 5 hearts left, an A with 3–4, or a B with 1–2.

### Build the APK for Meta VR Glasses

In Unity Hub, install **Unity 6000.7.0b9** with **Android Build Support**, including the SDK, NDK, and OpenJDK. Close the Editor, then run the following commands to fetch the assets with Git LFS, build the APK, install it, and launch the game.

```sh
git lfs pull
unity build . --target Android --execute-method Runeveil.Editor.RuneveilBuild.BuildApk
metavr app install Builds/Runeveil/Runeveil.apk
metavr app launch com.runeveil.golemduel
```

You can also build the same development APK from the Editor. Switch the build target to Android, then choose **Runeveil → Build Glasses APK**.

### Check eye tracking

Allow eye tracking when prompted on the glasses. Put them on, keep your head facing forward, and move just your eyes from side to side. Pinch and trace a Z, then look at the chest crystal just before releasing. If the bolt hits while Muse is winding up a punch, it interrupts the attack. In the simulator, select **Meta VR Glasses** as the device and **Look and Pinch** as the input.

Every 5 seconds, the development APK summarizes gaze sample validity and counts the frames in which it detected eye movement relative to the head. For each cast, it logs the spell, the hit location, and the outcome. The logs omit eye and finger coordinates. See [Meta's guide to eye tracking in Unity](https://developers.meta.com/vr/documentation/unity/move-eye-tracking-interactions/).
