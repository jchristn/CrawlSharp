namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using CrawlSharp.Web;
    using Touchstone.Core;

    /// <summary>
    /// Unit coverage for <see cref="RedirectPolicy"/>: which statuses are redirects, the method used after each one,
    /// Location resolution, and origin comparison.  No server is involved.
    /// </summary>
    public static class RedirectPolicySuite
    {
        private const string Id = "RedirectPolicy";
        private static readonly Uri Base = new Uri("http://127.0.0.1:8/docs/a");

        /// <summary>
        /// Build the suite.
        /// </summary>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case.Sync(Id, "IsRedirectStatus_Redirects", "301, 302, 303, 307 and 308 are redirects with or without a Location header", () =>
                {
                    foreach (int status in new[] { 301, 302, 303, 307, 308 })
                    {
                        Check.True(RedirectPolicy.IsRedirectStatus(status, true), status + " with Location should be a redirect");
                        Check.True(RedirectPolicy.IsRedirectStatus(status, false), status + " without Location should still be a redirect");
                    }
                }),

                Case.Sync(Id, "IsRedirectStatus_300NeedsLocation", "300 is a redirect only when it carries a Location header", () =>
                {
                    Check.True(RedirectPolicy.IsRedirectStatus(300, true));
                    Check.False(RedirectPolicy.IsRedirectStatus(300, false));
                }),

                Case.Sync(Id, "IsRedirectStatus_NotRedirects", "304, 305, 306, 200, 404 and 500 are never redirects, even with a Location header", () =>
                {
                    foreach (int status in new[] { 304, 305, 306, 200, 204, 404, 500, 309 })
                    {
                        Check.False(RedirectPolicy.IsRedirectStatus(status, true), status + " should not be a redirect");
                    }
                }),

                Case.Sync(Id, "RedirectMethod_Table", "The next method follows RFC 9110 for each redirect status", () =>
                {
                    Check.Equal(HttpMethod.Get, RedirectPolicy.GetRedirectMethod(HttpMethod.Get, 301));
                    Check.Equal(HttpMethod.Get, RedirectPolicy.GetRedirectMethod(HttpMethod.Post, 301));
                    Check.Equal(HttpMethod.Get, RedirectPolicy.GetRedirectMethod(HttpMethod.Post, 302));
                    Check.Equal(HttpMethod.Get, RedirectPolicy.GetRedirectMethod(HttpMethod.Post, 300));
                    Check.Equal(HttpMethod.Head, RedirectPolicy.GetRedirectMethod(HttpMethod.Head, 302));
                    Check.Equal(HttpMethod.Put, RedirectPolicy.GetRedirectMethod(HttpMethod.Put, 302));
                    Check.Equal(HttpMethod.Get, RedirectPolicy.GetRedirectMethod(HttpMethod.Post, 303));
                    Check.Equal(HttpMethod.Get, RedirectPolicy.GetRedirectMethod(HttpMethod.Put, 303));
                    Check.Equal(HttpMethod.Head, RedirectPolicy.GetRedirectMethod(HttpMethod.Head, 303));
                    Check.Equal(HttpMethod.Put, RedirectPolicy.GetRedirectMethod(HttpMethod.Put, 307));
                    Check.Equal(HttpMethod.Post, RedirectPolicy.GetRedirectMethod(HttpMethod.Post, 307));
                    Check.Equal(HttpMethod.Post, RedirectPolicy.GetRedirectMethod(HttpMethod.Post, 308));
                }),

                Case.Sync(Id, "RedirectMethod_NullThrows", "GetRedirectMethod rejects a null method", () =>
                {
                    Check.Throws<ArgumentNullException>(() => RedirectPolicy.GetRedirectMethod(null, 302));
                }),

                Case.Sync(Id, "ResolveLocation_Relative", "Relative and absolute Location values resolve per RFC 3986", () =>
                {
                    Check.Equal("http://127.0.0.1:8/docs/b", RedirectPolicy.ResolveLocation(Base, "b").ToString());
                    Check.Equal("http://127.0.0.1:8/b", RedirectPolicy.ResolveLocation(Base, "../b").ToString());
                    Check.Equal("http://127.0.0.1:8/b", RedirectPolicy.ResolveLocation(Base, "/b").ToString());
                    Check.Equal("http://127.0.0.1:8/docs/a?q=1", RedirectPolicy.ResolveLocation(Base, "?q=1").ToString());
                    Check.Equal("http://127.0.0.1:9/x", RedirectPolicy.ResolveLocation(Base, "//127.0.0.1:9/x").ToString());
                    Check.Equal("https://example.com/y", RedirectPolicy.ResolveLocation(Base, "https://example.com/y").ToString());
                    Check.Equal("http://127.0.0.1:8/docs/b", RedirectPolicy.ResolveLocation(Base, "  b  ").ToString());
                }),

                Case.Sync(Id, "ResolveLocation_StripsFragment", "The fragment is removed from the resolved target", () =>
                {
                    Uri resolved = RedirectPolicy.ResolveLocation(Base, "/final#section-2");
                    Check.Equal("http://127.0.0.1:8/final", resolved.ToString());
                    Check.Empty(resolved.Fragment);
                }),

                Case.Sync(Id, "ResolveLocation_RejectsSchemes", "Non-http schemes, empty values and unparseable values resolve to null", () =>
                {
                    Check.Null(RedirectPolicy.ResolveLocation(Base, "javascript:alert(1)"));
                    Check.Null(RedirectPolicy.ResolveLocation(Base, "mailto:someone@example.com"));
                    Check.Null(RedirectPolicy.ResolveLocation(Base, "ftp://example.com/file"));
                    Check.Null(RedirectPolicy.ResolveLocation(Base, "data:text/plain,hello"));
                    Check.Null(RedirectPolicy.ResolveLocation(Base, "http://"));
                    Check.Null(RedirectPolicy.ResolveLocation(Base, "http://exa mple.com:99999/"));
                    Check.Null(RedirectPolicy.ResolveLocation(Base, ""));
                    Check.Null(RedirectPolicy.ResolveLocation(Base, "   "));
                    Check.Null(RedirectPolicy.ResolveLocation(Base, null));
                }),

                Case.Sync(Id, "ResolveLocation_BadBaseThrows", "ResolveLocation rejects a null or relative base URL", () =>
                {
                    Check.Throws<ArgumentNullException>(() => RedirectPolicy.ResolveLocation(null, "/b"));
                    Check.Throws<ArgumentException>(() => RedirectPolicy.ResolveLocation(new Uri("/relative", UriKind.Relative), "/b"));
                }),

                Case.Sync(Id, "Origin_Comparison", "Scheme, host and port all matter, and default ports are normalized", () =>
                {
                    Check.True(RedirectPolicy.IsSameOrigin(new Uri("http://h/a"), new Uri("http://h:80/b")));
                    Check.True(RedirectPolicy.IsSameOrigin(new Uri("https://h/a"), new Uri("https://h:443/b")));
                    Check.True(RedirectPolicy.IsSameOrigin(new Uri("http://Example.COM/a"), new Uri("http://example.com/b")));
                    Check.False(RedirectPolicy.IsSameOrigin(new Uri("http://h/a"), new Uri("https://h/a")));
                    Check.False(RedirectPolicy.IsSameOrigin(new Uri("http://h:8080/a"), new Uri("http://h:8081/a")));
                    Check.False(RedirectPolicy.IsSameOrigin(new Uri("http://a.example.com/"), new Uri("http://b.example.com/")));
                    Check.Equal("https://example.com:443", RedirectPolicy.GetOrigin(new Uri("https://Example.com/path?q=1")));
                }),

                Case.Sync(Id, "Origin_BadInputThrows", "GetOrigin rejects null and relative URLs", () =>
                {
                    Check.Throws<ArgumentNullException>(() => RedirectPolicy.GetOrigin(null));
                    Check.Throws<ArgumentException>(() => RedirectPolicy.GetOrigin(new Uri("/relative", UriKind.Relative)));
                }),
            };

            return new TestSuiteDescriptor(Id, "Redirect policy rules", cases);
        }
    }
}
