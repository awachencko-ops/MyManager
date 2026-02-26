using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System;
using System.IO;

namespace MyManager
{
    public static class PdfWatermark
    {
        private static double MmToPt(double mm) => mm * 72.0 / 25.4;

        public static void Apply(OrderData order, bool isVertical = false)
        {
            if (string.IsNullOrEmpty(order.PrintPath) || !File.Exists(order.PrintPath))
            {
                throw new FileNotFoundException("Файл печатного спуска не найден.");
            }

            string watermarkText = $"Заказ № {order.Id} от {order.OrderDate:dd.MM.yyyy}";

            using PdfDocument doc = PdfReader.Open(order.PrintPath, PdfDocumentOpenMode.Modify);
            XFont font = new XFont("Arial", 10, XFontStyleEx.Regular);

            foreach (PdfPage page in doc.Pages)
            {
                using XGraphics gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

                if (isVertical)
                {
                    // --- ВЕРТИКАЛЬНЫЙ (СЛЕВА) ---
                    // 1. Переносим начало координат в точку рисования (7мм слева, центр по высоте)
                    double x = MmToPt(7);
                    double y = page.Height / 2;

                    gfx.TranslateTransform(x, y);

                    // 2. Поворачиваем на -90 градусов (против часовой)
                    gfx.RotateTransform(-90);

                    // 3. Рисуем текст в новых координатах (0,0)
                    // Используем BottomCenter, чтобы текст рос "вверх" от центральной точки
                    gfx.DrawString(watermarkText, font, XBrushes.Black,
                        new XPoint(0, 0), XStringFormats.BottomCenter);
                }
                else
                {
                    // --- ГОРИЗОНТАЛЬНЫЙ (СВЕРХУ) ---
                    double x = page.Width / 2;
                    double y = MmToPt(5);

                    gfx.DrawString(watermarkText, font, XBrushes.Black,
                        new XPoint(x, y), XStringFormats.TopCenter);
                }
            }
            doc.Save(order.PrintPath);
        }
    }
}