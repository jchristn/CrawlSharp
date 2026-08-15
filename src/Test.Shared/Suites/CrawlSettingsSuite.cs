namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using CrawlSharp.Web;
    using Touchstone.Core;

    /// <summary>
    /// Coverage for <see cref="CrawlSettings"/> defaults, clamping, and validation.
    /// </summary>
    public static class CrawlSettingsSuite
    {
        private const string Id = "CrawlSettings";

        /// <summary>
        /// Build the suite.
        /// </summary>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case.Sync(Id, "Defaults", "Default values match documented defaults", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    Check.Equal("CrawlSharp", s.UserAgent);
                    Check.Null(s.StartUrl);
                    Check.False(s.UseHeadlessBrowser);
                    Check.False(s.IgnoreRobotsText);
                    Check.True(s.IncludeSitemap);
                    Check.True(s.FollowLinks);
                    Check.True(s.FollowRedirects);
                    Check.True(s.RestrictToChildUrls);
                    Check.True(s.RestrictToSameSubdomain);
                    Check.True(s.RestrictToSameRootDomain);
                    Check.NotNull(s.AllowedDomains);
                    Check.Empty(s.AllowedDomains);
                    Check.NotNull(s.DeniedDomains);
                    Check.Empty(s.DeniedDomains);
                    Check.Equal(5, s.MaxCrawlDepth);
                    Check.NotNull(s.ExcludeLinkPatterns);
                    Check.Empty(s.ExcludeLinkPatterns);
                    Check.True(s.FollowExternalLinks);
                    Check.Equal(8, s.MaxParallelTasks);
                    Check.Equal(30000, s.PageTimeoutMs);
                    Check.Equal(5000, s.ThrottleMs);
                    Check.True(s.RetryOn429);
                    Check.Equal(3, s.MaxRetries);
                    Check.Equal(1000, s.RetryMinBackoffMs);
                    Check.Equal(30000, s.RetryMaxBackoffMs);
                    Check.True(s.RetryBackoffJitter);
                    Check.Equal(2500, s.RequestDelayMs);
                    Check.False(s.AutoExpandCollapsibles);
                    Check.Equal(0, s.PostLoadDelayMs);
                    Check.Equal(250, s.PostInteractionDelayMs);
                    Check.Equal(2, s.MaxExpansionPasses);
                    Check.NotNull(s.ExpansionSelectors);
                    Check.Empty(s.ExpansionSelectors);
                }),

                Case.Sync(Id, "StartUrl_Valid", "StartUrl accepts a valid absolute URL", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    s.StartUrl = "https://example.com/path";
                    Check.Equal("https://example.com/path", s.StartUrl);
                }),

                Case.Sync(Id, "StartUrl_Null_Throws", "StartUrl rejects null", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    Check.Throws<ArgumentNullException>(() => s.StartUrl = null);
                }),

                Case.Sync(Id, "StartUrl_Empty_Throws", "StartUrl rejects empty string", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    Check.Throws<ArgumentNullException>(() => s.StartUrl = String.Empty);
                }),

                Case.Sync(Id, "StartUrl_Invalid_Throws", "StartUrl rejects an unparseable URL", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    Check.Throws<UriFormatException>(() => s.StartUrl = "not a valid url");
                }),

                Case.Sync(Id, "UserAgent_Null_Throws", "UserAgent rejects null", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    Check.Throws<ArgumentNullException>(() => s.UserAgent = null);
                }),

                Case.Sync(Id, "UserAgent_Empty_Throws", "UserAgent rejects empty string", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    Check.Throws<ArgumentNullException>(() => s.UserAgent = String.Empty);
                }),

                Case.Sync(Id, "UserAgent_Valid", "UserAgent accepts a value", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    s.UserAgent = "MyAgent/1.0";
                    Check.Equal("MyAgent/1.0", s.UserAgent);
                }),

                Case.Sync(Id, "MaxCrawlDepth_Zero_Allowed", "MaxCrawlDepth accepts zero", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    s.MaxCrawlDepth = 0;
                    Check.Equal(0, s.MaxCrawlDepth);
                }),

                Case.Sync(Id, "MaxCrawlDepth_Negative_Throws", "MaxCrawlDepth rejects negatives", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    Check.Throws<ArgumentOutOfRangeException>(() => s.MaxCrawlDepth = -1);
                }),

                Case.Sync(Id, "MaxParallelTasks_Valid", "MaxParallelTasks accepts a positive value", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    s.MaxParallelTasks = 16;
                    Check.Equal(16, s.MaxParallelTasks);
                }),

                Case.Sync(Id, "MaxParallelTasks_Zero_Throws", "MaxParallelTasks rejects zero", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    Check.Throws<ArgumentOutOfRangeException>(() => s.MaxParallelTasks = 0);
                }),

                Case.Sync(Id, "PageTimeoutMs_ClampsLow", "PageTimeoutMs clamps values below 1000 up to 1000", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    s.PageTimeoutMs = 10;
                    Check.Equal(1000, s.PageTimeoutMs);
                }),

                Case.Sync(Id, "PageTimeoutMs_Valid", "PageTimeoutMs accepts values at or above 1000", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    s.PageTimeoutMs = 45000;
                    Check.Equal(45000, s.PageTimeoutMs);
                }),

                Case.Sync(Id, "ThrottleMs_Valid", "ThrottleMs accepts zero and positive values", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    s.ThrottleMs = 0;
                    Check.Equal(0, s.ThrottleMs);
                    s.ThrottleMs = 1234;
                    Check.Equal(1234, s.ThrottleMs);
                }),

                Case.Sync(Id, "ThrottleMs_Negative_Throws", "ThrottleMs rejects negatives", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    Check.Throws<ArgumentOutOfRangeException>(() => s.ThrottleMs = -1);
                }),

                Case.Sync(Id, "MaxRetries_ClampsLow", "MaxRetries clamps values below 1 up to 1", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    s.MaxRetries = 0;
                    Check.Equal(1, s.MaxRetries);
                    s.MaxRetries = -5;
                    Check.Equal(1, s.MaxRetries);
                }),

                Case.Sync(Id, "RetryMinBackoffMs_ClampsLow", "RetryMinBackoffMs clamps values below 100 up to 100", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    s.RetryMinBackoffMs = 10;
                    Check.Equal(100, s.RetryMinBackoffMs);
                }),

                Case.Sync(Id, "RetryMaxBackoffMs_ClampsLow", "RetryMaxBackoffMs clamps values below 1000 up to 1000", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    s.RetryMaxBackoffMs = 10;
                    Check.Equal(1000, s.RetryMaxBackoffMs);
                }),

                Case.Sync(Id, "RequestDelayMs_ClampsNegative", "RequestDelayMs clamps negatives up to zero", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    s.RequestDelayMs = -100;
                    Check.Equal(0, s.RequestDelayMs);
                }),

                Case.Sync(Id, "PostLoadDelayMs_Negative_Throws", "PostLoadDelayMs rejects negatives", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    Check.Throws<ArgumentOutOfRangeException>(() => s.PostLoadDelayMs = -1);
                }),

                Case.Sync(Id, "PostInteractionDelayMs_Negative_Throws", "PostInteractionDelayMs rejects negatives", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    Check.Throws<ArgumentOutOfRangeException>(() => s.PostInteractionDelayMs = -1);
                }),

                Case.Sync(Id, "MaxExpansionPasses_Zero_Throws", "MaxExpansionPasses rejects values below 1", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    Check.Throws<ArgumentOutOfRangeException>(() => s.MaxExpansionPasses = 0);
                }),

                Case.Sync(Id, "AllowedDomains_Null_ReplacedWithEmpty", "AllowedDomains null assignment becomes empty list", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    s.AllowedDomains = null;
                    Check.NotNull(s.AllowedDomains);
                    Check.Empty(s.AllowedDomains);
                }),

                Case.Sync(Id, "DeniedDomains_Null_ReplacedWithEmpty", "DeniedDomains null assignment becomes empty list", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    s.DeniedDomains = null;
                    Check.NotNull(s.DeniedDomains);
                    Check.Empty(s.DeniedDomains);
                }),

                Case.Sync(Id, "ExcludeLinkPatterns_Null_ReplacedWithEmpty", "ExcludeLinkPatterns null assignment becomes empty list", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    s.ExcludeLinkPatterns = null;
                    Check.NotNull(s.ExcludeLinkPatterns);
                    Check.Empty(s.ExcludeLinkPatterns);
                }),

                Case.Sync(Id, "ExcludeLinkPatterns_Accepts", "ExcludeLinkPatterns accepts regex patterns", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    s.ExcludeLinkPatterns = new List<Regex> { new Regex("\\.pdf$") };
                    Check.Count(1, s.ExcludeLinkPatterns);
                }),

                Case.Sync(Id, "ExpansionSelectors_Null_ReplacedWithEmpty", "ExpansionSelectors null assignment becomes empty list", () =>
                {
                    CrawlSettings s = new CrawlSettings();
                    s.ExpansionSelectors = null;
                    Check.NotNull(s.ExpansionSelectors);
                    Check.Empty(s.ExpansionSelectors);
                }),
            };

            return new TestSuiteDescriptor(Id, "CrawlSettings validation", cases);
        }
    }
}
