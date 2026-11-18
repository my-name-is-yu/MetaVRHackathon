using NUnit.Framework;

namespace Runeveil.Contracts.Tests
{
    public sealed class ReleaseSigningPolicyTests
    {
        [TestCase(null, "alias", "store-password", "key-password")]
        [TestCase("keystore", "", "store-password", "key-password")]
        [TestCase("keystore", "alias", " \t", "key-password")]
        [TestCase("keystore", "alias", "store-password", null)]
        public void IncompleteSigningConfigurationIsRejected(string keystorePath, string keyAlias,
            string keystorePassword, string keyPassword)
        {
            Assert.That(ReleaseSigningPolicy.HasCompleteSigningConfiguration(
                keystorePath, keyAlias, keystorePassword, keyPassword), Is.False);
        }

        [Test]
        public void CompleteSigningConfigurationIsAccepted()
        {
            Assert.That(ReleaseSigningPolicy.HasCompleteSigningConfiguration(
                "keystore", "alias", "store-password", "key-password"), Is.True);
        }
    }
}
