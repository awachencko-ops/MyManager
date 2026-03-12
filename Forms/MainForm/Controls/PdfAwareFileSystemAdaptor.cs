using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Manina.Windows.Forms;
using Manina.Windows.Forms.ImageListViewItemAdaptors;
using PdfiumViewer;

namespace MyManager
{
    internal sealed class PdfAwareFileSystemAdaptor : FileSystemAdaptor
    {
        private readonly ConcurrentDictionary<string, CachedThumbnail> _pdfCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _cacheSync = new();
        private readonly string _diskCacheDirectory;

        public PdfAwareFileSystemAdaptor(string diskCacheDirectory)
        {
            _diskCacheDirectory = string.IsNullOrWhiteSpace(diskCacheDirectory)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MyManager",
                    "ThumbnailCache",
                    "PdfPreviewCache")
                : diskCacheDirectory;

            Directory.CreateDirectory(_diskCacheDirectory);
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

            if (TryLoadCachedThumbnailFromDisk(normalizedPath, size, fileLength, lastWriteTicksUtc, out var diskCached))
            {
                StoreCachedThumbnail(normalizedPath, diskCached, size, fileLength, lastWriteTicksUtc);
                return diskCached;
            }

            var rendered = TryRenderPdfThumbnail(normalizedPath, size);
            if (rendered == null)
                return base.GetThumbnail(key, size, useEmbeddedThumbnails, useExifOrientation);

            StoreCachedThumbnail(normalizedPath, rendered, size, fileLength, lastWriteTicksUtc);
            SaveCachedThumbnailToDisk(normalizedPath, rendered, size, fileLength, lastWriteTicksUtc);
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
                    // GDI+ bitmaps are not thread-safe; if a cached object becomes unusable, evict it and re-render.
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

        private bool TryLoadCachedThumbnailFromDisk(
            string filePath,
            Size size,
            long fileLength,
            long lastWriteTicksUtc,
            out Bitmap thumbnail)
        {
            thumbnail = null!;
            if (!TryGetDiskCachePath(filePath, size, fileLength, lastWriteTicksUtc, out var cachePath))
                return false;

            if (!File.Exists(cachePath))
                return false;

            try
            {
                using var stream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
                thumbnail = new Bitmap(image);
                return true;
            }
            catch
            {
                try
                {
                    File.Delete(cachePath);
                }
                catch
                {
                    // Ignore cache cleanup failures.
                }

                return false;
            }
        }

        private void SaveCachedThumbnailToDisk(
            string filePath,
            Bitmap thumbnail,
            Size size,
            long fileLength,
            long lastWriteTicksUtc)
        {
            if (!TryGetDiskCachePath(filePath, size, fileLength, lastWriteTicksUtc, out var cachePath))
                return;

            try
            {
                var cacheFolder = Path.GetDirectoryName(cachePath);
                if (!string.IsNullOrWhiteSpace(cacheFolder))
                    Directory.CreateDirectory(cacheFolder);

                using var stream = new FileStream(cachePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                thumbnail.Save(stream, ImageFormat.Png);
            }
            catch
            {
                // Ignore cache write failures.
            }
        }

        private bool TryGetDiskCachePath(string filePath, Size size, long fileLength, long lastWriteTicksUtc, out string cachePath)
        {
            cachePath = string.Empty;
            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(_diskCacheDirectory))
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
                    "pdf-preview-v2");
                var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
                var hash = Convert.ToHexString(hashBytes);
                cachePath = Path.Combine(_diskCacheDirectory, $"{hash}.png");
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

                const int renderDpi = 110;
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
    }
}
