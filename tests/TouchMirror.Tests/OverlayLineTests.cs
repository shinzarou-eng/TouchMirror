using System.Text.Json;
using TouchMirror.Services;
using TouchMirror.Views;
using Xunit;

namespace TouchMirror.Tests;

public sealed class OverlayLineTests
{
    private static List<OverlayLine>? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return LocalApiHost.ToLines(doc.RootElement.EnumerateArray()
            .Select(e => e.Clone()).ToArray());
    }

    [Fact]
    public void Null_ReturnsNull()
        => Assert.Null(LocalApiHost.ToLines(null));

    [Fact]
    public void PlainString_LegacyLine()
    {
        var lines = Parse("""["hello"]""");
        var l = Assert.Single(lines!);
        Assert.Equal("hello", l.Text);
        Assert.Null(l.Kind);
        Assert.Null(l.Done);
        Assert.Null(l.Right);
        Assert.Null(l.Img);
    }

    [Fact]
    public void TypedLine_AllFields()
    {
        var lines = Parse(
            """[{"text":"Barchwork","k":"item","done":true,"right":"niv 37","v":0.5,"img":"https://x/29.png","color":"#fff"}]""");
        var l = Assert.Single(lines!);
        Assert.Equal("Barchwork", l.Text);
        Assert.Equal("item", l.Kind);
        Assert.True(l.Done);
        Assert.Equal("niv 37", l.Right);
        Assert.Equal(0.5, l.Value);
        Assert.Equal("https://x/29.png", l.Img);
        Assert.Equal("#fff", l.Color);
    }

    [Fact]
    public void NonBoolDone_Ignored()
    {
        var lines = Parse("""[{"text":"x","done":"yes"}]""");
        Assert.Null(Assert.Single(lines!).Done);
    }

    [Fact]
    public void NonStringNonObject_Skipped()
    {
        var lines = Parse("""["a",42,true,null,"b"]""");
        Assert.Equal(2, lines!.Count);
        Assert.Equal("a", lines[0].Text);
        Assert.Equal("b", lines[1].Text);
    }

    [Fact]
    public void Norm_StripsAccentsAndCase()
    {
        Assert.Equal("l'edente", GraphWidget.Norm("l'Édenté"));
        Assert.Equal("barchwork le multicolore", GraphWidget.Norm("Barchwork le Multicolore"));
        Assert.Equal("zatoishwan", GraphWidget.Norm("Zatoïshwan"));
    }
}
