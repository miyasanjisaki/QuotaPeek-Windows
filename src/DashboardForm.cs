using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace QuotaPeek.Windows
{
    internal sealed class DashboardForm : Form
    {
        private const int CardGap = 10;
        private const int SidePadding = 16;

        private static readonly bool HasDwmRoundedCorners = Environment.OSVersion.Version.Major >= 10 &&
                                                             Environment.OSVersion.Version.Build >= 22000;

        private readonly AppController _controller;
        private readonly Panel _header;
        private readonly FlowLayoutPanel _chips;
        private readonly FlowLayoutPanel _content;
        private readonly Panel _footer;
        private readonly ModernButton _refreshButton;
        private readonly ModernButton _settingsButton;
        private readonly ModernButton _hideButton;
        private readonly ModernButton _quitButton;
        private readonly ToolTip _toolTip;
        private readonly Timer _clockTimer;
        private ThemePalette _palette = ThemePalette.For("light");
        private string _status = "";
        private string _footerText = "";
        private bool _chipsShown;
        private string _viewKey;
        private int _autoHiddenAt = Environment.TickCount - 10_000;

        public DashboardForm(AppController controller)
        {
            _controller = controller;
            Text = "QuotaPeek";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size(UiHelpers.S(396), UiHelpers.S(480));
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            KeyPreview = true;
            Font = UiFonts.Body;
            DoubleBuffered = true;

            _toolTip = new ToolTip { InitialDelay = 400, ShowAlways = true };

            _header = new BufferedPanel { Dock = DockStyle.Top, Height = UiHelpers.S(68) };
            _header.Paint += PaintHeader;
            _header.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) WindowEffects.BeginDrag(this);
            };
            _refreshButton = HeaderButton(Glyphs.Refresh, "刷新");
            _refreshButton.Click += async delegate { await _controller.RefreshSelectedAsync(); };
            _settingsButton = HeaderButton(Glyphs.Settings, "设置");
            _settingsButton.Click += delegate { Hide(); _controller.ShowSettings(); };
            _hideButton = HeaderButton(Glyphs.Close, "收起（Esc）");
            _hideButton.GlyphFont = UiFonts.IconSmall;
            _hideButton.Click += delegate { Hide(); };
            _header.Controls.Add(_refreshButton);
            _header.Controls.Add(_settingsButton);
            _header.Controls.Add(_hideButton);
            _header.Resize += delegate { LayoutHeaderButtons(); };

            _chips = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(UiHelpers.S(SidePadding), 0, UiHelpers.S(SidePadding), UiHelpers.S(8)),
                Margin = Padding.Empty
            };

            _content = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(UiHelpers.S(SidePadding), UiHelpers.S(4), UiHelpers.S(SidePadding - CardGap), UiHelpers.S(6)),
                Margin = Padding.Empty
            };

            _footer = new BufferedPanel { Dock = DockStyle.Bottom, Height = UiHelpers.S(48) };
            _footer.Paint += PaintFooter;
            _quitButton = new ModernButton("退出", ButtonVariant.Ghost, Glyphs.Power);
            _quitButton.Click += delegate { _controller.Quit(); };
            _toolTip.SetToolTip(_quitButton, "完全退出 QuotaPeek");
            _footer.Controls.Add(_quitButton);
            _footer.Resize += delegate { LayoutFooter(); };

            Controls.Add(_content);
            Controls.Add(_chips);
            Controls.Add(_header);
            Controls.Add(_footer);

            _clockTimer = new Timer { Interval = 30_000 };
            _clockTimer.Tick += delegate { if (Visible) RefreshView(); };
            _clockTimer.Start();
            _controller.StateChanged += ControllerStateChanged;
            FormClosing += DashboardFormClosing;
            RefreshView();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                parameters.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW: keep the flyout out of Alt+Tab
                return parameters;
            }
        }

        /// <summary>
        /// True while the flyout is shown or was auto-hidden a moment ago. Pressing the tray icon deactivates
        /// (and hides) the flyout around the same time the press arrives, so the tray uses this at mouse-down
        /// to decide whether that click should close the flyout instead of reopening it.
        /// </summary>
        public bool IsOpenOrJustClosed
        {
            get { return Visible || unchecked(Environment.TickCount - _autoHiddenAt) < 300; }
        }

        public void ShowNearCursor()
        {
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            var margin = UiHelpers.S(12);
            var x = Math.Max(area.Left, area.Right - Width - margin);
            var y = Math.Max(area.Top, area.Bottom - Height - margin);
            Location = new Point(x, y);
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WindowEffects.Apply(this, _palette, true);
        }

        public void RefreshView()
        {
            if (IsDisposed) return;
            _palette = ThemePalette.For(_controller.Preferences.Appearance);
            _status = _controller.GetStatusTitle();
            // Keep the reader's place when the same view is rebuilt by a background refresh.
            var viewKey = _controller.GlobalError != null ? "<error>" :
                _controller.SelectedProfile != null ? _controller.SelectedProfile.Id : "<all>";
            var keptScroll = viewKey == _viewKey && _content.AutoScroll ? -_content.AutoScrollPosition.Y : 0;
            _viewKey = viewKey;

            // Only freeze painting while the flyout is on screen: WM_SETREDRAW(TRUE) sets WS_VISIBLE, so using it
            // on a hidden flyout would pop up a blank window that WinForms still believes is hidden.
            var freezePainting = Visible && IsHandleCreated;
            if (freezePainting) WindowEffects.SetRedraw(this, false);
            try
            {
                SuspendLayout();
                try
                {
                    UiHelpers.ApplyTheme(this, _palette);
                    WindowEffects.Apply(this, _palette, true);
                    _refreshButton.Enabled = !_controller.IsRefreshing && _controller.Preferences.Profiles.Count > 0 && _controller.GlobalError == null;
                    BuildChips();
                    BuildContent(ContentWidth(false));
                    _footerText = FooterText();
                }
                finally
                {
                    ResumeLayout(true);
                }
                FitToContent();
                if (keptScroll > 0 && _content.AutoScroll)
                {
                    _content.PerformLayout();
                    _content.AutoScrollPosition = new Point(0, keptScroll);
                }
            }
            finally
            {
                if (freezePainting)
                {
                    WindowEffects.SetRedraw(this, true);
                    Invalidate(true);
                }
            }
        }

        private ModernButton HeaderButton(string glyph, string tip)
        {
            var button = new ModernButton("", ButtonVariant.Ghost, glyph);
            _toolTip.SetToolTip(button, tip);
            return button;
        }

        private void LayoutHeaderButtons()
        {
            var size = _hideButton.Width;
            var top = UiHelpers.S(18);
            var right = _header.Width - UiHelpers.S(SidePadding - 4);
            _hideButton.Location = new Point(right - size, top);
            _settingsButton.Location = new Point(right - size * 2 - UiHelpers.S(2), top);
            _refreshButton.Location = new Point(right - size * 3 - UiHelpers.S(4), top);
        }

        private void LayoutFooter()
        {
            _quitButton.Location = new Point(_footer.Width - _quitButton.Width - UiHelpers.S(SidePadding - 6),
                (_footer.Height - _quitButton.Height) / 2 + UiHelpers.S(1));
        }

        private void PaintHeader(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(_palette.Window);
            var left = UiHelpers.S(SidePadding);
            var logo = UiHelpers.S(36f);
            Draw.Logo(g, _palette, new RectangleF(left, UiHelpers.S(16f), logo, logo));
            var textLeft = left + (int)logo + UiHelpers.S(12);
            Draw.Text(g, "QuotaPeek", UiFonts.Title, new Rectangle(textLeft, UiHelpers.S(13), UiHelpers.S(180), UiHelpers.S(24)), _palette.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            var tone = UiHelpers.StatusTone(_status);
            var color = _palette.ToneColor(tone);
            Draw.Dot(g, color, new PointF(textLeft + UiHelpers.S(3.5f), UiHelpers.S(46f)), UiHelpers.S(6f));
            Draw.Text(g, _status, UiFonts.Small, new Rectangle(textLeft + UiHelpers.S(13), UiHelpers.S(36), UiHelpers.S(180), UiHelpers.S(20)),
                tone == Tone.Neutral ? _palette.Muted : color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private void PaintFooter(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(_palette.Window);
            using (var pen = new Pen(_palette.Border))
            {
                g.DrawLine(pen, UiHelpers.S(SidePadding), 0, _footer.Width - UiHelpers.S(SidePadding), 0);
            }
            var left = UiHelpers.S(SidePadding);
            Draw.Glyph(g, _controller.IsRefreshing ? Glyphs.Refresh : Glyphs.Clock, UiFonts.IconSmall,
                new Rectangle(left, 0, UiHelpers.S(14), _footer.Height), _palette.Subtle,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            Draw.Text(g, _footerText, UiFonts.Small,
                new Rectangle(left + UiHelpers.S(20), 0, _quitButton.Left - left - UiHelpers.S(24), _footer.Height), _palette.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (HasDwmRoundedCorners) return;
            using (var pen = new Pen(_palette.BorderStrong))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            }
        }

        private int ContentWidth(bool withScrollbar)
        {
            var width = ClientSize.Width - UiHelpers.S(SidePadding) * 2;
            return withScrollbar ? width - SystemInformation.VerticalScrollBarWidth : width;
        }

        private void BuildChips()
        {
            while (_chips.Controls.Count > 0)
            {
                _toolTip.SetToolTip(_chips.Controls[0], null);
                _chips.Controls[0].Dispose();
            }
            var preferences = _controller.Preferences;
            if (_controller.GlobalError != null || preferences.Profiles.Count == 0)
            {
                _chipsShown = false;
                _chips.Visible = false;
                return;
            }
            _chipsShown = true;
            _chips.Visible = true;
            _chips.Controls.Add(Chip("全部槽位", null, preferences.SelectedProfileId == null || _controller.SelectedProfile == null, false));
            foreach (var profile in preferences.Profiles)
            {
                _chips.Controls.Add(Chip(profile.Name, profile.Id, profile.Id == preferences.SelectedProfileId, !profile.Enabled));
            }
            foreach (Control chip in _chips.Controls) ((IThemed)chip).ApplyPalette(_palette);
            // Many slots would wrap into an ever taller tab area; cap it at three rows and let it scroll instead.
            var full = FlowHeight(_chips, ClientSize.Width);
            var cap = UiHelpers.S(3 * 34) + _chips.Padding.Vertical;
            _chips.AutoScroll = full > cap;
            _chips.Height = Math.Min(full, cap);
            if (_chips.AutoScroll)
            {
                var selected = _chips.Controls.Cast<ModernButton>().FirstOrDefault(chip => chip.Selected);
                if (selected != null)
                {
                    _chips.PerformLayout();
                    _chips.ScrollControlIntoView(selected);
                }
            }
        }

        private ModernButton Chip(string text, string profileId, bool selected, bool paused)
        {
            var chip = new ModernButton(text, ButtonVariant.Chip, paused ? Glyphs.Pause : profileId == null ? Glyphs.Grid : null)
            {
                Selected = selected,
                Muted = paused,
                MaxTextWidth = UiHelpers.S(150),
                Margin = new Padding(0, 0, UiHelpers.S(6), UiHelpers.S(6))
            };
            chip.FitToContent();
            if (paused) _toolTip.SetToolTip(chip, text + "（已暂停）");
            else if (Draw.Measure(text, chip.Font).Width > chip.MaxTextWidth) _toolTip.SetToolTip(chip, text);
            chip.Click += delegate { if (!chip.Selected) _controller.SelectProfile(profileId); };
            return chip;
        }

        private void BuildContent(int width)
        {
            _content.SuspendLayout();
            try
            {
                // Scrolling is re-enabled by FitToContent only when the cards really overflow, so a transient
                // scrollbar during the rebuild cannot squeeze the two-column grid into one column.
                _content.AutoScroll = false;
                while (_content.Controls.Count > 0) _content.Controls[0].Dispose();
                var preferences = _controller.Preferences;
                if (_controller.GlobalError != null)
                {
                    AddNotice(width, Glyphs.Error, Tone.Danger, "配置读取失败", _controller.GlobalError);
                }
                else if (preferences.Profiles.Count == 0)
                {
                    var empty = Notice(Glyphs.Plug, Tone.Accent, "还没有连接",
                        "添加 VM2API 或 Claude 兼容服务的地址与 API Key，即可在这里查看 5 小时与 7 天额度。");
                    empty.Centered = true;
                    empty.ActionText = "添加连接";
                    empty.ActionClicked += delegate { Hide(); _controller.ShowSettings(); };
                    AddFull(empty, width);
                }
                else if (_controller.SelectedProfile == null)
                {
                    BuildOverview(width);
                }
                else
                {
                    BuildProfile(_controller.SelectedProfile, width);
                }
                foreach (Control child in _content.Controls) ((IThemed)child).ApplyPalette(_palette);
            }
            finally
            {
                _content.ResumeLayout(true);
            }
        }

        private void BuildOverview(int width)
        {
            var preferences = _controller.Preferences;
            foreach (var profile in preferences.Profiles)
            {
                ProfileState state;
                _controller.States.TryGetValue(profile.Id, out state);
                var card = new ProfileCard { ProfileName = profile.Name };
                var stale = _controller.IsStale(state);
                if (!profile.Enabled)
                {
                    card.Status = "已暂停";
                    card.StatusTone = Tone.Neutral;
                    card.Note = "已暂停查询，可在设置中重新启用";
                }
                else if (state == null || state.Snapshot == null)
                {
                    var failed = state != null && state.Error != null;
                    card.Status = failed ? "连接异常" : "等待更新";
                    card.StatusTone = failed ? Tone.Danger : Tone.Neutral;
                    card.Note = failed ? state.Error : "正在获取额度…";
                    card.NoteTone = failed ? Tone.Danger : Tone.Neutral;
                }
                else
                {
                    var tightest = state.Snapshot.Windows.OrderByDescending(window => window.UsedPercent).FirstOrDefault();
                    var tone = tightest == null ? Tone.Neutral : UiHelpers.QuotaTone(tightest.UsedPercent, preferences.WarningPercent, false);
                    if (stale)
                    {
                        card.Status = "数据已过期";
                        card.StatusTone = Tone.Warning;
                    }
                    else
                    {
                        card.Status = tone == Tone.Danger ? "额度耗尽" : tone == Tone.Warning ? "留意用量" : "额度充足";
                        card.StatusTone = tone;
                    }
                    foreach (var window in state.Snapshot.Windows)
                    {
                        card.AddRow(window.Title, Displayed(window),
                            UiHelpers.QuotaTone(window.UsedPercent, preferences.WarningPercent, false), stale);
                    }
                    card.Note = state.Error != null
                        ? "数据已过期 · " + state.Error
                        : (preferences.ShowRemaining ? "显示剩余额度 · " : "显示已用额度 · ") +
                          UiHelpers.AgeLabel(state.Snapshot.FetchedAt, DateTimeOffset.Now);
                    card.NoteTone = state.Error != null ? Tone.Warning : Tone.Neutral;
                }
                card.Width = width;
                card.Height = card.PreferredHeight;
                card.Margin = new Padding(0, 0, UiHelpers.S(CardGap), UiHelpers.S(CardGap));
                var id = profile.Id;
                card.Click += delegate { _controller.SelectProfile(id); };
                _content.Controls.Add(card);
            }
        }

        private void BuildProfile(ConnectionProfile profile, int width)
        {
            ProfileState state;
            _controller.States.TryGetValue(profile.Id, out state);
            if (!profile.Enabled)
            {
                var paused = Notice(Glyphs.Pause, Tone.Neutral, "此连接已暂停", "暂停期间不会查询额度。可在设置中重新启用。");
                paused.Centered = true;
                AddFull(paused, width);
                return;
            }
            if (state == null || state.Snapshot == null)
            {
                if (state != null && state.Error != null)
                {
                    AddNotice(width, Glyphs.Error, Tone.Danger, "无法获取额度", state.Error);
                }
                else
                {
                    var waiting = Notice(Glyphs.Refresh, Tone.Accent, "正在获取额度…", "首次更新通常只需要几秒钟。");
                    waiting.Centered = true;
                    AddFull(waiting, width);
                }
                return;
            }

            var stale = _controller.IsStale(state);
            if (state.Error != null)
            {
                AddNotice(width, Glyphs.Warning, Tone.Warning, "数据已过期", state.Error + " 下方为上次成功获取的数据。");
            }
            else if (stale)
            {
                AddNotice(width, Glyphs.Warning, Tone.Warning, "数据已过期",
                    "上次成功更新：" + UiHelpers.AgeLabel(state.Snapshot.FetchedAt, DateTimeOffset.Now) + "。");
            }

            var preferences = _controller.Preferences;
            var cardWidth = (width - UiHelpers.S(CardGap)) / 2;
            foreach (var window in state.Snapshot.Windows)
            {
                _content.Controls.Add(new QuotaCard
                {
                    Title = window.Title,
                    Caption = preferences.ShowRemaining ? "剩余" : "已用",
                    Displayed = Displayed(window),
                    Tone = UiHelpers.QuotaTone(window.UsedPercent, preferences.WarningPercent, false),
                    Stale = stale,
                    Footer = UiHelpers.ResetLabel(window.ResetsAt, DateTimeOffset.Now),
                    Width = cardWidth,
                    Height = QuotaCard.PreferredHeight,
                    Margin = new Padding(0, 0, UiHelpers.S(CardGap), UiHelpers.S(CardGap))
                });
            }
        }

        private double Displayed(QuotaWindow window)
        {
            return _controller.Preferences.ShowRemaining ? 100 - window.UsedPercent : window.UsedPercent;
        }

        private NoticeCard Notice(string glyph, Tone tone, string heading, string body)
        {
            return new NoticeCard { Glyph = glyph, Tone = tone, Heading = heading, Body = body };
        }

        private void AddNotice(int width, string glyph, Tone tone, string heading, string body)
        {
            AddFull(Notice(glyph, tone, heading, body), width);
        }

        private void AddFull(NoticeCard card, int width)
        {
            card.Margin = new Padding(0, 0, UiHelpers.S(CardGap), UiHelpers.S(CardGap));
            card.FitWidth(width);
            _content.Controls.Add(card);
        }

        private void ResizeContent(int width)
        {
            var half = (width - UiHelpers.S(CardGap)) / 2;
            foreach (Control child in _content.Controls)
            {
                var notice = child as NoticeCard;
                if (notice != null) notice.FitWidth(width);
                else if (child is QuotaCard) child.Width = half;
                else child.Width = width;
            }
        }

        private void FitToContent()
        {
            var contentHeight = FlowHeight(_content, ClientSize.Width);
            // Control.Visible reads false while the form is hidden, so use the remembered state.
            var chrome = _header.Height + (_chipsShown ? _chips.Height : 0) + _footer.Height;
            var area = Visible ? Screen.FromControl(this).WorkingArea : Screen.FromPoint(Cursor.Position).WorkingArea;
            var maximum = Math.Max(UiHelpers.S(320), area.Height - UiHelpers.S(24));
            var desired = Math.Max(UiHelpers.S(300), Math.Min(maximum, chrome + contentHeight));
            if (ClientSize.Height != desired)
            {
                var bottom = Bottom;
                ClientSize = new Size(ClientSize.Width, desired);
                if (Visible) Top = Math.Max(area.Top, bottom - Height);
            }
            var overflow = chrome + contentHeight > desired;
            _content.SuspendLayout();
            ResizeContent(ContentWidth(overflow));
            _content.AutoScroll = overflow;
            _content.ResumeLayout(true);
        }

        // Mirrors FlowLayoutPanel's left-to-right wrapping so the height does not depend on stale layout state.
        private static int FlowHeight(FlowLayoutPanel panel, int width)
        {
            var available = width - panel.Padding.Horizontal;
            int x = 0, row = 0, total = 0;
            foreach (Control child in panel.Controls)
            {
                var w = child.Width + child.Margin.Horizontal;
                var h = child.Height + child.Margin.Vertical;
                if (x > 0 && x + w > available)
                {
                    total += row;
                    x = 0;
                    row = 0;
                }
                x += w;
                row = Math.Max(row, h);
            }
            return total + row + panel.Padding.Vertical;
        }

        public void HideFlyout()
        {
            Hide();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!Visible) return;
            _autoHiddenAt = Environment.TickCount;
            Hide();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                Hide();
                return;
            }
            if (e.KeyCode == Keys.F5 && _refreshButton.Enabled)
            {
                e.Handled = true;
                _refreshButton.PerformClick();
                return;
            }
            base.OnKeyDown(e);
        }

        private string FooterText()
        {
            var preferences = _controller.Preferences;
            var cadence = "每 " + preferences.RefreshMinutes + " 分钟自动刷新";
            if (_controller.GlobalError != null || preferences.Profiles.Count == 0) return "QuotaPeek · 本地额度查看";
            if (_controller.IsRefreshing) return "正在更新… · " + cadence;
            DateTimeOffset? newest = null;
            var selected = _controller.SelectedProfile;
            foreach (var profile in preferences.Profiles)
            {
                if (selected != null && profile.Id != selected.Id) continue;
                ProfileState state;
                if (!_controller.States.TryGetValue(profile.Id, out state) || state.Snapshot == null) continue;
                if (!newest.HasValue || state.Snapshot.FetchedAt > newest.Value) newest = state.Snapshot.FetchedAt;
            }
            return newest.HasValue ? UiHelpers.AgeLabel(newest.Value, DateTimeOffset.Now) + " · " + cadence : cadence;
        }

        private void ControllerStateChanged(object sender, EventArgs e)
        {
            // ShowDashboard rebuilds the view before showing it, so a hidden flyout does not need background rebuilds.
            if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)delegate { if (Visible) RefreshView(); });
        }

        private void DashboardFormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _controller.StateChanged -= ControllerStateChanged;
                _clockTimer.Dispose();
                _toolTip.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class BufferedPanel : Panel
        {
            public BufferedPanel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw, true);
            }
        }
    }
}
