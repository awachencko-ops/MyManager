using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Replica
{
    public static class FileHashService
    {
        private const int MaxCachedHashes = 8192;
        private static readonly ConcurrentDictionary<string, CachedHashEntry> HashCache = new(StringComparer.OrdinalIgnoreCase);

        private readonly struct CachedHashEntry(string hash, long fileLength, long lastWriteUtcTicks)
        {
            public string Hash { get; } = hash;
            public long FileLength { get; } = fileLength;
            public long LastWriteUtcTicks { get; } = lastWriteUtcTicks;
        }

        public static bool TryComputeSha256(string path, out string hash, out string error)
        {
            hash = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "path is empty";
                return false;
            }

            try
            {
                var normalizedPath = NormalizePath(path);
                if (!TryGetFileFingerprint(normalizedPath, out var fileLength, out var lastWriteUtcTicks, out var fingerprintError))
                {
                    error = fingerprintError;
                    return false;
                }

                if (TryGetCachedHash(normalizedPath, fileLength, lastWriteUtcTicks, out var cachedHash))
                {
                    hash = cachedHash;
                    return true;
                }

                using var stream = new FileStream(normalizedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sha = SHA256.Create();
                var bytes = sha.ComputeHash(stream);
                hash = Convert.ToHexString(bytes);
                CacheHash(normalizedPath, fileLength, lastWriteUtcTicks, hash);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static string ComputeSha256OrEmpty(string path)
        {
            return TryComputeSha256(path, out var hash, out _) ? hash : string.Empty;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                return Path.GetFullPath(path.Trim());
            }
            catch
            {
                return path.Trim();
            }
        }

        private static bool TryGetFileFingerprint(string path, out long fileLength, out long lastWriteUtcTicks, out string error)
        {
            fileLength = 0;
            lastWriteUtcTicks = 0;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "path is empty";
                return false;
            }

            try
            {
                var fileInfo = new FileInfo(path);
                if (!fileInfo.Exists)
                {
                    error = "file not found";
                    return false;
                }

                fileLength = fileInfo.Length;
                lastWriteUtcTicks = fileInfo.LastWriteTimeUtc.Ticks;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TryGetCachedHash(string path, long fileLength, long lastWriteUtcTicks, out string hash)
        {
            hash = string.Empty;
            if (!HashCache.TryGetValue(path, out var cached))
                return false;

            if (cached.FileLength != fileLength || cached.LastWriteUtcTicks != lastWriteUtcTicks)
                return false;

            hash = cached.Hash;
            return !string.IsNullOrWhiteSpace(hash);
        }

        private static void CacheHash(string path, long fileLength, long lastWriteUtcTicks, string hash)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(hash))
                return;

            if (HashCache.Count > MaxCachedHashes)
                HashCache.Clear();

            HashCache[path] = new CachedHashEntry(hash, fileLength, lastWriteUtcTicks);
        }
    }
}
