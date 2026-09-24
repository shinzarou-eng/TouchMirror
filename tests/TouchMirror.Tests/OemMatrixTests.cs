using System.IO;
using System.Text.Json;
using TouchMirror.Services;
using Xunit;

namespace TouchMirror.Tests;

public sealed class OemMatrixTests
{
    private static Dictionary<string, string> Lang(string code)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "lang")))
            dir = dir.Parent;
        var json = File.ReadAllText(Path.Combine(dir!.FullName, "lang", code + ".json"));
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
    }

    [Fact]
    public void Every_Detectable_Brand_Has_An_Entry()
    {
        foreach (var brand in new[] { "Xiaomi", "Samsung", "Oppo", "Vivo", "Huawei", "Google" })
            Assert.NotNull(OemMatrix.For(brand));
    }

    [Fact]
    public void Aliases_Resolve_To_Canonical_Entries()
    {
        Assert.Equal("Oppo", OemMatrix.For("OnePlus")!.Brand);
        Assert.Equal("Oppo", OemMatrix.For("Realme")!.Brand);
        Assert.Equal("Huawei", OemMatrix.For("Honor")!.Brand);
    }

    [Fact]
    public void Unknown_Brand_Returns_Null()
    {
        Assert.Null(OemMatrix.For("Motorola"));
        Assert.Null(OemMatrix.For(null));
        Assert.Null(OemMatrix.For(""));
    }

    [Fact]
    public void Entries_With_Issues_Have_Title_And_Detail()
    {
        foreach (var e in OemMatrix.All)
            foreach (var i in e.Issues)
            {
                Assert.False(string.IsNullOrWhiteSpace(i.TitleKey));
                Assert.False(string.IsNullOrWhiteSpace(i.DetailKey));
            }
    }

    [Fact]
    public void Google_Entry_Is_Explicitly_Clean()
    {
        var e = OemMatrix.For("Google")!;
        Assert.Equal("Google", e.Brand);
        Assert.Empty(e.Issues);
        Assert.Null(e.RecommendationKey);
    }

    [Fact]
    public void All_Referenced_Keys_Exist_In_Both_Languages()
    {
        var fr = Lang("fr");
        var en = Lang("en");
        foreach (var e in OemMatrix.All)
        {
            foreach (var i in e.Issues)
            {
                Assert.True(fr.ContainsKey(i.TitleKey), $"fr manque {i.TitleKey}");
                Assert.True(fr.ContainsKey(i.DetailKey), $"fr manque {i.DetailKey}");
                Assert.True(en.ContainsKey(i.TitleKey), $"en manque {i.TitleKey}");
                Assert.True(en.ContainsKey(i.DetailKey), $"en manque {i.DetailKey}");
            }
            if (e.RecommendationKey is { } rk)
            {
                Assert.True(fr.ContainsKey(rk), $"fr manque {rk}");
                Assert.True(en.ContainsKey(rk), $"en manque {rk}");
            }
        }
        foreach (var k in new[] { "oem.none", "oem.known", "oem.rec_label", "oem.matrix_label" })
        {
            Assert.True(fr.ContainsKey(k), $"fr manque {k}");
            Assert.True(en.ContainsKey(k), $"en manque {k}");
        }
    }
}
