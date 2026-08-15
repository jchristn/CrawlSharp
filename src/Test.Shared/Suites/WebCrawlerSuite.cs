namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using CrawlSharp.Web;
    using Touchstone.Core;

    /// <summary>
    /// Integration coverage for <see cref="WebCrawler"/> against an in-process fixture server.
    /// These cases do not use the headless browser.
    /// </summary>
    public static class WebCrawlerSuite
    {
        private const string Id = "WebCrawler";

        /// <summary>
        /// Build the suite.
        /// </summary>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case.Sync(Id, "Ctor_NullSettings_Throws", "The constructor rejects null settings", () =>
                {
                    Check.Throws<ArgumentNullException>(() => new WebCrawler(null));
                }),

                Case.Sync(Id, "Delay_Negative_Throws", "The Delay property rejects negative values", () =>
                {
                    Settings settings = CrawlHelper.CreateSettings("http://127.0.0.1:1/");
                    using WebCrawler crawler = new WebCrawler(settings);
                    Check.Equal(0, crawler.Delay);
                    Check.Throws<ArgumentException>(() => crawler.Delay = -1);
                }),

                Case.Sync(Id, "Dispose_Idempotent", "Dispose can be called more than once", () =>
                {
                    Settings settings = CrawlHelper.CreateSettings("http://127.0.0.1:1/");
                    WebCrawler crawler = new WebCrawler(settings);
                    crawler.Dispose();
                    crawler.Dispose();
                }),

                Case.Async(Id, "SinglePage_ReturnsResource", "A single page crawl returns one populated resource", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/", "<html><body><h1>root</h1></body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"));
                    WebResource resource = await CrawlHelper.CrawlSingleAsync(settings, ct);

                    Check.Equal(200, resource.Status);
                    Check.NotNull(resource.Data);
                    Check.True(resource.ContentLength > 0);
                    Check.Contains("root", Encoding.UTF8.GetString(resource.Data));
                    Check.NotNull(resource.ContentType);
                    Check.NotNull(resource.MD5Hash);
                    Check.NotNull(resource.SHA1Hash);
                    Check.NotNull(resource.SHA256Hash);
                }),

                Case.Async(Id, "SyncCrawl_ReturnsResource", "The synchronous Crawl overload returns a resource", async ct =>
                {
                    await Task.Yield();
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/", "<html><body>sync</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"));
                    using WebCrawler crawler = new WebCrawler(settings);

                    List<WebResource> resources = new List<WebResource>();
                    foreach (WebResource wr in crawler.Crawl(HttpMethod.Get))
                        resources.Add(wr);

                    WebResource resource = Check.Single(resources);
                    Check.Equal(200, resource.Status);
                }),

                Case.Async(Id, "NotFound_ReturnsStatus404", "A missing page returns a 404 resource", async ct =>
                {
                    using FixtureServer server = new FixtureServer();

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/missing"));
                    WebResource resource = await CrawlHelper.CrawlSingleAsync(settings, ct);

                    Check.Equal(404, resource.Status);
                }),

                Case.Async(Id, "VisitedLinks_Populated", "VisitedLinks is populated after a crawl", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/", "<html><body>visited</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"));
                    using WebCrawler crawler = new WebCrawler(settings, ct);

                    await foreach (WebResource _ in crawler.CrawlAsync(ct))
                    {
                    }

                    Check.True(crawler.VisitedLinks.Count > 0);
                }),

                Case.Async(Id, "FollowLinks_DiscoversChildren", "FollowLinks discovers linked pages", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/", "<html><body><a href=\"/page1\">1</a><a href=\"/page2\">2</a></body></html>");
                    server.AddHtml("/page1", "<html><body>page1</body></html>");
                    server.AddHtml("/page2", "<html><body>page2</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"), configure: c =>
                    {
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 2;
                    });

                    List<WebResource> resources = await CrawlHelper.CrawlAllAsync(settings, ct);

                    Check.Contains(resources, r => r.Url == server.UrlFor("/page1"));
                    Check.Contains(resources, r => r.Url == server.UrlFor("/page2"));
                }),

                Case.Async(Id, "FollowLinksDisabled_OnlyRoot", "Links are not followed when FollowLinks is false", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/", "<html><body><a href=\"/page1\">1</a></body></html>");
                    server.AddHtml("/page1", "<html><body>page1</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"), configure: c =>
                    {
                        c.FollowLinks = false;
                        c.MaxCrawlDepth = 5;
                    });

                    List<WebResource> resources = await CrawlHelper.CrawlAllAsync(settings, ct);

                    Check.DoesNotContain(resources, r => r.Url == server.UrlFor("/page1"));
                }),

                Case.Async(Id, "MaxCrawlDepthZero_OnlyRoot", "MaxCrawlDepth of zero prevents recursion", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/", "<html><body><a href=\"/page1\">1</a></body></html>");
                    server.AddHtml("/page1", "<html><body>page1</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"), configure: c =>
                    {
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 0;
                    });

                    List<WebResource> resources = await CrawlHelper.CrawlAllAsync(settings, ct);

                    Check.DoesNotContain(resources, r => r.Url == server.UrlFor("/page1"));
                }),

                Case.Async(Id, "RestrictToChildUrls_BlocksNonChildren", "RestrictToChildUrls blocks non-descendant links", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/section", "<html><body><a href=\"/section/child\">c</a><a href=\"/other\">o</a></body></html>");
                    server.AddHtml("/section/child", "<html><body>child</body></html>");
                    server.AddHtml("/other", "<html><body>other</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/section"), configure: c =>
                    {
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 2;
                        c.RestrictToChildUrls = true;
                    });

                    List<WebResource> resources = await CrawlHelper.CrawlAllAsync(settings, ct);

                    Check.Contains(resources, r => r.Url == server.UrlFor("/section/child"));
                    Check.DoesNotContain(resources, r => r.Url == server.UrlFor("/other"));
                }),

                Case.Async(Id, "ExcludeLinkPatterns_SkipsMatches", "ExcludeLinkPatterns prevents matching links from being crawled", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/", "<html><body><a href=\"/keep\">k</a><a href=\"/skip.pdf\">s</a></body></html>");
                    server.AddHtml("/keep", "<html><body>keep</body></html>");
                    server.AddResponse("/skip.pdf", "application/pdf", Encoding.ASCII.GetBytes("%PDF-1.7"));

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"), configure: c =>
                    {
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 2;
                        c.ExcludeLinkPatterns = new List<Regex> { new Regex("\\.pdf$") };
                    });

                    List<WebResource> resources = await CrawlHelper.CrawlAllAsync(settings, ct);

                    Check.Contains(resources, r => r.Url == server.UrlFor("/keep"));
                    Check.DoesNotContain(resources, r => r.Url == server.UrlFor("/skip.pdf"));
                }),

                Case.Async(Id, "Redirect_ReturnsFinalContent", "A redirect resolves to the final page content", async ct =>
                {
                    // The underlying HTTP client transparently follows redirects, so the crawler
                    // returns the final page (status 200) for the requested URL.
                    using FixtureServer server = new FixtureServer();
                    server.AddRedirect("/start", server.UrlFor("/final"), 302);
                    server.AddHtml("/final", "<html><body>final-destination</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/start"), configure: c =>
                    {
                        c.FollowRedirects = true;
                    });

                    WebResource resource = await CrawlHelper.CrawlSingleAsync(settings, ct);

                    Check.Equal(200, resource.Status);
                    Check.Contains("final-destination", Encoding.UTF8.GetString(resource.Data));
                }),

                Case.Async(Id, "Redirect_HttpStackFollowsRegardlessOfSetting", "The HTTP stack follows redirects even when FollowRedirects is disabled", async ct =>
                {
                    // FollowRedirects governs the crawler's own 3xx handling, but the HTTP client still
                    // resolves redirects itself, so the final content is returned either way.
                    using FixtureServer server = new FixtureServer();
                    server.AddRedirect("/start", server.UrlFor("/final"), 302);
                    server.AddHtml("/final", "<html><body>final-destination</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/start"), configure: c =>
                    {
                        c.FollowRedirects = false;
                    });

                    WebResource resource = await CrawlHelper.CrawlSingleAsync(settings, ct);

                    Check.Equal(200, resource.Status);
                    Check.Contains("final-destination", Encoding.UTF8.GetString(resource.Data));
                }),

                Case.Async(Id, "RobotsDisallow_Respected", "Disallowed paths are skipped when robots.txt is honored", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddResponse("/robots.txt", "text/plain", Encoding.UTF8.GetBytes("User-agent: *\nDisallow: /secret\n"));
                    server.AddHtml("/", "<html><body><a href=\"/allowed\">a</a><a href=\"/secret\">s</a></body></html>");
                    server.AddHtml("/allowed", "<html><body>allowed</body></html>");
                    server.AddHtml("/secret", "<html><body>secret</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"), configure: c =>
                    {
                        c.IgnoreRobotsText = false;
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 2;
                    });

                    List<WebResource> resources = await CrawlHelper.CrawlAllAsync(settings, ct);

                    Check.Contains(resources, r => r.Url == server.UrlFor("/allowed"));
                    Check.DoesNotContain(resources, r => r.Url == server.UrlFor("/secret"));
                }),

                Case.Async(Id, "IgnoreRobots_CrawlsDisallowed", "Disallowed paths are crawled when robots.txt is ignored", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddResponse("/robots.txt", "text/plain", Encoding.UTF8.GetBytes("User-agent: *\nDisallow: /secret\n"));
                    server.AddHtml("/", "<html><body><a href=\"/secret\">s</a></body></html>");
                    server.AddHtml("/secret", "<html><body>secret</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"), configure: c =>
                    {
                        c.IgnoreRobotsText = true;
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 2;
                    });

                    List<WebResource> resources = await CrawlHelper.CrawlAllAsync(settings, ct);

                    Check.Contains(resources, r => r.Url == server.UrlFor("/secret"));
                }),

                Case.Async(Id, "Sitemap_Included", "Sitemap URLs are enqueued when sitemap inclusion is enabled", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    string sitemap =
                        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                        "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">" +
                        "<url><loc>" + server.UrlFor("/from-sitemap") + "</loc></url>" +
                        "</urlset>";
                    server.AddResponse("/sitemap.xml", "application/xml", Encoding.UTF8.GetBytes(sitemap));
                    server.AddHtml("/", "<html><body>root</body></html>");
                    server.AddHtml("/from-sitemap", "<html><body>from sitemap</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"), configure: c =>
                    {
                        c.IncludeSitemap = true;
                        c.FollowLinks = false;
                        c.MaxCrawlDepth = 1;
                    });

                    List<WebResource> resources = await CrawlHelper.CrawlAllAsync(settings, ct);

                    Check.Contains(resources, r => r.Url == server.UrlFor("/from-sitemap"));
                }),

                Case.Async(Id, "Retry429_ThenSucceeds", "A 429 response is retried until success", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHandler("/throttled", _ =>
                    {
                        int n = server.RequestCount("/throttled");
                        if (n < 3)
                            return new FixtureResponse { StatusCode = 429, ContentType = "text/plain", Body = Encoding.UTF8.GetBytes("slow down") };
                        return new FixtureResponse { StatusCode = 200, ContentType = "text/html", Body = Encoding.UTF8.GetBytes("<html><body>ok</body></html>") };
                    });

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/throttled"), configure: c =>
                    {
                        c.RetryOn429 = true;
                        c.MaxRetries = 3;
                        c.RetryMinBackoffMs = 100;
                        c.RetryMaxBackoffMs = 1000;
                        c.RetryBackoffJitter = false;
                    });

                    WebResource resource = await CrawlHelper.CrawlSingleAsync(settings, ct);

                    Check.Equal(200, resource.Status);
                    Check.True(server.RequestCount("/throttled") >= 3);
                }),

                Case.Async(Id, "Retry429_Exhausted_Returns429", "A persistently throttled resource returns 429 after retries", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddResponse("/always", "text/plain", Encoding.UTF8.GetBytes("nope"), 429);

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/always"), configure: c =>
                    {
                        c.RetryOn429 = true;
                        c.MaxRetries = 1;
                        c.RetryMinBackoffMs = 100;
                        c.RetryMaxBackoffMs = 1000;
                        c.RetryBackoffJitter = false;
                        c.ThrottleMs = 0;
                    });

                    WebResource resource = await CrawlHelper.CrawlSingleAsync(settings, ct);

                    Check.Equal(429, resource.Status);
                    Check.True(server.RequestCount("/always") >= 2);
                }),
            };

            return new TestSuiteDescriptor(Id, "Web crawler integration", cases);
        }
    }
}
