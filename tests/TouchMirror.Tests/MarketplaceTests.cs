using TouchMirror.Services;
using Xunit;

namespace TouchMirror.Tests;

public sealed class MarketplaceTests
{
    [Theory]
    [InlineData("1.0", "1.1", "1.1")]
    [InlineData("1.9", "1.10", "1.10")]
    [InlineData("1.1", "1.1", null)]
    [InlineData("1.2", "1.1", null)]
    [InlineData(null, "1.1", null)]
    [InlineData("1.0", "abc", null)]
    [InlineData("1.0", null, null)]
    [InlineData("2.0.1", "2.0.2", "2.0.2")]
    public void NewerVersion_Detects(string? installed, string? catalog, string? expected)
        => Assert.Equal(expected, MarketplaceService.NewerVersion(installed, catalog));
}
