// GÃ©nÃ©rÃ© par tools/sign-plugins.ps1 â€” ne pas Ã©diter Ã  la main.
using System.Collections.Generic;

namespace TouchMirror.Services;

public static class VerifiedPlugins
{
    public static readonly HashSet<string> Hashes = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "22c369f0611fc9122047658ed8707c7f914cff3d2142e6287b5f0fc9694b59b2", // plugins/reconnect/plugin.js
    };
}
