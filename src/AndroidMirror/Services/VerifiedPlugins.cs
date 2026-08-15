// GÃ©nÃ©rÃ© par tools/sign-plugins.ps1 â€” ne pas Ã©diter Ã  la main.
using System.Collections.Generic;

namespace TouchMirror.Services;

public static class VerifiedPlugins
{
    public static readonly HashSet<string> Hashes = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "f52363adfb80b4dd62ef0c3d96ac36bb2cc178d9dd2a77b6e101d44ad6162630", // marketplace/reconnect/plugin.js
        "72675b1735bde5fc8094103c83b0748a42a8dd794aedd507d9e06548e21a67e6", // marketplace/session-log/plugin.js
        "f52363adfb80b4dd62ef0c3d96ac36bb2cc178d9dd2a77b6e101d44ad6162630", // plugins/reconnect/plugin.js
    };
}
