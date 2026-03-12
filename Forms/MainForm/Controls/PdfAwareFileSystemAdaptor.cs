using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using Manina.Windows.Forms;
using Manina.Windows.Forms.ImageListViewItemAdaptors;
using PdfiumViewer;

namespace MyManager
{
    internal sealed class PdfAwareFileSystemAdaptor : FileSystemAdaptor
    {
        private readonly ConcurrentDictionary<string, CachedThumbnail> _pdfCache = new(StringComparer.OrdinalIgnoreCase);

        public override Image GetThumbnail(object key, Size size, UseEmbeddedThumbnails useEmbeddedThumbnails, bool useExifOrientation)
        {
            if (key is not string path || !IsPdfPath(path) || !File.Exists(path))
                return base.GetThumbnail(key, size, useEmbeddedThumbnails, useExifOrientation);

            var normalizedPath = NormalizePath(path);
            if (TryGetCachedThumbnail(normalizedPath, size, out var cached))
                return cached;

            var rendered = TryRenderPdfThumbnail(normalizedPath, size);
            if (rendered == null)
                return base.GetThumbnail(key, size, useEmbeddedThumbnails, useExifOrientation);

            StoreCachedThumbnail(normalizedPath, rendered, size);
            return rendered;
        }

        private bool TryGetCachedThumbnail(string filePath, Size size, out Image image)
        {
            image = null!;
            if (!_pdfCache.TryGetValue(filePath, out var cached))
                return false;

            var fileInfo = new FileInfo(filePath);
            var isValid =
                cached.FileLength == fileInfo.Length
                && cached.LastWriteTicksUtc == fileInfo.LastWriteTimeUtc.Ticks
                && cached.Size == size;

            if (!isValid)
            {
                if (_pdfCache.TryRemove(filePath, out var removed))
                    removed.Bitmap.Dispose();
                return false;
            }

            image = (Image)cached.Bitmap.Clone();
            return true;
        }

        private void StoreCachedThumbnail(string filePath, Bitmap thumbnail, Size size)
        {
            var fileInfo = new FileInfo(filePath);
            var cacheValue = new CachedThumbnail(
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc.Ticks,
                size,
                (Bitmap)thumbnail.Clone());

            if (_pdfCache.TryGetValue(filePath, out var existing))
                existing.Bitmap.Dispose();

            _pdfCache[filePath] = cacheValue;
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
                const int renderBasePixels = 1200;
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

                const int renderDpi = 150;
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

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : Path.GetFullPath(path).Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private sealed record CachedThumbnail(long FileLength, long LastWriteTicksUtc, Size Size, Bitmap Bitmap);
    }
}
