namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Text;
    using CrawlSharp.Web;
    using Touchstone.Core;

    /// <summary>
    /// Coverage for <see cref="WebResource"/> computed properties and validation.
    /// </summary>
    public static class WebResourceSuite
    {
        private const string Id = "WebResource";

        /// <summary>
        /// Build the suite.
        /// </summary>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case.Sync(Id, "Defaults", "A new WebResource exposes documented defaults", () =>
                {
                    WebResource r = new WebResource();
                    Check.Null(r.Url);
                    Check.Null(r.ParentUrl);
                    Check.Empty(r.Filename);
                    Check.Equal(0, r.Depth);
                    Check.Equal(0, r.Status);
                    Check.Equal(0L, r.ContentLength);
                    Check.Null(r.ContentType);
                    Check.Null(r.ETag);
                    Check.Null(r.MD5Hash);
                    Check.Null(r.SHA1Hash);
                    Check.Null(r.SHA256Hash);
                    Check.Null(r.LastModified);
                    Check.NotNull(r.Headers);
                    Check.Null(r.Data);
                }),

                Case.Sync(Id, "Filename_FromPath", "Filename is extracted from the URL path", () =>
                {
                    WebResource r = new WebResource { Url = "http://example.com/a/b/report.pdf" };
                    Check.Equal("report.pdf", r.Filename);
                }),

                Case.Sync(Id, "Filename_IgnoresQuery", "Filename ignores the query string", () =>
                {
                    WebResource r = new WebResource { Url = "http://example.com/dir/page.html?x=1&y=2" };
                    Check.Equal("page.html", r.Filename);
                }),

                Case.Sync(Id, "Filename_TrailingSlash_Empty", "Filename is empty when the path ends in a slash", () =>
                {
                    WebResource r = new WebResource { Url = "http://example.com/dir/" };
                    Check.Empty(r.Filename);
                }),

                Case.Sync(Id, "Filename_Unescaped", "Filename unescapes percent-encoded characters", () =>
                {
                    WebResource r = new WebResource { Url = "http://example.com/my%20file.txt" };
                    Check.Equal("my file.txt", r.Filename);
                }),

                Case.Sync(Id, "Filename_NullUrl_Empty", "Filename is empty when the URL is null", () =>
                {
                    WebResource r = new WebResource();
                    Check.Empty(r.Filename);
                }),

                Case.Sync(Id, "Filename_InvalidUrl_Empty", "Filename is empty when the URL is not a valid URI", () =>
                {
                    WebResource r = new WebResource { Url = "not a uri" };
                    Check.Empty(r.Filename);
                }),

                Case.Sync(Id, "Depth_Valid", "Depth accepts non-negative values", () =>
                {
                    WebResource r = new WebResource { Depth = 4 };
                    Check.Equal(4, r.Depth);
                }),

                Case.Sync(Id, "Depth_Negative_Throws", "Depth rejects negatives", () =>
                {
                    WebResource r = new WebResource();
                    Check.Throws<ArgumentOutOfRangeException>(() => r.Depth = -1);
                }),

                Case.Sync(Id, "Status_Valid", "Status accepts a valid HTTP status code", () =>
                {
                    WebResource r = new WebResource { Status = 200 };
                    Check.Equal(200, r.Status);
                    r.Status = 404;
                    Check.Equal(404, r.Status);
                    r.Status = 599;
                    Check.Equal(599, r.Status);
                }),

                Case.Sync(Id, "Status_Negative_ClampsTo400", "Status below zero is coerced to 400", () =>
                {
                    WebResource r = new WebResource { Status = -1 };
                    Check.Equal(400, r.Status);
                }),

                Case.Sync(Id, "Status_TooHigh_ClampsTo400", "Status above 599 is coerced to 400", () =>
                {
                    WebResource r = new WebResource { Status = 600 };
                    Check.Equal(400, r.Status);
                }),

                Case.Sync(Id, "ContentLength_TracksData", "ContentLength reflects the length of Data", () =>
                {
                    WebResource r = new WebResource();
                    Check.Equal(0L, r.ContentLength);
                    r.Data = Encoding.UTF8.GetBytes("hello");
                    Check.Equal(5L, r.ContentLength);
                }),

                Case.Sync(Id, "Headers_Null_ReplacedWithEmpty", "Headers null assignment becomes an empty collection", () =>
                {
                    WebResource r = new WebResource();
                    r.Headers = null;
                    Check.NotNull(r.Headers);
                    Check.Equal(0, r.Headers.Count);
                }),

                Case.Sync(Id, "LastModified_Rfc1123", "LastModified parses an RFC 1123 date header", () =>
                {
                    WebResource r = new WebResource();
                    NameValueCollection headers = new NameValueCollection { { "Last-Modified", "Sun, 06 Nov 1994 08:49:37 GMT" } };
                    r.Headers = headers;
                    Check.NotNull(r.LastModified);
                    DateTime dt = r.LastModified.Value;
                    Check.Equal(1994, dt.Year);
                    Check.Equal(11, dt.Month);
                    Check.Equal(6, dt.Day);
                    Check.Equal(8, dt.Hour);
                    Check.Equal(49, dt.Minute);
                    Check.Equal(37, dt.Second);
                }),

                Case.Sync(Id, "LastModified_Asctime", "LastModified parses an asctime date header", () =>
                {
                    WebResource r = new WebResource();
                    r.Headers = new NameValueCollection { { "Last-Modified", "Sun Nov 6 08:49:37 1994" } };
                    Check.NotNull(r.LastModified);
                    Check.Equal(1994, r.LastModified.Value.Year);
                }),

                Case.Sync(Id, "LastModified_Missing_Null", "LastModified is null when the header is absent", () =>
                {
                    WebResource r = new WebResource();
                    Check.Null(r.LastModified);
                }),

                Case.Sync(Id, "LastModified_Garbage_Null", "LastModified is null when the header is unparseable", () =>
                {
                    WebResource r = new WebResource();
                    r.Headers = new NameValueCollection { { "Last-Modified", "totally not a date" } };
                    Check.Null(r.LastModified);
                }),
            };

            return new TestSuiteDescriptor(Id, "WebResource properties", cases);
        }
    }
}
