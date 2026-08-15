namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using CrawlSharp.Web;
    using Touchstone.Core;

    /// <summary>
    /// Coverage for <see cref="RobotsFile"/> parsing and path-permission logic.
    /// </summary>
    public static class RobotsFileSuite
    {
        private const string Id = "RobotsFile";

        private const string Sample =
            "User-agent: *\n" +
            "Disallow: /admin\n" +
            "Allow: /admin/public\n" +
            "Crawl-delay: 10\n" +
            "Sitemap: http://example.com/sitemap.xml\n";

        /// <summary>
        /// Build the suite.
        /// </summary>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case.Sync(Id, "Empty_AllowsAll", "An empty robots file allows every path", () =>
                {
                    RobotsFile robots = new RobotsFile("");
                    Check.Equal("", robots.Contents);
                    Check.True(robots.IsPathAllowed("*", "/anything"));
                    Check.Empty(robots.Disallow);
                    Check.Empty(robots.Allow);
                }),

                Case.Sync(Id, "NullString_Normalized", "A null string is treated as empty content", () =>
                {
                    RobotsFile robots = new RobotsFile((string)null);
                    Check.Equal("", robots.Contents);
                    Check.True(robots.IsPathAllowed("*", "/anything"));
                }),

                Case.Sync(Id, "NullBytes_Normalized", "A null byte array is treated as empty content", () =>
                {
                    RobotsFile robots = new RobotsFile((byte[])null);
                    Check.Equal("", robots.Contents);
                }),

                Case.Sync(Id, "ByteCtor_ParsesUtf8", "The byte array constructor parses UTF-8 content", () =>
                {
                    RobotsFile robots = new RobotsFile(Encoding.UTF8.GetBytes(Sample));
                    Check.Contains("/admin", string.Join(",", robots.GetDisallowUrls("*")));
                }),

                Case.Sync(Id, "GetDisallowUrls", "Disallow URLs are parsed for the wildcard agent", () =>
                {
                    RobotsFile robots = new RobotsFile(Sample);
                    List<string> disallow = robots.GetDisallowUrls("*").ToList();
                    Check.Contains(disallow, u => u == "/admin");
                }),

                Case.Sync(Id, "GetDisallowUrls_DefaultAgent", "GetDisallowUrls defaults to the wildcard agent", () =>
                {
                    RobotsFile robots = new RobotsFile(Sample);
                    List<string> disallow = robots.GetDisallowUrls().ToList();
                    Check.Contains(disallow, u => u == "/admin");
                }),

                Case.Sync(Id, "GetAllowUrls", "Allow URLs are parsed for the wildcard agent", () =>
                {
                    RobotsFile robots = new RobotsFile(Sample);
                    List<string> allow = robots.GetAllowUrls("*").ToList();
                    Check.Contains(allow, u => u == "/admin/public");
                }),

                Case.Sync(Id, "GetCrawlDelay", "Crawl-delay is parsed for the wildcard agent", () =>
                {
                    RobotsFile robots = new RobotsFile(Sample);
                    Check.Equal(10m, robots.GetCrawlDelay("*"));
                }),

                Case.Sync(Id, "GetCrawlDelay_Unknown_Zero", "Crawl-delay is zero for an unknown agent", () =>
                {
                    RobotsFile robots = new RobotsFile(Sample);
                    Check.Equal(0m, robots.GetCrawlDelay("Nonexistent"));
                }),

                Case.Sync(Id, "GetCrawlDelay_Malformed_Ignored", "A non-numeric crawl-delay is ignored", () =>
                {
                    RobotsFile robots = new RobotsFile("User-agent: *\nCrawl-delay: abc\n");
                    Check.Equal(0m, robots.GetCrawlDelay("*"));
                }),

                Case.Sync(Id, "GetSitemap", "The sitemap URL is parsed", () =>
                {
                    RobotsFile robots = new RobotsFile(Sample);
                    Check.Equal("http://example.com/sitemap.xml", robots.GetSitemap("*"));
                }),

                Case.Sync(Id, "GetSitemap_Unknown_Null", "The sitemap is null for an unknown agent", () =>
                {
                    RobotsFile robots = new RobotsFile(Sample);
                    Check.Null(robots.GetSitemap("Nonexistent"));
                }),

                Case.Sync(Id, "HasRules_True", "HasRulesForUserAgent is true for a configured agent", () =>
                {
                    RobotsFile robots = new RobotsFile(Sample);
                    Check.True(robots.HasRulesForUserAgent("*"));
                }),

                Case.Sync(Id, "HasRules_UnknownAgent_False", "HasRulesForUserAgent is false for an unknown agent", () =>
                {
                    RobotsFile robots = new RobotsFile(Sample);
                    Check.False(robots.HasRulesForUserAgent("Nonexistent"));
                }),

                Case.Sync(Id, "HasRules_NullEmpty_False", "HasRulesForUserAgent is false for null or empty agents", () =>
                {
                    RobotsFile robots = new RobotsFile(Sample);
                    Check.False(robots.HasRulesForUserAgent(null));
                    Check.False(robots.HasRulesForUserAgent(""));
                }),

                Case.Sync(Id, "IsPathAllowed_DisallowedPath", "A disallowed path is blocked", () =>
                {
                    RobotsFile robots = new RobotsFile(Sample);
                    Check.False(robots.IsPathAllowed("*", "/admin/secret"));
                }),

                Case.Sync(Id, "IsPathAllowed_MoreSpecificAllow", "A more specific Allow overrides a broader Disallow", () =>
                {
                    RobotsFile robots = new RobotsFile(Sample);
                    Check.True(robots.IsPathAllowed("*", "/admin/public/page"));
                }),

                Case.Sync(Id, "IsPathAllowed_UnmatchedPath", "An unmatched path is allowed", () =>
                {
                    RobotsFile robots = new RobotsFile(Sample);
                    Check.True(robots.IsPathAllowed("*", "/index.html"));
                }),

                Case.Sync(Id, "IsPathAllowed_NormalizesLeadingSlash", "A path without a leading slash is normalized", () =>
                {
                    RobotsFile robots = new RobotsFile(Sample);
                    Check.False(robots.IsPathAllowed("*", "admin/secret"));
                }),

                Case.Sync(Id, "IsPathAllowed_EqualSpecificity_AllowWins", "Equally specific Allow and Disallow resolve to allow", () =>
                {
                    RobotsFile robots = new RobotsFile("User-agent: *\nDisallow: /docs\nAllow: /docs\n");
                    Check.True(robots.IsPathAllowed("*", "/docs/page"));
                }),

                Case.Sync(Id, "IsPathAllowed_SpecificAgentOverridesWildcard", "Specific agent rules take precedence over the wildcard", () =>
                {
                    RobotsFile robots = new RobotsFile(
                        "User-agent: Googlebot\nDisallow: /no-google\n" +
                        "User-agent: *\nDisallow: /no-all\n");

                    // Googlebot has its own rules and ignores the wildcard's /no-all.
                    Check.False(robots.IsPathAllowed("Googlebot", "/no-google/x"));
                    Check.True(robots.IsPathAllowed("Googlebot", "/no-all/x"));

                    // An unknown agent falls back to the wildcard rules.
                    Check.False(robots.IsPathAllowed("OtherBot", "/no-all/x"));
                }),

                Case.Sync(Id, "Parse_IgnoresComments", "Comment lines and pre-agent lines are ignored", () =>
                {
                    RobotsFile robots = new RobotsFile("# a comment\nDisallow: /orphan\nUser-agent: *\nDisallow: /blocked\n");
                    Check.True(robots.IsPathAllowed("*", "/orphan"));
                    Check.False(robots.IsPathAllowed("*", "/blocked"));
                }),
            };

            return new TestSuiteDescriptor(Id, "robots.txt parsing", cases);
        }
    }
}
