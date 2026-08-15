namespace Test.Shared
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// In-process HTTP server used to serve deterministic fixtures for crawler tests.
    /// Binds to a random loopback port so no elevation or URL ACL is required.
    /// </summary>
    public sealed class FixtureServer : IDisposable
    {
        private readonly Dictionary<string, Func<HttpListenerContext, FixtureResponse>> _Handlers =
            new Dictionary<string, Func<HttpListenerContext, FixtureResponse>>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, int> _RequestCounts =
            new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly HttpListener _Listener = new HttpListener();
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
        private readonly Task _ListenerTask;
        private bool _Disposed = false;

        /// <summary>
        /// Base URL (scheme, host, and port) of the fixture server, with no trailing slash.
        /// </summary>
        public string BaseUrl { get; }

        /// <summary>
        /// Instantiate and start the fixture server on a random loopback port.
        /// </summary>
        public FixtureServer()
        {
            int port = GetAvailablePort();
            BaseUrl = "http://127.0.0.1:" + port;
            _Listener.Prefixes.Add(BaseUrl + "/");
            _Listener.Start();
            _ListenerTask = Task.Run(() => ListenAsync(_Cts.Token));
        }

        /// <summary>
        /// Number of requests received for the given path.
        /// </summary>
        public int RequestCount(string path)
        {
            return _RequestCounts.TryGetValue(NormalizePath(path), out int count) ? count : 0;
        }

        /// <summary>
        /// Register an HTML response for a path.
        /// </summary>
        public void AddHtml(string path, string html)
        {
            AddResponse(path, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html ?? String.Empty));
        }

        /// <summary>
        /// Register a static response for a path.
        /// </summary>
        public void AddResponse(string path, string contentType, byte[] body, int statusCode = 200, IDictionary<string, string> headers = null)
        {
            FixtureResponse response = new FixtureResponse
            {
                StatusCode = statusCode,
                ContentType = contentType,
                Body = body ?? Array.Empty<byte>(),
                Headers = headers != null
                    ? new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };

            AddHandler(path, _ => response);
        }

        /// <summary>
        /// Register a redirect response for a path.
        /// </summary>
        public void AddRedirect(string path, string location, int statusCode = 302)
        {
            AddResponse(path, "text/plain; charset=utf-8", Array.Empty<byte>(), statusCode,
                new Dictionary<string, string> { { "Location", location } });
        }

        /// <summary>
        /// Register a dynamic handler for a path.  The handler is invoked for every matching request.
        /// </summary>
        public void AddHandler(string path, Func<HttpListenerContext, FixtureResponse> handler)
        {
            _Handlers[NormalizePath(path)] = handler;
        }

        /// <summary>
        /// Compute the absolute URL for a path against this server.
        /// </summary>
        public string UrlFor(string path)
        {
            return BaseUrl + NormalizePath(path);
        }

        /// <summary>
        /// Stop and dispose the fixture server.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;

            _Cts.Cancel();

            try
            {
                if (_Listener.IsListening) _Listener.Stop();
                _Listener.Close();
            }
            catch
            {
            }

            try
            {
                _ListenerTask.Wait(2000);
            }
            catch
            {
            }

            _Cts.Dispose();
            _Disposed = true;
        }

        private async Task ListenAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext context;

                try
                {
                    context = await _Listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                try
                {
                    await HandleRequestAsync(context).ConfigureAwait(false);
                }
                catch
                {
                    try
                    {
                        context.Response.StatusCode = 500;
                        context.Response.Close();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            string path = NormalizePath(context.Request.Url?.AbsolutePath);
            _RequestCounts.AddOrUpdate(path, 1, (_, current) => current + 1);

            FixtureResponse response;

            if (_Handlers.TryGetValue(path, out Func<HttpListenerContext, FixtureResponse> handler))
            {
                response = handler(context) ?? NotFound();
            }
            else
            {
                response = NotFound();
            }

            context.Response.StatusCode = response.StatusCode;
            if (!String.IsNullOrEmpty(response.ContentType)) context.Response.ContentType = response.ContentType;
            context.Response.ContentLength64 = response.Body.LongLength;

            foreach (KeyValuePair<string, string> header in response.Headers)
            {
                context.Response.Headers[header.Key] = header.Value;
            }

            if (!String.Equals(context.Request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase)
                && response.Body.Length > 0)
            {
                await context.Response.OutputStream.WriteAsync(response.Body, 0, response.Body.Length).ConfigureAwait(false);
            }

            context.Response.OutputStream.Close();
        }

        private static FixtureResponse NotFound()
        {
            return new FixtureResponse
            {
                StatusCode = 404,
                ContentType = "text/plain; charset=utf-8",
                Body = Encoding.UTF8.GetBytes("Not found"),
                Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
        }

        private static int GetAvailablePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static string NormalizePath(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return "/";
            if (!path.StartsWith("/")) path = "/" + path;
            return path;
        }
    }

    /// <summary>
    /// Response returned by a <see cref="FixtureServer"/> handler.
    /// </summary>
    public sealed class FixtureResponse
    {
        /// <summary>
        /// HTTP status code.
        /// </summary>
        public int StatusCode { get; set; } = 200;

        /// <summary>
        /// Content-Type header value.
        /// </summary>
        public string ContentType { get; set; } = "text/plain; charset=utf-8";

        /// <summary>
        /// Response body.
        /// </summary>
        public byte[] Body { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Additional response headers.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
