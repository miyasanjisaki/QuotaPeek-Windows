using System;
using System.Linq;
using System.Text;

namespace QuotaPeek.Windows
{
    internal static class CoreTests
    {
        private static int _passed;

        private static int Main()
        {
            try
            {
                EndpointNormalization();
                EndpointSecurity();
                ParsesQuotaWindows();
                RejectsInvalidPayloads();
                SanitizesServerErrors();
                Console.WriteLine("Core tests passed: " + _passed);
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        private static void EndpointNormalization()
        {
            var cases = new[]
            {
                new[] { "https://example.com", "https://example.com/v1/usage" },
                new[] { "https://example.com/", "https://example.com/v1/usage" },
                new[] { "https://example.com/v1", "https://example.com/v1/usage" },
                new[] { "https://example.com/v1/usage///", "https://example.com/v1/usage" },
                new[] { " https://example.com/proxy/team/ ", "https://example.com/proxy/team/v1/usage" },
                new[] { "https://example.com:8443/proxy/v1", "https://example.com:8443/proxy/v1/usage" },
                new[] { "https://example.com/team%20one", "https://example.com/team%20one/v1/usage" }
            };
            foreach (var item in cases)
            {
                Equal(item[1], QuotaClient.NormalizeEndpoint(item[0]).AbsoluteUri, "endpoint " + item[0]);
            }
        }

        private static void EndpointSecurity()
        {
            foreach (var value in new[] { "http://localhost:8080", "http://127.0.0.1:8080/proxy", "http://[::1]:8080" })
            {
                try
                {
                    Equal("http", QuotaClient.NormalizeEndpoint(value).Scheme, "loopback HTTP " + value);
                }
                catch (Exception error)
                {
                    throw new InvalidOperationException("loopback rejected: " + value, error);
                }
            }
            foreach (var value in new[]
            {
                "", "example.com", "http://example.com", "http://192.168.1.1", "http://localhost.evil.test",
                "ftp://localhost", "https://user@example.com", "https://example.com?key=secret", "https://example.com#part",
                "https://example.com/a b", "https://example.com:0", "https://example.com:65536"
            })
            {
                Throws(value, delegate { QuotaClient.NormalizeEndpoint(value); });
            }
        }

        private static void ParsesQuotaWindows()
        {
            var snapshot = Parse("{\"unit\":\"percent_used\",\"five_hour\":{\"utilization\":0,\"resets_at\":\"2026-10-15T17:19:59.796566+00:00\"},\"seven_day\":{\"utilization\":58,\"resets_at\":1792436400},\"source\":\"vm2api\"}");
            Equal(2, snapshot.Windows.Count, "window count");
            Equal(0d, snapshot.Windows[0].UsedPercent, "zero utilization");
            Equal(58d, snapshot.Windows[1].UsedPercent, "seven day utilization");
            Equal("vm2api", snapshot.Source, "source");
            True(snapshot.Windows.All(window => window.ResetsAt.HasValue), "reset dates");

            var envelope = Parse("{\"data\":{\"five_hour\":{\"utilization\":0.25},\"seven_day_sonnet\":{\"utilization\":12.5},\"seven_day_opus\":null}}");
            Equal(2, envelope.Windows.Count, "envelope windows");
            Equal(12.5d, envelope.Windows[1].UsedPercent, "fractional utilization");

            var unknown = Parse("{\"five_hour\":{\"utilization\":null},\"seven_day\":{\"utilization\":0}}");
            Equal(1, unknown.Windows.Count, "null is unknown");
            Equal("seven_day", unknown.Windows[0].Id, "known window retained");

            foreach (var invalidReset in new[] { "null", "42", "true", "\"tomorrow\"", "\"\"" })
            {
                var invalid = Parse("{\"five_hour\":{\"utilization\":8,\"resets_at\":" + invalidReset + "}}");
                True(!invalid.Windows[0].ResetsAt.HasValue, "invalid reset ignored: " + invalidReset);
            }
        }

        private static void RejectsInvalidPayloads()
        {
            foreach (var json in new[]
            {
                "not json", "{}", "[]", "{\"data\":null}", "{\"unit\":\"fraction\",\"five_hour\":{\"utilization\":50}}",
                "{\"five_hour\":{\"utilization\":true}}", "{\"five_hour\":{\"utilization\":\"58\"}}",
                "{\"five_hour\":{\"utilization\":-1}}", "{\"five_hour\":{\"utilization\":101}}",
                "{\"five_hour\":{\"utilization\":null}}"
            })
            {
                Throws(json, delegate { Parse(json); });
            }
        }

        private static void SanitizesServerErrors()
        {
            Equal("pool_unavailable", QuotaClient.ExtractErrorCode(Encoding.UTF8.GetBytes("{\"error\":{\"code\":\"pool_unavailable\"}}")), "safe error");
            Equal(null, QuotaClient.ExtractErrorCode(Encoding.UTF8.GetBytes("{\"error\":{\"code\":\"Bearer sk-secret leaked\"}}")), "secret-like error rejected");
            Equal(null, QuotaClient.ExtractErrorCode(Encoding.UTF8.GetBytes("{\"error\":{\"code\":\"invalid_key\\n\"}}")), "trailing newline rejected");
            Equal("服务端出错（503 pool_unavailable），请稍后重试；持续出现请联系服务提供方。",
                new QuotaException(QuotaErrorKind.Http, 503, "pool_unavailable").Message, "http message");
        }

        private static QuotaSnapshot Parse(string json)
        {
            return QuotaClient.Parse(Encoding.UTF8.GetBytes(json));
        }

        private static void Throws(string label, Action action)
        {
            try
            {
                action();
                throw new InvalidOperationException("Expected failure: " + label);
            }
            catch (QuotaException)
            {
                _passed++;
            }
        }

        private static void Equal<T>(T expected, T actual, string label)
        {
            if (!object.Equals(expected, actual))
            {
                throw new InvalidOperationException(label + ": expected " + expected + ", actual " + actual);
            }
            _passed++;
        }

        private static void True(bool value, string label)
        {
            if (!value) throw new InvalidOperationException(label + ": expected true");
            _passed++;
        }
    }
}
