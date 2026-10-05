using System.IO;

namespace TouchMirror.Services;

public static class AppPaths
{
    public static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
#if DEBUG
        "TouchMirror-dev");
#else
        "TouchMirror");
#endif
}
