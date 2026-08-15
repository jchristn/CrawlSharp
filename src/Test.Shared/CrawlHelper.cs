namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using CrawlSharp.Web;

    /// <summary>
    /// Helpers for constructing crawler settings and executing crawls against a fixture server.
    /// </summary>
    public static class CrawlHelper
    {
        /// <summary>
        /// Build a fast, deterministic set of crawler settings suitable for tests.  Networking delays,
        /// sitemap inclusion, and robots.txt processing are disabled unless overridden.
        /// </summary>
        public static Settings CreateSettings(string startUrl, bool headless = false, Action<CrawlSettings> configure = null)
        {
            Settings settings = new Settings();
            settings.Crawl.StartUrl = startUrl;
            settings.Crawl.UserAgent = "CrawlSharp.Tests";
            settings.Crawl.UseHeadlessBrowser = headless;
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
            settings.Crawl.RetryOn429 = false;
            configure?.Invoke(settings.Crawl);
            return settings;
        }

        /// <summary>
        /// Execute a crawl and collect every returned resource.
        /// </summary>
        public static async Task<List<WebResource>> CrawlAllAsync(Settings settings, CancellationToken token = default)
        {
            using WebCrawler crawler = new WebCrawler(settings, token);
            List<WebResource> resources = new List<WebResource>();

            await foreach (WebResource resource in crawler.CrawlAsync(token))
            {
                resources.Add(resource);
            }

            return resources;
        }

        /// <summary>
        /// Execute a crawl and return the single expected resource.
        /// </summary>
        public static async Task<WebResource> CrawlSingleAsync(Settings settings, CancellationToken token = default)
        {
            List<WebResource> resources = await CrawlAllAsync(settings, token).ConfigureAwait(false);
            return Check.Single(resources, "Expected exactly one crawled resource but received " + resources.Count + ".");
        }
    }
}
