using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Replica;

internal static class ReplicaApiLaunchLocator
{
    private const int MaxAncestorDepth = 6;

    public static IReadOnlyList<string> ResolveExecutableCandidates(string baseDirectory) =>
        ResolveCandidates(baseDirectory, "Replica.Api.exe");

    public static IReadOnlyList<string> ResolveDllCandidates(string baseDirectory) =>
        ResolveCandidates(baseDirectory, "Replica.Api.dll");

    public static IReadOnlyList<string> ResolveProjectCandidates(string baseDirectory)
    {
        var normalizedBaseDirectory = NormalizeBaseDirectory(baseDirectory);
        if (string.IsNullOrWhiteSpace(normalizedBaseDirectory))
            return Array.Empty<string>();

        var candidates = new List<string>();
        foreach (var ancestor in EnumerateAncestors(normalizedBaseDirectory))
        {
            candidates.Add(Path.Combine(ancestor, "Replica.Api.csproj"));
            candidates.Add(Path.Combine(ancestor, "Replica.Api", "Replica.Api.csproj"));
        }

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ResolveCandidates(string baseDirectory, string fileName)
    {
        var normalizedBaseDirectory = NormalizeBaseDirectory(baseDirectory);
        if (string.IsNullOrWhiteSpace(normalizedBaseDirectory))
            return Array.Empty<string>();

        var candidates = new List<string>();
        foreach (var ancestor in EnumerateAncestors(normalizedBaseDirectory))
        {
            candidates.Add(Path.Combine(ancestor, fileName));
            candidates.Add(Path.Combine(ancestor, "Replica.Api", "bin", "Debug", "net8.0", fileName));
            candidates.Add(Path.Combine(ancestor, "Replica.Api", "bin", "Release", "net8.0", fileName));
        }

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateAncestors(string baseDirectory)
    {
        var current = baseDirectory;
        for (var depth = 0; depth <= MaxAncestorDepth && !string.IsNullOrWhiteSpace(current); depth++)
        {
            yield return current;

            var parent = Directory.GetParent(current);
            if (parent == null)
                yield break;

            current = parent.FullName;
        }
    }

    private static string NormalizeBaseDirectory(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            return string.Empty;

        try
        {
            return Path.GetFullPath(baseDirectory);
        }
        catch
        {
            return string.Empty;
        }
    }
}
