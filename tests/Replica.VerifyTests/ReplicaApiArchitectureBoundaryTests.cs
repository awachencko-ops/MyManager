using System.Text.RegularExpressions;
using Xunit;

namespace Replica.VerifyTests;

public sealed class ReplicaApiArchitectureBoundaryTests
{
    [Fact]
    public void ApiApplicationLayer_DoesNotReferencePresentationNamespaces()
    {
        var repoRoot = FindRepositoryRoot();
        var applicationDir = Path.Combine(repoRoot, "Replica.Api", "Application");
        var offenders = FindFilesWithUsingNamespace(
            applicationDir,
            [
                "Replica.Api.Controllers",
                "Replica.Api.Hubs"
            ]);

        Assert.True(
            offenders.Count == 0,
            "Application layer must not reference presentation namespaces directly. Offenders: "
            + string.Join(", ", offenders.OrderBy(x => x, StringComparer.Ordinal)));
    }

    [Fact]
    public void ApiPresentationLayer_InfrastructureCoupling_DoesNotExpandBeyondBaseline()
    {
        var repoRoot = FindRepositoryRoot();
        var presentationDirs = new[]
        {
            Path.Combine(repoRoot, "Replica.Api", "Controllers"),
            Path.Combine(repoRoot, "Replica.Api", "Hubs")
        };

        var offenders = FindFilesWithUsingNamespace(
            presentationDirs,
            [
                "Replica.Api.Infrastructure",
                "Replica.Api.Data",
                "Replica.Api.Services"
            ]);

        var allowedBaseline = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            NormalizeRelativePath(repoRoot, Path.Combine(repoRoot, "Replica.Api", "Controllers", "AuthController.cs")),
            NormalizeRelativePath(repoRoot, Path.Combine(repoRoot, "Replica.Api", "Controllers", "DiagnosticsController.cs")),
            NormalizeRelativePath(repoRoot, Path.Combine(repoRoot, "Replica.Api", "Controllers", "OrdersController.cs")),
            NormalizeRelativePath(repoRoot, Path.Combine(repoRoot, "Replica.Api", "Controllers", "UsersController.cs"))
        };

        var unexpected = offenders
            .Where(path => !allowedBaseline.Contains(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unexpected.Count == 0,
            "New Presentation->Infrastructure/Data/Services coupling detected: "
            + string.Join(", ", unexpected));
    }

    private static List<string> FindFilesWithUsingNamespace(string rootDirectory, IReadOnlyCollection<string> namespaces)
    {
        return FindFilesWithUsingNamespace([rootDirectory], namespaces);
    }

    private static List<string> FindFilesWithUsingNamespace(IEnumerable<string> rootDirectories, IReadOnlyCollection<string> namespaces)
    {
        var repoRoot = FindRepositoryRoot();
        var results = new List<string>();
        if (namespaces.Count == 0)
            return results;

        var pattern = @"^\s*using\s+(" + string.Join("|", namespaces.Select(Regex.Escape)) + @")\s*;";
        var matcher = new Regex(pattern, RegexOptions.Multiline | RegexOptions.CultureInvariant);

        foreach (var root in rootDirectories)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                continue;

            foreach (var filePath in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (IsBuildArtifactPath(filePath))
                    continue;

                var content = File.ReadAllText(filePath);
                if (!matcher.IsMatch(content))
                    continue;

                results.Add(NormalizeRelativePath(repoRoot, filePath));
            }
        }

        return results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsBuildArtifactPath(string path)
    {
        return path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRelativePath(string repoRoot, string absolutePath)
    {
        return Path.GetRelativePath(repoRoot, absolutePath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var apiProjectPath = Path.Combine(current.FullName, "Replica.Api", "Replica.Api.csproj");
            if (File.Exists(apiProjectPath))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root could not be resolved from test runtime location.");
    }
}
