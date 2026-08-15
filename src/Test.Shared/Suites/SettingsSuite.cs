namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using CrawlSharp.Web;
    using Touchstone.Core;

    /// <summary>
    /// Coverage for <see cref="Settings"/> and <see cref="AuthenticationSettings"/>.
    /// </summary>
    public static class SettingsSuite
    {
        private const string Id = "Settings";

        /// <summary>
        /// Build the suite.
        /// </summary>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case.Sync(Id, "Defaults", "Settings expose non-null Authentication and Crawl by default", () =>
                {
                    Settings s = new Settings();
                    Check.NotNull(s.Authentication);
                    Check.NotNull(s.Crawl);
                }),

                Case.Sync(Id, "Authentication_Null_ReplacedWithDefault", "Authentication null assignment becomes a default instance", () =>
                {
                    Settings s = new Settings();
                    s.Authentication = null;
                    Check.NotNull(s.Authentication);
                    Check.Equal(AuthenticationTypeEnum.None, s.Authentication.Type);
                }),

                Case.Sync(Id, "Authentication_Assign", "Authentication accepts an assigned instance", () =>
                {
                    Settings s = new Settings();
                    AuthenticationSettings auth = new AuthenticationSettings { Type = AuthenticationTypeEnum.Basic };
                    s.Authentication = auth;
                    Check.Equal(AuthenticationTypeEnum.Basic, s.Authentication.Type);
                }),

                Case.Sync(Id, "Crawl_Null_Throws", "Crawl rejects null", () =>
                {
                    Settings s = new Settings();
                    Check.Throws<ArgumentNullException>(() => s.Crawl = null);
                }),

                Case.Sync(Id, "Crawl_Assign", "Crawl accepts an assigned instance", () =>
                {
                    Settings s = new Settings();
                    CrawlSettings crawl = new CrawlSettings { MaxCrawlDepth = 9 };
                    s.Crawl = crawl;
                    Check.Equal(9, s.Crawl.MaxCrawlDepth);
                }),

                Case.Sync(Id, "Auth_Defaults", "AuthenticationSettings defaults are None with null credentials", () =>
                {
                    AuthenticationSettings a = new AuthenticationSettings();
                    Check.Equal(AuthenticationTypeEnum.None, a.Type);
                    Check.Null(a.Username);
                    Check.Null(a.Password);
                    Check.Null(a.ApiKeyHeader);
                    Check.Null(a.ApiKey);
                    Check.Null(a.BearerToken);
                }),

                Case.Sync(Id, "Auth_RoundTrip", "AuthenticationSettings stores assigned credentials", () =>
                {
                    AuthenticationSettings a = new AuthenticationSettings
                    {
                        Type = AuthenticationTypeEnum.BearerToken,
                        Username = "u",
                        Password = "p",
                        ApiKeyHeader = "x-api-key",
                        ApiKey = "abc",
                        BearerToken = "tok"
                    };
                    Check.Equal(AuthenticationTypeEnum.BearerToken, a.Type);
                    Check.Equal("u", a.Username);
                    Check.Equal("p", a.Password);
                    Check.Equal("x-api-key", a.ApiKeyHeader);
                    Check.Equal("abc", a.ApiKey);
                    Check.Equal("tok", a.BearerToken);
                }),

                Case.Sync(Id, "Auth_EnumValues", "AuthenticationTypeEnum defines the expected members", () =>
                {
                    Check.Equal(0, (int)AuthenticationTypeEnum.None);
                    Check.Equal(1, (int)AuthenticationTypeEnum.Basic);
                    Check.Equal(2, (int)AuthenticationTypeEnum.ApiKey);
                    Check.Equal(3, (int)AuthenticationTypeEnum.BearerToken);
                }),
            };

            return new TestSuiteDescriptor(Id, "Settings and authentication", cases);
        }
    }
}
