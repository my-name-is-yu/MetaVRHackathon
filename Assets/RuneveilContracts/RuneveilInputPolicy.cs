using UnityEngine;

namespace Runeveil.Contracts
{
    public readonly struct RuneveilAimRays
    {
        public Ray? SpellRay { get; }
        public Ray? MenuActionRay { get; }

        internal RuneveilAimRays(Ray? spellRay, Ray? menuActionRay)
        {
            SpellRay = spellRay;
            MenuActionRay = menuActionRay;
        }
    }

    public static class RuneveilInputPolicy
    {
        public static RuneveilAimRays Resolve(Ray? eyeRay, Ray headRay, bool menuActionAvailable)
        {
            Ray? menuActionRay = menuActionAvailable ? eyeRay ?? headRay : null;
            return new RuneveilAimRays(eyeRay, menuActionRay);
        }
    }
}
