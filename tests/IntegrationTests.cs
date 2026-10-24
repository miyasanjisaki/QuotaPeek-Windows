using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QuotaPeek.Windows
{
    internal static class IntegrationTests
    {
        private static int _passed;
        private static int _exitCode = 1;
        private static readonly List<string> TestCredentialIds = new List<string>();
        private const string FakeKey = "qp-integration-fictional-key";

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]
        private static extern IntPtr GetDlgItem(IntPtr dialog, int id);
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [STAThread]
        private static int Main()
        {
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Control.CheckForIllegalCrossThreadCalls = true;
            if (Directory.Exists(SettingsStore.DirectoryPath))
                throw new InvalidOperationException("Integration tests require a fresh isolated executable directory.");
            using (var controller = new AppController(new string[0]))
            using (var timer = new System.Windows.Forms.Timer { Interval = 200 })
            {
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    Console.Error.WriteLine(e.Exception);
                    controller.Quit();
                };
                timer.Tick += async delegate
                {
                    timer.Stop();
                    try
                    {
                        await RunAsync(controller);
                        Console.WriteLine("Integration tests passed: " + _passed);
                        Console.WriteLine("Window captures: " + AppDomain.CurrentDomain.BaseDirectory);
                        _exitCode = 0;
                    }
                    catch (Exception error) { Console.Error.WriteLine(error); }
                    finally { controller.Quit(); }
                };
                timer.Start();
                try { Application.Run(controller); }
                finally
                {
                    foreach (var id in TestCredentialIds) CredentialStore.Delete(id);
                    if (File.Exists(SettingsStore.FilePath)) File.Delete(SettingsStore.FilePath);
                    if (Directory.Exists(SettingsStore.DirectoryPath)) Directory.Delete(SettingsStore.DirectoryPath);
                }
            }
            return _exitCode;
        }

        private static async Task RunAsync(AppController controller)
        {
            var settings = Application.OpenForms.OfType<SettingsForm>().Single();
            True(settings.Visible, "first launch shows settings");
            Capture(settings, "settings-first-run.png");
            var tabs = Field<TabControl>(settings, "_tabs");
            tabs.SelectedIndex = 1;
            Capture(settings, "settings-general.png");
            tabs.SelectedIndex = 0;
            CredentialRoundTrip();

            using (var server = new LoopbackServer())
            {
                await VerifyHttpAsync(server);
                var profile = new ConnectionProfile { Name = "演示连接 · 模拟数据", BaseUrl = server.BaseUrl + "/ok" };
                TestCredentialIds.Add(profile.Id);
                True((await controller.SaveProfileAsync(profile, FakeKey)).Success, "validated profile saved");
                Equal(FakeKey, CredentialStore.Read(profile.Id), "profile credential round trip");
                var saved = File.ReadAllText(SettingsStore.FilePath, Encoding.UTF8);
                True(!saved.Contains(FakeKey), "preferences contain no API key");
                Equal(profile.Name, SettingsStore.Load().Profiles.Single().Name, "UTF-8 settings reload");
                Equal(4, controller.States[profile.Id].Snapshot.Windows.Count, "all four windows retained");

                using (var locked = new FileStream(SettingsStore.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    var failed = await controller.SaveProfileAsync(profile, FakeKey + "-changed");
                    True(!failed.Success, "locked settings file blocks save");
                    Equal(FakeKey, CredentialStore.Read(profile.Id), "failed save restores previous credential");
                }

                settings.RefreshFromModel();
                VerifyDraftDiscard(settings);
                controller.Preferences.Appearance = "light";
                controller.ShowDashboard();
                var dashboard = Application.OpenForms.OfType<DashboardForm>().Single();
                var content = Field<FlowLayoutPanel>(dashboard, "_content");
                True(content.Width <= dashboard.ClientSize.Width && content.Height >= 300, "dashboard layout preserves quota area");
                Equal(content.Controls[0].Top, content.Controls[1].Top, "quota cards fit in two columns with scrollbar");
                Capture(dashboard, "dashboard-light.png");
                var oldCards = Field<FlowLayoutPanel>(dashboard, "_content").Controls.Cast<Control>().ToArray();
                controller.Preferences.Appearance = "dark";
                dashboard.RefreshView();
                True(oldCards.All(card => card.IsDisposed), "refresh disposes old quota controls");
                Capture(dashboard, "dashboard-dark.png");
                controller.SelectProfile(null);
                dashboard.RefreshView();
                Capture(dashboard, "dashboard-overview.png");
                controller.SelectProfile(profile.Id);
                dashboard.RefreshView();
                dashboard.Close();
                True(!dashboard.Visible && !dashboard.IsDisposed, "close hides dashboard");
                dashboard.RefreshView();
                controller.Preferences.Appearance = "light";
                dashboard.RefreshView();
                Application.DoEvents();
                True(!IsWindowVisible(dashboard.Handle), "background refresh keeps a hidden dashboard hidden");
                controller.ShowDashboard();
                True(dashboard.Visible, "tray action reopens dashboard");
                await VerifyTrayToggleAsync(controller, dashboard);
                await VerifySecondLaunchShowsFlyoutAsync(controller, dashboard);
                VerifyManySlots(controller, dashboard);
                VerifyProfilesMustBeArray();

                var failedProfile = profile.Clone();
                failedProfile.BaseUrl = server.BaseUrl + "/unauthorized";
                True(!(await controller.SaveProfileAsync(failedProfile, FakeKey)).Success, "failed validation preserves saved profile");
                Equal(profile.BaseUrl, controller.Preferences.Profiles.Single().BaseUrl, "failed validation keeps URL");

                await VerifyQueuedEditAsync(controller, profile, server);
                var resume = typeof(AppController).GetMethod("PowerModeChanged", BindingFlags.Instance | BindingFlags.NonPublic);
                var resumed = new TaskCompletionSource<bool>();
                EventHandler observeResume = delegate
                {
                    if (!controller.IsRefreshing) resumed.TrySetResult(true);
                };
                controller.StateChanged += observeResume;
                await Task.Run(() => resume.Invoke(controller, new object[]
                {
                    null, new Microsoft.Win32.PowerModeChangedEventArgs(Microsoft.Win32.PowerModes.Resume)
                }));
                True(await Task.WhenAny(resumed.Task, Task.Delay(3000)) == resumed.Task, "resume callback safely refreshes from a worker thread");
                controller.StateChanged -= observeResume;

                controller.Preferences.Profiles[0].BaseUrl = server.BaseUrl + "/unauthorized";
                await controller.RefreshAllAsync();
                Equal("数据已过期", controller.GetStatusTitle(), "refresh failure marks last good snapshot stale");
                True(controller.States[profile.Id].Snapshot != null, "last good quota retained after HTTP error");
                dashboard.RefreshView();
                Capture(dashboard, "dashboard-stale.png");
                True(controller.DeleteProfile(profile.Id).Success, "delete profile succeeds");
                Equal("", CredentialStore.Read(profile.Id), "delete removes its credential");
            }
            settings.Dispose();
            controller.ShowSettings();
            True(Application.OpenForms.OfType<SettingsForm>().Single().Visible, "closed settings can be recreated");
            var released = OpenAndCloseSettings(controller, 4);
            // Pending window messages briefly reference closed forms, so drain them before collecting.
            for (var round = 0; round < 5; round++)
            {
                Application.DoEvents();
                await Task.Delay(100);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            // The controller keeps a reference to the most recent window, so only the earlier ones must be gone.
            True(released.Take(released.Count - 1).All(reference => !reference.IsAlive), "closed settings windows are released");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static List<WeakReference> OpenAndCloseSettings(AppController controller, int count)
        {
            var references = new List<WeakReference>();
            for (var index = 0; index < count; index++)
            {
                controller.ShowSettings();
                var form = Application.OpenForms.OfType<SettingsForm>().Single();
                references.Add(new WeakReference(form));
                form.Close();
                Application.DoEvents();
            }
            return references;
        }

        private static void TrayPress(AppController controller, string method)
        {
            typeof(AppController).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, new object[] { null, new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0) });
        }

        private static void Deactivate(DashboardForm dashboard)
        {
            typeof(DashboardForm).GetMethod("OnDeactivate", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(dashboard, new object[] { EventArgs.Empty });
        }

        private static async Task VerifyTrayToggleAsync(AppController controller, DashboardForm dashboard)
        {
            TrayPress(controller, "TrayIconMouseDown");
            TrayPress(controller, "TrayIconMouseClick");
            True(!dashboard.Visible, "tray click closes an open flyout");
            await Task.Delay(400);
            TrayPress(controller, "TrayIconMouseDown");
            TrayPress(controller, "TrayIconMouseClick");
            True(dashboard.Visible, "tray click opens a closed flyout");

            // Pressing the tray icon deactivates the flyout first; that press must still close it.
            Deactivate(dashboard);
            TrayPress(controller, "TrayIconMouseDown");
            TrayPress(controller, "TrayIconMouseClick");
            True(!dashboard.Visible, "tray press that deactivated the flyout keeps it closed");

            // Explicit requests (menu, notification) are never swallowed right after an auto-hide.
            controller.ShowDashboard();
            Deactivate(dashboard);
            controller.ShowDashboard();
            True(dashboard.Visible, "explicit show right after auto-hide opens the flyout");

            // A long press on a closed flyout's icon still opens it.
            dashboard.HideFlyout();
            await Task.Delay(400);
            TrayPress(controller, "TrayIconMouseDown");
            await Task.Delay(600);
            TrayPress(controller, "TrayIconMouseClick");
            True(dashboard.Visible, "long tray press opens a closed flyout");
        }

        private static async Task VerifySecondLaunchShowsFlyoutAsync(AppController controller, DashboardForm dashboard)
        {
            var name = @"Local\QuotaPeek.IntegrationTest." + Guid.NewGuid().ToString("N");
            using (var primary = new SingleInstance(name))
            {
                True(primary.IsPrimary, "first launch owns the instance");
                primary.ListenForShowRequests(controller.RequestShowDashboard);
                dashboard.HideFlyout();
                await Task.Delay(400);
                using (var second = new SingleInstance(name))
                {
                    True(!second.IsPrimary, "second launch detects the running instance");
                    True(second.SignalPrimary(), "second launch reaches the running instance");
                }
                for (var waited = 0; waited < 3000 && !dashboard.Visible; waited += 50) await Task.Delay(50);
                True(dashboard.Visible, "second launch shows the quota flyout");
            }
        }

        private static void VerifyManySlots(AppController controller, DashboardForm dashboard)
        {
            var preferences = controller.Preferences;
            var originalSelection = preferences.SelectedProfileId;
            var originalCount = preferences.Profiles.Count;
            for (var index = 0; index < 24; index++)
            {
                preferences.Profiles.Add(new ConnectionProfile { Name = "一个名字相当长的测试槽位 " + index, BaseUrl = "https://example.invalid", Enabled = true });
            }
            try
            {
                preferences.SelectedProfileId = null;
                dashboard.RefreshView();
                var chips = Field<FlowLayoutPanel>(dashboard, "_chips");
                var content = Field<FlowLayoutPanel>(dashboard, "_content");
                True(chips.Height <= UiHelpers.S(3 * 34) + chips.Padding.Vertical, "slot tabs are capped at three rows");
                True(content.Height >= UiHelpers.S(150), "quota area keeps usable height with many slots");
                True(content.AutoScroll, "many slots make the overview scroll");
                content.AutoScrollPosition = new Point(0, UiHelpers.S(300));
                var before = -content.AutoScrollPosition.Y;
                dashboard.RefreshView();
                True(before > 0 && -content.AutoScrollPosition.Y == before, "background refresh keeps the scroll position");
            }
            finally
            {
                preferences.Profiles.RemoveRange(originalCount, preferences.Profiles.Count - originalCount);
                preferences.SelectedProfileId = originalSelection;
                dashboard.RefreshView();
            }
        }

        private static void VerifyProfilesMustBeArray()
        {
            var read = typeof(SettingsStore).GetMethod("ReadProfiles", BindingFlags.Static | BindingFlags.NonPublic);
            try
            {
                read.Invoke(null, new object[] { new Dictionary<string, object>() });
                throw new InvalidOperationException("profiles object accepted as an empty list");
            }
            catch (TargetInvocationException error) { True(error.InnerException is InvalidDataException, "profiles object blocks loading"); }
            Equal(0, ((List<ConnectionProfile>)read.Invoke(null, new object[] { new object[0] })).Count, "empty profiles array loads");
        }

        private static async Task VerifyQueuedEditAsync(AppController controller, ConnectionProfile profile, LoopbackServer server)
        {
            var gate = Field<SemaphoreSlim>(controller, "_requestGate");
            for (var i = 0; i < 3; i++) gate.Wait();
            var oldPathCount = server.Paths.Count(path => path == "/ok/v1/usage");
            var queued = controller.RefreshAllAsync();
            try
            {
                var updated = profile.Clone();
                updated.BaseUrl = server.BaseUrl + "/updated";
                True((await controller.SaveProfileAsync(updated, FakeKey + "-updated")).Success, "edit succeeds while old refresh waits for a slot");
            }
            finally { gate.Release(3); }
            await queued;
            Equal(oldPathCount, server.Paths.Count(path => path == "/ok/v1/usage"), "queued refresh never sends the new key to the old URL");
        }

        private static void CredentialRoundTrip()
        {
            var id = "integration-" + Guid.NewGuid().ToString("N");
            TestCredentialIds.Add(id);
            Equal("", CredentialStore.Read(id), "fresh test credential absent");
            CredentialStore.Save(id, FakeKey);
            Equal(FakeKey, CredentialStore.Read(id), "credential write/read");
            CredentialStore.Save(id, FakeKey + "-updated");
            Equal(FakeKey + "-updated", CredentialStore.Read(id), "credential update/read");
            CredentialStore.Delete(id);
            Equal("", CredentialStore.Read(id), "credential delete");
        }

        private static async Task VerifyHttpAsync(LoopbackServer server)
        {
            var snapshot = await QuotaClient.FetchAsync(server.BaseUrl + "/ok", FakeKey, TimeSpan.FromSeconds(5), CancellationToken.None);
            Equal(24.5d, snapshot.Windows[0].UsedPercent, "HTTP parses real response bytes");
            Equal("GET", server.LastMethod, "HTTP method");
            Equal("/ok/v1/usage", server.Paths.Last(), "HTTP normalized request path");
            Equal("Bearer " + FakeKey, server.LastAuthorization, "HTTP bearer authorization");
            await ExpectQuotaError(server, "/redirect", QuotaErrorKind.RedirectForbidden);
            True(!server.Paths.Any(path => path.StartsWith("/leak/")), "redirect target never requested");
            await ExpectQuotaError(server, "/unauthorized", QuotaErrorKind.Http);
            await ExpectQuotaError(server, "/oversized", QuotaErrorKind.ResponseTooLarge);
            await ExpectQuotaError(server, "/streamlarge", QuotaErrorKind.ResponseTooLarge);
            var exact = await QuotaClient.FetchAsync(server.BaseUrl + "/exact", FakeKey, TimeSpan.FromSeconds(5), CancellationToken.None);
            Equal(4, exact.Windows.Count, "exact 1 MiB response accepted");
            await ExpectQuotaError(server, "/slowheaders", QuotaErrorKind.TimedOut, 1);
            await ExpectQuotaError(server, "/slowbody", QuotaErrorKind.TimedOut, 1);
            using (var cancellation = new CancellationTokenSource(100))
            {
                try
                {
                    await QuotaClient.FetchAsync(server.BaseUrl + "/slowheaders", FakeKey, TimeSpan.FromSeconds(5), cancellation.Token);
                    throw new InvalidOperationException("External cancellation was ignored.");
                }
                catch (OperationCanceledException) { _passed++; }
            }
        }

        private static async Task ExpectQuotaError(LoopbackServer server, string path, QuotaErrorKind kind, int timeoutSeconds = 5)
        {
            var elapsed = Stopwatch.StartNew();
            try
            {
                await QuotaClient.FetchAsync(server.BaseUrl + path, FakeKey, TimeSpan.FromSeconds(timeoutSeconds), CancellationToken.None);
                throw new InvalidOperationException("Expected " + kind + " from " + path);
            }
            catch (QuotaException error) { Equal(kind, error.Kind, path); }
            if (kind == QuotaErrorKind.TimedOut) True(elapsed.Elapsed < TimeSpan.FromSeconds(2), path + " timeout enforced during I/O");
        }

        private static void VerifyDraftDiscard(SettingsForm settings)
        {
            var tabs = Field<TabControl>(settings, "_tabs");
            var name = Field<TextBox>(settings, "_nameBox");
            var original = name.Text;
            name.Text = "unsaved change";
            AnswerNextDialog(7, () => tabs.SelectedIndex = 1);
            Equal(0, tabs.SelectedIndex, "declining discard keeps connection tab");
            Equal("unsaved change", name.Text, "declining discard keeps draft");
            AnswerNextDialog(6, () => tabs.SelectedIndex = 1);
            Equal(1, tabs.SelectedIndex, "confirming discard changes tab");
            tabs.SelectedIndex = 0;
            Equal(original, name.Text, "discarded connection draft does not return");
            tabs.SelectedIndex = 1;
            var display = Field<ComboBox>(settings, "_displayBox");
            display.SelectedIndex = 1;
            AnswerNextDialog(6, () => tabs.SelectedIndex = 0);
            tabs.SelectedIndex = 1;
            Equal(0, display.SelectedIndex, "discarded general draft does not return");
            tabs.SelectedIndex = 0;
        }

        private static void AnswerNextDialog(int buttonId, Action action)
        {
            // WinForms timers run inside MessageBox's nested message loop.
            using (var timer = new System.Windows.Forms.Timer { Interval = 100 })
            {
                var clicked = false;
                timer.Tick += delegate
                {
                    var dialog = FindWindow("#32770", "QuotaPeek");
                    if (dialog == IntPtr.Zero) return;
                    clicked = true;
                    timer.Stop();
                    SendMessage(GetDlgItem(dialog, buttonId), 0x00F5, IntPtr.Zero, IntPtr.Zero);
                };
                timer.Start();
                action();
                True(clicked, "draft change requests a discard decision");
            }
        }

        private static void Capture(Form form, string name)
        {
            form.PerformLayout();
            form.Refresh();
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name), ImageFormat.Png);
            }
        }

        private static T Field<T>(object value, string name)
        {
            return (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
        }

        private static void True(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            _passed++;
        }

        private static void Equal<T>(T expected, T actual, string label)
        {
            if (!object.Equals(expected, actual)) throw new InvalidOperationException(label + ": values differ");
            _passed++;
        }
    }
}
