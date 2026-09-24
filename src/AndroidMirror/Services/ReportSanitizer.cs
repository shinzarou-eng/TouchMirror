using System.Text.RegularExpressions;

namespace TouchMirror.Services;

public static class ReportSanitizer
{
    private static readonly Regex AdbSerialLine = new(
        @"(?m)^(\S*\d\S*)([ \t]+)(device|unauthorized|offline|recovery|sideload|bootloader|host|detached|authorizing)\b",
        RegexOptions.Compiled);
    private static readonly Regex Bearer = new(
        @"\b(?i:(bearer))(\s+)[A-Za-z0-9._~+/=-]{6,}", RegexOptions.Compiled);
    private static readonly Regex SecretKv = new(
        @"(?i)\b(token|api[_-]?key|apikey|secret|password|passwd|pwd|authorization|key)(\s*[:=]\s*)[A-Za-z0-9._~+/=-]{6,}",
        RegexOptions.Compiled);
    private static readonly Regex GuidRe = new(
        @"\b[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\b",
        RegexOptions.Compiled);
    private static readonly Regex Mac = new(
        @"\b(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}\b", RegexOptions.Compiled);
    private static readonly Regex Ipv6 = new(
        @"\b(?:[0-9A-Fa-f]{1,4}:){1,7}:(?:[0-9A-Fa-f]{0,4}:?){0,7}", RegexOptions.Compiled);
    private static readonly Regex Ipv4 = new(
        @"\b(\d{1,3})\.(\d{1,3})\.\d{1,3}\.\d{1,3}\b", RegexOptions.Compiled);
    private static readonly Regex LongHex = new(
        @"\b[0-9A-Fa-f]{24,}\b", RegexOptions.Compiled);
    private static readonly Regex LongToken = new(
        @"\b(?=[A-Za-z0-9+/]*\d)(?=[A-Za-z0-9+/]*[A-Z])(?=[A-Za-z0-9+/]*[a-z])[A-Za-z0-9+/]{24,}={0,2}\b",
        RegexOptions.Compiled);
    private static readonly Regex UserPath = new(
        @"(?i)([A-Z]:)\\Users\\[^\\\s""']+", RegexOptions.Compiled);
    private static readonly Regex Email = new(
        @"\b[\w.+-]+@[\w-]+\.[\w.-]+\b", RegexOptions.Compiled);

    public static string Sanitize(string? report, IEnumerable<string> knownIds)
    {
        if (string.IsNullOrEmpty(report))
            return report ?? "";
        var s = report;
        foreach (var id in knownIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
            s = s.Replace(id, "«serial»");
        s = Bearer.Replace(s, m => m.Groups[1].Value + m.Groups[2].Value + "«secret»");
        s = SecretKv.Replace(s, m => m.Groups[1].Value + m.Groups[2].Value + "«secret»");
        s = AdbSerialLine.Replace(s, m => "«serial»" + m.Groups[2].Value + m.Groups[3].Value);
        s = GuidRe.Replace(s, "«uuid»");
        s = Mac.Replace(s, "xx:xx:xx:xx:xx:xx");
        s = Ipv6.Replace(s, "«ipv6»");
        s = Ipv4.Replace(s, "$1.$2.×.×");
        s = LongHex.Replace(s, "«clé»");
        s = LongToken.Replace(s, "«jeton»");
        s = UserPath.Replace(s, m => m.Groups[1].Value + "\\Users\\…");
        s = Email.Replace(s, "[email]");
        var machine = Environment.MachineName;
        if (machine.Length >= 4)
            s = s.Replace(machine, "«machine»", StringComparison.OrdinalIgnoreCase);
        return s;
    }
}
