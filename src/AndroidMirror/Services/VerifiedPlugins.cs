// GÃ©nÃ©rÃ© par tools/sign-plugins.ps1 â€” ne pas Ã©diter Ã  la main.
using System.Collections.Generic;

namespace TouchMirror.Services;

public static class VerifiedPlugins
{
    public static readonly HashSet<string> Hashes = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "9ab45421d16f1478483d759165e4c81730de1421830581942f4e7455682b2b28", // plugins/watchdog/plugin.js
    };
}
