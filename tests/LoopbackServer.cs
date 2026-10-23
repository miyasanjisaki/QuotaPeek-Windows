using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace QuotaPeek.Windows
{
    // A loopback-only HTTP peer: it never contacts a real provider or uses real credentials.
    internal sealed class LoopbackServer : IDisposable
    {
        private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly ConcurrentBag<TcpClient> _clients = new ConcurrentBag<TcpClient>();
        private readonly ConcurrentBag<Task> _requests = new ConcurrentBag<Task>();
        private readonly Task _accept;
        private volatile bool _stopping;
        public readonly ConcurrentQueue<string> Paths = new ConcurrentQueue<string>();
        public string LastAuthorization;
        public string LastMethod;
        public string BaseUrl { get; private set; }
        public const string Payload = "{\"five_hour\":{\"utilization\":24.5,\"resets_at\":\"2026-10-23T02:00:00Z\"},\"seven_day\":{\"utilization\":63,\"resets_at\":\"2026-10-28T08:00:00Z\"},\"seven_day_sonnet\":{\"utilization\":12},\"seven_day_opus\":{\"utilization\":87}}";

        public LoopbackServer()
        {
            _listener.Start();
            BaseUrl = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
            _accept = AcceptAsync();
        }

        private async Task AcceptAsync()
        {
            while (!_stopping)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch (ObjectDisposedException) { if (_stopping) return; throw; }
                catch (SocketException) { if (_stopping) return; throw; }
                _clients.Add(client);
                _requests.Add(RespondAsync(client));
            }
        }

        private async Task RespondAsync(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
                {
                    var first = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (first == null) return;
                    var parts = first.Split(' ');
                    var path = parts[1];
                    LastMethod = parts[0];
                    Paths.Enqueue(path);
                    string line;
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync().ConfigureAwait(false)))
                    {
                        if (line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
                            LastAuthorization = line.Substring("Authorization:".Length).Trim();
                    }

                    var status = path.StartsWith("/redirect/") ? "302 Found" :
                        path.StartsWith("/unauthorized/") ? "401 Unauthorized" : "200 OK";
                    var body = path.StartsWith("/unauthorized/") ? "{\"error\":{\"code\":\"invalid_api_key\"}}" : Payload;
                    if (path.StartsWith("/exact/")) body = body.PadRight(1_048_576);
                    if (path.StartsWith("/streamlarge/")) body = body.PadRight(1_048_577);
                    var bytes = Encoding.UTF8.GetBytes(body);
                    var headers = "HTTP/1.1 " + status + "\r\nContent-Type: application/json\r\nConnection: close\r\n";
                    if (!path.StartsWith("/streamlarge/"))
                        headers += "Content-Length: " + (path.StartsWith("/oversized/") ? 1_048_577 : bytes.Length) + "\r\n";
                    if (path.StartsWith("/redirect/")) headers += "Location: " + BaseUrl + "/leak/v1/usage\r\n";
                    if (path.StartsWith("/slowheaders/")) await Task.Delay(2200).ConfigureAwait(false);
                    var headerBytes = Encoding.ASCII.GetBytes(headers + "\r\n");
                    await stream.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(false);
                    if (path.StartsWith("/slowbody/")) await Task.Delay(2200).ConfigureAwait(false);
                    if (!path.StartsWith("/oversized/")) await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                }
            }
            catch (IOException) { /* Client deliberately closes rejected or timed-out responses. */ }
            catch (ObjectDisposedException) { if (!_stopping) throw; }
        }

        public void Dispose()
        {
            _stopping = true;
            _listener.Stop();
            foreach (var client in _clients) client.Close();
            _accept.GetAwaiter().GetResult();
            Task.WhenAll(_requests.ToArray()).GetAwaiter().GetResult();
        }
    }
}
