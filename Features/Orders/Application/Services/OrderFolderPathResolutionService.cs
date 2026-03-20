using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Replica;

public sealed record OrderBrowseFolderResolution(bool Success, string FolderPath, string Reason);

public sealed class OrderFolderPathResolutionService
{
    private const string DefaultReason = "Папка не определена";

    public OrderBrowseFolderResolution ResolveBrowseFolderPath(OrderData order, string ordersRootPath, string tempRootPath)
    {
        if (order == null)
            return new OrderBrowseFolderResolution(false, string.Empty, DefaultReason);

        if (!OrderTopologyService.IsMultiOrder(order))
        {
            var folderPath = ResolvePreferredOrderFolder(order, ordersRootPath, tempRootPath);
            return new OrderBrowseFolderResolution(!string.IsNullOrWhiteSpace(folderPath), folderPath, DefaultReason);
        }

        var commonFolder = ResolveCommonFolderForGroupOrder(order, ordersRootPath);
        return commonFolder;
    }

    public string ResolvePreferredOrderFolder(OrderData order, string ordersRootPath, string tempRootPath)
    {
        if (order == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(order.FolderName))
            return Path.Combine(ordersRootPath, order.FolderName);

        var knownPath = FirstNotEmpty(
            order.PrintPath,
            order.PreparedPath,
            order.SourcePath,
            order.Items?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.PrintPath))?.PrintPath,
            order.Items?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.PreparedPath))?.PreparedPath,
            order.Items?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.SourcePath))?.SourcePath);

        if (!string.IsNullOrWhiteSpace(knownPath))
            return Path.GetDirectoryName(knownPath) ?? ordersRootPath;

        return !string.IsNullOrWhiteSpace(tempRootPath) ? tempRootPath : ordersRootPath;
    }

    private static OrderBrowseFolderResolution ResolveCommonFolderForGroupOrder(OrderData order, string ordersRootPath)
    {
        if (!string.IsNullOrWhiteSpace(order.FolderName))
            return new OrderBrowseFolderResolution(true, Path.Combine(ordersRootPath, order.FolderName), DefaultReason);

        var directories = GetGroupDirectoryCandidates(order)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (directories.Count == 0)
            return new OrderBrowseFolderResolution(false, string.Empty, DefaultReason);

        var distinctRoots = directories
            .Select(GetPathRootSafe)
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinctRoots.Count > 1)
            return new OrderBrowseFolderResolution(false, string.Empty, "Пути не совпадают");

        var commonDirectory = FindCommonDirectory(directories);
        if (string.IsNullOrWhiteSpace(commonDirectory))
            return new OrderBrowseFolderResolution(false, string.Empty, DefaultReason);

        return new OrderBrowseFolderResolution(true, commonDirectory, DefaultReason);
    }

    private static IEnumerable<string> GetGroupDirectoryCandidates(OrderData order)
    {
        if (order?.Items != null)
        {
            foreach (var item in order.Items.Where(item => item != null))
            {
                var itemPaths = new[] { item.SourcePath, item.PreparedPath, item.PrintPath };
                foreach (var rawPath in itemPaths)
                {
                    var cleanPath = CleanPath(rawPath);
                    if (string.IsNullOrWhiteSpace(cleanPath))
                        continue;

                    var candidateDirectory = Path.HasExtension(cleanPath)
                        ? Path.GetDirectoryName(cleanPath)
                        : cleanPath;
                    if (!string.IsNullOrWhiteSpace(candidateDirectory))
                        yield return NormalizePath(candidateDirectory);
                }
            }
        }

        var orderPaths = new[] { order?.SourcePath, order?.PreparedPath, order?.PrintPath };
        foreach (var rawPath in orderPaths)
        {
            var cleanPath = CleanPath(rawPath);
            if (string.IsNullOrWhiteSpace(cleanPath))
                continue;

            var candidateDirectory = Path.HasExtension(cleanPath)
                ? Path.GetDirectoryName(cleanPath)
                : cleanPath;
            if (!string.IsNullOrWhiteSpace(candidateDirectory))
                yield return NormalizePath(candidateDirectory);
        }
    }

    private static string FindCommonDirectory(IReadOnlyList<string> directories)
    {
        if (directories == null || directories.Count == 0)
            return string.Empty;

        var commonPath = directories[0];
        if (string.IsNullOrWhiteSpace(commonPath))
            return string.Empty;

        for (var index = 1; index < directories.Count; index++)
        {
            var candidatePath = directories[index];
            if (string.IsNullOrWhiteSpace(candidatePath))
                continue;

            while (!IsDirectoryPrefix(candidatePath, commonPath))
            {
                var parentPath = Path.GetDirectoryName(
                    commonPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(parentPath))
                    return string.Empty;

                commonPath = parentPath;
            }
        }

        return commonPath;
    }

    private static bool IsDirectoryPrefix(string path, string prefix)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(prefix))
            return false;

        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        if (path.Length == prefix.Length)
            return true;

        var boundary = path[prefix.Length];
        return boundary == Path.DirectorySeparatorChar || boundary == Path.AltDirectorySeparatorChar;
    }

    private static string GetPathRootSafe(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try
        {
            return Path.GetPathRoot(path) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? FirstNotEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate;
        }

        return null;
    }

    private static string CleanPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().Trim('"');
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
        }
    }
}
