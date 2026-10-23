using System;

namespace QuotaPeek.Windows
{
    internal sealed partial class SettingsForm
    {
        private void LoadGeneral()
        {
            var preferences = _controller.Preferences;
            _refreshBox.SelectedIndex = Array.IndexOf(new[] { 1, 2, 5, 15, 30 }, preferences.RefreshMinutes);
            _displayBox.SelectedIndex = preferences.ShowRemaining ? 0 : 1;
            _trayModeBox.SelectedIndex = preferences.TrayMode == "tightest" ? 1 : 0;
            _showNameBox.Checked = preferences.ShowProfileName;
            _appearanceBox.SelectedIndex = preferences.Appearance == "dark" ? 2 : preferences.Appearance == "light" ? 1 : 0;
            _notificationsBox.Checked = preferences.Notifications;
            _warningBox.SelectedIndex = Array.IndexOf(new[] { 50, 60, 70, 80, 90, 95 }, preferences.WarningPercent);
            _warningBox.Enabled = _notificationsBox.Checked;
            _startupBox.Checked = _controller.IsRunAtLoginEnabled();
            _generalStatus.Text = "";
        }

        private void SaveGeneral()
        {
            if (_busy) return;
            var refreshValues = new[] { 1, 2, 5, 15, 30 };
            var warningValues = new[] { 50, 60, 70, 80, 90, 95 };
            var candidate = _controller.Preferences.Clone();
            candidate.RefreshMinutes = refreshValues[Math.Max(0, _refreshBox.SelectedIndex)];
            candidate.ShowRemaining = _displayBox.SelectedIndex != 1;
            candidate.TrayMode = _trayModeBox.SelectedIndex == 1 ? "tightest" : "selected";
            candidate.ShowProfileName = _showNameBox.Checked;
            candidate.Appearance = SelectedAppearance();
            candidate.Notifications = _notificationsBox.Checked;
            candidate.WarningPercent = warningValues[Math.Max(0, _warningBox.SelectedIndex)];
            var result = _controller.SaveGeneral(candidate, _startupBox.Checked);
            SetStatus(_generalStatus, result.Message, result.Success);
            if (result.Success)
            {
                LoadGeneral();
                ApplyCurrentTheme();
                SetStatus(_generalStatus, result.Message, true);
            }
        }

        private bool GeneralDirty()
        {
            if (_loading) return false;
            var preferences = _controller.Preferences;
            var refreshValues = new[] { 1, 2, 5, 15, 30 };
            var warningValues = new[] { 50, 60, 70, 80, 90, 95 };
            return refreshValues[Math.Max(0, _refreshBox.SelectedIndex)] != preferences.RefreshMinutes ||
                   (_displayBox.SelectedIndex != 1) != preferences.ShowRemaining ||
                   (_trayModeBox.SelectedIndex == 1 ? "tightest" : "selected") != preferences.TrayMode ||
                   _showNameBox.Checked != preferences.ShowProfileName ||
                   SelectedAppearance() != preferences.Appearance ||
                   _notificationsBox.Checked != preferences.Notifications ||
                   warningValues[Math.Max(0, _warningBox.SelectedIndex)] != preferences.WarningPercent ||
                   _startupBox.Checked != _controller.IsRunAtLoginEnabled();
        }

        private string SelectedAppearance()
        {
            return _appearanceBox.SelectedIndex == 2 ? "dark" : _appearanceBox.SelectedIndex == 1 ? "light" : "system";
        }

        private void ApplyCurrentTheme()
        {
            _palette = ThemePalette.For(SelectedAppearance());
            UiHelpers.ApplyTheme(this, _palette);
            WindowEffects.Apply(this, _palette, false);
            Invalidate(true);
        }
    }
}
