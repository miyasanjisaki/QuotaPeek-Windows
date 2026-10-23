using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QuotaPeek.Windows
{
    internal sealed partial class SettingsForm : Form
    {
        private readonly AppController _controller;
        private readonly TabControl _tabs;
        private readonly ListBox _profileList;
        private readonly TextBox _nameBox;
        private readonly TextBox _urlBox;
        private readonly TextBox _keyBox;
        private readonly CheckBox _enabledBox;
        private readonly CheckBox _showKeyBox;
        private readonly Button _saveProfileButton;
        private readonly Button _deleteButton;
        private readonly Button _upButton;
        private readonly Button _downButton;
        private readonly Label _connectionStatus;
        private readonly ComboBox _refreshBox;
        private readonly ComboBox _displayBox;
        private readonly ComboBox _trayModeBox;
        private readonly CheckBox _showNameBox;
        private readonly ComboBox _appearanceBox;
        private readonly CheckBox _notificationsBox;
        private readonly ComboBox _warningBox;
        private readonly CheckBox _startupBox;
        private readonly Label _generalStatus;
        private bool _loading = true;
        private bool _busy;
        private string _draftId;
        private ConnectionProfile _originalProfile;
        private string _originalKey = "";

        private readonly ModernButton _connectionsNav;
        private readonly ModernButton _generalNav;
        private readonly Panel _navBar;
        private readonly Label _emptyListLabel;
        private readonly ToolTip _toolTip;
        private ThemePalette _palette = ThemePalette.For("light");

        public SettingsForm(AppController controller)
        {
            _controller = controller;
            Text = "QuotaPeek 设置";
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(UiHelpers.S(780), UiHelpers.S(610));
            MinimumSize = new Size(UiHelpers.S(720), UiHelpers.S(600));
            StartPosition = FormStartPosition.CenterScreen;
            Font = UiFonts.Body;
            DoubleBuffered = true;
            Icon = IconFactory.CreateAppIcon();
            _toolTip = new ToolTip { InitialDelay = 400, ShowAlways = true };
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;

            _tabs = new HeaderlessTabControl { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = Point.Empty };
            _tabs.Deselecting += TabsDeselecting;
            _tabs.SelectedIndexChanged += delegate { UpdateNav(); };

            _navBar = new NavPanel { Dock = DockStyle.Top, Height = UiHelpers.S(76) };
            _navBar.Paint += PaintNavBar;
            _connectionsNav = new ModernButton("连接", ButtonVariant.Chip, Glyphs.Plug);
            _generalNav = new ModernButton("通用", ButtonVariant.Chip, Glyphs.Settings);
            _connectionsNav.Click += delegate { _tabs.SelectedIndex = 0; };
            _generalNav.Click += delegate { _tabs.SelectedIndex = 1; };
            _navBar.Controls.Add(_connectionsNav);
            _navBar.Controls.Add(_generalNav);
            _navBar.Resize += delegate { LayoutNav(); };

            Controls.Add(_tabs);
            Controls.Add(_navBar);

            var connectionsPage = new TabPage("连接") { Padding = new Padding(UiHelpers.S(20), UiHelpers.S(4), UiHelpers.S(20), UiHelpers.S(20)) };
            var generalPage = new TabPage("通用") { Padding = new Padding(UiHelpers.S(20), UiHelpers.S(4), UiHelpers.S(20), UiHelpers.S(20)) };
            _tabs.TabPages.Add(connectionsPage);
            _tabs.TabPages.Add(generalPage);

            // Connections: a list of saved slots on the left, the editor card on the right.
            var editorCard = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(UiHelpers.S(24), UiHelpers.S(20), UiHelpers.S(24), UiHelpers.S(18))
            };
            var spacer = new Panel { Dock = DockStyle.Left, Width = UiHelpers.S(16) };
            var sidebar = new RoundedPanel
            {
                Dock = DockStyle.Left,
                Width = UiHelpers.S(228),
                Padding = new Padding(UiHelpers.S(10), UiHelpers.S(14), UiHelpers.S(10), UiHelpers.S(10))
            };
            connectionsPage.Controls.Add(editorCard);
            connectionsPage.Controls.Add(spacer);
            connectionsPage.Controls.Add(sidebar);

            var listLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            listLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            listLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            listLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sidebar.Controls.Add(listLayout);
            listLayout.Controls.Add(new Label
            {
                Text = "已保存的连接",
                Tag = "muted",
                Font = UiFonts.Small,
                AutoSize = true,
                Margin = new Padding(UiHelpers.S(6), 0, 0, UiHelpers.S(8))
            }, 0, 0);
            _profileList = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = UiHelpers.S(40),
                Margin = Padding.Empty
            };
            _profileList.DrawItem += DrawProfileItem;
            _profileList.SelectedIndexChanged += ProfileSelectionChanged;
            _emptyListLabel = new Label
            {
                Text = "还没有已保存的连接。" + Environment.NewLine + "在右侧填写信息并保存即可添加。",
                Tag = "muted",
                Font = UiFonts.Small,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Visible = false
            };
            var listHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            listHost.Controls.Add(_profileList);
            listHost.Controls.Add(_emptyListLabel);
            listLayout.Controls.Add(listHost, 0, 1);

            var listButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0, UiHelpers.S(8), 0, 0)
            };
            var addButton = new ModernButton("添加", ButtonVariant.Secondary, Glyphs.Add) { Margin = new Padding(0, 0, UiHelpers.S(6), 0) };
            addButton.Click += delegate { AddProfile(); };
            _deleteButton = IconButton(Glyphs.Delete, "删除此连接");
            _deleteButton.Click += delegate { DeleteProfile(); };
            _upButton = IconButton(Glyphs.Up, "上移");
            _downButton = IconButton(Glyphs.Down, "下移");
            _upButton.Click += delegate { MoveProfile(-1); };
            _downButton.Click += delegate { MoveProfile(1); };
            listButtons.Controls.Add(addButton);
            listButtons.Controls.Add(_upButton);
            listButtons.Controls.Add(_downButton);
            listButtons.Controls.Add(_deleteButton);
            listLayout.Controls.Add(listButtons, 0, 2);

            var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Margin = Padding.Empty };
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var row = 0; row < 7; row++) editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            editorCard.Controls.Add(editor);
            editor.Controls.Add(new Label
            {
                Text = "连接详情",
                Font = UiFonts.Heading,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, UiHelpers.S(2))
            }, 0, 0);
            editor.Controls.Add(new Label
            {
                Text = "填写兼容 /v1/usage 的服务地址与 API Key。",
                Tag = "muted",
                Font = UiFonts.Small,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, UiHelpers.S(10))
            }, 0, 1);

            _nameBox = new TextBox();
            _urlBox = new TextBox();
            _keyBox = new TextBox { UseSystemPasswordChar = true };
            _enabledBox = new CheckBox { Text = "启用此连接", AutoSize = true, Margin = new Padding(0, 0, UiHelpers.S(20), 0) };
            _showKeyBox = new CheckBox { Text = "显示 API Key", AutoSize = true, Margin = new Padding(0, 0, UiHelpers.S(20), 0) };
            _showKeyBox.CheckedChanged += delegate { _keyBox.UseSystemPasswordChar = !_showKeyBox.Checked; };
            AddStackedField(editor, 2, "名称", _nameBox);
            AddStackedField(editor, 3, "API 地址", _urlBox);
            AddStackedField(editor, 4, "API Key", _keyBox);
            var toggles = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, UiHelpers.S(4), 0, 0) };
            toggles.Controls.Add(_showKeyBox);
            toggles.Controls.Add(_enabledBox);
            editor.Controls.Add(toggles, 0, 5);

            var hint = new HintLabel
            {
                Glyph = Glyphs.Info,
                Text = "地址可填写服务根地址、/v1 或完整 /v1/usage；远程地址必须使用 HTTPS。保存前会发起一次真实请求，验证失败不会改动原配置。API Key 仅保存到 Windows 凭据管理器。",
                Dock = DockStyle.Top,
                Margin = new Padding(0, UiHelpers.S(16), 0, 0)
            };
            editor.Controls.Add(hint, 0, 6);
            editor.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 7);

            var connectionFooter = ActionRow(out _connectionStatus);
            _saveProfileButton = new ModernButton("验证并保存", ButtonVariant.Primary, Glyphs.Save) { Anchor = AnchorStyles.Right, Margin = Padding.Empty };
            _saveProfileButton.Click += async delegate { await SaveProfileAsync(); };
            connectionFooter.Controls.Add(_saveProfileButton, 1, 0);
            editor.Controls.Add(connectionFooter, 0, 8);

            // General: one card of label/description rows.
            var generalCard = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(UiHelpers.S(24), UiHelpers.S(20), UiHelpers.S(24), UiHelpers.S(18))
            };
            generalPage.Controls.Add(generalCard);
            var general = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 11, Margin = Padding.Empty };
            general.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            general.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiHelpers.S(220)));
            for (var row = 0; row < 9; row++) general.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            general.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            general.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            generalCard.Controls.Add(general);
            var generalHeading = new Label
            {
                Text = "通用设置",
                Font = UiFonts.Heading,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, UiHelpers.S(6))
            };
            general.Controls.Add(generalHeading, 0, 0);
            general.SetColumnSpan(generalHeading, 2);

            _refreshBox = DropDown("1 分钟", "2 分钟", "5 分钟", "15 分钟", "30 分钟");
            _displayBox = DropDown("显示剩余", "显示已用");
            _trayModeBox = DropDown("当前槽位", "最紧张槽位");
            _showNameBox = new CheckBox { Text = "在托盘提示中显示", AutoSize = true };
            _appearanceBox = DropDown("跟随系统", "浅色", "深色");
            _notificationsBox = new CheckBox { Text = "启用额度提醒", AutoSize = true };
            _notificationsBox.CheckedChanged += delegate { _warningBox.Enabled = _notificationsBox.Checked; };
            _warningBox = DropDown("50%", "60%", "70%", "80%", "90%", "95%");
            _startupBox = new CheckBox { Text = "登录 Windows 时启动", AutoSize = true };
            AddSettingRow(general, 1, "自动刷新", "后台查询额度的间隔。", _refreshBox);
            AddSettingRow(general, 2, "额度显示", "百分比显示剩余额度还是已用额度。", _displayBox);
            AddSettingRow(general, 3, "托盘汇总", "托盘图标数字取自哪个槽位。", _trayModeBox);
            AddSettingRow(general, 4, "槽位名称", "鼠标悬停托盘图标时显示槽位名称。", _showNameBox);
            AddSettingRow(general, 5, "外观", "浅色、深色或跟随 Windows 设置。", _appearanceBox);
            AddSettingRow(general, 6, "额度提醒", "用量超过阈值或耗尽时弹出托盘提醒。", _notificationsBox);
            AddSettingRow(general, 7, "提醒阈值", "已用额度达到此比例时提醒。", _warningBox);
            AddSettingRow(general, 8, "开机启动", "仅对当前 Windows 用户生效。", _startupBox);
            general.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 9);

            var generalFooter = ActionRow(out _generalStatus);
            var saveGeneral = new ModernButton("保存通用设置", ButtonVariant.Primary, Glyphs.Save) { Anchor = AnchorStyles.Right, Margin = Padding.Empty };
            saveGeneral.Click += delegate { SaveGeneral(); };
            generalFooter.Controls.Add(saveGeneral, 1, 0);
            general.Controls.Add(generalFooter, 0, 10);
            general.SetColumnSpan(generalFooter, 2);

            FormClosing += SettingsFormClosing;
            RefreshFromModel();
            UpdateNav();
        }

        public void RefreshFromModel()
        {
            if (Visible && (ConnectionDirty() || GeneralDirty())) return;
            _loading = true;
            try
            {
                RefreshProfileList(_controller.Preferences.SelectedProfileId ?? _controller.Preferences.Profiles.FirstOrDefault()?.Id);
                if (_profileList.SelectedItem == null) AddProfile(false);
                else LoadProfile(((ProfileListItem)_profileList.SelectedItem).Id);
                LoadGeneral();
                ApplyCurrentTheme();
            }
            finally
            {
                _loading = false;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WindowEffects.Apply(this, _palette, false);
        }

        private ModernButton IconButton(string glyph, string tip)
        {
            var button = new ModernButton("", ButtonVariant.Ghost, glyph) { Margin = new Padding(0, 0, UiHelpers.S(2), 0) };
            button.AccessibleName = tip;
            _toolTip.SetToolTip(button, tip);
            return button;
        }

        private void SystemPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            // "Follow system" should react while the window stays open; the event arrives on a system thread.
            if (e.Category != Microsoft.Win32.UserPreferenceCategory.General) return;
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((Action)delegate { if (!IsDisposed && SelectedAppearance() == "system") ApplyCurrentTheme(); });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
                _toolTip.Dispose();
                var icon = Icon;
                Icon = null;
                if (icon != null) icon.Dispose();
            }
            base.Dispose(disposing);
        }

        private static ComboBox DropDown(params string[] values)
        {
            var box = new ModernComboBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            box.Items.AddRange(values);
            return box;
        }

        private static void AddStackedField(TableLayoutPanel layout, int row, string label, TextBox box)
        {
            var stack = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, UiHelpers.S(12)) };
            stack.Controls.Add(new Label
            {
                Text = label,
                Font = UiFonts.BodyBold,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, UiHelpers.S(6))
            }, 0, 0);
            var frame = new InputFrame(box) { Margin = Padding.Empty };
            stack.Controls.Add(frame, 0, 1);
            frame.Dock = DockStyle.Fill;
            layout.Controls.Add(stack, 0, row);
        }

        private static void AddSettingRow(TableLayoutPanel layout, int row, string label, string description, Control field)
        {
            var text = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, UiHelpers.S(5), UiHelpers.S(12), UiHelpers.S(5)) };
            text.Controls.Add(new Label { Text = label, Font = UiFonts.BodyBold, AutoSize = true, Margin = Padding.Empty }, 0, 0);
            text.Controls.Add(new Label { Text = description, Tag = "muted", Font = UiFonts.Small, AutoSize = true, Margin = new Padding(0, UiHelpers.S(3), 0, 0) }, 0, 1);
            layout.Controls.Add(text, 0, row);
            field.Margin = new Padding(0, UiHelpers.S(8), 0, UiHelpers.S(5));
            field.Anchor = field is CheckBox ? AnchorStyles.Left : AnchorStyles.Left | AnchorStyles.Right;
            layout.Controls.Add(field, 1, row);
        }

        private static TableLayoutPanel ActionRow(out Label status)
        {
            var row = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, Height = UiHelpers.S(44), Margin = new Padding(0, UiHelpers.S(10), 0, 0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            status = new Label { Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = new Padding(0, 0, UiHelpers.S(12), 0) };
            row.Controls.Add(status, 0, 0);
            return row;
        }

        private void LayoutNav()
        {
            var right = _navBar.Width - UiHelpers.S(20);
            var top = (_navBar.Height - _generalNav.Height) / 2 + UiHelpers.S(2);
            _generalNav.Location = new Point(right - _generalNav.Width, top);
            _connectionsNav.Location = new Point(_generalNav.Left - _connectionsNav.Width - UiHelpers.S(6), top);
        }

        private void UpdateNav()
        {
            _connectionsNav.Selected = _tabs.SelectedIndex == 0;
            _generalNav.Selected = _tabs.SelectedIndex == 1;
        }

        private void PaintNavBar(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(_palette.Window);
            var left = UiHelpers.S(22);
            var logo = UiHelpers.S(36f);
            Draw.Logo(g, _palette, new RectangleF(left, (_navBar.Height - logo) / 2f + UiHelpers.S(2), logo, logo));
            var textLeft = left + (int)logo + UiHelpers.S(12);
            Draw.Text(g, "QuotaPeek 设置", UiFonts.Title, new Rectangle(textLeft, UiHelpers.S(18), UiHelpers.S(260), UiHelpers.S(24)), _palette.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            Draw.Text(g, "管理连接、刷新频率与提醒", UiFonts.Small, new Rectangle(textLeft, UiHelpers.S(42), UiHelpers.S(260), UiHelpers.S(20)), _palette.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private void DrawProfileItem(object sender, DrawItemEventArgs e)
        {
            var g = e.Graphics;
            var p = _palette;
            using (var background = new SolidBrush(p.Surface))
            {
                g.FillRectangle(background, e.Bounds);
            }
            if (e.Index < 0 || e.Index >= _profileList.Items.Count) return;
            var item = (ProfileListItem)_profileList.Items[e.Index];
            var selected = (e.State & DrawItemState.Selected) != 0;
            var rect = new RectangleF(e.Bounds.X + UiHelpers.S(2f), e.Bounds.Y + UiHelpers.S(2f), e.Bounds.Width - UiHelpers.S(4f), e.Bounds.Height - UiHelpers.S(4f));
            if (selected)
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Draw.Fill(g, UiHelpers.Blend(p.Surface, p.Accent, p.Dark ? 0.24 : 0.12), rect, UiHelpers.S(8f));
                Draw.Fill(g, p.Accent, new RectangleF(rect.X + UiHelpers.S(1f), rect.Y + UiHelpers.S(9f), UiHelpers.S(3f), rect.Height - UiHelpers.S(18f)), UiHelpers.S(1.5f));
            }
            var centerY = rect.Y + rect.Height / 2f;
            Draw.Dot(g, item.Enabled ? p.Good : p.Subtle, new PointF(rect.X + UiHelpers.S(16f), centerY), UiHelpers.S(7f), false);
            var textLeft = (int)rect.X + UiHelpers.S(28);
            var tagWidth = item.Enabled ? 0 : Draw.Measure("已暂停", UiFonts.Small).Width + UiHelpers.S(8);
            Draw.Text(g, item.Name, selected ? UiFonts.BodyBold : UiFonts.Body,
                new Rectangle(textLeft, (int)rect.Y, (int)rect.Right - textLeft - tagWidth - UiHelpers.S(8), (int)rect.Height),
                item.Enabled ? p.Text : p.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (!item.Enabled)
            {
                Draw.Text(g, "已暂停", UiFonts.Small, new Rectangle((int)rect.Right - tagWidth - UiHelpers.S(6), (int)rect.Y, tagWidth, (int)rect.Height),
                    p.Subtle, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        private void RefreshProfileList(string selectId)
        {
            _loading = true;
            try
            {
                _profileList.Items.Clear();
                foreach (var profile in _controller.Preferences.Profiles)
                {
                    _profileList.Items.Add(new ProfileListItem(profile.Id, profile.Name, profile.Enabled));
                }
                for (var index = 0; index < _profileList.Items.Count; index++)
                {
                    if (((ProfileListItem)_profileList.Items[index]).Id == selectId)
                    {
                        _profileList.SelectedIndex = index;
                        break;
                    }
                }
            }
            finally
            {
                _loading = false;
            }
            _emptyListLabel.Visible = _profileList.Items.Count == 0;
            _profileList.Visible = _profileList.Items.Count > 0;
            UpdateProfileButtons();
        }

        private void LoadProfile(string profileId)
        {
            var profile = _controller.Preferences.Profiles.FirstOrDefault(value => value.Id == profileId);
            if (profile == null) return;
            _loading = true;
            try
            {
                _draftId = profile.Id;
                _originalProfile = profile.Clone();
                _originalKey = _controller.GetKey(profile.Id);
                _nameBox.Text = profile.Name;
                _urlBox.Text = profile.BaseUrl;
                _keyBox.Text = _originalKey;
                _showKeyBox.Checked = false;
                _enabledBox.Checked = profile.Enabled;
                _connectionStatus.Text = "";
            }
            finally
            {
                _loading = false;
            }
            UpdateProfileButtons();
        }

        private void AddProfile(bool selectTab = true)
        {
            if (selectTab && !ConfirmDiscardConnection()) return;
            if (selectTab) _tabs.SelectedIndex = 0;
            var names = _controller.Preferences.Profiles.Select(profile => profile.Name).ToArray();
            var number = 1;
            while (names.Contains("vm-" + number.ToString("00"))) number++;
            _loading = true;
            try
            {
                _profileList.ClearSelected();
                _draftId = Guid.NewGuid().ToString("D");
                _originalProfile = null;
                _originalKey = "";
                _nameBox.Text = "vm-" + number.ToString("00");
                _urlBox.Text = _controller.Preferences.Profiles.FirstOrDefault()?.BaseUrl ?? "";
                _keyBox.Text = "";
                _showKeyBox.Checked = false;
                _enabledBox.Checked = true;
                _connectionStatus.Text = "新连接尚未保存。";
            }
            finally
            {
                _loading = false;
            }
            UpdateProfileButtons();
        }

        private async Task SaveProfileAsync()
        {
            if (_busy) return;
            var profile = new ConnectionProfile
            {
                Id = _draftId,
                Name = _nameBox.Text,
                BaseUrl = _urlBox.Text,
                Enabled = _enabledBox.Checked
            };
            SetBusy(true);
            _connectionStatus.Text = "正在验证连接…";
            _connectionStatus.ForeColor = _palette.Muted;
            try
            {
                var result = await _controller.SaveProfileAsync(profile, _keyBox.Text);
                SetStatus(_connectionStatus, result.Message, result.Success);
                if (result.Success)
                {
                    RefreshProfileList(profile.Id);
                    LoadProfile(profile.Id);
                    SetStatus(_connectionStatus, result.Message, true);
                }
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void DeleteProfile()
        {
            if (_originalProfile == null || _busy) return;
            if (MessageBox.Show(this, "只删除本机连接与保存的 API Key。是否继续？", "删除连接",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            var result = _controller.DeleteProfile(_originalProfile.Id);
            SetStatus(_connectionStatus, result.Message, result.Success);
            if (!result.Success) return;
            RefreshProfileList(_controller.Preferences.Profiles.FirstOrDefault()?.Id);
            if (_profileList.SelectedItem == null) AddProfile(false);
            else LoadProfile(((ProfileListItem)_profileList.SelectedItem).Id);
            SetStatus(_connectionStatus, result.Message, true);
        }

        private void MoveProfile(int offset)
        {
            if (_originalProfile == null || _busy) return;
            if (ConnectionDirty())
            {
                MessageBox.Show(this, "请先保存或放弃当前修改。", "QuotaPeek", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var result = _controller.MoveProfile(_originalProfile.Id, offset);
            SetStatus(_connectionStatus, result.Message, result.Success);
            if (result.Success) RefreshProfileList(_originalProfile.Id);
        }

        private bool ConnectionDirty()
        {
            if (_loading) return false;
            if (_originalProfile == null)
            {
                return !string.IsNullOrWhiteSpace(_urlBox.Text) || !string.IsNullOrWhiteSpace(_keyBox.Text);
            }
            return _nameBox.Text != _originalProfile.Name || _urlBox.Text != _originalProfile.BaseUrl ||
                   _keyBox.Text != _originalKey || _enabledBox.Checked != _originalProfile.Enabled;
        }

        private bool ConfirmDiscardConnection()
        {
            return !ConnectionDirty() || MessageBox.Show(this, "放弃当前未保存的连接修改？", "QuotaPeek",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private void ProfileSelectionChanged(object sender, EventArgs e)
        {
            if (_loading || _profileList.SelectedItem == null) return;
            var next = ((ProfileListItem)_profileList.SelectedItem).Id;
            if (_originalProfile != null && next == _originalProfile.Id) return;
            if (!ConfirmDiscardConnection())
            {
                RefreshProfileList(_originalProfile == null ? null : _originalProfile.Id);
                return;
            }
            LoadProfile(next);
        }

        private void TabsDeselecting(object sender, TabControlCancelEventArgs e)
        {
            if (_loading) return;
            var dirty = e.TabPageIndex == 0 ? ConnectionDirty() : GeneralDirty();
            if (!dirty) return;
            if (MessageBox.Show(this, "放弃当前页面未保存的修改？", "QuotaPeek", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            if (e.TabPageIndex == 0)
            {
                var id = _controller.Preferences.SelectedProfileId ?? _controller.Preferences.Profiles.FirstOrDefault()?.Id;
                RefreshProfileList(id);
                if (id == null) AddProfile(false); else LoadProfile(id);
            }
            else LoadGeneral();
        }

        private void UpdateProfileButtons()
        {
            var exists = _originalProfile != null;
            _deleteButton.Enabled = exists && !_busy;
            var index = exists ? _controller.Preferences.Profiles.FindIndex(profile => profile.Id == _originalProfile.Id) : -1;
            _upButton.Enabled = exists && !_busy && index > 0;
            _downButton.Enabled = exists && !_busy && index >= 0 && index < _controller.Preferences.Profiles.Count - 1;
        }

        private void SetBusy(bool value)
        {
            _busy = value;
            _saveProfileButton.Enabled = !value;
            _profileList.Enabled = !value;
            _tabs.Enabled = !value;
            _connectionsNav.Enabled = !value;
            _generalNav.Enabled = !value;
            UseWaitCursor = value;
            UpdateProfileButtons();
        }

        private void SetStatus(Label label, string message, bool success)
        {
            label.Text = message;
            label.ForeColor = success ? _palette.Good : _palette.Danger;
        }

        private void SettingsFormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason != CloseReason.UserClosing) return;
            if (_busy)
            {
                e.Cancel = true;
                return;
            }
            var dirty = _tabs.SelectedIndex == 0 ? ConnectionDirty() : GeneralDirty();
            if (dirty && MessageBox.Show(this, "放弃未保存的修改并关闭？", "QuotaPeek", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
            {
                e.Cancel = true;
            }
        }

        private sealed class ProfileListItem
        {
            public string Id { get; private set; }
            public string Name { get; private set; }
            public bool Enabled { get; private set; }

            public ProfileListItem(string id, string name, bool enabled)
            {
                Id = id;
                Name = name;
                Enabled = enabled;
            }

            public override string ToString()
            {
                return Enabled ? Name : Name + "（已暂停）";
            }
        }

        private sealed class NavPanel : Panel
        {
            public NavPanel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw, true);
            }
        }
    }
}
