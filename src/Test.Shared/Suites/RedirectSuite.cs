namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using CrawlSharp.Web;
    using Touchstone.Core;

    /// <summary>
    /// Integration coverage for redirect following: loops, hop limits, relative Location values, crawl scope,
    /// robots.txt, cookies within a chain, and how redirected pages are recorded and de-duplicated.
    /// Every crawl here runs under a timeout so a regression that hangs fails fast.
    /// </summary>
    public static class RedirectSuite
    {
        private const string Id = "Redirect";
        private const int TimeoutSeconds = 15;

        /// <summary>
        /// Build the suite.
        /// </summary>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case.Async(Id, "Ordinary_Followed", "A 302 is followed and both addresses are recorded", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddRedirect("/start", "/final", 302);
                    server.AddHtml("/final", "<html><body>final-destination</body></html>");

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(CrawlHelper.CreateSettings(server.UrlFor("/start")), TimeoutSeconds, ct);

                    Check.Equal(200, resource.Status);
                    Check.Equal(server.UrlFor("/start"), resource.Url);
                    Check.Equal(server.UrlFor("/final"), resource.FinalUrl);
                    Check.Equal(RedirectOutcomeEnum.Followed, resource.RedirectOutcome);
                    RedirectHop hop = Check.Single(resource.RedirectChain);
                    Check.Equal(server.UrlFor("/start"), hop.Url);
                    Check.Equal(302, hop.Status);
                    Check.Equal(server.UrlFor("/final"), hop.Location);
                    Check.Contains("final-destination", Encoding.UTF8.GetString(resource.Data));
                    Check.Equal(1, server.RequestCount("/start"));
                    Check.Equal(1, server.RequestCount("/final"));
                }),

                Case.Async(Id, "NoRedirect_OutcomeNone", "A page that does not redirect has outcome None, an empty chain and FinalUrl equal to Url", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/", "<html><body>plain</body></html>");

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(CrawlHelper.CreateSettings(server.UrlFor("/")), TimeoutSeconds, ct);

                    Check.Equal(RedirectOutcomeEnum.None, resource.RedirectOutcome);
                    Check.Empty(resource.RedirectChain);
                    Check.Equal(resource.Url, resource.FinalUrl);
                }),

                Case.Async(Id, "MultiHop_ChainRecorded", "Every hop of a 301, 307, 308 chain is recorded in order", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddRedirect("/h1", "/h2", 301);
                    server.AddRedirect("/h2", "/h3", 307);
                    server.AddRedirect("/h3", "/h4", 308);
                    server.AddHtml("/h4", "<html><body>end</body></html>");

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(CrawlHelper.CreateSettings(server.UrlFor("/h1")), TimeoutSeconds, ct);

                    Check.Equal(200, resource.Status);
                    Check.Equal(server.UrlFor("/h4"), resource.FinalUrl);
                    Check.Count(3, resource.RedirectChain);
                    Check.Equal(301, resource.RedirectChain[0].Status);
                    Check.Equal(307, resource.RedirectChain[1].Status);
                    Check.Equal(308, resource.RedirectChain[2].Status);
                    Check.Equal(server.UrlFor("/h3"), resource.RedirectChain[2].Url);
                    Check.Equal(server.UrlFor("/h4"), resource.RedirectChain[2].Location);
                }),

                Case.Async(Id, "Loop_Terminates", "An A to B to A loop ends after one pass with each URL requested once", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddRedirect("/loop-a", "/loop-b", 302);
                    server.AddRedirect("/loop-b", "/loop-a", 302);

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(CrawlHelper.CreateSettings(server.UrlFor("/loop-a")), TimeoutSeconds, ct);

                    Check.Equal(RedirectOutcomeEnum.LoopDetected, resource.RedirectOutcome);
                    Check.Equal(302, resource.Status);
                    Check.Count(2, resource.RedirectChain);
                    Check.Equal(1, server.RequestCount("/loop-a"));
                    Check.Equal(1, server.RequestCount("/loop-b"));
                }),

                Case.Async(Id, "Loop_SelfRedirect", "A page that redirects to itself ends as a loop after one request", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddRedirect("/self", "/self", 302);

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(CrawlHelper.CreateSettings(server.UrlFor("/self")), TimeoutSeconds, ct);

                    Check.Equal(RedirectOutcomeEnum.LoopDetected, resource.RedirectOutcome);
                    Check.Equal(1, server.RequestCount("/self"));
                }),

                Case.Async(Id, "Loop_FromLinkedPage", "A loop reached through a link is reported once and the rest of the crawl completes", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/", "<html><body><a href=\"/page2\">2</a><a href=\"/loop-a\">loop</a></body></html>");
                    server.AddHtml("/page2", "<html><body>page2</body></html>");
                    server.AddRedirect("/loop-a", "/loop-b", 302);
                    server.AddRedirect("/loop-b", "/loop-a", 302);

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"), configure: c =>
                    {
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 2;
                    });

                    List<WebResource> resources = await CrawlHelper.CrawlAllWithinAsync(settings, TimeoutSeconds, ct);

                    Check.Contains(resources, r => r.Url == server.UrlFor("/page2") && r.Status == 200);
                    WebResource loop = Check.Single(resources.Where(r => r.Url == server.UrlFor("/loop-a")));
                    Check.Equal(RedirectOutcomeEnum.LoopDetected, loop.RedirectOutcome);
                    Check.DoesNotContain(resources, r => r.Url == server.UrlFor("/loop-b"));
                    Check.Equal(1, server.RequestCount("/loop-a"));
                    Check.Equal(1, server.RequestCount("/loop-b"));
                }),

                Case.Async(Id, "HopLimit_Exceeded", "A chain longer than MaxRedirects stops without requesting the hop past the limit", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    for (int i = 0; i < 15; i++) server.AddRedirect("/r" + i, "/r" + (i + 1), 302);
                    server.AddHtml("/r15", "<html><body>end</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/r0"), configure: c => c.MaxRedirects = 10);
                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(settings, TimeoutSeconds, ct);

                    Check.Equal(RedirectOutcomeEnum.MaxRedirectsExceeded, resource.RedirectOutcome);
                    Check.Equal(302, resource.Status);
                    Check.Count(11, resource.RedirectChain);
                    Check.Equal(server.UrlFor("/r10"), resource.FinalUrl);
                    Check.Equal(1, server.RequestCount("/r10"));
                    Check.Equal(0, server.RequestCount("/r11"));
                }),

                Case.Async(Id, "HopLimit_Boundary", "A chain of exactly MaxRedirects hops is followed to the end", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    for (int i = 0; i < 10; i++) server.AddRedirect("/r" + i, "/r" + (i + 1), 302);
                    server.AddHtml("/r10", "<html><body>end</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/r0"), configure: c => c.MaxRedirects = 10);
                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(settings, TimeoutSeconds, ct);

                    Check.Equal(200, resource.Status);
                    Check.Equal(RedirectOutcomeEnum.Followed, resource.RedirectOutcome);
                    Check.Count(10, resource.RedirectChain);
                }),

                Case.Async(Id, "HopLimit_One", "MaxRedirects of 1 follows one hop and stops at the second", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddRedirect("/one", "/two", 302);
                    server.AddRedirect("/two", "/three", 302);
                    server.AddHtml("/three", "<html><body>end</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/one"), configure: c => c.MaxRedirects = 1);
                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(settings, TimeoutSeconds, ct);

                    Check.Equal(RedirectOutcomeEnum.MaxRedirectsExceeded, resource.RedirectOutcome);
                    Check.Equal(1, server.RequestCount("/two"));
                    Check.Equal(0, server.RequestCount("/three"));
                }),

                Case.Async(Id, "Relative_Locations", "Relative Location values resolve against the URL that sent them", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddRedirect("/docs/a", "b", 302);
                    server.AddHtml("/docs/b", "<html><body>docs-b</body></html>");
                    server.AddRedirect("/up/x", "../y", 302);
                    server.AddHtml("/y", "<html><body>y</body></html>");
                    server.AddHandler("/q", context => String.IsNullOrEmpty(context.Request.Url.Query)
                        ? RedirectResponse("?z=1", 302)
                        : HtmlResponse("query " + context.Request.Url.Query));

                    WebResource docs = await CrawlHelper.CrawlSingleWithinAsync(CrawlHelper.CreateSettings(server.UrlFor("/docs/a")), TimeoutSeconds, ct);
                    WebResource up = await CrawlHelper.CrawlSingleWithinAsync(CrawlHelper.CreateSettings(server.UrlFor("/up/x")), TimeoutSeconds, ct);
                    WebResource query = await CrawlHelper.CrawlSingleWithinAsync(CrawlHelper.CreateSettings(server.UrlFor("/q")), TimeoutSeconds, ct);

                    Check.Equal(server.UrlFor("/docs/b"), docs.FinalUrl);
                    Check.Contains("docs-b", Encoding.UTF8.GetString(docs.Data));
                    Check.Equal(server.UrlFor("/y"), up.FinalUrl);
                    Check.Equal(server.UrlFor("/q?z=1"), query.FinalUrl);
                    Check.Contains("query ?z=1", Encoding.UTF8.GetString(query.Data));
                }),

                Case.Async(Id, "AbsoluteLocation_Followed", "An absolute Location on the same server is followed", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddRedirect("/start", server.UrlFor("/final"), 301);
                    server.AddHtml("/final", "<html><body>abs</body></html>");

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(CrawlHelper.CreateSettings(server.UrlFor("/start")), TimeoutSeconds, ct);

                    Check.Equal(200, resource.Status);
                    Check.Equal(server.UrlFor("/final"), resource.FinalUrl);
                }),

                Case.Async(Id, "NotFollowed_WhenDisabled", "FollowRedirects false returns the 302 and never requests the target", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddRedirect("/start", "/final", 302);
                    server.AddHtml("/final", "<html><body>final</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/start"), configure: c => c.FollowRedirects = false);
                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(settings, TimeoutSeconds, ct);

                    Check.Equal(302, resource.Status);
                    Check.Equal(RedirectOutcomeEnum.NotFollowed, resource.RedirectOutcome);
                    Check.Equal("/final", resource.Headers["Location"]);
                    Check.Empty(resource.RedirectChain);
                    Check.Equal(resource.Url, resource.FinalUrl);
                    Check.Equal(0, server.RequestCount("/final"));
                }),

                Case.Async(Id, "NotFollowed_TargetNotQueued", "With FollowRedirects false, a link to the target in the redirect body is not queued", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHandler("/", _ => new FixtureResponse
                    {
                        StatusCode = 302,
                        ContentType = "text/html; charset=utf-8",
                        Body = Encoding.UTF8.GetBytes("<html><body>Moved to <a href=\"/final\">here</a></body></html>"),
                        Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "Location", "/final" } }
                    });
                    server.AddHtml("/final", "<html><body>final</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"), configure: c =>
                    {
                        c.FollowRedirects = false;
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 2;
                    });

                    List<WebResource> resources = await CrawlHelper.CrawlAllWithinAsync(settings, TimeoutSeconds, ct);

                    WebResource resource = Check.Single(resources);
                    Check.Equal(RedirectOutcomeEnum.NotFollowed, resource.RedirectOutcome);
                    Check.Equal(0, server.RequestCount("/final"));
                }),

                Case.Async(Id, "OutOfScope_ChildUrls", "A redirect outside RestrictToChildUrls is not followed", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddRedirect("/section/start", "/other", 302);
                    server.AddHtml("/other", "<html><body>other</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/section/start"), configure: c =>
                    {
                        c.FollowLinks = true;
                        c.RestrictToChildUrls = true;
                    });

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(settings, TimeoutSeconds, ct);

                    Check.Equal(RedirectOutcomeEnum.OutOfScope, resource.RedirectOutcome);
                    Check.Equal(302, resource.Status);
                    Check.Equal(server.UrlFor("/other"), Check.Single(resource.RedirectChain).Location);
                    Check.Equal(0, server.RequestCount("/other"));
                }),

                Case.Async(Id, "OutOfScope_ChildRedirectFollowed", "A redirect that stays within the child URL scope is followed", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddRedirect("/section/start", "/section/start/moved", 301);
                    server.AddHtml("/section/start/moved", "<html><body>moved</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/section/start"), configure: c =>
                    {
                        c.FollowLinks = true;
                        c.RestrictToChildUrls = true;
                    });

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(settings, TimeoutSeconds, ct);

                    Check.Equal(RedirectOutcomeEnum.Followed, resource.RedirectOutcome);
                    Check.Equal(200, resource.Status);
                }),

                Case.Async(Id, "Scope_NotAppliedWithoutFollowLinks", "Scope filters do not block redirects when FollowLinks is false, matching their documented behavior", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddRedirect("/section/start", "/other", 302);
                    server.AddHtml("/other", "<html><body>other</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/section/start"), configure: c =>
                    {
                        c.FollowLinks = false;
                        c.RestrictToChildUrls = true;
                    });

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(settings, TimeoutSeconds, ct);

                    Check.Equal(RedirectOutcomeEnum.Followed, resource.RedirectOutcome);
                    Check.Equal(200, resource.Status);
                    Check.Equal(1, server.RequestCount("/other"));
                }),

                Case.Async(Id, "OutOfScope_DeniedDomain", "A redirect to a denied domain is not followed and causes no network error", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddRedirect("/start", "http://denied.invalid/x", 302);

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/start"), configure: c =>
                    {
                        c.FollowLinks = true;
                        c.RestrictToChildUrls = false;
                        c.RestrictToSameSubdomain = false;
                        c.RestrictToSameRootDomain = false;
                        c.FollowExternalLinks = true;
                        c.DeniedDomains = new List<string> { "denied.invalid" };
                    });

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(settings, TimeoutSeconds, ct);

                    Check.Equal(RedirectOutcomeEnum.OutOfScope, resource.RedirectOutcome);
                    Check.Equal("http://denied.invalid/x", Check.Single(resource.RedirectChain).Location);
                }),

                Case.Async(Id, "OutOfScope_ExcludePattern", "A redirect to a URL matching ExcludeLinkPatterns is not followed", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddRedirect("/start", "/skip.pdf", 302);
                    server.AddResponse("/skip.pdf", "application/pdf", Encoding.ASCII.GetBytes("%PDF-1.7"));

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/start"), configure: c =>
                    {
                        c.FollowLinks = true;
                        c.RestrictToChildUrls = false;
                        c.ExcludeLinkPatterns = new List<Regex> { new Regex("\\.pdf$") };
                    });

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(settings, TimeoutSeconds, ct);

                    Check.Equal(RedirectOutcomeEnum.OutOfScope, resource.RedirectOutcome);
                    Check.Equal(0, server.RequestCount("/skip.pdf"));
                }),

                Case.Async(Id, "Robots_BlocksHop", "A redirect to a path disallowed by robots.txt is not followed", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddResponse("/robots.txt", "text/plain", Encoding.UTF8.GetBytes("User-agent: *\nDisallow: /secret\n"));
                    server.AddRedirect("/start", "/secret", 302);
                    server.AddHtml("/secret", "<html><body>secret</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/start"), configure: c => c.IgnoreRobotsText = false);
                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(settings, TimeoutSeconds, ct);

                    Check.Equal(RedirectOutcomeEnum.RobotsDisallowed, resource.RedirectOutcome);
                    Check.Equal(0, server.RequestCount("/secret"));
                }),

                Case.Async(Id, "Robots_IgnoredFollowsHop", "With robots.txt ignored, a redirect to a disallowed path is followed", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddResponse("/robots.txt", "text/plain", Encoding.UTF8.GetBytes("User-agent: *\nDisallow: /secret\n"));
                    server.AddRedirect("/start", "/secret", 302);
                    server.AddHtml("/secret", "<html><body>secret</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/start"), configure: c => c.IgnoreRobotsText = true);
                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(settings, TimeoutSeconds, ct);

                    Check.Equal(RedirectOutcomeEnum.Followed, resource.RedirectOutcome);
                    Check.Equal(1, server.RequestCount("/secret"));
                }),

                Case.Async(Id, "RobotsFile_RedirectOnSameHostFollowed", "A robots.txt redirect on the start host is followed and its rules apply", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddRedirect("/robots.txt", "/real-robots.txt", 301);
                    server.AddResponse("/real-robots.txt", "text/plain", Encoding.UTF8.GetBytes("User-agent: *\nDisallow: /secret\n"));
                    server.AddHtml("/", "<html><body><a href=\"/secret\">s</a><a href=\"/open\">o</a></body></html>");
                    server.AddHtml("/secret", "<html><body>secret</body></html>");
                    server.AddHtml("/open", "<html><body>open</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"), configure: c =>
                    {
                        c.IgnoreRobotsText = false;
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 1;
                    });

                    List<WebResource> resources = await CrawlHelper.CrawlAllWithinAsync(settings, TimeoutSeconds, ct);

                    Check.Equal(1, server.RequestCount("/real-robots.txt"));
                    Check.Contains(resources, r => r.Url == server.UrlFor("/open"));
                    Check.Equal(0, server.RequestCount("/secret"));
                }),

                Case.Async(Id, "NotModified_NotARedirect", "A 304 with a Location header is returned as-is, not followed", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddResponse("/nm", "text/plain", Array.Empty<byte>(), 304, new Dictionary<string, string> { { "Location", "/target" } });
                    server.AddHtml("/target", "<html><body>target</body></html>");

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(CrawlHelper.CreateSettings(server.UrlFor("/nm")), TimeoutSeconds, ct);

                    Check.Equal(304, resource.Status);
                    Check.Equal(RedirectOutcomeEnum.None, resource.RedirectOutcome);
                    Check.Equal(0, server.RequestCount("/target"));
                }),

                Case.Async(Id, "MultipleChoices_WithLocation_Followed", "A 300 with a Location header is followed", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddRedirect("/choices", "/chosen", 300);
                    server.AddHtml("/chosen", "<html><body>chosen</body></html>");

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(CrawlHelper.CreateSettings(server.UrlFor("/choices")), TimeoutSeconds, ct);

                    Check.Equal(200, resource.Status);
                    Check.Equal(RedirectOutcomeEnum.Followed, resource.RedirectOutcome);
                }),

                Case.Async(Id, "MultipleChoices_WithoutLocation_Returned", "A 300 without a Location header is returned as a normal response", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddResponse("/choices", "text/html", Encoding.UTF8.GetBytes("<html><body>pick one</body></html>"), 300);

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(CrawlHelper.CreateSettings(server.UrlFor("/choices")), TimeoutSeconds, ct);

                    Check.Equal(300, resource.Status);
                    Check.Equal(RedirectOutcomeEnum.None, resource.RedirectOutcome);
                }),

                Case.Async(Id, "MissingLocation_Reported", "A 302 without a Location header ends with MissingLocation", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddResponse("/broken", "text/plain", Encoding.UTF8.GetBytes("moved somewhere"), 302);

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(CrawlHelper.CreateSettings(server.UrlFor("/broken")), TimeoutSeconds, ct);

                    Check.Equal(302, resource.Status);
                    Check.Equal(RedirectOutcomeEnum.MissingLocation, resource.RedirectOutcome);
                    Check.Empty(resource.RedirectChain);
                    Check.Contains("moved somewhere", Encoding.UTF8.GetString(resource.Data));
                }),

                Case.Async(Id, "InvalidLocation_Reported", "A 302 to a non-http Location ends with InvalidLocation and records the raw value", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddRedirect("/bad", "javascript:alert(1)", 302);

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(CrawlHelper.CreateSettings(server.UrlFor("/bad")), TimeoutSeconds, ct);

                    Check.Equal(302, resource.Status);
                    Check.Equal(RedirectOutcomeEnum.InvalidLocation, resource.RedirectOutcome);
                    Check.Equal("javascript:alert(1)", Check.Single(resource.RedirectChain).Location);
                }),

                Case.Async(Id, "LinkBase_UsesFinalUrl", "Links on a redirected page resolve against its final URL", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddRedirect("/docs", "/docs/", 301);
                    server.AddHtml("/docs/", "<html><body><a href=\"guide.html\">guide</a></body></html>");
                    server.AddHtml("/docs/guide.html", "<html><body>guide</body></html>");
                    server.AddHtml("/guide.html", "<html><body>wrong</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/docs"), configure: c =>
                    {
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 1;
                    });

                    List<WebResource> resources = await CrawlHelper.CrawlAllWithinAsync(settings, TimeoutSeconds, ct);

                    Check.Contains(resources, r => r.Url == server.UrlFor("/docs/guide.html"));
                    Check.Equal(0, server.RequestCount("/guide.html"));
                }),

                Case.Async(Id, "SharedTarget_YieldedOnce", "Two links that redirect to the same page yield it once and fetch it once", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/", "<html><body><a href=\"/a\">a</a><a href=\"/b\">b</a></body></html>");
                    server.AddRedirect("/a", "/final", 302);
                    server.AddRedirect("/b", "/final", 302);
                    server.AddHtml("/final", "<html><body>final</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"), configure: c =>
                    {
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 1;
                    });

                    List<WebResource> resources = await CrawlHelper.CrawlAllWithinAsync(settings, TimeoutSeconds, ct);

                    Check.Equal(1, server.RequestCount("/final"));
                    Check.Equal(1, resources.Count(r => r.FinalUrl == server.UrlFor("/final")));
                    Check.Equal(resources.Count, resources.Select(r => r.FinalUrl).Distinct().Count(), "No two resources may share a FinalUrl.");
                }),

                Case.Async(Id, "VisitedTarget_NotReplayed", "A later link to a redirect's target is not requested again", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/", "<html><body><a href=\"/moved\">m</a><a href=\"/final\">f</a></body></html>");
                    server.AddRedirect("/moved", "/final", 301);
                    server.AddHtml("/final", "<html><body>final</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"), configure: c =>
                    {
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 1;
                    });

                    List<WebResource> resources = await CrawlHelper.CrawlAllWithinAsync(settings, TimeoutSeconds, ct);

                    Check.Equal(1, server.RequestCount("/final"));
                    Check.Equal(1, server.RequestCount("/moved"));
                    Check.Equal(1, resources.Count(r => r.FinalUrl == server.UrlFor("/final")));
                }),

                Case.Async(Id, "Cookie_CarriedWithinChain", "A cookie set by a redirect is sent on the next hop, so a self-redirect gate completes", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHandler("/bounce", context =>
                    {
                        string cookie = context.Request.Headers["Cookie"] ?? String.Empty;
                        if (cookie.Contains("gate=open", StringComparison.Ordinal)) return HtmlResponse("passed the gate");

                        FixtureResponse redirect = RedirectResponse("/bounce", 302);
                        redirect.Headers["Set-Cookie"] = "gate=open; Path=/; Expires=Wed, 21 Oct 2037 07:28:00 GMT";
                        return redirect;
                    });

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(CrawlHelper.CreateSettings(server.UrlFor("/bounce")), TimeoutSeconds, ct);

                    Check.Equal(200, resource.Status);
                    Check.Equal(RedirectOutcomeEnum.Followed, resource.RedirectOutcome);
                    Check.Contains("passed the gate", Encoding.UTF8.GetString(resource.Data));
                    Check.Equal(2, server.RequestCount("/bounce"));
                }),

                Case.Async(Id, "Cookie_ChangingEveryHop_StopsAtLimit", "A self-redirect that sets a new cookie every time still stops at MaxRedirects", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHandler("/churn", context =>
                    {
                        FixtureResponse redirect = RedirectResponse("/churn", 302);
                        redirect.Headers["Set-Cookie"] = "n=" + server.RequestCount("/churn") + "; Path=/";
                        return redirect;
                    });

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/churn"), configure: c => c.MaxRedirects = 5);
                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(settings, TimeoutSeconds, ct);

                    Check.Equal(RedirectOutcomeEnum.MaxRedirectsExceeded, resource.RedirectOutcome);
                    Check.Equal(6, server.RequestCount("/churn"));
                }),

                Case.Async(Id, "Cookie_NotSharedAcrossChains", "Cookies from one page's redirect chain are not sent when retrieving another page", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/", "<html><body><a href=\"/set\">s</a><a href=\"/check\">c</a></body></html>");
                    server.AddHandler("/set", _ =>
                    {
                        FixtureResponse redirect = RedirectResponse("/landing", 302);
                        redirect.Headers["Set-Cookie"] = "chain=one; Path=/";
                        return redirect;
                    });
                    server.AddHtml("/landing", "<html><body>landing</body></html>");
                    server.AddHtml("/check", "<html><body>check</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"), configure: c =>
                    {
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 1;
                    });

                    await CrawlHelper.CrawlAllWithinAsync(settings, TimeoutSeconds, ct);

                    Check.Contains("chain=one", Check.Single(server.Requests("/landing")).Headers["Cookie"] ?? String.Empty);
                    Check.Null(Check.Single(server.Requests("/check")).Headers["Cookie"]);
                }),

                Case.Async(Id, "SeeOther_UsesGet", "The request after a 303 uses GET", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddRedirect("/form", "/result", 303);
                    server.AddHtml("/result", "<html><body>result</body></html>");

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(CrawlHelper.CreateSettings(server.UrlFor("/form")), TimeoutSeconds, ct);

                    Check.Equal(200, resource.Status);
                    Check.Equal("GET", Check.Single(server.Requests("/result")).Method);
                }),

                Case.Async(Id, "HopRetriesOn429", "A redirect hop that returns 429 is retried like any other request", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddRedirect("/start", "/busy", 302);
                    server.AddHandler("/busy", _ => server.RequestCount("/busy") < 2
                        ? new FixtureResponse { StatusCode = 429, ContentType = "text/plain", Body = Encoding.UTF8.GetBytes("slow down") }
                        : HtmlResponse("ready"));

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/start"), configure: c =>
                    {
                        c.RetryOn429 = true;
                        c.MaxRetries = 3;
                        c.RetryMinBackoffMs = 100;
                        c.RetryMaxBackoffMs = 1000;
                        c.RetryBackoffJitter = false;
                    });

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(settings, TimeoutSeconds, ct);

                    Check.Equal(200, resource.Status);
                    Check.Equal(RedirectOutcomeEnum.Followed, resource.RedirectOutcome);
                    Check.Count(1, resource.RedirectChain);
                    Check.Equal(2, server.RequestCount("/busy"));
                }),

                Case.Async(Id, "VisitedLinks_IncludeEveryHop", "VisitedLinks records the requested URL, each intermediate hop and the final URL", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddRedirect("/one", "/two", 302);
                    server.AddRedirect("/two", "/three", 302);
                    server.AddHtml("/three", "<html><body>three</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/one"));
                    using WebCrawler crawler = new WebCrawler(settings, ct);
                    await foreach (WebResource _ in crawler.CrawlAsync(ct))
                    {
                    }

                    Dictionary<Uri, WebResource> visited = crawler.VisitedLinks;
                    WebResource one = visited[new Uri(server.UrlFor("/one"))];
                    Check.True(ReferenceEquals(one, visited[new Uri(server.UrlFor("/two"))]));
                    Check.True(ReferenceEquals(one, visited[new Uri(server.UrlFor("/three"))]));
                }),
            };

            return new TestSuiteDescriptor(Id, "Redirect following", cases);
        }

        private static FixtureResponse RedirectResponse(string location, int statusCode)
        {
            return new FixtureResponse
            {
                StatusCode = statusCode,
                ContentType = "text/plain; charset=utf-8",
                Body = Array.Empty<byte>(),
                Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "Location", location } }
            };
        }

        private static FixtureResponse HtmlResponse(string body)
        {
            return new FixtureResponse
            {
                StatusCode = 200,
                ContentType = "text/html; charset=utf-8",
                Body = Encoding.UTF8.GetBytes("<html><body>" + body + "</body></html>")
            };
        }
    }
}
