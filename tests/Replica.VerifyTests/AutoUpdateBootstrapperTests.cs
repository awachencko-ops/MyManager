using Xunit;

namespace Replica.VerifyTests;

public sealed class AutoUpdateBootstrapperTests
{
    [Fact]
    public void ShouldStart_WhenLanPostgreSqlAndApiUrlConfigured_ReturnsTrue()
    {
        var settings = new AppSettings
        {
            OrdersStorageBackend = OrdersStorageMode.LanPostgreSql,
            LanApiBaseUrl = "http://localhost:5000/"
        };

        Assert.True(AutoUpdateBootstrapper.ShouldStart(settings));
    }

    [Fact]
    public void ShouldStart_WhenFileSystemMode_ReturnsFalse()
    {
        var settings = new AppSettings
        {
            OrdersStorageBackend = OrdersStorageMode.FileSystem,
            LanApiBaseUrl = "http://localhost:5000/"
        };

        Assert.False(AutoUpdateBootstrapper.ShouldStart(settings));
    }

    [Fact]
    public void ResolveManifestUrl_AppendsUpdatesUpdateXml()
    {
        var url = AutoUpdateBootstrapper.ResolveManifestUrl("http://localhost:5000/");

        Assert.Equal("http://localhost:5000/updates/update.xml", url);
    }

    [Fact]
    public void ResolveManifestUrl_InvalidUrl_ReturnsEmpty()
    {
        var url = AutoUpdateBootstrapper.ResolveManifestUrl("not-a-url");

        Assert.Equal(string.Empty, url);
    }
}
