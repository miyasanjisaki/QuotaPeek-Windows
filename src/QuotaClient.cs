using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace QuotaPeek.Windows
{
    internal static class QuotaClient
    {
        private const int MaximumResponseBytes = 1_048_576;
        private const int MaximumErrorBytes = 16_384;
        private static readonly Regex SafeErrorCode = new Regex(@"\A[A-Za-z][A-Za-z0-9_.-]{0,63}\z", RegexOptions.CultureInvariant);
        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            var client = new HttpClient(handler);
            client.Timeout = Timeout.InfiniteTimeSpan;
            return client;
        }

        public static Uri NormalizeEndpoint(string baseUrl)
        {
            var input = (baseUrl ?? "").Trim();
            Uri uri;
            if (input.Length == 0 || input.Any(char.IsWhiteSpace) || input.IndexOf('?') >= 0 || input.IndexOf('#') >= 0 ||
                !Uri.TryCreate(input, UriKind.Absolute, out uri) || string.IsNullOrEmpty(uri.Host) ||
                !string.IsNullOrEmpty(uri.UserInfo) || uri.Port < 1 || uri.Port > 65535)
            {
                throw new QuotaException(QuotaErrorKind.InvalidUrl);
            }

            var scheme = uri.Scheme.ToLowerInvariant();
            var host = uri.Host.ToLowerInvariant();
            IPAddress address;
            var parsedAddress = IPAddress.TryParse(host.Trim('[', ']'), out address);
            var loopback = host == "localhost" || host == "127.0.0.1" ||
                           (parsedAddress && address.Equals(IPAddress.IPv6Loopback));
            if (scheme != Uri.UriSchemeHttps && !(scheme == Uri.UriSchemeHttp && loopback))
            {
                throw new QuotaException(QuotaErrorKind.InsecureUrl);
            }

            var path = uri.AbsolutePath;
            while (path.EndsWith("/", StringComparison.Ordinal))
            {
                path = path.Substring(0, path.Length - 1);
            }
            if (path.EndsWith("/v1/usage", StringComparison.Ordinal))
            {
                // Already normalized.
            }
            else if (path.EndsWith("/v1", StringComparison.Ordinal))
            {
                path += "/usage";
            }
            else
            {
                path += "/v1/usage";
            }

            Uri endpoint;
            if (!Uri.TryCreate(uri.GetLeftPart(UriPartial.Authority) + path, UriKind.Absolute, out endpoint))
            {
                throw new QuotaException(QuotaErrorKind.InvalidUrl);
            }
            return endpoint;
        }

        public static async Task<QuotaSnapshot> FetchAsync(string baseUrl, string apiKey, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var endpoint = NormalizeEndpoint(baseUrl);
            var key = (apiKey ?? "").Trim();
            if (key.Length == 0 || (apiKey ?? "").Any(char.IsControl))
            {
                throw new QuotaException(QuotaErrorKind.InvalidKey);
            }
            if (timeout.TotalSeconds < 1 || timeout.TotalSeconds > 60 || double.IsNaN(timeout.TotalSeconds) || double.IsInfinity(timeout.TotalSeconds))
            {
                throw new QuotaException(QuotaErrorKind.InvalidTimeout);
            }

            using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var request = new HttpRequestMessage(HttpMethod.Get, endpoint))
            {
                timeoutSource.CancelAfter(timeout);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };

                try
                {
                    using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token))
                    {
                        var status = (int)response.StatusCode;
                        if (status >= 300 && status <= 399)
                        {
                            throw new QuotaException(QuotaErrorKind.RedirectForbidden);
                        }
                        if (status < 200 || status > 299)
                        {
                            var errorData = await ReadLimitedAsync(response.Content, MaximumErrorBytes, timeoutSource.Token, false);
                            throw new QuotaException(QuotaErrorKind.Http, status, ExtractErrorCode(errorData));
                        }
                        if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > MaximumResponseBytes)
                        {
                            throw new QuotaException(QuotaErrorKind.ResponseTooLarge);
                        }
                        var data = await ReadLimitedAsync(response.Content, MaximumResponseBytes, timeoutSource.Token, true);
                        return Parse(data);
                    }
                }
                catch (QuotaException)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    throw new QuotaException(QuotaErrorKind.TimedOut);
                }
                catch (Exception error) when (timeoutSource.IsCancellationRequested &&
                    (error is IOException || error is HttpRequestException || error is ObjectDisposedException))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new QuotaException(QuotaErrorKind.TimedOut);
                }
                catch (HttpRequestException)
                {
                    throw new QuotaException(QuotaErrorKind.Network);
                }
                catch (IOException)
                {
                    throw new QuotaException(QuotaErrorKind.Network);
                }
            }
        }

        private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int limit, CancellationToken cancellationToken, bool failWhenExceeded)
        {
            using (var source = await content.ReadAsStreamAsync())
            // .NET Framework response streams may ignore cancellation while a body read is pending.
            using (cancellationToken.Register(source.Dispose))
            using (var destination = new MemoryStream())
            {
                var buffer = new byte[8192];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (read == 0) break;
                    var remaining = limit - (int)destination.Length;
                    if (read > remaining)
                    {
                        if (failWhenExceeded)
                        {
                            throw new QuotaException(QuotaErrorKind.ResponseTooLarge);
                        }
                        destination.Write(buffer, 0, Math.Max(0, remaining));
                        break;
                    }
                    destination.Write(buffer, 0, read);
                }
                return destination.ToArray();
            }
        }

        public static QuotaSnapshot Parse(byte[] data)
        {
            object parsed;
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = MaximumResponseBytes };
                parsed = serializer.DeserializeObject(Encoding.UTF8.GetString(data));
            }
            catch (ArgumentException)
            {
                throw new QuotaException(QuotaErrorKind.InvalidJson);
            }
            catch (InvalidOperationException)
            {
                throw new QuotaException(QuotaErrorKind.InvalidJson);
            }

            var root = parsed as IDictionary<string, object>;
            if (root == null)
            {
                throw new QuotaException(QuotaErrorKind.UnsupportedFormat);
            }
            ValidateUnit(root);

            object envelope;
            if (root.TryGetValue("data", out envelope))
            {
                root = envelope as IDictionary<string, object>;
                if (root == null)
                {
                    throw new QuotaException(QuotaErrorKind.UnsupportedFormat);
                }
                ValidateUnit(root);
            }

            var definitions = new[]
            {
                new[] { "five_hour", "5 小时" },
                new[] { "seven_day", "7 天" },
                new[] { "seven_day_sonnet", "Sonnet · 7 天" },
                new[] { "seven_day_opus", "Opus · 7 天" }
            };
            var windows = new List<QuotaWindow>();
            var recognized = false;
            foreach (var definition in definitions)
            {
                object raw;
                if (!root.TryGetValue(definition[0], out raw)) continue;
                recognized = true;
                if (raw == null) continue;

                var window = raw as IDictionary<string, object>;
                if (window == null)
                {
                    throw new QuotaException(QuotaErrorKind.UnsupportedFormat);
                }
                object utilization;
                if (!window.TryGetValue("utilization", out utilization) || utilization == null) continue;

                double used;
                if (!TryNumber(utilization, out used) || double.IsNaN(used) || double.IsInfinity(used) || used < 0 || used > 100)
                {
                    throw new QuotaException(QuotaErrorKind.InvalidUtilization);
                }
                object rawReset;
                window.TryGetValue("resets_at", out rawReset);
                windows.Add(new QuotaWindow(definition[0], definition[1], used, ParseResetDate(rawReset)));
            }

            if (!recognized)
            {
                throw new QuotaException(QuotaErrorKind.UnsupportedFormat);
            }
            if (windows.Count == 0)
            {
                throw new QuotaException(QuotaErrorKind.NoWindows);
            }
            object source;
            root.TryGetValue("source", out source);
            return new QuotaSnapshot(windows, DateTimeOffset.Now, source as string);
        }

        private static void ValidateUnit(IDictionary<string, object> value)
        {
            object unit;
            if (value.TryGetValue("unit", out unit) && (!(unit is string) || (string)unit != "percent_used"))
            {
                throw new QuotaException(QuotaErrorKind.UnsupportedUnit);
            }
        }

        private static bool TryNumber(object value, out double result)
        {
            result = 0;
            if (value == null || value is bool || value is string) return false;
            switch (Type.GetTypeCode(value.GetType()))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Single:
                    result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    return true;
                default:
                    return false;
            }
        }

        private static DateTimeOffset? ParseResetDate(object raw)
        {
            if (raw == null || raw is bool) return null;
            var text = raw as string;
            if (text != null)
            {
                DateTimeOffset parsed;
                var formats = new[]
                {
                    "yyyy-MM-dd'T'HH:mm:ss'Z'",
                    "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
                    "yyyy-MM-dd'T'HH:mm:sszzz",
                    "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"
                };
                if (DateTimeOffset.TryParseExact(text, formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
                {
                    return parsed;
                }
                if (text.Length == 0 || text.Any(character => character < '0' || character > '9')) return null;
            }

            double seconds;
            if (text != null)
            {
                if (!double.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out seconds)) return null;
            }
            else if (!TryNumber(raw, out seconds))
            {
                return null;
            }
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 978307200 || seconds > 4102444800) return null;
            try
            {
                return new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(seconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        internal static string ExtractErrorCode(byte[] data)
        {
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = MaximumErrorBytes };
                var root = serializer.DeserializeObject(Encoding.UTF8.GetString(data)) as IDictionary<string, object>;
                if (root == null) return null;
                object nested;
                IDictionary<string, object> error = root;
                if (root.TryGetValue("error", out nested) && nested is IDictionary<string, object>)
                {
                    error = (IDictionary<string, object>)nested;
                }
                foreach (var field in new[] { "code", "type" })
                {
                    object value;
                    if (error.TryGetValue(field, out value) && value is string && SafeErrorCode.IsMatch((string)value))
                    {
                        return (string)value;
                    }
                }
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            return null;
        }
    }
}
