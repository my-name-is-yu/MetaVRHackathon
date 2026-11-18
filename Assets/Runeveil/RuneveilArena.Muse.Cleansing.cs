using UnityEngine;
using UnityEngine.Rendering;

namespace Runeveil
{
    public sealed partial class RuneveilArena
    {
        private const float MuseReleaseSeconds = 0.47f;
        private const float MuseLookUpSeconds = 1.12f;
        private const float MusePortraitFadeSeconds = 0.70f;
        private static readonly int CleansingAmount = Shader.PropertyToID("_Cleansing");
        private static readonly int RelaxedFaceTexture = Shader.PropertyToID("_RelaxedFace");
        private static readonly int RelaxedFaceAmount = Shader.PropertyToID("_RelaxedFaceWeight");

        private Material cleansingPlush;
        private Material cleansingPaw;
        private Material cleansingFace;
        private bool hasRestoredPortrait;
        private bool cleansingMuse;
        private float museCleansingAge;
        private Vector3 cleansingBodyStart;
        private Vector3 cleansingBodyScale;
        private Quaternion cleansingBodyRotation;
        private Quaternion cleansingHeadRotation;
        private ReleasedArmPose cleansingAttackArm;
        private ReleasedArmPose cleansingAnchorArm;

        internal bool MuseCleansingComplete => museRig != null
            ? museRig.CleansingComplete
            : cleansingMuse && museCleansingAge >= MuseLookUpSeconds + (hasRestoredPortrait ? MusePortraitFadeSeconds : 0f);

        internal bool MuseRestoredFormVisible => museRig != null
            ? museRig.RestoredFormVisible
            : (hasRestoredPortrait && MuseVictoryPortraitBlend > 0f) || MuseCleansingComplete;

        // The rig's restored form stays on stage behind the result UI.
        internal float MuseVictoryPortraitBlend => museRig == null
            ? CleansingProgress(museCleansingAge, MuseLookUpSeconds, MuseLookUpSeconds + MusePortraitFadeSeconds)
            : 0f;

        private void ConfigureMuseCleansing(Material plush, Material paw, Material face)
        {
            cleansingPlush = plush;
            cleansingPaw = paw;
            cleansingFace = face;
            Texture2D restored = Resources.Load<Texture2D>("Runeveil/MuseRestored");
            hasRestoredPortrait = restored != null;
            if (hasRestoredPortrait) cleansingFace.SetTexture(RelaxedFaceTexture, restored);
        }

        private void TickMuseCleansing(float dt, bool won)
        {
            if (!won)
            {
                if (!cleansingMuse) return;
                cleansingMuse = false;
                museCleansingAge = 0f;
                cleansingPlush.SetFloat(CleansingAmount, 0f);
                cleansingPaw.SetFloat(CleansingAmount, 0f);
                cleansingFace.SetFloat(CleansingAmount, 0f);
                cleansingFace.SetFloat(RelaxedFaceAmount, 0f);
                SetCleansingWisps(false);
                boss.gameObject.SetActive(true);
                return;
            }

            if (!cleansingMuse)
            {
                // Capture the last combat pose, including a half-raised fist or a lightning stagger.
                cleansingMuse = true;
                cleansingBodyStart = museBody.localPosition;
                cleansingBodyScale = museBody.localScale;
                cleansingBodyRotation = museBody.localRotation;
                cleansingHeadRotation = museHead.localRotation;
                cleansingAttackArm = new ReleasedArmPose(attackArm);
                cleansingAnchorArm = new ReleasedArmPose(anchorArm);
                SetCleansingWisps(true);
            }
            museCleansingAge = Mathf.Min(museCleansingAge + dt, MuseLookUpSeconds + MusePortraitFadeSeconds);
        }

        private void PoseMuseCleansing(Transform head)
        {
            if (!cleansingMuse || !boss.gameObject.activeSelf) return;
            float release = CleansingProgress(museCleansingAge, 0f, MuseReleaseSeconds);
            float lookUp = CleansingProgress(museCleansingAge, MuseReleaseSeconds, MuseLookUpSeconds);
            museBody.SetLocalPositionAndRotation(
                Vector3.Lerp(cleansingBodyStart, Vector3.down * (0.13f - 0.055f * lookUp), release),
                Quaternion.Slerp(cleansingBodyRotation, Quaternion.Euler(-6f + 4f * lookUp, 0f, 0f), release));
            museBody.localScale = Vector3.Lerp(cleansingBodyScale, Vector3.one, release);
            head.localRotation = Quaternion.Slerp(cleansingHeadRotation,
                Quaternion.Euler(-19f + 22f * lookUp, 0f, -3f * lookUp), release);
            cleansingAttackArm.Drop(attackArm, 1f, release);
            cleansingAnchorArm.Drop(anchorArm, -1f, release);

            float corruptionFade = CleansingProgress(museCleansingAge, 0f, MuseLookUpSeconds);
            cleansingPlush.SetFloat(CleansingAmount, corruptionFade);
            cleansingPaw.SetFloat(CleansingAmount, corruptionFade);
            cleansingFace.SetFloat(CleansingAmount, corruptionFade);
            if (hasRestoredPortrait)
                cleansingFace.SetFloat(RelaxedFaceAmount,
                    CleansingProgress(museCleansingAge, MuseReleaseSeconds - 0.04f, MuseReleaseSeconds + 0.09f));
            Tint(thornRenderer, Color.Lerp(Color.white, Rgb(0xff4a4a), rage), 1f - release);

            // Keep the last 3D pose visible through the portrait crossfade.
            if (hasRestoredPortrait && MuseVictoryPortraitBlend >= 1f)
                boss.gameObject.SetActive(false);
        }

        private static float CleansingProgress(float age, float start, float end) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, age));

        private void SetCleansingWisps(bool fading)
        {
            Material wisps = thornRenderer.sharedMaterial;
            wisps.SetFloat("_SrcBlend", (float)(fading ? BlendMode.SrcAlpha : BlendMode.One));
            wisps.SetFloat("_DstBlend", (float)(fading ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            wisps.SetFloat("_ZWrite", fading ? 0f : 1f);
            wisps.SetOverrideTag("RenderType", fading ? "Transparent" : "Opaque");
            wisps.renderQueue = fading ? (int)RenderQueue.Transparent : -1;
        }

        private readonly struct ReleasedArmPose
        {
            private readonly Vector3 elbow;
            private readonly Vector3 wrist;
            private readonly Quaternion rotation;

            public ReleasedArmPose(Arm arm)
            {
                elbow = arm.Elbow;
                wrist = arm.Fist.localPosition;
                rotation = arm.Fist.localRotation;
            }

            public void Drop(Arm arm, float side, float release)
            {
                Vector3 relaxedElbow = new(side * 1.10f, 1.21f, -0.05f);
                Vector3 relaxedWrist = new(side * 1.20f, 0.76f, -0.17f);
                Quaternion relaxedRotation = Quaternion.Euler(5f, -14f * side, 8f * side);
                arm.Pose(Vector3.Lerp(elbow, relaxedElbow, release),
                    Vector3.Lerp(wrist, relaxedWrist, release),
                    Quaternion.Slerp(rotation, relaxedRotation, release).eulerAngles);
            }
        }
    }
}
