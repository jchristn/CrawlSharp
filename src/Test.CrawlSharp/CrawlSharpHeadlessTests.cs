namespace Test.CrawlSharp
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using CrawlSharpWeb = global::CrawlSharp.Web;
    using HtmlAgilityPack;
    using Xunit;

    public class CrawlSharpHeadlessTests
    {
        [Fact]
        public void CrawlSettings_Defaults_AutoExpandDisabled()
        {
            CrawlSharpWeb.CrawlSettings settings = new CrawlSharpWeb.CrawlSettings();

            Assert.False(settings.AutoExpandCollapsibles);
            Assert.Equal(0, settings.PostLoadDelayMs);
            Assert.Equal(250, settings.PostInteractionDelayMs);
            Assert.Equal(2, settings.MaxExpansionPasses);
            Assert.Empty(settings.ExpansionSelectors);
        }

        [Fact]
        public async Task Headless_ReturnsRenderedHtml_AfterClientRendering()
        {
            using FixtureServer server = new FixtureServer();
            server.AddHtml("/rendered", @"<!DOCTYPE html>
<html>
  <body>
    <div id='content'>server</div>
    <script>
      window.addEventListener('load', function () {
        document.getElementById('content').innerHTML = '<span id=""hydrated"">hydrated</span>';
      });
    </script>
  </body>
</html>");

            CrawlSharpWeb.Settings settings = CreateHeadlessSettings(server.UrlFor("/rendered"));
            CrawlSharpWeb.WebResource resource = await CrawlSingleAsync(settings);
            string html = Encoding.UTF8.GetString(resource.Data);

            Assert.Contains("id=\"hydrated\"", html);
            Assert.Contains(">hydrated<", html);
        }

        [Fact]
        public async Task Headless_Details_AreOpened_WhenAutoExpandEnabled()
        {
            using FixtureServer server = new FixtureServer();
            server.AddHtml("/details", @"<!DOCTYPE html>
<html>
  <body>
    <details id='extra'>
      <summary>More</summary>
      <div>Details content</div>
    </details>
  </body>
</html>");

            CrawlSharpWeb.Settings disabledSettings = CreateHeadlessSettings(server.UrlFor("/details"));
            CrawlSharpWeb.Settings enabledSettings = CreateHeadlessSettings(server.UrlFor("/details"), crawl =>
            {
                crawl.AutoExpandCollapsibles = true;
            });

            CrawlSharpWeb.WebResource disabledResource = await CrawlSingleAsync(disabledSettings);
            CrawlSharpWeb.WebResource enabledResource = await CrawlSingleAsync(enabledSettings);
            HtmlDocument disabledDoc = LoadHtml(disabledResource);
            HtmlDocument enabledDoc = LoadHtml(enabledResource);
            HtmlNode disabledDetails = disabledDoc.DocumentNode.SelectSingleNode("//details[@id='extra']");
            HtmlNode enabledDetails = enabledDoc.DocumentNode.SelectSingleNode("//details[@id='extra']");

            Assert.NotNull(disabledDetails);
            Assert.NotNull(enabledDetails);
            Assert.Null(disabledDetails.Attributes["open"]);
            Assert.NotNull(enabledDetails.Attributes["open"]);
        }

        [Fact]
        public async Task Headless_DynamicAccordionContent_IsExpanded_WhenAutoExpandEnabled()
        {
            using FixtureServer server = new FixtureServer();
            server.AddHtml("/dynamic", @"<!DOCTYPE html>
<html>
  <body>
    <button id='toggle' aria-expanded='false' aria-controls='panel' onclick='togglePanel(this)'>Toggle</button>
    <div id='panel'></div>
    <script>
      function togglePanel(button) {
        if (!window.loadedPanel) {
          document.getElementById('panel').innerHTML = '<div id=""dynamic-content"">Dynamic content</div>';
          window.loadedPanel = true;
        }
        button.setAttribute('aria-expanded', 'true');
      }
    </script>
  </body>
</html>");

            CrawlSharpWeb.Settings disabledSettings = CreateHeadlessSettings(server.UrlFor("/dynamic"));
            CrawlSharpWeb.Settings enabledSettings = CreateHeadlessSettings(server.UrlFor("/dynamic"), crawl =>
            {
                crawl.AutoExpandCollapsibles = true;
            });

            CrawlSharpWeb.WebResource disabledResource = await CrawlSingleAsync(disabledSettings);
            CrawlSharpWeb.WebResource enabledResource = await CrawlSingleAsync(enabledSettings);
            HtmlDocument disabledDoc = LoadHtml(disabledResource);
            HtmlDocument enabledDoc = LoadHtml(enabledResource);
            HtmlNode enabledNode = enabledDoc.DocumentNode.SelectSingleNode("//*[@id='dynamic-content']");

            Assert.Null(disabledDoc.DocumentNode.SelectSingleNode("//*[@id='dynamic-content']"));
            Assert.NotNull(enabledNode);
            Assert.Equal("Dynamic content", enabledNode.InnerText.Trim());
        }

        [Fact]
        public async Task Headless_CustomExpansionSelectors_AreApplied()
        {
            using FixtureServer server = new FixtureServer();
            server.AddHtml("/custom", @"<!DOCTYPE html>
<html>
  <body>
    <button class='faq-toggle' onclick='document.getElementById(""panel"").innerHTML = ""<div id=\""custom-content\"">Custom content</div>"";'>Open</button>
    <div id='panel'></div>
  </body>
</html>");

            CrawlSharpWeb.Settings settings = CreateHeadlessSettings(server.UrlFor("/custom"), crawl =>
            {
                crawl.AutoExpandCollapsibles = true;
                crawl.ExpansionSelectors = new List<string>
                {
                    ".faq-toggle"
                };
            });

            CrawlSharpWeb.WebResource resource = await CrawlSingleAsync(settings);
            HtmlDocument document = LoadHtml(resource);
            HtmlNode customNode = document.DocumentNode.SelectSingleNode("//*[@id='custom-content']");

            Assert.NotNull(customNode);
            Assert.Equal("Custom content", customNode.InnerText.Trim());
        }

        [Fact]
        public async Task Headless_RevealedLinks_AreDiscovered_OnlyWhenAutoExpandEnabled()
        {
            using FixtureServer server = new FixtureServer();
            server.AddHtml("/links", @"<!DOCTYPE html>
<html>
  <body>
    <button id='toggle' aria-expanded='false' aria-controls='panel' onclick='togglePanel(this)'>Toggle</button>
    <div id='panel'></div>
    <script>
      function togglePanel(button) {
        if (!window.loadedPanel) {
          document.getElementById('panel').innerHTML = '<a id=""dynamic-link"" href=""/links/dynamic-child"">Dynamic child</a>';
          window.loadedPanel = true;
        }
        button.setAttribute('aria-expanded', 'true');
      }
    </script>
  </body>
</html>");
            server.AddHtml("/links/dynamic-child", @"<!DOCTYPE html><html><body><div id='child'>child page</div></body></html>");

            CrawlSharpWeb.Settings disabledSettings = CreateHeadlessSettings(server.UrlFor("/links"), crawl =>
            {
                crawl.FollowLinks = true;
                crawl.MaxCrawlDepth = 1;
            });
            CrawlSharpWeb.Settings enabledSettings = CreateHeadlessSettings(server.UrlFor("/links"), crawl =>
            {
                crawl.AutoExpandCollapsibles = true;
                crawl.FollowLinks = true;
                crawl.MaxCrawlDepth = 1;
            });

            List<CrawlSharpWeb.WebResource> disabledResources = await CrawlAllAsync(disabledSettings);
            List<CrawlSharpWeb.WebResource> enabledResources = await CrawlAllAsync(enabledSettings);

            Assert.DoesNotContain(disabledResources, r => r.Url == server.UrlFor("/links/dynamic-child"));
            Assert.Contains(enabledResources, r => r.Url == server.UrlFor("/links/dynamic-child"));
        }

        [Fact]
        public async Task Headless_PdfRoute_FallsBackToRestClient_AndReturnsBinaryBytes()
        {
            using FixtureServer server = new FixtureServer();
            byte[] pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF");
            server.AddResponse("/file.pdf", "application/pdf", pdfBytes);

            CrawlSharpWeb.Settings settings = CreateHeadlessSettings(server.UrlFor("/file.pdf"));
            CrawlSharpWeb.WebResource resource = await CrawlSingleAsync(settings);

            Assert.Equal("application/pdf", resource.ContentType);
            Assert.Equal(pdfBytes, resource.Data);
            Assert.StartsWith("%PDF-1.7", Encoding.ASCII.GetString(resource.Data));
        }

        private static CrawlSharpWeb.Settings CreateHeadlessSettings(string url, Action<CrawlSharpWeb.CrawlSettings>? configure = null)
        {
            CrawlSharpWeb.Settings settings = new CrawlSharpWeb.Settings();
            settings.Crawl.StartUrl = url;
            settings.Crawl.UserAgent = "CrawlSharp.Tests";
            settings.Crawl.UseHeadlessBrowser = true;
            settings.Crawl.IgnoreRobotsText = true;
            settings.Crawl.IncludeSitemap = false;
            settings.Crawl.FollowLinks = false;
            settings.Crawl.FollowRedirects = true;
            settings.Crawl.RestrictToChildUrls = true;
            settings.Crawl.RestrictToSameSubdomain = true;
            settings.Crawl.RestrictToSameRootDomain = true;
            settings.Crawl.FollowExternalLinks = false;
            settings.Crawl.MaxParallelTasks = 1;
            settings.Crawl.MaxCrawlDepth = 0;
            settings.Crawl.PageTimeoutMs = 10000;
            settings.Crawl.RequestDelayMs = 0;
            settings.Crawl.ThrottleMs = 0;
            configure?.Invoke(settings.Crawl);
            return settings;
        }

        private static async Task<CrawlSharpWeb.WebResource> CrawlSingleAsync(CrawlSharpWeb.Settings settings)
        {
            List<CrawlSharpWeb.WebResource> resources = await CrawlAllAsync(settings);
            return Assert.Single(resources);
        }

        private static async Task<List<CrawlSharpWeb.WebResource>> CrawlAllAsync(CrawlSharpWeb.Settings settings)
        {
            using CrawlSharpWeb.WebCrawler crawler = new CrawlSharpWeb.WebCrawler(settings);
            List<CrawlSharpWeb.WebResource> resources = new List<CrawlSharpWeb.WebResource>();

            await foreach (CrawlSharpWeb.WebResource resource in crawler.CrawlAsync())
            {
                resources.Add(resource);
            }

            return resources;
        }

        private static HtmlDocument LoadHtml(CrawlSharpWeb.WebResource resource)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(Encoding.UTF8.GetString(resource.Data));
            return document;
        }

        private sealed class FixtureServer : IDisposable
        {
            private readonly Dictionary<string, FixtureResponse> _Responses = new Dictionary<string, FixtureResponse>(StringComparer.OrdinalIgnoreCase);
            private readonly HttpListener _Listener = new HttpListener();
            private readonly CancellationTokenSource _CancellationTokenSource = new CancellationTokenSource();
            private readonly Task _ListenerTask;
            private bool _Disposed = false;

            public string BaseUrl { get; }

            public FixtureServer()
            {
                int port = GetAvailablePort();
                BaseUrl = "http://127.0.0.1:" + port;
                _Listener.Prefixes.Add(BaseUrl + "/");
                _Listener.Start();
                _ListenerTask = Task.Run(() => ListenAsync(_CancellationTokenSource.Token));
            }

            public void AddHtml(string path, string html)
            {
                AddResponse(path, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
            }

            public void AddResponse(string path, string contentType, byte[] body, int statusCode = 200, Dictionary<string, string>? headers = null)
            {
                string normalizedPath = NormalizePath(path);
                _Responses[normalizedPath] = new FixtureResponse
                {
                    StatusCode = statusCode,
                    ContentType = contentType,
                    Body = body ?? Array.Empty<byte>(),
                    Headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                };
            }

            public string UrlFor(string path)
            {
                return BaseUrl + NormalizePath(path);
            }

            public void Dispose()
            {
                if (_Disposed) return;

                _CancellationTokenSource.Cancel();

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

                _CancellationTokenSource.Dispose();
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

                if (!_Responses.TryGetValue(path, out FixtureResponse? response))
                {
                    response = new FixtureResponse
                    {
                        StatusCode = 404,
                        ContentType = "text/plain; charset=utf-8",
                        Body = Encoding.UTF8.GetBytes("Not found"),
                        Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    };
                }

                context.Response.StatusCode = response.StatusCode;
                context.Response.ContentType = response.ContentType;
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

            private static int GetAvailablePort()
            {
                TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }

            private static string NormalizePath(string? path)
            {
                if (String.IsNullOrWhiteSpace(path)) return "/";
                if (!path.StartsWith("/")) path = "/" + path;
                return path;
            }

            private sealed class FixtureResponse
            {
                public int StatusCode { get; set; }

                public string ContentType { get; set; } = String.Empty;

                public byte[] Body { get; set; } = Array.Empty<byte>();

                public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
