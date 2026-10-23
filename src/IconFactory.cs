using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace QuotaPeek.Windows
{
    internal static class IconFactory
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        public static Icon CreateAppIcon()
        {
            using (var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                Draw.Logo(graphics, ThemePalette.For("dark"), new RectangleF(1, 1, 30, 30));
                return ToIcon(bitmap);
            }
        }

        private static Icon ToIcon(Bitmap bitmap)
        {
            var handle = bitmap.GetHicon();
            try
            {
                using (var icon = Icon.FromHandle(handle))
                {
                    return (Icon)icon.Clone();
                }
            }
            finally
            {
                DestroyIcon(handle);
            }
        }

        public static Icon Create(double? displayedPercent, double? usedPercent, int warningPercent, bool hasError)
        {
            using (var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                graphics.Clear(Color.Transparent);

                var color = Color.FromArgb(48, 164, 155);
                if (hasError) color = Color.FromArgb(211, 82, 76);
                else if (usedPercent.HasValue && usedPercent.Value >= 100) color = Color.FromArgb(211, 82, 76);
                else if (usedPercent.HasValue && usedPercent.Value >= warningPercent) color = Color.FromArgb(227, 151, 46);

                using (var shadow = new SolidBrush(Color.FromArgb(45, 0, 0, 0)))
                using (var fill = new SolidBrush(color))
                using (var border = new Pen(Color.FromArgb(80, Color.White), 1f))
                {
                    graphics.FillEllipse(shadow, 3, 4, 27, 27);
                    graphics.FillEllipse(fill, 2, 2, 27, 27);
                    graphics.DrawEllipse(border, 2.5f, 2.5f, 26, 26);
                }

                var text = hasError ? "!" : displayedPercent.HasValue
                    ? Math.Round(displayedPercent.Value, MidpointRounding.AwayFromZero).ToString("0")
                    : "–";
                var fontSize = text.Length >= 3 ? 8f : text.Length == 2 ? 9.5f : 12f;
                using (var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Point))
                using (var brush = new SolidBrush(Color.White))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    graphics.DrawString(text, font, brush, new RectangleF(1, 1, 29, 29), format);
                }

                return ToIcon(bitmap);
            }
        }
    }
}
