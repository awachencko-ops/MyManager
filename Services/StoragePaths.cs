using System;
using System.IO;

namespace Replica
{
    internal static class StoragePaths
    {
        public static string AppBaseDirectory => AppContext.BaseDirectory;

        public static string ResolveFilePath(string configuredPath, string defaultFileName)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
                return Path.Combine(AppBaseDirectory, defaultFileName);

            return Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(AppBaseDirectory, configuredPath);
        }


        public static string ResolveExistingFilePath(string configuredPath, string defaultFileName)
        {
            var resolvedPath = ResolveFilePath(configuredPath, defaultFileName);
            if (File.Exists(resolvedPath))
                return resolvedPath;

            if (!string.IsNullOrWhiteSpace(configuredPath) && !Path.IsPathRooted(configuredPath))
            {
                var legacyPath = Path.GetFullPath(configuredPath, Environment.CurrentDirectory);
                if (File.Exists(legacyPath))
                    return legacyPath;
            }

            return resolvedPath;
        }

        public static string ResolveFolderPath(string configuredPath, string defaultFolderName)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
                return Path.Combine(AppBaseDirectory, defaultFolderName);

            return Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(AppBaseDirectory, configuredPath);
        }
    }
}
