using System.IO;
using System.Linq;

namespace Replica.VerifyTests;

public sealed class ReplicaApiLaunchLocatorTests
{
    [Fact]
    public void ResolveDllCandidates_WhenAppRunsFromBinDebug_FindsSiblingApiProjectBeforeDesktopFallback()
    {
        var baseDirectory = @"C:\Users\user\Desktop\MyManager 1.0.1\bin\Debug\net8.0-windows";

        var candidates = ReplicaApiLaunchLocator.ResolveDllCandidates(baseDirectory);

        var expectedProjectCandidate = Path.GetFullPath(
            Path.Combine(baseDirectory, @"..\..\..\Replica.Api\bin\Debug\net8.0\Replica.Api.dll"));
        var wrongDesktopCandidate = Path.GetFullPath(
            Path.Combine(baseDirectory, @"..\..\..\..\Replica.Api\bin\Debug\net8.0\Replica.Api.dll"));

        var expectedIndex = candidates.ToList().FindIndex(path => string.Equals(path, expectedProjectCandidate, System.StringComparison.OrdinalIgnoreCase));
        var wrongIndex = candidates.ToList().FindIndex(path => string.Equals(path, wrongDesktopCandidate, System.StringComparison.OrdinalIgnoreCase));

        Assert.True(expectedIndex >= 0, "Expected the sibling Replica.Api project path to be present.");
        Assert.True(wrongIndex >= 0, "Expected the old desktop-level fallback path to still be representable later in the search.");
        Assert.True(expectedIndex < wrongIndex, "The correct sibling project path should be preferred over the desktop-level fallback.");
    }
}
