// GÃ©nÃ©rÃ© par tools/sign-plugins.ps1 â€” ne pas Ã©diter Ã  la main.
using System.Collections.Generic;

namespace TouchMirror.Services;

public static class VerifiedPlugins
{
    public static readonly HashSet<string> Hashes = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "70f47f042efdc366d5d1e663614df478a6db46edc1cae248a809e95965ed1fa3", // watchdog.ps1
    };
}
