using UnityEngine;

namespace Runeveil
{
    public sealed partial class RuneveilArena
    {
        private const float MuseCollapseSeconds = 0.27f;
        private const float MuseStandSeconds = 0.43f;
        private const float MuseRecoilSeconds = 0.27f;

        private float museStaggerProgress;
        private float museChestRecoil;
        private float museHornRecoil;
        private float museAttackingPawRecoil;
        private float museRestingPawRecoil;

        private void TickMuseReactions(float deltaTime, bool staggered, bool won)
        {
            bool collapsing = staggered && !won;
            float collapseTime = collapsing ? MuseCollapseSeconds : MuseStandSeconds;
            museStaggerProgress = Mathf.MoveTowards(museStaggerProgress, collapsing ? 1f : 0f, deltaTime / collapseTime);
            down = Mathf.SmoothStep(0f, 1f, museStaggerProgress);

            float settle = deltaTime / MuseRecoilSeconds;
            museChestRecoil = Mathf.MoveTowards(museChestRecoil, 0f, settle);
            museHornRecoil = Mathf.MoveTowards(museHornRecoil, 0f, settle);
            museAttackingPawRecoil = Mathf.MoveTowards(museAttackingPawRecoil, 0f, settle);
            museRestingPawRecoil = Mathf.MoveTowards(museRestingPawRecoil, 0f, settle);
        }

        private void RecordMuseReaction(ArenaTarget target, bool strong, int level)
        {
            // Set the contact pose immediately so the existing hit-stop holds the reaction.
            float amount = strong ? 1f : Mathf.Min(1f, 0.72f + 0.09f * (level - 1));
            switch (target)
            {
                case ArenaTarget.Body:
                    museChestRecoil = Mathf.Max(museChestRecoil, amount * 0.42f);
                    break;
                case ArenaTarget.Core:
                    museChestRecoil = Mathf.Max(museChestRecoil, amount);
                    break;
                case ArenaTarget.LeftHorn:
                    museHornRecoil = Mathf.Max(museHornRecoil, amount);
                    break;
                case ArenaTarget.RightHorn:
                    museHornRecoil = Mathf.Min(museHornRecoil, -amount);
                    break;
                case ArenaTarget.RightHand:
                    museRestingPawRecoil = Mathf.Max(museRestingPawRecoil, amount);
                    break;
                case ArenaTarget.RaisedFist:
                    museAttackingPawRecoil = Mathf.Max(museAttackingPawRecoil, amount);
                    break;
            }
        }

        private void ResetMuseReactions()
        {
            museStaggerProgress = museChestRecoil = museHornRecoil = museAttackingPawRecoil = museRestingPawRecoil = 0f;
            down = 0f;
        }

        private void ApplyMuseBodyReaction(ref Vector3 bodyOffset, ref Vector3 bodyEuler, ref Vector3 bodyScale, ref Vector3 headEuler)
        {
            bodyOffset += new Vector3(-0.13f, -0.14f, -0.05f) * down;
            bodyEuler += new Vector3(-12f, 0f, 9f) * down;
            bodyScale = Vector3.Scale(bodyScale, new Vector3(1f + 0.045f * down, 1f - 0.18f * down, 1f));
            headEuler += new Vector3(-7f, 0f, -4f) * down;

            float chest = Mathf.SmoothStep(0f, 1f, museChestRecoil) * (1f - 0.72f * down);
            float chestPitch = 12f * chest;
            Vector3 waist = Vector3.up * 0.97f;
            bodyOffset += waist - Quaternion.Euler(chestPitch, 0f, 0f) * waist;
            bodyEuler.x += chestPitch;

            float horn = Mathf.SmoothStep(0f, 1f, Mathf.Abs(museHornRecoil)) * Mathf.Sign(museHornRecoil);
            headEuler += new Vector3(2f * Mathf.Abs(horn), 5f * horn, 16f * horn);
        }

        private void ApplyMuseArmReaction(bool attacking, ref Vector3 elbow, ref Vector3 wrist, ref Vector3 fistEuler)
        {
            float side = attacking ? 1f : -1f;
            float paw = Mathf.SmoothStep(0f, 1f, attacking ? museAttackingPawRecoil : museRestingPawRecoil);
            elbow += new Vector3(side * 0.06f, 0.09f, 0.12f) * paw;
            wrist += new Vector3(-side * 0.16f, 0.19f, 0.29f) * paw;
            fistEuler += new Vector3(-15f, side * 12f, side * 17f) * paw;

            if (down <= 0f) return;
            if (attacking)
            {
                // The lifted fist loses its support before the body finishes sinking.
                float slack = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, museStaggerProgress * 1.35f));
                elbow = Vector3.Lerp(elbow, new Vector3(1.23f, 1.37f, -0.11f), slack);
                wrist = Vector3.Lerp(wrist, new Vector3(1.43f, 0.95f, -0.28f), slack);
                fistEuler = Vector3.Lerp(fistEuler, new Vector3(-25f, -18f, 10f), slack);
                return;
            }

            // Anchor the resting paw to a tile in arena coordinates while the body folds over it.
            var braceEuler = new Vector3(12f, 10f, 5f);
            Vector3 pad = Quaternion.Euler(braceEuler) * (Vector3.up * 0.42f);
            float wristHeight = stage.InverseTransformVector(museBody.TransformVector(pad)).y + 0.018f;
            var braceInArena = new Vector3(-1.43f, wristHeight, BossDepth - 0.67f);
            Vector3 braceWrist = museBody.InverseTransformPoint(stage.TransformPoint(braceInArena));
            elbow = Vector3.Lerp(elbow, new Vector3(-1.30f, 1.28f, -0.20f), down);
            wrist = Vector3.Lerp(wrist, braceWrist, down);
            fistEuler = Vector3.Lerp(fistEuler, braceEuler, down);
        }
    }
}
