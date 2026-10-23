using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace QuotaPeek.Windows
{
    internal static class SettingsStore
    {
        private const int SchemaVersion = 2;

        public static string DirectoryPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"); }
        }

        public static string FilePath
        {
            get { return Path.Combine(DirectoryPath, "preferences.json"); }
        }

        public static Preferences Load()
        {
            if (!File.Exists(FilePath)) return new Preferences();

            var serializer = new JavaScriptSerializer { MaxJsonLength = 1_048_576 };
            var root = serializer.DeserializeObject(File.ReadAllText(FilePath, Encoding.UTF8)) as IDictionary<string, object>;
            if (root == null) throw new InvalidDataException("配置文件不是 JSON 对象。");

            var version = RequiredInt(root, "version");
            if (version != SchemaVersion) throw new InvalidDataException("配置文件版本不受支持。");
            var preferences = new Preferences
            {
                Profiles = ReadProfiles(Required(root, "profiles")),
                SelectedProfileId = OptionalString(root, "selectedProfileID", null),
                RefreshMinutes = OptionalInt(root, "refreshMinutes", 5),
                ShowRemaining = OptionalBool(root, "showRemaining", true),
                Appearance = OptionalString(root, "appearance", "system"),
                Notifications = OptionalBool(root, "notifications", false),
                WarningPercent = OptionalInt(root, "warningPercent", 80),
                TrayMode = OptionalString(root, "menuBarMode", "selected"),
                ShowProfileName = OptionalBool(root, "showProfileName", true)
            };
            preferences.Validate();
            return preferences;
        }

        public static void Save(Preferences preferences)
        {
            var value = preferences.Clone();
            value.Validate();
            var root = new Dictionary<string, object>
            {
                { "version", SchemaVersion },
                { "profiles", value.Profiles.Select(profile => new Dictionary<string, object>
                    {
                        { "id", profile.Id },
                        { "name", profile.Name },
                        { "baseURL", profile.BaseUrl },
                        { "enabled", profile.Enabled }
                    }).ToArray() },
                { "selectedProfileID", value.SelectedProfileId },
                { "refreshMinutes", value.RefreshMinutes },
                { "showRemaining", value.ShowRemaining },
                { "appearance", value.Appearance },
                { "notifications", value.Notifications },
                { "warningPercent", value.WarningPercent },
                { "menuBarMode", value.TrayMode },
                { "showProfileName", value.ShowProfileName }
            };

            Directory.CreateDirectory(DirectoryPath);
            var serializer = new JavaScriptSerializer { MaxJsonLength = 1_048_576 };
            var temporary = Path.Combine(DirectoryPath, ".preferences-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(temporary, serializer.Serialize(root), new UTF8Encoding(false));
                if (File.Exists(FilePath))
                {
                    File.Replace(temporary, FilePath, null);
                }
                else
                {
                    File.Move(temporary, FilePath);
                }
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static List<ConnectionProfile> ReadProfiles(object raw)
        {
            // JavaScriptSerializer returns object[] for arrays and a dictionary (also IEnumerable) for objects;
            // only a real array is a valid list, anything else must block saving instead of loading as empty.
            var values = raw as object[];
            if (values == null) throw new InvalidDataException("连接列表格式无效。");
            var profiles = new List<ConnectionProfile>();
            foreach (var item in values)
            {
                var profile = item as IDictionary<string, object>;
                if (profile == null) throw new InvalidDataException("连接配置格式无效。");
                profiles.Add(new ConnectionProfile
                {
                    Id = RequiredString(profile, "id"),
                    Name = RequiredString(profile, "name"),
                    BaseUrl = RequiredString(profile, "baseURL"),
                    Enabled = RequiredBool(profile, "enabled")
                });
            }
            return profiles;
        }

        private static object Required(IDictionary<string, object> value, string key)
        {
            object result;
            if (!value.TryGetValue(key, out result) || result == null) throw new InvalidDataException("配置缺少字段：" + key);
            return result;
        }

        private static string RequiredString(IDictionary<string, object> value, string key)
        {
            var result = Required(value, key) as string;
            if (result == null) throw new InvalidDataException("配置字段类型无效：" + key);
            return result;
        }

        private static int RequiredInt(IDictionary<string, object> value, string key)
        {
            var result = Required(value, key);
            if (!(result is int)) throw new InvalidDataException("配置字段类型无效：" + key);
            return (int)result;
        }

        private static bool RequiredBool(IDictionary<string, object> value, string key)
        {
            var result = Required(value, key);
            if (!(result is bool)) throw new InvalidDataException("配置字段类型无效：" + key);
            return (bool)result;
        }

        private static string OptionalString(IDictionary<string, object> value, string key, string fallback)
        {
            object result;
            if (!value.TryGetValue(key, out result) || result == null) return fallback;
            if (!(result is string)) throw new InvalidDataException("配置字段类型无效：" + key);
            return (string)result;
        }

        private static int OptionalInt(IDictionary<string, object> value, string key, int fallback)
        {
            object result;
            if (!value.TryGetValue(key, out result)) return fallback;
            if (!(result is int)) throw new InvalidDataException("配置字段类型无效：" + key);
            return (int)result;
        }

        private static bool OptionalBool(IDictionary<string, object> value, string key, bool fallback)
        {
            object result;
            if (!value.TryGetValue(key, out result)) return fallback;
            if (!(result is bool)) throw new InvalidDataException("配置字段类型无效：" + key);
            return (bool)result;
        }
    }
}
