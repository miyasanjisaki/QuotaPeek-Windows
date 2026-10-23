using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QuotaPeek.Windows
{
    internal enum Tone
    {
        Neutral,
        Good,
        Warning,
        Danger,
        Accent
    }

    internal sealed class ThemePalette
    {
        public bool Dark { get; private set; }
        public Color Window { get; private set; }
        public Color Surface { get; private set; }
        public Color SurfaceHover { get; private set; }
        public Color Input { get; private set; }
        public Color Text { get; private set; }
        public Color Muted { get; private set; }
        public Color Subtle { get; private set; }
        public Color Border { get; private set; }
        public Color BorderStrong { get; private set; }
        public Color Track { get; private set; }
        public Color Accent { get; private set; }
        public Color AccentHover { get; private set; }
        public Color AccentAlt { get; private set; }
        public Color OnAccent { get; private set; }
        public Color Good { get; private set; }
        public Color Warning { get; private set; }
        public Color Danger { get; private set; }
        public Color Stale { get; private set; }

        public static ThemePalette For(string appearance)
        {
            var dark = appearance == "dark" || (appearance == "system" && SystemUsesDarkMode());
            return dark
                ? new ThemePalette
                {
                    Dark = true,
                    Window = Color.FromArgb(22, 23, 27),
                    Surface = Color.FromArgb(32, 34, 40),
                    SurfaceHover = Color.FromArgb(41, 43, 51),
                    Input = Color.FromArgb(26, 27, 32),
                    Text = Color.FromArgb(236, 237, 241),
                    Muted = Color.FromArgb(156, 160, 172),
                    Subtle = Color.FromArgb(112, 116, 128),
                    Border = Color.FromArgb(47, 49, 58),
                    BorderStrong = Color.FromArgb(66, 69, 80),
                    Track = Color.FromArgb(50, 52, 62),
                    Accent = Color.FromArgb(139, 124, 246),
                    AccentHover = Color.FromArgb(156, 143, 248),
                    AccentAlt = Color.FromArgb(94, 160, 255),
                    OnAccent = Color.White,
                    Good = Color.FromArgb(52, 199, 154),
                    Warning = Color.FromArgb(245, 170, 50),
                    Danger = Color.FromArgb(240, 100, 100),
                    Stale = Color.FromArgb(122, 126, 138)
                }
                : new ThemePalette
                {
                    Dark = false,
                    Window = Color.FromArgb(243, 244, 247),
                    Surface = Color.White,
                    SurfaceHover = Color.FromArgb(247, 247, 250),
                    Input = Color.White,
                    Text = Color.FromArgb(26, 28, 34),
                    Muted = Color.FromArgb(100, 105, 119),
                    Subtle = Color.FromArgb(150, 155, 168),
                    Border = Color.FromArgb(226, 228, 234),
                    BorderStrong = Color.FromArgb(204, 207, 216),
                    Track = Color.FromArgb(234, 236, 241),
                    Accent = Color.FromArgb(99, 82, 214),
                    AccentHover = Color.FromArgb(84, 67, 198),
                    AccentAlt = Color.FromArgb(56, 132, 240),
                    OnAccent = Color.White,
                    Good = Color.FromArgb(18, 160, 124),
                    Warning = Color.FromArgb(214, 128, 10),
                    Danger = Color.FromArgb(218, 62, 62),
                    Stale = Color.FromArgb(150, 154, 165)
                };
        }

        public Color ToneColor(Tone tone)
        {
            switch (tone)
            {
                case Tone.Good: return Good;
                case Tone.Warning: return Warning;
                case Tone.Danger: return Danger;
                case Tone.Accent: return Accent;
                default: return Subtle;
            }
        }

        private static bool SystemUsesDarkMode()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return value is int && (int)value == 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    internal interface IThemed
    {
        void ApplyPalette(ThemePalette palette);
    }

    internal static class Glyphs
    {
        public const string Refresh = "\uE72C";
        public const string Settings = "\uE713";
        public const string Close = "\uE8BB";
        public const string Power = "\uE7E8";
        public const string Clock = "\uE823";
        public const string Warning = "\uE7BA";
        public const string Info = "\uE946";
        public const string Error = "\uE783";
        public const string Add = "\uE710";
        public const string Delete = "\uE74D";
        public const string Up = "\uE74A";
        public const string Down = "\uE74B";
        public const string Chevron = "\uE76C";
        public const string Pause = "\uE769";
        public const string Plug = "\uE774";
        public const string Save = "\uE74E";
        public const string Grid = "\uE8A9";
    }

    internal static class UiFonts
    {
        private static readonly string[] InstalledFamilies = FontFamily.Families.Select(family => family.Name).ToArray();

        public static readonly Font Body = new Font("Microsoft YaHei UI", 9f);
        public static readonly Font BodyBold = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        public static readonly Font Small = new Font("Microsoft YaHei UI", 8.25f);
        public static readonly Font Title = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold);
        public static readonly Font Heading = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
        public static readonly Font Number = new Font(Family("Segoe UI Semibold", "Segoe UI"), 22f);
        public static readonly Font NumberSuffix = new Font(Family("Segoe UI Semibold", "Segoe UI"), 11f);
        public static readonly Font NumberSmall = new Font(Family("Segoe UI Semibold", "Segoe UI"), 9.5f);
        public static readonly string IconFamily = Family("Segoe Fluent Icons", "Segoe MDL2 Assets");
        public static readonly Font Icon = new Font(IconFamily, 10.5f);
        public static readonly Font IconSmall = new Font(IconFamily, 8.5f);
        public static readonly Font IconLarge = new Font(IconFamily, 14f);

        private static string Family(string preferred, string fallback)
        {
            return InstalledFamilies.Contains(preferred) ? preferred : fallback;
        }
    }

    internal static class UiHelpers
    {
        private static readonly float DpiScale = ReadDpiScale();

        public static int S(int pixels)
        {
            return (int)Math.Round(pixels * DpiScale);
        }

        public static float S(float pixels)
        {
            return pixels * DpiScale;
        }

        private static float ReadDpiScale()
        {
            try
            {
                using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
                {
                    return Math.Max(1f, graphics.DpiX / 96f);
                }
            }
            catch
            {
                return 1f;
            }
        }

        public static string Percent(double value)
        {
            return Number(value) + "%";
        }

        public static string Number(double value)
        {
            return value.ToString(value == Math.Truncate(value) ? "0" : "0.#", CultureInfo.CurrentCulture);
        }

        public static string ResetLabel(DateTimeOffset? reset, DateTimeOffset now)
        {
            if (!reset.HasValue) return "重置时间未知";
            var minutes = (int)Math.Ceiling((reset.Value - now).TotalMinutes);
            if (minutes <= 0) return "等待重置更新";
            if (minutes < 60) return minutes + "分钟后重置";
            if (minutes < 1440) return (minutes / 60) + "小时" + (minutes % 60) + "分后重置";
            return (minutes / 1440) + "天" + ((minutes % 1440) / 60) + "小时后重置";
        }

        public static string AgeLabel(DateTimeOffset date, DateTimeOffset now)
        {
            var seconds = Math.Max(0, (int)(now - date).TotalSeconds);
            if (seconds < 60) return "刚刚更新";
            if (seconds < 3600) return (seconds / 60) + " 分钟前";
            return (seconds / 3600) + " 小时前";
        }

        public static Tone QuotaTone(double usedPercent, int warningPercent, bool stale)
        {
            if (stale) return Tone.Neutral;
            if (usedPercent >= 100) return Tone.Danger;
            if (usedPercent >= warningPercent) return Tone.Warning;
            return Tone.Good;
        }

        public static Color QuotaColor(double usedPercent, int warningPercent, bool stale, ThemePalette palette)
        {
            return stale ? palette.Stale : palette.ToneColor(QuotaTone(usedPercent, warningPercent, false));
        }

        public static Tone StatusTone(string status)
        {
            switch (status)
            {
                case "额度充足": return Tone.Good;
                case "留意用量":
                case "数据已过期":
                case "部分异常": return Tone.Warning;
                case "额度耗尽":
                case "连接异常":
                case "配置异常": return Tone.Danger;
                case "更新中": return Tone.Accent;
                default: return Tone.Neutral;
            }
        }

        public static Color Blend(Color from, Color to, double amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromArgb(
                (int)Math.Round(from.R + (to.R - from.R) * amount),
                (int)Math.Round(from.G + (to.G - from.G) * amount),
                (int)Math.Round(from.B + (to.B - from.B) * amount));
        }

        public static void ApplyTheme(Control root, ThemePalette palette)
        {
            Theme(root, palette);
            foreach (Control child in root.Controls)
            {
                ApplyTheme(child, palette);
            }
        }

        private static void Theme(Control control, ThemePalette palette)
        {
            var themed = control as IThemed;
            if (themed != null)
            {
                themed.ApplyPalette(palette);
                return;
            }
            if (control is Form || control is TabPage || control is TabControl)
            {
                control.BackColor = palette.Window;
                control.ForeColor = palette.Text;
            }
            else if (control is TextBoxBase)
            {
                control.BackColor = palette.Input;
                control.ForeColor = palette.Text;
            }
            else if (control is ComboBox)
            {
                control.BackColor = palette.Input;
                control.ForeColor = palette.Text;
                ((ComboBox)control).FlatStyle = FlatStyle.Flat;
            }
            else if (control is ListBox)
            {
                control.BackColor = palette.Surface;
                control.ForeColor = palette.Text;
            }
            else if (control is Label)
            {
                var role = control.Tag as string;
                control.ForeColor = role == "muted" ? palette.Muted : role == "subtle" ? palette.Subtle : palette.Text;
            }
            else
            {
                control.ForeColor = palette.Text;
            }
        }
    }

    internal static class Draw
    {
        public static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            var diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            if (diameter <= 0.5f)
            {
                path.AddRectangle(rect);
                return path;
            }
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void Fill(Graphics graphics, Color color, RectangleF rect, float radius)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            using (var path = Rounded(rect, radius))
            using (var brush = new SolidBrush(color))
            {
                graphics.FillPath(brush, path);
            }
        }

        public static void Stroke(Graphics graphics, Color color, RectangleF rect, float radius, float width = 1f)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            var inset = width / 2f;
            var inner = new RectangleF(rect.X + inset, rect.Y + inset, rect.Width - width, rect.Height - width);
            using (var path = Rounded(inner, radius))
            using (var pen = new Pen(color, width))
            {
                graphics.DrawPath(pen, path);
            }
        }

        public static void Card(Graphics graphics, ThemePalette palette, Rectangle bounds, Color fill, Color border, float radius)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            Fill(graphics, fill, rect, radius);
            Stroke(graphics, border, rect, radius);
        }

        public static void Bar(Graphics graphics, Color track, Color fill, RectangleF rect, double percent)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var radius = rect.Height / 2f;
            Fill(graphics, track, rect, radius);
            var value = (float)(Math.Max(0, Math.Min(100, percent)) / 100d);
            if (value <= 0) return;
            var width = Math.Max(rect.Height, rect.Width * value);
            Fill(graphics, fill, new RectangleF(rect.X, rect.Y, width, rect.Height), radius);
        }

        public static void Dot(Graphics graphics, Color color, PointF center, float diameter, bool halo = true)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (halo)
            {
                var outer = diameter * 2f;
                using (var brush = new SolidBrush(Color.FromArgb(48, color)))
                {
                    graphics.FillEllipse(brush, center.X - outer / 2, center.Y - outer / 2, outer, outer);
                }
            }
            using (var brush = new SolidBrush(color))
            {
                graphics.FillEllipse(brush, center.X - diameter / 2, center.Y - diameter / 2, diameter, diameter);
            }
        }

        public static void Text(Graphics graphics, string text, Font font, Rectangle bounds, Color color,
            TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter)
        {
            TextRenderer.DrawText(graphics, text ?? "", font, bounds, color,
                flags | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }

        public static void Glyph(Graphics graphics, string glyph, Font font, Rectangle bounds, Color color,
            TextFormatFlags alignment = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter)
        {
            TextRenderer.DrawText(graphics, glyph ?? "", font, bounds, color,
                alignment | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoClipping);
        }

        public static void WrappedText(Graphics graphics, string text, Font font, Rectangle bounds, Color color)
        {
            TextRenderer.DrawText(graphics, text ?? "", font, bounds, color,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix |
                TextFormatFlags.TextBoxControl);
        }

        public static int WrappedHeight(string text, Font font, int width)
        {
            var size = TextRenderer.MeasureText(text ?? "", font, new Size(Math.Max(1, width), int.MaxValue),
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix |
                TextFormatFlags.TextBoxControl);
            return size.Height;
        }

        // TextRenderer ignores NoPadding unless it measures against a device context, so keep one for the UI thread.
        private static readonly Graphics MeasureContext = Graphics.FromHwnd(IntPtr.Zero);

        public static Size Measure(string text, Font font)
        {
            return TextRenderer.MeasureText(MeasureContext, text ?? "", font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        public static void Logo(Graphics graphics, ThemePalette palette, RectangleF rect)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Rounded(rect, rect.Width * 0.28f))
            using (var brush = new LinearGradientBrush(rect, palette.Accent, palette.AccentAlt, 45f))
            {
                graphics.FillPath(brush, path);
            }
            var inset = rect.Width * 0.24f;
            var ring = new RectangleF(rect.X + inset, rect.Y + inset, rect.Width - inset * 2, rect.Height - inset * 2);
            var stroke = Math.Max(2f, rect.Width * 0.1f);
            using (var track = new Pen(Color.FromArgb(80, Color.White), stroke))
            using (var arc = new Pen(Color.White, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                graphics.DrawEllipse(track, ring);
                graphics.DrawArc(arc, ring, 135, 200);
            }
            var dot = rect.Width * 0.12f;
            using (var brush = new SolidBrush(Color.White))
            {
                graphics.FillEllipse(brush, rect.X + rect.Width / 2 - dot / 2, rect.Y + rect.Height / 2 - dot / 2, dot, dot);
            }
        }
    }

    internal static class WindowEffects
    {
        private const int DwmUseImmersiveDarkModeLegacy = 19;
        private const int DwmUseImmersiveDarkMode = 20;
        private const int DwmWindowCornerPreference = 33;
        private const int DwmBorderColor = 34;
        private const int DwmCaptionColor = 35;
        private const int DwmTextColor = 36;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        public static void Apply(Form form, ThemePalette palette, bool roundedPopup)
        {
            if (form == null || !form.IsHandleCreated) return;
            try
            {
                var dark = palette.Dark ? 1 : 0;
                if (DwmSetWindowAttribute(form.Handle, DwmUseImmersiveDarkMode, ref dark, 4) != 0)
                {
                    DwmSetWindowAttribute(form.Handle, DwmUseImmersiveDarkModeLegacy, ref dark, 4);
                }
                if (roundedPopup)
                {
                    var round = 2;
                    DwmSetWindowAttribute(form.Handle, DwmWindowCornerPreference, ref round, 4);
                }
                var border = ColorRef(palette.BorderStrong);
                DwmSetWindowAttribute(form.Handle, DwmBorderColor, ref border, 4);
                var caption = ColorRef(palette.Window);
                DwmSetWindowAttribute(form.Handle, DwmCaptionColor, ref caption, 4);
                var text = ColorRef(palette.Text);
                DwmSetWindowAttribute(form.Handle, DwmTextColor, ref text, 4);
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        public static void BeginDrag(Form form)
        {
            ReleaseCapture();
            SendMessage(form.Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        }

        /// <summary>Note: re-enabling redraw also sets WS_VISIBLE, so only call this on a window that is shown.</summary>
        public static void SetRedraw(Control control, bool enabled)
        {
            if (control.IsHandleCreated) SendMessage(control.Handle, 0x000B, (IntPtr)(enabled ? 1 : 0), IntPtr.Zero);
        }

        private static int ColorRef(Color color)
        {
            return color.R | (color.G << 8) | (color.B << 16);
        }
    }
}
