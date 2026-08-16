using System.Net;
using System.Net.Sockets;

namespace Launcher.Tests
{
    /// <summary>
    /// Serves a folder's contents over HTTP on a free loopback port — the same role a
    /// manual PowerShell server script used to fill for a whole day. Turned into a
    /// test fixture so it runs on every change instead of by hand.
    /// </summary>
    public sealed class TestServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly string _root;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        public string BaseUrl { get; }

        public TestServer(string root)
        {
            _root = root;
            int port = GetFreePort();
            BaseUrl = $"http://localhost:{port}/";

            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
            _loop = Task.Run(() => LoopAsync(_cts.Token));
        }

        private async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync();
                }
                catch
                {
                    return; // listener stopped
                }

                string rel = Uri.UnescapeDataString(ctx.Request.Url!.AbsolutePath.TrimStart('/'));
                string full = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));

                if (rel.Length > 0 && File.Exists(full))
                {
                    byte[] bytes = await File.ReadAllBytesAsync(full, token);
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentLength64 = bytes.Length;
                    await ctx.Response.OutputStream.WriteAsync(bytes, token);
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                }

                ctx.Response.Close();
            }
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); _listener.Close(); } catch { /* already stopped */ }
        }
    }
}
