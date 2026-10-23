using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QuotaPeek.Windows
{
    internal sealed partial class AppController
    {
        public async Task RefreshSelectedAsync()
        {
            var selected = SelectedProfile;
            if (selected == null) await RefreshAllAsync();
            else await RefreshProfileAsync(selected.Id);
        }

        public async Task RefreshAllAsync()
        {
            var tasks = Preferences.Profiles.Where(profile => profile.Enabled)
                .Select(profile => RefreshProfileAsync(profile.Id)).ToArray();
            await Task.WhenAll(tasks);
        }

        private async Task RefreshProfileAsync(string profileId)
        {
            var profile = Preferences.Profiles.FirstOrDefault(value => value.Id == profileId && value.Enabled);
            if (profile == null) return;
            var state = GetState(profileId);
            if (state.Refreshing) return;

            state.Refreshing = true;
            state.LastAttempt = DateTimeOffset.Now;
            var version = ++state.RequestVersion;
            NotifyStateChanged();

            var acquired = false;
            try
            {
                await _requestGate.WaitAsync(_shutdown.Token);
                acquired = true;
                var queuedProfile = Preferences.Profiles.FirstOrDefault(value => value.Id == profileId);
                if (state.RequestVersion != version || queuedProfile == null || !queuedProfile.Enabled ||
                    queuedProfile.BaseUrl != profile.BaseUrl) return;
                var key = GetKey(profileId);
                if (key.Length == 0)
                {
                    state.Error = "未找到此连接的 API Key，请在设置中重新保存。";
                    return;
                }
                var snapshot = await QuotaClient.FetchAsync(profile.BaseUrl, key, TimeSpan.FromSeconds(15), _shutdown.Token);
                var current = Preferences.Profiles.FirstOrDefault(value => value.Id == profileId);
                if (state.RequestVersion != version || current == null || !current.Enabled || current.BaseUrl != profile.BaseUrl) return;
                state.Snapshot = snapshot;
                state.Error = null;
                NotifyIfNeeded(current, snapshot);
            }
            catch (OperationCanceledException)
            {
                if (!_shutdown.IsCancellationRequested && state.RequestVersion == version)
                {
                    state.Error = "额度查询已取消。";
                }
            }
            catch (QuotaException error)
            {
                if (state.RequestVersion == version) state.Error = error.Message;
            }
            finally
            {
                if (acquired) _requestGate.Release();
                if (state.RequestVersion == version) state.Refreshing = false;
                NotifyStateChanged();
            }
        }

        public async Task<OperationResult> SaveProfileAsync(ConnectionProfile source, string apiKey)
        {
            if (!_storageAvailable) return OperationResult.Fail(GlobalError ?? "本机存储不可用，未更改配置。");
            if (source == null || string.IsNullOrWhiteSpace(source.Id)) return OperationResult.Fail("连接标识无效，请重新添加连接。");

            var profile = source.Clone();
            profile.Name = (profile.Name ?? "").Trim();
            if (profile.Name.Length == 0) profile.Name = "我的额度";
            if (profile.Name.Length > 40) profile.Name = profile.Name.Substring(0, 40);
            profile.BaseUrl = (profile.BaseUrl ?? "").Trim();
            var key = (apiKey ?? "").Trim();

            QuotaSnapshot snapshot;
            try
            {
                snapshot = await QuotaClient.FetchAsync(profile.BaseUrl, key, TimeSpan.FromSeconds(15), _shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                return OperationResult.Fail("连接验证已取消，原有配置未更改。");
            }
            catch (QuotaException error)
            {
                return OperationResult.Fail(error.Message);
            }

            string oldKey;
            try
            {
                oldKey = CredentialStore.Read(profile.Id);
            }
            catch
            {
                return OperationResult.Fail("无法读取原有 Windows 凭据，未保存更改。");
            }

            var candidate = Preferences.Clone();
            var index = candidate.Profiles.FindIndex(value => value.Id == profile.Id);
            var oldProfile = index >= 0 ? candidate.Profiles[index] : null;
            if (index >= 0) candidate.Profiles[index] = profile;
            else candidate.Profiles.Add(profile);
            if (candidate.Profiles.Count == 1) candidate.SelectedProfileId = profile.Id;
            candidate.Validate();

            try
            {
                CredentialStore.Save(profile.Id, key);
            }
            catch
            {
                return OperationResult.Fail("无法将 API Key 保存到 Windows 凭据管理器，原有配置未更改。");
            }

            try
            {
                SettingsStore.Save(candidate);
            }
            catch
            {
                if (!RestoreCredential(profile.Id, oldKey))
                {
                    DisableStorageAfterRollbackFailure();
                    return OperationResult.Fail(GlobalError);
                }
                return OperationResult.Fail("无法保存配置，已恢复原有 API Key；原有连接未更改。");
            }

            Preferences = candidate;
            _apiKeys[profile.Id] = key;
            var state = GetState(profile.Id);
            state.RequestVersion++;
            state.Snapshot = snapshot;
            state.Error = null;
            state.Refreshing = false;
            state.LastAttempt = DateTimeOffset.Now;
            if (oldProfile == null || oldProfile.BaseUrl != profile.BaseUrl || oldKey != key || !profile.Enabled)
            {
                ClearNotificationState(profile.Id);
            }
            RestartRefreshTimer();
            if (profile.Enabled) NotifyIfNeeded(profile, snapshot);
            NotifyStateChanged();
            return OperationResult.Ok("连接已验证并保存。");
        }

        public OperationResult DeleteProfile(string profileId)
        {
            if (!_storageAvailable) return OperationResult.Fail(GlobalError ?? "本机存储不可用，未更改配置。");
            var index = Preferences.Profiles.FindIndex(profile => profile.Id == profileId);
            if (index < 0) return OperationResult.Fail("没有找到要删除的连接。");

            string oldKey;
            try
            {
                oldKey = CredentialStore.Read(profileId);
                CredentialStore.Delete(profileId);
            }
            catch
            {
                return OperationResult.Fail("无法删除 Windows 凭据，连接配置已保留。");
            }

            var candidate = Preferences.Clone();
            candidate.Profiles.RemoveAt(index);
            if (candidate.SelectedProfileId == profileId) candidate.SelectedProfileId = null;
            try
            {
                SettingsStore.Save(candidate);
            }
            catch
            {
                if (!RestoreCredential(profileId, oldKey))
                {
                    DisableStorageAfterRollbackFailure();
                    return OperationResult.Fail(GlobalError);
                }
                return OperationResult.Fail("无法保存删除操作，已恢复此连接的 API Key；连接未删除。");
            }

            Preferences = candidate;
            _apiKeys.Remove(profileId);
            _states.Remove(profileId);
            ClearNotificationState(profileId);
            RestartRefreshTimer();
            NotifyStateChanged();
            return OperationResult.Ok("连接已删除。");
        }

        public OperationResult SaveGeneral(Preferences source, bool runAtLogin)
        {
            if (!_storageAvailable) return OperationResult.Fail(GlobalError ?? "本机存储不可用，未更改配置。");
            var candidate = source.Clone();
            candidate.Profiles = Preferences.Profiles.Select(profile => profile.Clone()).ToList();
            candidate.SelectedProfileId = Preferences.SelectedProfileId;
            candidate.Validate();
            try
            {
                SettingsStore.Save(candidate);
            }
            catch
            {
                return OperationResult.Fail("无法保存本机配置，原有设置未更改。请检查文件权限和可用空间。");
            }

            var notificationsWereEnabled = Preferences.Notifications;
            Preferences = candidate;
            if (!candidate.Notifications && notificationsWereEnabled) _notifiedWindows.Clear();
            RestartRefreshTimer();
            try
            {
                SetRunAtLogin(runAtLogin);
            }
            catch
            {
                NotifyStateChanged();
                return OperationResult.Ok("通用设置已保存，但无法更改登录时启动。请检查注册表权限。");
            }
            NotifyStateChanged();
            return OperationResult.Ok("通用设置已保存。");
        }

        public void SelectProfile(string profileId)
        {
            if (!_storageAvailable) return;
            if (profileId != null && !Preferences.Profiles.Any(profile => profile.Id == profileId)) return;
            var candidate = Preferences.Clone();
            candidate.SelectedProfileId = profileId;
            try
            {
                SettingsStore.Save(candidate);
                Preferences = candidate;
                NotifyStateChanged();
            }
            catch
            {
            }
        }

        public OperationResult MoveProfile(string profileId, int offset)
        {
            if (!_storageAvailable) return OperationResult.Fail(GlobalError ?? "本机存储不可用。");
            var candidate = Preferences.Clone();
            var index = candidate.Profiles.FindIndex(profile => profile.Id == profileId);
            if (index < 0) return OperationResult.Fail("没有找到要移动的连接。");
            var destination = Math.Max(0, Math.Min(candidate.Profiles.Count - 1, index + offset));
            if (destination == index) return OperationResult.Ok("顺序未改变。");
            var profile = candidate.Profiles[index];
            candidate.Profiles.RemoveAt(index);
            candidate.Profiles.Insert(destination, profile);
            try
            {
                SettingsStore.Save(candidate);
                Preferences = candidate;
                NotifyStateChanged();
                return OperationResult.Ok("顺序已更新。");
            }
            catch
            {
                return OperationResult.Fail("无法保存新的连接顺序。");
            }
        }
    }
}
