using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QuotaPeek.Windows
{
    internal enum ButtonVariant
    {
        Primary,
        Secondary,
        Ghost,
        Chip
    }

    /// <summary>A fully owner-drawn button that keeps the keyboard and accessibility behaviour of Button.</summary>
    internal sealed class ModernButton : Button, IThemed
    {
        private ThemePalette _palette = ThemePalette.For("light");
        private ButtonVariant _variant;
        private string _glyph;
        private bool _selected;
        private bool _hover;
        private bool _pressed;

        public ModernButton(string text, ButtonVariant variant, string glyph = null)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Font = variant == ButtonVariant.Primary ? UiFonts.BodyBold : UiFonts.Body;
            Text = text ?? "";
            _variant = variant;
            _glyph = glyph;
            Cursor = Cursors.Hand;
            AutoSize = false;
            Size = GetPreferredSize(Size.Empty);
        }

        public bool IconOnly { get { return string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(_glyph); } }
        public bool Muted { get; set; }
        public int MaxTextWidth { get; set; }
        public Font GlyphFont { get; set; }

        public bool Selected
        {
            get { return _selected; }
            set { _selected = value; Invalidate(); }
        }

        public string Glyph
        {
            get { return _glyph; }
            set { _glyph = value; Invalidate(); }
        }

        public void ApplyPalette(ThemePalette palette)
        {
            _palette = palette;
            Invalidate();
        }

        public void FitToContent()
        {
            Size = GetPreferredSize(Size.Empty);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var height = UiHelpers.S(_variant == ButtonVariant.Chip ? 28 : 32);
            if (IconOnly) return new Size(height, height);
            var text = Draw.Measure(Text, Font).Width;
            if (MaxTextWidth > 0) text = Math.Min(text, MaxTextWidth);
            var glyph = string.IsNullOrEmpty(_glyph) ? 0 : UiHelpers.S(22);
            var padding = UiHelpers.S(_variant == ButtonVariant.Chip ? 12 : 14);
            return new Size(text + glyph + padding * 2, height);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) _pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var parentColor = Parent != null ? Parent.BackColor : _palette.Window;
            g.Clear(parentColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var p = _palette;
            Color fill, border, fore;
            switch (_variant)
            {
                case ButtonVariant.Primary:
                    fill = _pressed ? UiHelpers.Blend(p.Accent, Color.Black, 0.12) : _hover ? p.AccentHover : p.Accent;
                    border = fill;
                    fore = p.OnAccent;
                    break;
                case ButtonVariant.Chip:
                    if (_selected)
                    {
                        fill = p.Accent;
                        border = p.Accent;
                        fore = p.OnAccent;
                    }
                    else
                    {
                        fill = _hover ? p.SurfaceHover : p.Surface;
                        border = _hover ? p.BorderStrong : p.Border;
                        fore = Muted ? p.Muted : p.Text;
                    }
                    break;
                case ButtonVariant.Ghost:
                    fill = _pressed ? UiHelpers.Blend(parentColor, p.Text, 0.12) : _hover ? UiHelpers.Blend(parentColor, p.Text, 0.07) : parentColor;
                    border = fill;
                    fore = _hover ? p.Text : p.Muted;
                    break;
                default:
                    fill = _pressed ? UiHelpers.Blend(p.Surface, p.Text, 0.08) : _hover ? p.SurfaceHover : p.Surface;
                    border = _hover ? p.BorderStrong : p.Border;
                    fore = p.Text;
                    break;
            }
            if (!Enabled)
            {
                fill = _variant == ButtonVariant.Ghost ? parentColor : UiHelpers.Blend(fill, parentColor, 0.45);
                border = UiHelpers.Blend(border, parentColor, 0.45);
                fore = UiHelpers.Blend(fore, parentColor, 0.55);
            }

            var rect = new RectangleF(0, 0, Width, Height);
            var radius = _variant == ButtonVariant.Chip ? Height / 2f : UiHelpers.S(8f);
            Draw.Fill(g, fill, rect, radius);
            if (border != fill) Draw.Stroke(g, border, rect, radius);
            if (Focused && ShowFocusCues)
            {
                Draw.Stroke(g, Color.FromArgb(180, p.Accent), new RectangleF(1, 1, Width - 2, Height - 2), radius, UiHelpers.S(2f));
            }

            var hasGlyph = !string.IsNullOrEmpty(_glyph);
            if (IconOnly)
            {
                Draw.Glyph(g, _glyph, GlyphFont ?? UiFonts.Icon, ClientRectangle, fore);
                return;
            }
            var textWidth = Draw.Measure(Text, Font).Width;
            if (MaxTextWidth > 0) textWidth = Math.Min(textWidth, MaxTextWidth);
            var glyphWidth = hasGlyph ? UiHelpers.S(22) : 0;
            var left = (Width - textWidth - glyphWidth) / 2;
            if (hasGlyph)
            {
                Draw.Glyph(g, _glyph, UiFonts.IconSmall, new Rectangle(left, 0, UiHelpers.S(16), Height), fore);
            }
            Draw.Text(g, Text, Font, new Rectangle(left + glyphWidth, 0, textWidth + UiHelpers.S(2), Height), fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>A rounded card container whose children inherit its surface colour.</summary>
    internal class RoundedPanel : Panel, IThemed
    {
        protected ThemePalette Palette = ThemePalette.For("light");

        public RoundedPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Radius = 12;
        }

        public int Radius { get; set; }

        public virtual void ApplyPalette(ThemePalette palette)
        {
            Palette = palette;
            BackColor = palette.Surface;
            ForeColor = palette.Text;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Palette.Window);
            Draw.Card(e.Graphics, Palette, new Rectangle(0, 0, Width, Height), BackColor, Palette.Border, UiHelpers.S((float)Radius));
        }
    }

    /// <summary>A rounded frame around a borderless TextBox so inputs match the rest of the UI.</summary>
    internal sealed class InputFrame : RoundedPanel
    {
        private readonly TextBox _box;

        public InputFrame(TextBox box)
        {
            _box = box;
            Radius = 8;
            box.BorderStyle = BorderStyle.None;
            box.Dock = DockStyle.Fill;
            box.Font = UiFonts.Body;
            Padding = new Padding(UiHelpers.S(10), UiHelpers.S(8), UiHelpers.S(10), UiHelpers.S(4));
            Height = UiHelpers.S(34);
            Dock = DockStyle.Top;
            Controls.Add(box);
            box.GotFocus += delegate { Invalidate(); };
            box.LostFocus += delegate { Invalidate(); };
            Cursor = Cursors.IBeam;
            Click += delegate { _box.Focus(); };
        }

        public override void ApplyPalette(ThemePalette palette)
        {
            Palette = palette;
            BackColor = palette.Input;
            _box.BackColor = palette.Input;
            _box.ForeColor = palette.Text;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Palette.Window);
            var border = _box.Focused ? Palette.Accent : Palette.BorderStrong;
            Draw.Card(e.Graphics, Palette, new Rectangle(0, 0, Width, Height), Palette.Input, border, UiHelpers.S((float)Radius));
            if (_box.Focused)
            {
                Draw.Stroke(e.Graphics, Palette.Accent, new RectangleF(0, 0, Width, Height), UiHelpers.S((float)Radius), UiHelpers.S(1.6f));
            }
        }
    }

    /// <summary>A borderless, owner-drawn drop-down list.</summary>
    internal sealed class ModernComboBox : ComboBox, IThemed
    {
        private ThemePalette _palette = ThemePalette.For("light");

        public ModernComboBox()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            Font = UiFonts.Body;
            ItemHeight = UiHelpers.S(24);
        }

        public void ApplyPalette(ThemePalette palette)
        {
            _palette = palette;
            BackColor = palette.Input;
            ForeColor = palette.Text;
            Invalidate();
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var editPortion = (e.State & DrawItemState.ComboBoxEdit) != 0;
            var selected = (e.State & DrawItemState.Selected) != 0 && !editPortion;
            var back = selected ? _palette.Accent : _palette.Input;
            var fore = selected ? _palette.OnAccent : Enabled ? _palette.Text : _palette.Subtle;
            using (var brush = new SolidBrush(back))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }
            var bounds = new Rectangle(e.Bounds.X + UiHelpers.S(6), e.Bounds.Y, e.Bounds.Width - UiHelpers.S(8), e.Bounds.Height);
            Draw.Text(e.Graphics, Items[e.Index].ToString(), Font, bounds, fore);
        }
    }

    /// <summary>A tinted, auto-height note with a leading icon.</summary>
    internal sealed class HintLabel : CanvasControl
    {
        private bool _measuring;

        public string Glyph { get; set; }

        private int Desired(int width)
        {
            var pad = UiHelpers.S(12);
            return Draw.WrappedHeight(Text, UiFonts.Small, width - pad * 2 - UiHelpers.S(24)) + pad * 2;
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (_measuring || Width <= 0) return;
            _measuring = true;
            try
            {
                var height = Desired(Width);
                if (Height != height) Height = height;
            }
            finally
            {
                _measuring = false;
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if (Width > 0) Height = Desired(Width);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Palette;
            g.Clear(ParentColor);
            Draw.Fill(g, UiHelpers.Blend(ParentColor, p.Accent, p.Dark ? 0.12 : 0.06), new RectangleF(0, 0, Width, Height), UiHelpers.S(8f));
            var pad = UiHelpers.S(12);
            Draw.Glyph(g, Glyph ?? Glyphs.Info, UiFonts.IconSmall, new Rectangle(pad, pad, UiHelpers.S(16), UiHelpers.S(18)), p.Accent,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            Draw.WrappedText(g, Text, UiFonts.Small, new Rectangle(pad + UiHelpers.S(24), pad, Width - pad * 2 - UiHelpers.S(24), Height - pad * 2), p.Muted);
        }
    }

    /// <summary>A TabControl whose tab strip is hidden; navigation is drawn separately.</summary>
    internal sealed class HeaderlessTabControl : TabControl
    {
        protected override void WndProc(ref Message m)
        {
            // TCM_ADJUSTRECT: let pages fill the whole control so no native tab strip or border is shown.
            if (m.Msg == 0x1328 && !DesignMode)
            {
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }
    }

    internal abstract class CanvasControl : Control, IThemed
    {
        protected ThemePalette Palette = ThemePalette.For("light");
        protected bool Hovered;

        protected CanvasControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            Font = UiFonts.Body;
        }

        public bool Interactive { get; set; }

        public virtual void ApplyPalette(ThemePalette palette)
        {
            Palette = palette;
            Invalidate();
        }

        protected Color ParentColor
        {
            get { return Parent != null ? Parent.BackColor : Palette.Window; }
        }

        protected override void OnMouseEnter(EventArgs e) { Hovered = true; if (Interactive) Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hovered = false; if (Interactive) Invalidate(); base.OnMouseLeave(e); }

        protected void PaintCard(Graphics g, Color? border = null)
        {
            g.Clear(ParentColor);
            var fill = Interactive && Hovered ? Palette.SurfaceHover : Palette.Surface;
            var stroke = border ?? (Interactive && Hovered ? Palette.BorderStrong : Palette.Border);
            Draw.Card(g, Palette, new Rectangle(0, 0, Width, Height), fill, stroke, UiHelpers.S(12f));
        }
    }

    /// <summary>One quota window: label, large percentage, progress bar and reset countdown.</summary>
    internal sealed class QuotaCard : CanvasControl
    {
        public string Title { get; set; }
        public string Caption { get; set; }
        public double Displayed { get; set; }
        public Tone Tone { get; set; }
        public bool Stale { get; set; }
        public string Footer { get; set; }

        public static int PreferredHeight { get { return UiHelpers.S(142); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            PaintCard(g);
            var p = Palette;
            var color = Stale ? p.Stale : p.ToneColor(Tone);
            var pad = UiHelpers.S(16);
            var inner = Width - pad * 2;

            var titleRect = new Rectangle(pad, UiHelpers.S(14), inner - UiHelpers.S(56), UiHelpers.S(20));
            Draw.Text(g, Title, UiFonts.Body, titleRect, p.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            var caption = Stale ? "已过期" : Caption;
            var captionSize = Draw.Measure(caption, UiFonts.Small);
            var pillWidth = captionSize.Width + UiHelpers.S(14);
            var pill = new RectangleF(Width - pad - pillWidth, UiHelpers.S(14), pillWidth, UiHelpers.S(20));
            Draw.Fill(g, Color.FromArgb(p.Dark ? 46 : 30, color), pill, pill.Height / 2);
            Draw.Text(g, caption, UiFonts.Small, Rectangle.Round(pill), Stale ? p.Muted : color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            var number = UiHelpers.Number(Displayed);
            var numberSize = Draw.Measure(number, UiFonts.Number);
            var numberTop = UiHelpers.S(42);
            var numberColor = Stale ? p.Muted : Tone == Tone.Good ? p.Text : color;
            Draw.Text(g, number, UiFonts.Number, new Rectangle(pad - UiHelpers.S(1), numberTop, numberSize.Width + UiHelpers.S(4), numberSize.Height),
                numberColor, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            var suffixSize = Draw.Measure("%", UiFonts.NumberSuffix);
            var suffixTop = numberTop + numberSize.Height - suffixSize.Height - UiHelpers.S(5);
            Draw.Text(g, "%", UiFonts.NumberSuffix, new Rectangle(pad + numberSize.Width + UiHelpers.S(3), suffixTop, suffixSize.Width + UiHelpers.S(4), suffixSize.Height),
                p.Muted, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding);

            Draw.Bar(g, p.Track, color, new RectangleF(pad, UiHelpers.S(92), inner, UiHelpers.S(6)), Displayed);

            var footerTop = UiHelpers.S(108);
            Draw.Glyph(g, Glyphs.Clock, UiFonts.IconSmall, new Rectangle(pad, footerTop, UiHelpers.S(14), UiHelpers.S(20)), p.Subtle,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            Draw.Text(g, Footer, UiFonts.Small, new Rectangle(pad + UiHelpers.S(18), footerTop, inner - UiHelpers.S(18), UiHelpers.S(20)), p.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>A clickable connection summary for the all-slots overview.</summary>
    internal sealed class ProfileCard : CanvasControl
    {
        private readonly List<Row> _rows = new List<Row>();

        public ProfileCard()
        {
            Interactive = true;
            Cursor = Cursors.Hand;
        }

        public string ProfileName { get; set; }
        public string Status { get; set; }
        public Tone StatusTone { get; set; }
        public string Note { get; set; }
        public Tone NoteTone { get; set; }

        public void AddRow(string title, double displayed, Tone tone, bool stale)
        {
            _rows.Add(new Row { Title = title, Displayed = displayed, Tone = tone, Stale = stale });
        }

        public int PreferredHeight
        {
            get
            {
                var height = UiHelpers.S(16 + 22 + 8);
                height += _rows.Count * UiHelpers.S(24);
                height += UiHelpers.S(_rows.Count > 0 ? 6 : 0) + UiHelpers.S(20) + UiHelpers.S(14);
                return height;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            PaintCard(g);
            var p = Palette;
            var pad = UiHelpers.S(16);
            var inner = Width - pad * 2;
            var top = UiHelpers.S(16);
            var statusColor = p.ToneColor(StatusTone);

            Draw.Dot(g, statusColor, new PointF(pad + UiHelpers.S(4f), top + UiHelpers.S(11f)), UiHelpers.S(7f));
            var statusSize = Draw.Measure(Status, UiFonts.Small);
            var chevronWidth = UiHelpers.S(16);
            var nameRect = new Rectangle(pad + UiHelpers.S(16), top, inner - UiHelpers.S(16) - statusSize.Width - chevronWidth - UiHelpers.S(12), UiHelpers.S(22));
            Draw.Text(g, ProfileName, UiFonts.BodyBold, nameRect, p.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            Draw.Text(g, Status, UiFonts.Small, new Rectangle(Width - pad - chevronWidth - statusSize.Width - UiHelpers.S(6), top, statusSize.Width + UiHelpers.S(4), UiHelpers.S(22)),
                StatusTone == Tone.Neutral ? p.Muted : statusColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            Draw.Glyph(g, Glyphs.Chevron, UiFonts.IconSmall, new Rectangle(Width - pad - chevronWidth, top, chevronWidth, UiHelpers.S(22)),
                Hovered ? p.Text : p.Subtle, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

            var y = top + UiHelpers.S(22 + 8);
            var labelWidth = UiHelpers.S(96);
            var valueWidth = UiHelpers.S(52);
            foreach (var row in _rows)
            {
                var color = row.Stale ? p.Stale : p.ToneColor(row.Tone);
                var rowHeight = UiHelpers.S(24);
                Draw.Text(g, row.Title, UiFonts.Small, new Rectangle(pad, y, labelWidth, rowHeight), p.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                var barHeight = UiHelpers.S(6f);
                var barRect = new RectangleF(pad + labelWidth, y + (rowHeight - barHeight) / 2f, inner - labelWidth - valueWidth - UiHelpers.S(8), barHeight);
                Draw.Bar(g, p.Track, color, barRect, row.Displayed);
                Draw.Text(g, UiHelpers.Percent(row.Displayed), UiFonts.NumberSmall, new Rectangle(Width - pad - valueWidth, y, valueWidth, rowHeight),
                    row.Stale ? p.Muted : p.Text, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                y += rowHeight;
            }
            if (_rows.Count > 0) y += UiHelpers.S(6);
            var noteColor = NoteTone == Tone.Neutral ? p.Subtle : p.ToneColor(NoteTone);
            Draw.Text(g, Note, UiFonts.Small, new Rectangle(pad, y, inner, UiHelpers.S(20)), noteColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private sealed class Row
        {
            public string Title;
            public double Displayed;
            public Tone Tone;
            public bool Stale;
        }
    }

    /// <summary>An informational card with an icon, wrapped text and an optional action.</summary>
    internal sealed class NoticeCard : CanvasControl
    {
        private Rectangle _actionBounds;
        private bool _actionHover;

        public NoticeCard()
        {
            Cursor = Cursors.Default;
        }

        public string Heading { get; set; }
        public string Body { get; set; }
        public string Glyph { get; set; }
        public Tone Tone { get; set; }
        public string ActionText { get; set; }
        public bool Centered { get; set; }
        public event EventHandler ActionClicked;

        public void FitWidth(int width)
        {
            Width = width;
            Height = MeasureHeight(width);
        }

        private int TextWidth(int width)
        {
            var pad = UiHelpers.S(16);
            return Centered ? width - pad * 2 : width - pad * 2 - UiHelpers.S(44);
        }

        private int MeasureHeight(int width)
        {
            var pad = UiHelpers.S(16);
            var textWidth = TextWidth(width);
            var height = pad;
            if (Centered) height += UiHelpers.S(56);
            if (!string.IsNullOrEmpty(Heading)) height += Draw.WrappedHeight(Heading, UiFonts.Heading, textWidth) + UiHelpers.S(4);
            if (!string.IsNullOrEmpty(Body)) height += Draw.WrappedHeight(Body, UiFonts.Small, textWidth);
            if (!string.IsNullOrEmpty(ActionText)) height += UiHelpers.S(16 + 34);
            height += pad;
            return Centered ? height : Math.Max(height, UiHelpers.S(64));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Palette;
            var color = p.ToneColor(Tone);
            PaintCard(g, Tone == Tone.Warning || Tone == Tone.Danger ? Color.FromArgb(p.Dark ? 90 : 110, color) : (Color?)null);
            var pad = UiHelpers.S(16);
            var textWidth = TextWidth(Width);
            var circle = UiHelpers.S(Centered ? 44f : 32f);
            float circleX = Centered ? (Width - circle) / 2f : pad;
            float circleY = pad;
            using (var brush = new SolidBrush(Color.FromArgb(p.Dark ? 44 : 28, color)))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(brush, circleX, circleY, circle, circle);
            }
            Draw.Glyph(g, Glyph ?? Glyphs.Info, Centered ? UiFonts.IconLarge : UiFonts.Icon,
                Rectangle.Round(new RectangleF(circleX, circleY, circle, circle)), color);

            var x = Centered ? pad : pad + UiHelpers.S(44);
            var y = Centered ? pad + UiHelpers.S(56) : pad + UiHelpers.S(1);
            var align = Centered ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left;
            if (!string.IsNullOrEmpty(Heading))
            {
                var h = Draw.WrappedHeight(Heading, UiFonts.Heading, textWidth);
                TextRenderer.DrawText(g, Heading, UiFonts.Heading, new Rectangle(x, y, textWidth, h), p.Text,
                    align | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
                y += h + UiHelpers.S(4);
            }
            if (!string.IsNullOrEmpty(Body))
            {
                var h = Draw.WrappedHeight(Body, UiFonts.Small, textWidth);
                TextRenderer.DrawText(g, Body, UiFonts.Small, new Rectangle(x, y, textWidth, h),
                    Tone == Tone.Warning || Tone == Tone.Danger ? UiHelpers.Blend(p.Muted, color, 0.35) : p.Muted,
                    align | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
                y += h;
            }
            if (!string.IsNullOrEmpty(ActionText))
            {
                y += UiHelpers.S(16);
                var size = Draw.Measure(ActionText, UiFonts.BodyBold);
                var width = size.Width + UiHelpers.S(22 + 28);
                var left = Centered ? (Width - width) / 2 : x;
                _actionBounds = new Rectangle(left, y, width, UiHelpers.S(34));
                Draw.Fill(g, _actionHover ? p.AccentHover : p.Accent, _actionBounds, UiHelpers.S(8f));
                Draw.Glyph(g, Glyphs.Add, UiFonts.IconSmall, new Rectangle(left + UiHelpers.S(14), y, UiHelpers.S(16), _actionBounds.Height), p.OnAccent,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                Draw.Text(g, ActionText, UiFonts.BodyBold, new Rectangle(left + UiHelpers.S(36), y, size.Width + UiHelpers.S(4), _actionBounds.Height), p.OnAccent,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            else
            {
                _actionBounds = Rectangle.Empty;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var hover = _actionBounds.Contains(e.Location);
            if (hover != _actionHover)
            {
                _actionHover = hover;
                Cursor = hover ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_actionHover)
            {
                _actionHover = false;
                Cursor = Cursors.Default;
                Invalidate();
            }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && _actionBounds.Contains(e.Location) && ActionClicked != null) ActionClicked(this, EventArgs.Empty);
        }
    }
}
