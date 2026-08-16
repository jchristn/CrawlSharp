namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Text;
    using System.Threading.Tasks;
    using CrawlSharp.Web;
    using Touchstone.Core;

    /// <summary>
    /// Integration coverage verifying that <see cref="WebCrawler"/> transmits the credentials
    /// configured on <see cref="AuthenticationSettings"/> for each supported authentication type.
    /// <para>
    /// Each case wires the fixture server to demand a specific credential and return 401 when it is
    /// absent, then asserts both the positive path (credential supplied, request succeeds) and the
    /// negative path (credential omitted, request is rejected).
    /// </para>
    /// </summary>
    public static class AuthenticationCrawlSuite
    {
        private const string Id = "AuthenticationCrawl";

        /// <summary>
        /// Build the suite.
        /// </summary>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case.Async(Id, "Basic_Authorized", "Basic credentials produce an Authorization header and a 200", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    string expected = "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("user:pass"));
                    server.AddHandler("/secure", context => GuardHeader(context, "Authorization", expected));

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/secure"));
                    settings.Authentication = new AuthenticationSettings
                    {
                        Type = AuthenticationTypeEnum.Basic,
                        Username = "user",
                        Password = "pass"
                    };

                    WebResource resource = await CrawlHelper.CrawlSingleAsync(settings, ct);

                    Check.Equal(200, resource.Status);
                    Check.Contains("granted", Encoding.UTF8.GetString(resource.Data));
                }),

                Case.Async(Id, "Basic_MissingCredentials_401", "Omitting Basic credentials yields a 401", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    string expected = "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("user:pass"));
                    server.AddHandler("/secure", context => GuardHeader(context, "Authorization", expected));

                    // No Authentication configured, so no Authorization header is sent.
                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/secure"));

                    WebResource resource = await CrawlHelper.CrawlSingleAsync(settings, ct);

                    Check.Equal(401, resource.Status);
                }),

                Case.Async(Id, "Basic_WrongPassword_401", "Incorrect Basic credentials yield a 401", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    string expected = "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("user:pass"));
                    server.AddHandler("/secure", context => GuardHeader(context, "Authorization", expected));

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/secure"));
                    settings.Authentication = new AuthenticationSettings
                    {
                        Type = AuthenticationTypeEnum.Basic,
                        Username = "user",
                        Password = "wrong"
                    };

                    WebResource resource = await CrawlHelper.CrawlSingleAsync(settings, ct);

                    Check.Equal(401, resource.Status);
                }),

                Case.Async(Id, "BearerToken_Authorized", "A bearer token produces an Authorization header and a 200", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHandler("/secure", context => GuardHeader(context, "Authorization", "Bearer secret-token"));

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/secure"));
                    settings.Authentication = new AuthenticationSettings
                    {
                        Type = AuthenticationTypeEnum.BearerToken,
                        BearerToken = "secret-token"
                    };

                    WebResource resource = await CrawlHelper.CrawlSingleAsync(settings, ct);

                    Check.Equal(200, resource.Status);
                }),

                Case.Async(Id, "BearerToken_Missing_401", "Omitting the bearer token yields a 401", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHandler("/secure", context => GuardHeader(context, "Authorization", "Bearer secret-token"));

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/secure"));

                    WebResource resource = await CrawlHelper.CrawlSingleAsync(settings, ct);

                    Check.Equal(401, resource.Status);
                }),

                Case.Async(Id, "ApiKey_Authorized", "An API key is sent on the configured header and yields a 200", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHandler("/secure", context => GuardHeader(context, "x-api-key", "abc123"));

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/secure"));
                    settings.Authentication = new AuthenticationSettings
                    {
                        Type = AuthenticationTypeEnum.ApiKey,
                        ApiKeyHeader = "x-api-key",
                        ApiKey = "abc123"
                    };

                    WebResource resource = await CrawlHelper.CrawlSingleAsync(settings, ct);

                    Check.Equal(200, resource.Status);
                }),

                Case.Async(Id, "ApiKey_WrongValue_401", "An incorrect API key value yields a 401", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHandler("/secure", context => GuardHeader(context, "x-api-key", "abc123"));

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/secure"));
                    settings.Authentication = new AuthenticationSettings
                    {
                        Type = AuthenticationTypeEnum.ApiKey,
                        ApiKeyHeader = "x-api-key",
                        ApiKey = "not-the-key"
                    };

                    WebResource resource = await CrawlHelper.CrawlSingleAsync(settings, ct);

                    Check.Equal(401, resource.Status);
                }),
            };

            return new TestSuiteDescriptor(Id, "Authenticated crawling", cases);
        }

        /// <summary>
        /// Return a 200 body when the named request header exactly matches the expected value,
        /// otherwise a 401.  Used to prove that the crawler transmitted the configured credential.
        /// </summary>
        private static FixtureResponse GuardHeader(HttpListenerContext context, string headerName, string expectedValue)
        {
            string actual = context.Request.Headers[headerName];

            if (String.Equals(actual, expectedValue, StringComparison.Ordinal))
            {
                return new FixtureResponse
                {
                    StatusCode = 200,
                    ContentType = "text/html; charset=utf-8",
                    Body = Encoding.UTF8.GetBytes("<html><body>granted</body></html>")
                };
            }

            return new FixtureResponse
            {
                StatusCode = 401,
                ContentType = "text/plain; charset=utf-8",
                Body = Encoding.UTF8.GetBytes("unauthorized")
            };
        }
    }
}
