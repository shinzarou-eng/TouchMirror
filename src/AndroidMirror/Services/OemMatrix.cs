namespace TouchMirror.Services;

public sealed record OemIssue(string TitleKey, string DetailKey);

public sealed record OemEntry(
    string Brand,
    IReadOnlyList<OemIssue> Issues,
    string? RecommendationKey = null);

public static class OemMatrix
{
    private static readonly OemEntry[] Entries =
    {
        new("Xiaomi",
        [
            new("oem.xiaomi.1.title", "oem.xiaomi.1.detail"),
            new("oem.xiaomi.2.title", "oem.xiaomi.2.detail"),
            new("oem.xiaomi.3.title", "oem.xiaomi.3.detail"),
        ], "oem.xiaomi.rec"),
        new("Samsung",
        [
            new("oem.samsung.1.title", "oem.samsung.1.detail"),
            new("oem.samsung.2.title", "oem.samsung.2.detail"),
            new("oem.samsung.3.title", "oem.samsung.3.detail"),
        ], "oem.samsung.rec"),
        new("Oppo",
        [
            new("oem.oppo.1.title", "oem.oppo.1.detail"),
            new("oem.oppo.2.title", "oem.oppo.2.detail"),
            new("oem.oppo.3.title", "oem.oppo.3.detail"),
        ]),
        new("Vivo",
        [
            new("oem.vivo.1.title", "oem.vivo.1.detail"),
            new("oem.vivo.2.title", "oem.vivo.2.detail"),
        ]),
        new("Huawei",
        [
            new("oem.huawei.1.title", "oem.huawei.1.detail"),
            new("oem.huawei.2.title", "oem.huawei.2.detail"),
            new("oem.huawei.3.title", "oem.huawei.3.detail"),
        ]),
        new("Google", []),
    };

    public static IReadOnlyList<OemEntry> All => Entries;

    public static OemEntry? For(string? brand) => brand switch
    {
        "Xiaomi" => Find("Xiaomi"),
        "Samsung" => Find("Samsung"),
        "Oppo" or "OnePlus" or "Realme" => Find("Oppo"),
        "Vivo" => Find("Vivo"),
        "Huawei" or "Honor" => Find("Huawei"),
        "Google" => Find("Google"),
        _ => null,
    };

    private static OemEntry? Find(string brand)
    {
        foreach (var e in Entries)
            if (e.Brand == brand)
                return e;
        return null;
    }
}
