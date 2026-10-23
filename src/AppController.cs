using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using FormsTimer = System.Windows.Forms.Timer;

namespace QuotaPeek.Windows
{
    internal sealed partial class AppController : ApplicationContext
    {
        private const string RunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "QuotaPeek";

        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _trayMenu;
        private readonly FormsTimer _refreshTimer;
        private readonly FormsTimer _statusTimer;
        private readonly SemaphoreSlim _requestGate = new SemaphoreSlim(3, 3);
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private readonly Dictionary<string, ProfileState> _states = new Dictionary<string, ProfileState>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _apiKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _notifiedWindows = new HashSet<string>(StringComparer.Ordinal);
        private readonly string[] _arguments;
        private readonly Control _uiDispatcher;
        private Icon _currentIcon;
        private DashboardForm _dashboard;
        private SettingsForm _settings;
        private bool _storageAvailable;
        private bool _exiting;
        private bool _trayClickCloses;

        public Preferences Preferences { get; private set; }
        public string GlobalError { get; private set; }
        public IDictionary<string, ProfileState> States { get { return _states; } }
        public event EventHandler StateChanged;

        public AppController(string[] arguments)
        {
            _uiDispatcher = new Control();
            _uiDispatcher.CreateControl();
            _arguments = arguments ?? new string[0];
            LoadConfiguration();

            _trayMenu = new ContextMenuStrip();
            _trayMenu.Opening += delegate { BuildTrayMenu(); };
            _trayIcon = new NotifyIcon
            {
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _trayIcon.MouseDown += TrayIconMouseDown;
            _trayIcon.MouseClick += TrayIconMouseClick;
            _trayIcon.BalloonTipClicked += delegate { ShowDashboard(); };

            _refreshTimer = new FormsTimer();
            _refreshTimer.Tick += async delegate { await RefreshAllAsync(); };
            _statusTimer = new FormsTimer { Interval = 30_000 };
            _statusTimer.Tick += delegate { NotifyStateChanged(); };
            _statusTimer.Start();
            RestartRefreshTimer();
            SystemEvents.PowerModeChanged += PowerModeChanged;
            UpdateTray();

            var startupTimer = new FormsTimer { Interval = 10 };
            startupTimer.Tick += async delegate
            {
                startupTimer.Stop();
                startupTimer.Dispose();
                if (Preferences.Profiles.Count == 0 || _arguments.Contains("--settings", StringComparer.OrdinalIgnoreCase))
                {
                    ShowSettings();
                }
                else
                {
                    await RefreshAllAsync();
                    if (_arguments.Contains("--show", StringComparer.OrdinalIgnoreCase)) ShowDashboard();
                }
            };
            startupTimer.Start();
        }

        private void LoadConfiguration()
        {
            try
            {
                Preferences = SettingsStore.Load();
                _storageAvailable = true;
                foreach (var profile in Preferences.Profiles)
                {
                    var state = new ProfileState();
                    try
                    {
                        var key = CredentialStore.Read(profile.Id);
                        _apiKeys[profile.Id] = key;
                        if (key.Length == 0) state.Error = "未找到此连接的 API Key，请在设置中重新保存。";
                    }
                    catch
                    {
                        state.Error = "无法读取 Windows 凭据管理器中的 API Key，请在设置中重新保存。";
                    }
                    _states[profile.Id] = state;
                }
            }
            catch
            {
                Preferences = new Preferences();
                _storageAvailable = false;
                GlobalError = "读取本机配置失败。为避免覆盖原有连接，已停止保存；请检查 preferences.json 后重新启动。";
            }
        }

        public ConnectionProfile SelectedProfile
        {
            get { return Preferences.Profiles.FirstOrDefault(profile => profile.Id == Preferences.SelectedProfileId); }
        }

        public bool IsRefreshing
        {
            get { return _states.Values.Any(state => state.Refreshing); }
        }

        public bool IsStale(ProfileState state)
        {
            if (state == null || state.Snapshot == null) return false;
            return state.Error != null || DateTimeOffset.Now - state.Snapshot.FetchedAt > TimeSpan.FromMinutes(Math.Max(1, Preferences.RefreshMinutes) * 2);
        }

        public string GetStatusTitle()
        {
            if (GlobalError != null) return "配置异常";
            if (Preferences.Profiles.Count == 0) return "待配置";
            var selected = SelectedProfile;
            if (selected != null)
            {
                if (!selected.Enabled) return "已暂停";
                ProfileState state;
                _states.TryGetValue(selected.Id, out state);
                if (state != null && state.Error != null) return state.Snapshot == null ? "连接异常" : "数据已过期";
                if (state != null && state.Refreshing) return "更新中";
                if (IsStale(state)) return "数据已过期";
                return UsageStatus(MostUsed(state));
            }
            if (!Preferences.Profiles.Any(profile => profile.Enabled)) return "全部已暂停";
            if (HasProfileIssues()) return Tightest() == null ? "连接异常" : "部分异常";
            if (IsRefreshing) return "更新中";
            var tightest = Tightest();
            return UsageStatus(tightest == null ? null : tightest.Window);
        }

        public string GetKey(string profileId)
        {
            string value;
            if (_apiKeys.TryGetValue(profileId, out value)) return value;
            try
            {
                value = CredentialStore.Read(profileId);
                _apiKeys[profileId] = value;
                return value;
            }
            catch
            {
                return "";
            }
        }

        public bool IsRunAtLoginEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath))
                {
                    return key != null && key.GetValue(RunValueName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public void ShowDashboard()
        {
            if (_dashboard == null || _dashboard.IsDisposed) _dashboard = new DashboardForm(this);
            _dashboard.RefreshView();
            _dashboard.ShowNearCursor();
        }

        public void ShowSettings()
        {
            if (_settings == null || _settings.IsDisposed) _settings = new SettingsForm(this);
            _settings.RefreshFromModel();
            _settings.Show();
            if (_settings.WindowState == FormWindowState.Minimized) _settings.WindowState = FormWindowState.Normal;
            _settings.Activate();
        }

        public void Quit()
        {
            ExitThread();
        }

        private void BuildTrayMenu()
        {
            _trayMenu.Items.Clear();
            var overview = new ToolStripMenuItem("全部槽位") { Checked = Preferences.SelectedProfileId == null };
            overview.Click += delegate { SelectProfile(null); ShowDashboard(); };
            _trayMenu.Items.Add(overview);
            foreach (var item in Preferences.Profiles)
            {
                var profileId = item.Id;
                var menuItem = new ToolStripMenuItem(item.Enabled ? item.Name : item.Name + "（已暂停）")
                {
                    Checked = Preferences.SelectedProfileId == item.Id
                };
                menuItem.Click += delegate { SelectProfile(profileId); ShowDashboard(); };
                _trayMenu.Items.Add(menuItem);
            }
            _trayMenu.Items.Add(new ToolStripSeparator());
            var refresh = new ToolStripMenuItem("全部刷新");
            refresh.Click += async delegate { await RefreshAllAsync(); };
            _trayMenu.Items.Add(refresh);
            var settings = new ToolStripMenuItem("设置…");
            settings.Click += delegate { ShowSettings(); };
            _trayMenu.Items.Add(settings);
            _trayMenu.Items.Add(new ToolStripSeparator());
            var exit = new ToolStripMenuItem("退出 QuotaPeek");
            exit.Click += delegate { Quit(); };
            _trayMenu.Items.Add(exit);
        }

        private void TrayIconMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            // Decide at press time: the press itself deactivates and hides an open flyout, so by the time the
            // click completes it can no longer be told apart from a closed one.
            _trayClickCloses = _dashboard != null && !_dashboard.IsDisposed && _dashboard.IsOpenOrJustClosed;
        }

        private void TrayIconMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var closes = _trayClickCloses;
            _trayClickCloses = false;
            if (closes) _dashboard.HideFlyout();
            else ShowDashboard();
        }

        private void UpdateTray()
        {
            var display = ResolveTrayDisplay();
            var hasError = GlobalError != null || HasProfileIssues();
            var newIcon = IconFactory.Create(display == null ? (double?)null : DisplayedPercent(display.Window),
                display == null ? (double?)null : display.Window.UsedPercent, Preferences.WarningPercent, hasError);
            _trayIcon.Icon = newIcon;
            var oldIcon = _currentIcon;
            _currentIcon = newIcon;
            if (oldIcon != null) oldIcon.Dispose();

            var profileName = display != null && Preferences.ShowProfileName ? display.Profile.Name + " · " : "";
            var detail = display == null
                ? GetStatusTitle()
                : profileName + display.Window.Title + " " +
                  (Preferences.ShowRemaining ? "剩余 " : "已用 ") + UiHelpers.Percent(DisplayedPercent(display.Window));
            var tooltip = "QuotaPeek · " + detail;
            if (tooltip.Length > 63) tooltip = tooltip.Substring(0, 63);
            _trayIcon.Text = tooltip;
        }

        private TrayDisplay ResolveTrayDisplay()
        {
            if (Preferences.TrayMode == "selected")
            {
                var profile = SelectedProfile;
                if (profile != null)
                {
                    if (!profile.Enabled) return null;
                    ProfileState state;
                    _states.TryGetValue(profile.Id, out state);
                    var window = MostUsed(state);
                    return window == null ? null : new TrayDisplay(profile, window);
                }
            }
            return Tightest();
        }

        private TrayDisplay Tightest()
        {
            TrayDisplay result = null;
            foreach (var profile in Preferences.Profiles.Where(value => value.Enabled))
            {
                ProfileState state;
                if (!_states.TryGetValue(profile.Id, out state) || state.Error != null || IsStale(state)) continue;
                var window = MostUsed(state);
                if (window != null && (result == null || window.UsedPercent > result.Window.UsedPercent))
                {
                    result = new TrayDisplay(profile, window);
                }
            }
            return result;
        }

        private QuotaWindow MostUsed(ProfileState state)
        {
            return state == null || state.Snapshot == null ? null : state.Snapshot.Windows.OrderByDescending(window => window.UsedPercent).FirstOrDefault();
        }

        private double DisplayedPercent(QuotaWindow window)
        {
            return Preferences.ShowRemaining ? 100 - window.UsedPercent : window.UsedPercent;
        }

        private string UsageStatus(QuotaWindow window)
        {
            if (window == null) return "等待数据";
            if (window.UsedPercent >= 100) return "额度耗尽";
            return window.UsedPercent >= Preferences.WarningPercent ? "留意用量" : "额度充足";
        }

        private bool HasProfileIssues()
        {
            foreach (var profile in Preferences.Profiles.Where(value => value.Enabled))
            {
                ProfileState state;
                if (_states.TryGetValue(profile.Id, out state) && (state.Error != null || IsStale(state))) return true;
            }
            return false;
        }

        private ProfileState GetState(string profileId)
        {
            ProfileState state;
            if (!_states.TryGetValue(profileId, out state))
            {
                state = new ProfileState();
                _states[profileId] = state;
            }
            return state;
        }

        private void NotifyIfNeeded(ConnectionProfile profile, QuotaSnapshot snapshot)
        {
            if (!Preferences.Notifications || !profile.Enabled) return;
            var active = new HashSet<string>(snapshot.Windows.Select(window => NotificationKey(profile.Id, window)), StringComparer.Ordinal);
            _notifiedWindows.RemoveWhere(value => value.StartsWith(profile.Id + "|", StringComparison.Ordinal) && !active.Contains(value));
            foreach (var window in snapshot.Windows.Where(value => value.UsedPercent >= Preferences.WarningPercent))
            {
                var key = NotificationKey(profile.Id, window);
                if (!_notifiedWindows.Add(key)) continue;
                _trayIcon.BalloonTipTitle = profile.Name + " · 额度提醒";
                _trayIcon.BalloonTipText = window.Title + "已使用 " + Math.Round(window.UsedPercent, MidpointRounding.AwayFromZero) + "% ，请留意重置时间。";
                _trayIcon.BalloonTipIcon = ToolTipIcon.Warning;
                _trayIcon.ShowBalloonTip(5000);
            }
        }

        private static string NotificationKey(string profileId, QuotaWindow window)
        {
            return profileId + "|" + window.Id + "|" + (window.ResetsAt.HasValue ? window.ResetsAt.Value.ToUnixTimeSeconds().ToString() : "none");
        }

        private void ClearNotificationState(string profileId)
        {
            _notifiedWindows.RemoveWhere(value => value.StartsWith(profileId + "|", StringComparison.Ordinal));
        }

        private bool RestoreCredential(string profileId, string value)
        {
            try
            {
                if (string.IsNullOrEmpty(value)) CredentialStore.Delete(profileId);
                else CredentialStore.Save(profileId, value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void DisableStorageAfterRollbackFailure()
        {
            _storageAvailable = false;
            GlobalError = "配置保存失败，且此连接的 Windows 凭据无法恢复。已停止继续保存；请检查本机配置后重新启动。";
            NotifyStateChanged();
        }

        private void SetRunAtLogin(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunRegistryPath))
            {
                if (key == null) throw new InvalidOperationException("无法打开启动项注册表。");
                if (enabled)
                {
                    key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\" --background", RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(RunValueName, false);
                }
            }
        }

        private void RestartRefreshTimer()
        {
            _refreshTimer.Stop();
            if (Preferences.Profiles.Any(profile => profile.Enabled))
            {
                _refreshTimer.Interval = Preferences.RefreshMinutes * 60_000;
                _refreshTimer.Start();
            }
        }

        private void PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume && !_exiting)
            {
                _uiDispatcher.BeginInvoke((Action)(async () => await RefreshAllAsync()));
            }
        }

        private void NotifyStateChanged()
        {
            if (_exiting) return;
            UpdateTray();
            var handler = StateChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        protected override void ExitThreadCore()
        {
            if (_exiting) return;
            _exiting = true;
            SystemEvents.PowerModeChanged -= PowerModeChanged;
            _shutdown.Cancel();
            _refreshTimer.Stop();
            _statusTimer.Stop();
            _trayIcon.Visible = false;
            if (_dashboard != null) _dashboard.Dispose();
            if (_settings != null) _settings.Dispose();
            _trayIcon.Dispose();
            _trayMenu.Dispose();
            if (_currentIcon != null) _currentIcon.Dispose();
            _refreshTimer.Dispose();
            _statusTimer.Dispose();
            _uiDispatcher.Dispose();
            base.ExitThreadCore();
        }

        private sealed class TrayDisplay
        {
            public ConnectionProfile Profile { get; private set; }
            public QuotaWindow Window { get; private set; }

            public TrayDisplay(ConnectionProfile profile, QuotaWindow window)
            {
                Profile = profile;
                Window = window;
            }
        }
    }
}
