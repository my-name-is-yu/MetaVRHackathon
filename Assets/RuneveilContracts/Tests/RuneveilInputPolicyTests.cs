using NUnit.Framework;
using UnityEngine;

namespace Runeveil.Contracts.Tests
{
    public sealed class RuneveilInputPolicyTests
    {
        static readonly Ray HeadRay = new(Vector3.zero, Vector3.forward);
        static readonly Ray EyeRay = new(Vector3.up, Vector3.right);

        [Test]
        public void DeniedEyeAccessUsesHeadRayForMenuOnly()
        {
            var rays = RuneveilInputPolicy.Resolve(null, HeadRay, true);

            Assert.That(rays.MenuActionRay, Is.EqualTo(HeadRay));
            Assert.That(rays.SpellRay, Is.Null);
        }

        [Test]
        public void RecoveredEyeRayTakesPriorityForMenuAndSpells()
        {
            var denied = RuneveilInputPolicy.Resolve(null, HeadRay, true);
            var recovered = RuneveilInputPolicy.Resolve(EyeRay, HeadRay, true);

            Assert.That(denied.MenuActionRay, Is.EqualTo(HeadRay));
            Assert.That(recovered.MenuActionRay, Is.EqualTo(EyeRay));
            Assert.That(recovered.SpellRay, Is.EqualTo(EyeRay));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NoMenuActionPreservesOnlyTheEyeRay(bool hasEyeRay)
        {
            Ray? eyeRay = hasEyeRay ? EyeRay : null;
            var rays = RuneveilInputPolicy.Resolve(eyeRay, HeadRay, false);

            Assert.That(rays.MenuActionRay, Is.Null);
            Assert.That(rays.SpellRay, Is.EqualTo(eyeRay));
        }
    }
}
