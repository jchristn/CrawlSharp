namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using CrawlSharp.Web;
    using Touchstone.Core;

    /// <summary>
    /// Integration coverage for the discovered-link domain filters
    /// (<see cref="CrawlSettings.DeniedDomains"/> and <see cref="CrawlSettings.AllowedDomains"/>)
    /// and for parallel crawling via <see cref="CrawlSettings.MaxParallelTasks"/>.
    /// <para>
    /// The fixture server binds to a loopback host, so these cases exercise the host-matching logic
    /// by allowing or denying the fixture's own host (127.0.0.1).  The start URL is always retrieved;
    /// the filters govern only which discovered links are enqueued.
    /// </para>
    /// </summary>
    public static class DomainFilterSuite
    {
        private const string Id = "DomainFilter";

        /// <summary>
        /// Build the suite.
        /// </summary>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case.Async(Id, "DeniedDomain_SkipsChild", "A discovered link on a denied host is not crawled", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/hub", "<html><body><a href=\"/hub/child\">c</a></body></html>");
                    server.AddHtml("/hub/child", "<html><body>child</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/hub"), configure: c =>
                    {
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 2;
                        c.DeniedDomains = new List<string> { "127.0.0.1" };
                    });

                    List<WebResource> resources = await CrawlHelper.CrawlAllAsync(settings, ct);

                    // The start URL is still returned, but the denied child is filtered out.
                    Check.Contains(resources, r => r.Url == server.UrlFor("/hub"));
                    Check.DoesNotContain(resources, r => r.Url == server.UrlFor("/hub/child"));
                }),

                Case.Async(Id, "AllowedDomain_Match_CrawlsChild", "A discovered link on an allowed host is crawled", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/hub", "<html><body><a href=\"/hub/child\">c</a></body></html>");
                    server.AddHtml("/hub/child", "<html><body>child</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/hub"), configure: c =>
                    {
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 2;
                        c.AllowedDomains = new List<string> { "127.0.0.1" };
                    });

                    List<WebResource> resources = await CrawlHelper.CrawlAllAsync(settings, ct);

                    Check.Contains(resources, r => r.Url == server.UrlFor("/hub/child"));
                }),

                Case.Async(Id, "AllowedDomain_NoMatch_SkipsChild", "A discovered link outside the allow-list is not crawled", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/hub", "<html><body><a href=\"/hub/child\">c</a></body></html>");
                    server.AddHtml("/hub/child", "<html><body>child</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/hub"), configure: c =>
                    {
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 2;
                        // The fixture host (127.0.0.1) is intentionally absent from the allow-list.
                        c.AllowedDomains = new List<string> { "example.com" };
                    });

                    List<WebResource> resources = await CrawlHelper.CrawlAllAsync(settings, ct);

                    Check.Contains(resources, r => r.Url == server.UrlFor("/hub"));
                    Check.DoesNotContain(resources, r => r.Url == server.UrlFor("/hub/child"));
                }),

                Case.Async(Id, "MaxParallelTasks_CrawlsAllChildren", "Parallel crawling returns every discovered page exactly once", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    System.Text.StringBuilder links = new System.Text.StringBuilder("<html><body>");
                    for (int i = 0; i < 12; i++)
                    {
                        links.Append("<a href=\"/hub/child").Append(i).Append("\">c").Append(i).Append("</a>");
                        server.AddHtml("/hub/child" + i, "<html><body>child " + i + "</body></html>");
                    }
                    links.Append("</body></html>");
                    server.AddHtml("/hub", links.ToString());

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/hub"), configure: c =>
                    {
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 2;
                        c.MaxParallelTasks = 4;
                    });

                    List<WebResource> resources = await CrawlHelper.CrawlAllAsync(settings, ct);

                    for (int i = 0; i < 12; i++)
                    {
                        string childUrl = server.UrlFor("/hub/child" + i);
                        Check.Equal(1, resources.Count(r => r.Url == childUrl));
                    }
                }),
            };

            return new TestSuiteDescriptor(Id, "Domain filtering and parallelism", cases);
        }
    }
}
