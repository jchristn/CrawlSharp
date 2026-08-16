namespace Test.Shared
{
    using System.Collections.Generic;
    using Test.Shared.Suites;
    using Touchstone.Core;

    /// <summary>
    /// Central source of truth for all CrawlSharp Touchstone test suites.  Every runner
    /// (CLI, xUnit, and NUnit) consumes <see cref="All"/>.
    /// </summary>
    public static class CrawlSharpSuites
    {
        /// <summary>
        /// The complete, ordered set of CrawlSharp test suites.
        /// </summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get
            {
                return new List<TestSuiteDescriptor>
                {
                    CrawlSettingsSuite.Build(),
                    SettingsSuite.Build(),
                    WebResourceSuite.Build(),
                    ModelsSuite.Build(),
                    RobotsFileSuite.Build(),
                    SitemapParserSuite.Build(),
                    HashHelperSuite.Build(),
                    WebCrawlerSuite.Build(),
                    AuthenticationCrawlSuite.Build(),
                    DomainFilterSuite.Build(),
                    HeadlessSuite.Build()
                };
            }
        }
    }
}
