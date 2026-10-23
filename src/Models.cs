using System;
using System.Collections.Generic;
using System.Linq;

namespace QuotaPeek.Windows
{
    internal sealed class ConnectionProfile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string BaseUrl { get; set; }
        public bool Enabled { get; set; }

        public ConnectionProfile()
        {
            Id = Guid.NewGuid().ToString("D");
            Name = "";
            BaseUrl = "";
            Enabled = true;
        }

        public ConnectionProfile Clone()
        {
            return new ConnectionProfile
            {
                Id = Id,
                Name = Name,
                BaseUrl = BaseUrl,
                Enabled = Enabled
            };
        }

        public override string ToString()
        {
            return Name;
        }
    }

    internal sealed class Preferences
    {
        public List<ConnectionProfile> Profiles { get; set; }
        public string SelectedProfileId { get; set; }
        public int RefreshMinutes { get; set; }
        public bool ShowRemaining { get; set; }
        public string Appearance { get; set; }
        public bool Notifications { get; set; }
        public int WarningPercent { get; set; }
        public string TrayMode { get; set; }
        public bool ShowProfileName { get; set; }

        public Preferences()
        {
            Profiles = new List<ConnectionProfile>();
            RefreshMinutes = 5;
            ShowRemaining = true;
            Appearance = "system";
            Notifications = false;
            WarningPercent = 80;
            TrayMode = "selected";
            ShowProfileName = true;
        }

        public Preferences Clone()
        {
            return new Preferences
            {
                Profiles = Profiles.Select(profile => profile.Clone()).ToList(),
                SelectedProfileId = SelectedProfileId,
                RefreshMinutes = RefreshMinutes,
                ShowRemaining = ShowRemaining,
                Appearance = Appearance,
                Notifications = Notifications,
                WarningPercent = WarningPercent,
                TrayMode = TrayMode,
                ShowProfileName = ShowProfileName
            };
        }

        public void Validate()
        {
            if (Profiles == null)
            {
                throw new InvalidOperationException("配置文件缺少连接列表。");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var profile in Profiles)
            {
                if (profile == null || string.IsNullOrWhiteSpace(profile.Id))
                {
                    throw new InvalidOperationException("配置文件包含无效的连接标识。");
                }
                if (!ids.Add(profile.Id))
                {
                    throw new InvalidOperationException("配置文件包含重复的连接标识。");
                }

                profile.Name = (profile.Name ?? "").Trim();
                if (profile.Name.Length == 0)
                {
                    profile.Name = "我的额度";
                }
                if (profile.Name.Length > 40)
                {
                    profile.Name = profile.Name.Substring(0, 40);
                }
                profile.BaseUrl = (profile.BaseUrl ?? "").Trim();
            }

            if (SelectedProfileId != null && !Profiles.Any(profile => profile.Id == SelectedProfileId))
            {
                SelectedProfileId = null;
            }
            if (!new[] { 1, 2, 5, 15, 30 }.Contains(RefreshMinutes))
            {
                RefreshMinutes = 5;
            }
            if (Appearance != "system" && Appearance != "light" && Appearance != "dark")
            {
                Appearance = "system";
            }
            if (TrayMode != "selected" && TrayMode != "tightest")
            {
                TrayMode = "selected";
            }
            WarningPercent = Math.Max(50, Math.Min(95, WarningPercent));
        }
    }

    internal sealed class QuotaWindow
    {
        public string Id { get; private set; }
        public string Title { get; private set; }
        public double UsedPercent { get; private set; }
        public DateTimeOffset? ResetsAt { get; private set; }

        public QuotaWindow(string id, string title, double usedPercent, DateTimeOffset? resetsAt)
        {
            Id = id;
            Title = title;
            UsedPercent = usedPercent;
            ResetsAt = resetsAt;
        }
    }

    internal sealed class QuotaSnapshot
    {
        public IList<QuotaWindow> Windows { get; private set; }
        public DateTimeOffset FetchedAt { get; private set; }
        public string Source { get; private set; }

        public QuotaSnapshot(IList<QuotaWindow> windows, DateTimeOffset fetchedAt, string source)
        {
            Windows = windows;
            FetchedAt = fetchedAt;
            Source = source;
        }
    }

    internal sealed class ProfileState
    {
        public QuotaSnapshot Snapshot { get; set; }
        public bool Refreshing { get; set; }
        public string Error { get; set; }
        public DateTimeOffset? LastAttempt { get; set; }
        public int RequestVersion { get; set; }
    }

    internal enum QuotaErrorKind
    {
        InvalidJson,
        UnsupportedFormat,
        UnsupportedUnit,
        InvalidUtilization,
        NoWindows,
        InvalidUrl,
        InsecureUrl,
        InvalidKey,
        InvalidTimeout,
        RedirectForbidden,
        ResponseTooLarge,
        InvalidResponse,
        Http,
        Network,
        TimedOut
    }

    internal sealed class QuotaException : Exception
    {
        public QuotaErrorKind Kind { get; private set; }
        public int? StatusCode { get; private set; }
        public string Reason { get; private set; }

        public QuotaException(QuotaErrorKind kind)
            : this(kind, null, null)
        {
        }

        public QuotaException(QuotaErrorKind kind, int? statusCode, string reason)
            : base(CreateMessage(kind, statusCode, reason))
        {
            Kind = kind;
            StatusCode = statusCode;
            Reason = reason;
        }

        private static string CreateMessage(QuotaErrorKind kind, int? statusCode, string reason)
        {
            switch (kind)
            {
                case QuotaErrorKind.InvalidJson:
                    return "响应不是有效的 JSON，请检查服务地址。";
                case QuotaErrorKind.UnsupportedFormat:
                    return "响应格式不受支持，需要 VM2API 或 Claude 兼容的用量接口。";
                case QuotaErrorKind.UnsupportedUnit:
                    return "用量单位不受支持，需要 percent_used 百分比。";
                case QuotaErrorKind.InvalidUtilization:
                    return "响应中的用量无效，必须是 0 到 100 的数字百分比。";
                case QuotaErrorKind.NoWindows:
                    return "响应没有可用的用量窗口；未知用量不会显示为零。";
                case QuotaErrorKind.InvalidUrl:
                    return "服务地址无效，请输入不含账号、密码、查询参数或片段的完整 HTTPS 地址。";
                case QuotaErrorKind.InsecureUrl:
                    return "为保护 API Key，仅允许 HTTPS；本机 localhost、127.0.0.1 或 ::1 可使用 HTTP。";
                case QuotaErrorKind.InvalidKey:
                    return "请输入有效的 API Key，且不要包含换行或控制字符。";
                case QuotaErrorKind.InvalidTimeout:
                    return "请求超时时间必须为 1 到 60 秒。";
                case QuotaErrorKind.RedirectForbidden:
                    return "接口尝试重定向，已阻止以保护 API Key；请直接填写最终服务地址。";
                case QuotaErrorKind.ResponseTooLarge:
                    return "接口响应过大，已停止读取；请检查是否填写了正确的用量接口。";
                case QuotaErrorKind.InvalidResponse:
                    return "服务器未返回有效的 HTTP 响应。";
                case QuotaErrorKind.TimedOut:
                    return "请求超时，请检查网络或稍后刷新。";
                case QuotaErrorKind.Network:
                    return "无法连接用量服务，请检查网络、服务地址及 TLS 证书。";
                case QuotaErrorKind.Http:
                    var code = statusCode ?? 0;
                    var detail = reason == null ? "（" + code + "）" : "（" + code + " " + reason + "）";
                    if (code == 401) return "认证失败" + detail + "，请检查 API Key。";
                    if (code == 403) return "无权读取用量" + detail + "，请检查 API Key 的权限。";
                    if (code == 404) return "未找到用量接口" + detail + "，请检查服务地址及其路径前缀。";
                    if (code == 429) return "请求过于频繁" + detail + "，请稍后刷新。";
                    if (code >= 500 && code <= 599) return "服务端出错" + detail + "，请稍后重试；持续出现请联系服务提供方。";
                    return "用量请求失败" + detail + "，请检查服务配置。";
                default:
                    return "无法读取额度。";
            }
        }
    }

    internal sealed class OperationResult
    {
        public bool Success { get; private set; }
        public string Message { get; private set; }

        private OperationResult(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        public static OperationResult Ok(string message)
        {
            return new OperationResult(true, message);
        }

        public static OperationResult Fail(string message)
        {
            return new OperationResult(false, message);
        }
    }
}
