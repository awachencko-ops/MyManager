using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Manina.Windows.Forms;
using Manina.Windows.Forms.ImageListViewItemAdaptors;
using PdfiumViewer;

namespace Replica
{
    internal sealed class PdfAwareFileSystemAdaptor : FileSystemAdaptor
    {
        private static readonly JsonSerializerOptions SyncStateJsonOptions = new() { WriteIndented = true };
        private const int MaxSyncedFileEntries = 20000;
        private const int SyncStateFlushThreshold = 25;
        private static readonly TimeSpan SyncStateMaxFlushDelay = TimeSpan.FromSeconds(10);

        private readonly ConcurrentDictionary<string, CachedThumbnail> _pdfCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _cacheSync = new();

        private readonly string _localDiskCacheDirectory;
        private readonly ThumbnailCacheIndex _localCacheIndex;
        private readonly string _sharedDiskCacheDirectory;
        private readonly ThumbnailCacheIndex? _sharedCacheIndex;
        private readonly bool _hasSharedCache;

        private readonly ConcurrentDictionary<string, byte> _mirrorInFlight = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _syncStateFilePath;
        private readonly object _syncStateLock = new();
        private CacheSyncState _syncState;
        private int _syncStatePendingChanges;
        private DateTime _lastSyncStateSaveUtc = DateTime.MinValue;

        public PdfAwareFileSystemAdaptor(string localDiskCacheDirectory, string sharedDiskCacheDirectory = "")
        {
            _localDiskCacheDirectory = ResolveLocalCacheDirectory(localDiskCacheDirectory);
            Directory.CreateDirectory(_localDiskCacheDirectory);
            _localCacheIndex = new ThumbnailCacheIndex(Path.Combine(_localDiskCacheDirectory, "thumb-index.db"));

            var normalizedSharedPath = NormalizeCacheDirectory(sharedDiskCacheDirectory);
            if (!string.IsNullOrWhiteSpace(normalizedSharedPath) && !ArePathsEqual(normalizedSharedPath, _localDiskCacheDirectory))
            {
                try
                {
                    Directory.CreateDirectory(normalizedSharedPath);
                    _sharedDiskCacheDirectory = normalizedSharedPath;
                    _sharedCacheIndex = new ThumbnailCacheIndex(Path.Combine(_sharedDiskCacheDirectory, "thumb-index.db"));
                    _hasSharedCache = true;
                }
                catch
                {
                    _sharedDiskCacheDirectory = string.Empty;
                    _sharedCacheIndex = null;
                    _hasSharedCache = false;
                }
            }
            else
            {
                _sharedDiskCacheDirectory = string.Empty;
                _sharedCacheIndex = null;
                _hasSharedCache = false;
            }

            _syncStateFilePath = Path.Combine(_localDiskCacheDirectory, "sync-state.json");
            _syncState = LoadSyncState(_sharedDiskCacheDirectory);
        }

        public override Image GetThumbnail(object key, Size size, UseEmbeddedThumbnails useEmbeddedThumbnails, bool useExifOrientation)
        {
            var path = ResolvePath(key);
            if (string.IsNullOrWhiteSpace(path) || !IsPdfPath(path) || !File.Exists(path))
                return base.GetThumbnail(key, size, useEmbeddedThumbnails, useExifOrientation);

            var normalizedPath = NormalizePath(path);
            if (!IsPdfReadyForPreview(normalizedPath))
                return base.GetThumbnail(key, size, useEmbeddedThumbnails, useExifOrientation);

            if (!TryGetFileMetadata(normalizedPath, out var fileLength, out var lastWriteTicksUtc) || fileLength <= 0)
                return base.GetThumbnail(key, size, useEmbeddedThumbnails, useExifOrientation);

            if (TryGetCachedThumbnail(normalizedPath, size, fileLength, lastWriteTicksUtc, out var cached))
                return cached;

            if (TryLoadCachedThumbnailFromLocalDisk(normalizedPath, size, fileLength, lastWriteTicksUtc, out var localCached, out _))
            {
                StoreCachedThumbnail(normalizedPath, localCached, size, fileLength, lastWriteTicksUtc);
                return localCached;
            }

            if (TryLoadCachedThumbnailFromSharedDisk(normalizedPath, size, fileLength, lastWriteTicksUtc, out var sharedCached, out var sharedCacheFileName))
            {
                var importedCacheFileName = SaveCachedThumbnailToLocalDisk(normalizedPath, sharedCached, size, fileLength, lastWriteTicksUtc);
                StoreCachedThumbnail(normalizedPath, sharedCached, size, fileLength, lastWriteTicksUtc);
                if (!string.IsNullOrWhiteSpace(importedCacheFileName))
                    MarkCacheFileSynced(importedCacheFileName, pulled: true, pushed: false);
                else if (!string.IsNullOrWhiteSpace(sharedCacheFileName))
                    MarkCacheFileSynced(sharedCacheFileName, pulled: true, pushed: false);

                return sharedCached;
            }

            var rendered = TryRenderPdfThumbnail(normalizedPath, size);
            if (rendered == null)
                return base.GetThumbnail(key, size, useEmbeddedThumbnails, useExifOrientation);

            var localCacheFileName = SaveCachedThumbnailToLocalDisk(normalizedPath, rendered, size, fileLength, lastWriteTicksUtc);
            StoreCachedThumbnail(normalizedPath, rendered, size, fileLength, lastWriteTicksUtc);
            if (!string.IsNullOrWhiteSpace(localCacheFileName))
                MirrorThumbnailToSharedAsync(localCacheFileName, normalizedPath, size, fileLength, lastWriteTicksUtc);

            return rendered;
        }

        private bool TryGetCachedThumbnail(string filePath, Size size, long fileLength, long lastWriteTicksUtc, out Image image)
        {
            image = null!;
            lock (_cacheSync)
            {
                if (!_pdfCache.TryGetValue(filePath, out var cached))
                    return false;

                var isValid =
                    cached.FileLength == fileLength
                    && cached.LastWriteTicksUtc == lastWriteTicksUtc
                    && cached.Size == size;

                if (!isValid)
                {
                    if (_pdfCache.TryRemove(filePath, out var removedByMismatch))
                        removedByMismatch.Bitmap.Dispose();
                    return false;
                }

                try
                {
                    image = (Image)cached.Bitmap.Clone();
                    return true;
                }
                catch (InvalidOperationException)
                {
                    if (_pdfCache.TryRemove(filePath, out var removedByCloneFailure))
                        removedByCloneFailure.Bitmap.Dispose();
                    return false;
                }
            }
        }

        private void StoreCachedThumbnail(string filePath, Bitmap thumbnail, Size size, long fileLength, long lastWriteTicksUtc)
        {
            var cachedBitmap = (Bitmap)thumbnail.Clone();
            var cacheValue = new CachedThumbnail(
                fileLength,
                lastWriteTicksUtc,
                size,
                cachedBitmap);

            lock (_cacheSync)
            {
                if (_pdfCache.TryGetValue(filePath, out var existing))
                    existing.Bitmap.Dispose();

                _pdfCache[filePath] = cacheValue;
            }
        }

        private bool TryLoadCachedThumbnailFromLocalDisk(
            string filePath,
            Size size,
            long fileLength,
            long lastWriteTicksUtc,
            out Bitmap thumbnail,
            out string cacheFileName)
        {
            return TryLoadCachedThumbnailFromDisk(
                _localCacheIndex,
                _localDiskCacheDirectory,
                filePath,
                size,
                fileLength,
                lastWriteTicksUtc,
                out thumbnail,
                out cacheFileName);
        }

        private bool TryLoadCachedThumbnailFromSharedDisk(
            string filePath,
            Size size,
            long fileLength,
            long lastWriteTicksUtc,
            out Bitmap thumbnail,
            out string cacheFileName)
        {
            thumbnail = null!;
            cacheFileName = string.Empty;

            if (!_hasSharedCache || _sharedCacheIndex == null)
                return false;

            return TryLoadCachedThumbnailFromDisk(
                _sharedCacheIndex,
                _sharedDiskCacheDirectory,
                filePath,
                size,
                fileLength,
                lastWriteTicksUtc,
                out thumbnail,
                out cacheFileName);
        }

        private static bool TryLoadCachedThumbnailFromDisk(
            ThumbnailCacheIndex cacheIndex,
            string cacheDirectory,
            string filePath,
            Size size,
            long fileLength,
            long lastWriteTicksUtc,
            out Bitmap thumbnail,
            out string cacheFileName)
        {
            thumbnail = null!;
            cacheFileName = string.Empty;

            if (cacheIndex.TryGetCacheFileName(filePath, fileLength, lastWriteTicksUtc, size, out var indexedCacheFileName))
            {
                if (TryLoadBitmapFromCacheFile(cacheDirectory, indexedCacheFileName, out thumbnail))
                {
                    cacheFileName = indexedCacheFileName;
                    return true;
                }

                cacheIndex.Remove(filePath, fileLength, lastWriteTicksUtc, size);
                TryDeleteFile(Path.Combine(cacheDirectory, indexedCacheFileName));
            }

            if (!TryBuildCacheFileName(filePath, size, fileLength, lastWriteTicksUtc, out var deterministicCacheFileName))
                return false;

            if (!TryLoadBitmapFromCacheFile(cacheDirectory, deterministicCacheFileName, out thumbnail))
                return false;

            cacheIndex.Upsert(filePath, fileLength, lastWriteTicksUtc, size, deterministicCacheFileName);
            cacheFileName = deterministicCacheFileName;
            return true;
        }

        private static bool TryLoadBitmapFromCacheFile(string cacheDirectory, string cacheFileName, out Bitmap thumbnail)
        {
            thumbnail = null!;
            if (string.IsNullOrWhiteSpace(cacheFileName) || string.IsNullOrWhiteSpace(cacheDirectory))
                return false;

            var cachePath = Path.Combine(cacheDirectory, cacheFileName);
            if (!File.Exists(cachePath))
                return false;

            if (TryLoadBitmapFromPath(cachePath, out thumbnail))
                return true;

            TryDeleteFile(cachePath);
            return false;
        }

        private static bool TryLoadBitmapFromPath(string cachePath, out Bitmap thumbnail)
        {
            thumbnail = null!;
            try
            {
                using var stream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
                thumbnail = new Bitmap(image);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                File.Delete(path);
            }
            catch
            {
                // Ignore cleanup failures.
            }
        }

        private string SaveCachedThumbnailToLocalDisk(
            string filePath,
            Bitmap thumbnail,
            Size size,
            long fileLength,
            long lastWriteTicksUtc)
        {
            if (!TryBuildCacheFileName(filePath, size, fileLength, lastWriteTicksUtc, out var cacheFileName))
                return string.Empty;

            try
            {
                var cachePath = Path.Combine(_localDiskCacheDirectory, cacheFileName);
                var cacheFolder = Path.GetDirectoryName(cachePath);
                if (!string.IsNullOrWhiteSpace(cacheFolder))
                    Directory.CreateDirectory(cacheFolder);

                using var stream = new FileStream(cachePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                thumbnail.Save(stream, ImageFormat.Png);
                _localCacheIndex.Upsert(filePath, fileLength, lastWriteTicksUtc, size, cacheFileName);
                return cacheFileName;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void MirrorThumbnailToSharedAsync(
            string cacheFileName,
            string filePath,
            Size size,
            long fileLength,
            long lastWriteTicksUtc)
        {
            if (!_hasSharedCache || _sharedCacheIndex == null || string.IsNullOrWhiteSpace(cacheFileName))
                return;

            if (!_mirrorInFlight.TryAdd(cacheFileName, 0))
                return;

            _ = Task.Run(() =>
            {
                try
                {
                    var localPath = Path.Combine(_localDiskCacheDirectory, cacheFileName);
                    if (!File.Exists(localPath))
                        return;

                    var sharedPath = Path.Combine(_sharedDiskCacheDirectory, cacheFileName);
                    if (!IsCacheFileMarkedSynced(cacheFileName) || !File.Exists(sharedPath))
                    {
                        var sharedFolder = Path.GetDirectoryName(sharedPath);
                        if (!string.IsNullOrWhiteSpace(sharedFolder))
                            Directory.CreateDirectory(sharedFolder);

                        File.Copy(localPath, sharedPath, overwrite: true);
                    }

                    _sharedCacheIndex.Upsert(filePath, fileLength, lastWriteTicksUtc, size, cacheFileName);
                    MarkCacheFileSynced(cacheFileName, pulled: false, pushed: true);
                }
                catch
                {
                    // Ignore shared cache mirror failures.
                }
                finally
                {
                    _mirrorInFlight.TryRemove(cacheFileName, out _);
                }
            });
        }

        private bool IsCacheFileMarkedSynced(string cacheFileName)
        {
            lock (_syncStateLock)
                return _syncState.SyncedFiles.ContainsKey(cacheFileName);
        }

        private void MarkCacheFileSynced(string cacheFileName, bool pulled, bool pushed)
        {
            if (string.IsNullOrWhiteSpace(cacheFileName))
                return;

            lock (_syncStateLock)
            {
                _syncState.SyncedFiles[cacheFileName] = DateTime.UtcNow.Ticks;
                if (pulled)
                    _syncState.LastPullUtc = DateTime.UtcNow.ToString("O");
                if (pushed)
                    _syncState.LastPushUtc = DateTime.UtcNow.ToString("O");

                TrimSyncStateUnsafe();
                _syncStatePendingChanges++;

                if (_syncStatePendingChanges >= SyncStateFlushThreshold
                    || DateTime.UtcNow - _lastSyncStateSaveUtc >= SyncStateMaxFlushDelay)
                {
                    SaveSyncStateUnsafe();
                }
            }
        }

        private CacheSyncState LoadSyncState(string sharedCachePath)
        {
            var normalizedSharedPath = NormalizeForComparison(sharedCachePath);
            try
            {
                if (!File.Exists(_syncStateFilePath))
                    return CreateAndPersistInitialSyncState(sharedCachePath);

                var json = File.ReadAllText(_syncStateFilePath);
                var loaded = JsonSerializer.Deserialize<CacheSyncState>(json) ?? new CacheSyncState();
                loaded.SyncedFiles = loaded.SyncedFiles == null
                    ? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, long>(loaded.SyncedFiles, StringComparer.OrdinalIgnoreCase);

                if (!string.Equals(NormalizeForComparison(loaded.SharedCachePath), normalizedSharedPath, StringComparison.OrdinalIgnoreCase))
                {
                    loaded.SharedCachePath = sharedCachePath ?? string.Empty;
                    loaded.LastPullUtc = string.Empty;
                    loaded.LastPushUtc = string.Empty;
                    loaded.SyncedFiles.Clear();
                    PersistSyncStateToDisk(loaded);
                }

                return loaded;
            }
            catch
            {
                return CreateAndPersistInitialSyncState(sharedCachePath);
            }
        }

        private CacheSyncState CreateAndPersistInitialSyncState(string sharedCachePath)
        {
            var state = new CacheSyncState
            {
                SharedCachePath = sharedCachePath ?? string.Empty,
                LastPullUtc = string.Empty,
                LastPushUtc = string.Empty,
                SyncedFiles = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            };
            PersistSyncStateToDisk(state);
            return state;
        }

        private void TrimSyncStateUnsafe()
        {
            if (_syncState.SyncedFiles.Count <= MaxSyncedFileEntries)
                return;

            var removeCount = _syncState.SyncedFiles.Count - MaxSyncedFileEntries;
            foreach (var key in _syncState.SyncedFiles
                         .OrderBy(pair => pair.Value)
                         .Take(removeCount)
                         .Select(pair => pair.Key)
                         .ToList())
            {
                _syncState.SyncedFiles.Remove(key);
            }
        }

        private void SaveSyncStateUnsafe()
        {
            PersistSyncStateToDisk(_syncState);
            _syncStatePendingChanges = 0;
            _lastSyncStateSaveUtc = DateTime.UtcNow;
        }

        private void PersistSyncStateToDisk(CacheSyncState state)
        {
            try
            {
                var folder = Path.GetDirectoryName(_syncStateFilePath);
                if (!string.IsNullOrWhiteSpace(folder))
                    Directory.CreateDirectory(folder);

                var json = JsonSerializer.Serialize(state, SyncStateJsonOptions);
                File.WriteAllText(_syncStateFilePath, json, Encoding.UTF8);
            }
            catch
            {
                // Ignore sync-state write failures.
            }
        }

        private static string ResolveLocalCacheDirectory(string configuredDirectory)
        {
            if (!string.IsNullOrWhiteSpace(configuredDirectory))
                return NormalizeCacheDirectory(configuredDirectory);

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Replica",
                "ThumbnailCache",
                "PdfPreviewCache");
        }

        private static string NormalizeCacheDirectory(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : Path.GetFullPath(path.Trim())
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool ArePathsEqual(string leftPath, string rightPath)
        {
            return string.Equals(
                NormalizeForComparison(leftPath),
                NormalizeForComparison(rightPath),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeForComparison(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return path.Trim()
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .TrimEnd(Path.DirectorySeparatorChar);
        }

        private static bool TryBuildCacheFileName(string filePath, Size size, long fileLength, long lastWriteTicksUtc, out string cacheFileName)
        {
            cacheFileName = string.Empty;
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            try
            {
                var source = string.Join(
                    "|",
                    filePath,
                    fileLength.ToString(CultureInfo.InvariantCulture),
                    lastWriteTicksUtc.ToString(CultureInfo.InvariantCulture),
                    size.Width.ToString(CultureInfo.InvariantCulture),
                    size.Height.ToString(CultureInfo.InvariantCulture),
                    "pdf-preview-v3");
                var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
                var hash = Convert.ToHexString(hashBytes);
                cacheFileName = $"{hash}.png";
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Bitmap? TryRenderPdfThumbnail(string pdfPath, Size targetSize)
        {
            try
            {
                using var document = PdfDocument.Load(pdfPath);
                if (document.PageCount <= 0)
                    return null;

                var pageSize = document.PageSizes.Count > 0 ? document.PageSizes[0] : new SizeF(1f, 1f);
                var pageWidth = Math.Max(1f, pageSize.Width);
                var pageHeight = Math.Max(1f, pageSize.Height);
                var targetMaxSide = Math.Max(Math.Max(targetSize.Width, targetSize.Height), 1);
                var renderBasePixels = Math.Clamp(targetMaxSide * 3, 360, 900);
                int renderWidth;
                int renderHeight;

                if (pageWidth >= pageHeight)
                {
                    renderWidth = renderBasePixels;
                    renderHeight = Math.Max(200, (int)Math.Round(renderBasePixels * (pageHeight / pageWidth)));
                }
                else
                {
                    renderHeight = renderBasePixels;
                    renderWidth = Math.Max(200, (int)Math.Round(renderBasePixels * (pageWidth / pageHeight)));
                }

                const int renderDpi = 72;
                using var rendered = document.Render(
                    page: 0,
                    width: renderWidth,
                    height: renderHeight,
                    dpiX: renderDpi,
                    dpiY: renderDpi,
                    flags: PdfRenderFlags.Annotations);

                if (rendered == null)
                    return null;

                return ComposeThumbnailCanvas(rendered, targetSize);
            }
            catch
            {
                return null;
            }
        }

        private static Bitmap ComposeThumbnailCanvas(Image source, Size targetSize)
        {
            var canvas = new Bitmap(targetSize.Width, targetSize.Height);
            using var graphics = Graphics.FromImage(canvas);

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(Color.FromArgb(248, 250, 253));

            var frameRect = new Rectangle(2, 2, targetSize.Width - 4, targetSize.Height - 4);
            using var frameBackBrush = new SolidBrush(Color.White);
            graphics.FillRectangle(frameBackBrush, frameRect);
            using var frameBorderPen = new Pen(Color.FromArgb(205, 212, 225));
            graphics.DrawRectangle(frameBorderPen, frameRect);

            var imageRect = Rectangle.Inflate(frameRect, -4, -4);
            var drawRect = FitImageToBounds(source.Size, imageRect);
            graphics.DrawImage(source, drawRect);

            return canvas;
        }

        private static Rectangle FitImageToBounds(Size sourceSize, Rectangle targetBounds)
        {
            if (sourceSize.Width <= 0 || sourceSize.Height <= 0 || targetBounds.Width <= 0 || targetBounds.Height <= 0)
                return targetBounds;

            var widthRatio = targetBounds.Width / (double)sourceSize.Width;
            var heightRatio = targetBounds.Height / (double)sourceSize.Height;
            var scale = Math.Min(widthRatio, heightRatio);
            if (scale <= 0)
                scale = 1d;

            var drawWidth = Math.Max(1, (int)Math.Round(sourceSize.Width * scale));
            var drawHeight = Math.Max(1, (int)Math.Round(sourceSize.Height * scale));
            var drawX = targetBounds.Left + (targetBounds.Width - drawWidth) / 2;
            var drawY = targetBounds.Top + (targetBounds.Height - drawHeight) / 2;

            return new Rectangle(drawX, drawY, drawWidth, drawHeight);
        }

        private static bool IsPdfPath(string path)
        {
            return string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolvePath(object key)
        {
            if (key is string path)
                return path;

            if (key is ImageListViewItem item && !string.IsNullOrWhiteSpace(item.FilePath))
                return item.FilePath;

            return string.Empty;
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : Path.GetFullPath(path).Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsPdfReadyForPreview(string filePath)
        {
            return TryGetFileMetadata(filePath, out var fileLength, out _) && fileLength > 0;
        }

        private static bool TryGetFileMetadata(string filePath, out long fileLength, out long lastWriteTicksUtc)
        {
            fileLength = 0;
            lastWriteTicksUtc = 0;

            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists)
                    return false;

                fileLength = fileInfo.Length;
                lastWriteTicksUtc = fileInfo.LastWriteTimeUtc.Ticks;
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (System.Security.SecurityException)
            {
                return false;
            }
        }

        private sealed record CachedThumbnail(long FileLength, long LastWriteTicksUtc, Size Size, Bitmap Bitmap);

        private sealed class CacheSyncState
        {
            public string SharedCachePath { get; set; } = string.Empty;
            public string LastPullUtc { get; set; } = string.Empty;
            public string LastPushUtc { get; set; } = string.Empty;
            public Dictionary<string, long> SyncedFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
