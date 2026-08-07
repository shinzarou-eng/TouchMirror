// GÃ©nÃ©rÃ© par tools/sign-plugins.ps1 â€” ne pas Ã©diter Ã  la main.
using System.Collections.Generic;

namespace TouchMirror.Services;

public static class VerifiedPlugins
{
    public static readonly HashSet<string> Hashes = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "22c369f0611fc9122047658ed8707c7f914cff3d2142e6287b5f0fc9694b59b2", // marketplace/reconnect/plugin.js
        "82b3d462ae7ae3cfb941193e8dd5d10e5e16ddc24e4475c04170ae581a54526a", // marketplace/session-log/plugin.js
        "22c369f0611fc9122047658ed8707c7f914cff3d2142e6287b5f0fc9694b59b2", // plugins/reconnect/plugin.js
    };
}
