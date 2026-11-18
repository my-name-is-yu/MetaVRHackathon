namespace Runeveil.Contracts
{
    public static class ReleaseSigningPolicy
    {
        public static bool HasCompleteSigningConfiguration(string keystorePath, string keyAlias,
            string keystorePassword, string keyPassword)
        {
            return !string.IsNullOrWhiteSpace(keystorePath) &&
                !string.IsNullOrWhiteSpace(keyAlias) &&
                !string.IsNullOrWhiteSpace(keystorePassword) &&
                !string.IsNullOrWhiteSpace(keyPassword);
        }
    }
}
