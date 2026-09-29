namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Threading.Tasks;
    using CrawlSharp.Web;
    using Touchstone.Core;

    /// <summary>
    /// Integration coverage verifying that <see cref="WebCrawler"/> transmits the credentials
    /// configured on <see cref="AuthenticationSettings"/> for each supported authentication type,
    /// only to origins in the credential scope, and that incomplete settings are rejected up front.
    /// <para>
    /// Each case wires the fixture server to demand a specific credential and return 401 when it is
    /// absent, then asserts both the positive path (credential supplied, request succeeds) and the
    /// negative path (credential omitted, request is rejected).  Cross-origin cases use two fixture
    /// servers on the same host but different ports, which are different origins.
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

                Case.Async(Id, "Basic_SameOriginRedirect_KeepsCredentials", "Basic credentials are sent to a same-origin redirect target", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHandler("/start", context => GuardedRedirect(context, "Authorization", BasicValue, "/target"));
                    server.AddHandler("/target", context => GuardHeader(context, "Authorization", BasicValue));

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(WithAuth(server.UrlFor("/start"), BasicAuth()), TimeoutSeconds, ct);

                    Check.Equal(200, resource.Status);
                    Check.Contains("granted", Encoding.UTF8.GetString(resource.Data));
                    Check.Equal(RedirectOutcomeEnum.Followed, resource.RedirectOutcome);
                }),

                Case.Async(Id, "Bearer_SameOriginRedirect_KeepsCredentials", "A bearer token is sent to a same-origin redirect target", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHandler("/start", context => GuardedRedirect(context, "Authorization", "Bearer secret-token", "/target"));
                    server.AddHandler("/target", context => GuardHeader(context, "Authorization", "Bearer secret-token"));

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(WithAuth(server.UrlFor("/start"), BearerAuth()), TimeoutSeconds, ct);

                    Check.Equal(200, resource.Status);
                    Check.Contains("granted", Encoding.UTF8.GetString(resource.Data));
                }),

                Case.Async(Id, "ApiKey_SameOriginRedirect_KeepsCredentials", "An API key is sent to a same-origin redirect target", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHandler("/start", context => GuardedRedirect(context, "x-api-key", "abc123", "/target"));
                    server.AddHandler("/target", context => GuardHeader(context, "x-api-key", "abc123"));

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(WithAuth(server.UrlFor("/start"), ApiKeyAuth()), TimeoutSeconds, ct);

                    Check.Equal(200, resource.Status);
                    Check.Contains("granted", Encoding.UTF8.GetString(resource.Data));
                }),

                Case.Async(Id, "Basic_CrossOriginRedirect_DropsCredentials", "Basic credentials are not sent to a redirect target on another origin", async ct =>
                {
                    using FixtureServer origin = new FixtureServer();
                    using FixtureServer other = new FixtureServer();
                    origin.AddRedirect("/start", other.UrlFor("/target"), 302);
                    other.AddHtml("/target", "<html><body>other origin</body></html>");

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(WithAuth(origin.UrlFor("/start"), BasicAuth()), TimeoutSeconds, ct);

                    Check.Equal(200, resource.Status);
                    Check.Equal(BasicValue, Check.Single(origin.Requests("/start")).Headers["Authorization"]);
                    Check.Null(Check.Single(other.Requests("/target")).Headers["Authorization"]);
                }),

                Case.Async(Id, "Bearer_CrossOriginRedirect_DropsCredentials", "A bearer token is not sent to a redirect target on another origin", async ct =>
                {
                    using FixtureServer origin = new FixtureServer();
                    using FixtureServer other = new FixtureServer();
                    origin.AddRedirect("/start", other.UrlFor("/target"), 302);
                    other.AddHtml("/target", "<html><body>other origin</body></html>");

                    await CrawlHelper.CrawlSingleWithinAsync(WithAuth(origin.UrlFor("/start"), BearerAuth()), TimeoutSeconds, ct);

                    Check.Equal("Bearer secret-token", Check.Single(origin.Requests("/start")).Headers["Authorization"]);
                    Check.Null(Check.Single(other.Requests("/target")).Headers["Authorization"]);
                }),

                Case.Async(Id, "ApiKey_CrossOriginRedirect_DropsCredentials", "An API key is not sent to a redirect target on another origin (the 1.0.22 leak)", async ct =>
                {
                    using FixtureServer origin = new FixtureServer();
                    using FixtureServer other = new FixtureServer();
                    origin.AddRedirect("/start", other.UrlFor("/target"), 302);
                    other.AddHtml("/target", "<html><body>other origin</body></html>");

                    await CrawlHelper.CrawlSingleWithinAsync(WithAuth(origin.UrlFor("/start"), ApiKeyAuth()), TimeoutSeconds, ct);

                    Check.Equal("abc123", Check.Single(origin.Requests("/start")).Headers["x-api-key"]);
                    Check.Null(Check.Single(other.Requests("/target")).Headers["x-api-key"]);
                }),

                Case.Async(Id, "CrossOrigin_ThenBack_ReattachesOnlyOnOrigin", "In an A to B to A chain, both A requests carry credentials and the B request does not", async ct =>
                {
                    using FixtureServer origin = new FixtureServer();
                    using FixtureServer other = new FixtureServer();
                    origin.AddRedirect("/start", other.UrlFor("/bounce"), 302);
                    other.AddRedirect("/bounce", origin.UrlFor("/end"), 302);
                    origin.AddHandler("/end", context => GuardHeader(context, "x-api-key", "abc123"));

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(WithAuth(origin.UrlFor("/start"), ApiKeyAuth()), TimeoutSeconds, ct);

                    Check.Equal(200, resource.Status);
                    Check.Count(2, resource.RedirectChain);
                    Check.Equal("abc123", Check.Single(origin.Requests("/start")).Headers["x-api-key"]);
                    Check.Null(Check.Single(other.Requests("/bounce")).Headers["x-api-key"]);
                    Check.Equal("abc123", Check.Single(origin.Requests("/end")).Headers["x-api-key"]);
                }),

                Case.Async(Id, "CredentialOrigins_AllowsListedOrigin", "An origin listed in CredentialOrigins receives credentials", async ct =>
                {
                    using FixtureServer origin = new FixtureServer();
                    using FixtureServer other = new FixtureServer();
                    origin.AddRedirect("/start", other.UrlFor("/target"), 302);
                    other.AddHandler("/target", context => GuardHeader(context, "Authorization", BasicValue));

                    AuthenticationSettings auth = BasicAuth();
                    auth.CredentialOrigins = new List<string> { other.BaseUrl };

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(WithAuth(origin.UrlFor("/start"), auth), TimeoutSeconds, ct);

                    Check.Equal(200, resource.Status);
                    Check.Equal(BasicValue, Check.Single(other.Requests("/target")).Headers["Authorization"]);
                }),

                Case.Async(Id, "CredentialOrigins_PathIgnored", "A CredentialOrigins entry with a path still covers its whole origin", async ct =>
                {
                    using FixtureServer origin = new FixtureServer();
                    using FixtureServer other = new FixtureServer();
                    origin.AddRedirect("/start", other.UrlFor("/deep/target"), 302);
                    other.AddHandler("/deep/target", context => GuardHeader(context, "Authorization", BasicValue));

                    AuthenticationSettings auth = BasicAuth();
                    auth.CredentialOrigins = new List<string> { other.UrlFor("/somewhere/else") };

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(WithAuth(origin.UrlFor("/start"), auth), TimeoutSeconds, ct);

                    Check.Equal(200, resource.Status);
                }),

                Case.Async(Id, "SameOriginLink_Credentials", "A discovered link on the start origin receives credentials", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHandler("/", context => GuardedHtml(context, "Authorization", BasicValue, "<a href=\"/child\">c</a>"));
                    server.AddHandler("/child", context => GuardHeader(context, "Authorization", BasicValue));

                    Settings settings = WithAuth(server.UrlFor("/"), BasicAuth(), c =>
                    {
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 1;
                    });

                    List<WebResource> resources = await CrawlHelper.CrawlAllWithinAsync(settings, TimeoutSeconds, ct);

                    Check.Contains(resources, r => r.Url == server.UrlFor("/child") && r.Status == 200);
                }),

                Case.Async(Id, "ExternalLink_NoCredentials", "A discovered link on another origin does not receive credentials", async ct =>
                {
                    using FixtureServer origin = new FixtureServer();
                    using FixtureServer other = new FixtureServer();
                    origin.AddHtml("/", "<html><body><a href=\"" + other.UrlFor("/page") + "\">other</a></body></html>");
                    other.AddHtml("/page", "<html><body>other page</body></html>");

                    Settings settings = WithAuth(origin.UrlFor("/"), BasicAuth(), c =>
                    {
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 1;
                        c.FollowExternalLinks = true;
                        c.RestrictToChildUrls = false;
                    });

                    List<WebResource> resources = await CrawlHelper.CrawlAllWithinAsync(settings, TimeoutSeconds, ct);

                    Check.Contains(resources, r => r.Url == other.UrlFor("/page"));
                    Check.Equal(BasicValue, Check.Single(origin.Requests("/")).Headers["Authorization"]);
                    Check.Null(Check.Single(other.Requests("/page")).Headers["Authorization"]);
                }),

                Case.Async(Id, "Loop_Authenticated_AllRequestsAuthorized", "On an authenticated site with a redirect and a loop, every request is authorized and the crawl ends", async ct =>
                {
                    // Mirrors the AssistantHub CrawlStubServer that exposed the 1.0.22 hang and the lost credentials.
                    using FixtureServer server = new FixtureServer();
                    server.AddHandler("/", context => GuardedHtml(context, "Authorization", BasicValue,
                        "<a href=\"/moved\">moved</a><a href=\"/loop-a\">loop</a><a href=\"/page\">page</a>"));
                    server.AddHandler("/moved", context => GuardedRedirect(context, "Authorization", BasicValue, "/target"));
                    server.AddHandler("/target", context => GuardHeader(context, "Authorization", BasicValue));
                    server.AddHandler("/loop-a", context => GuardedRedirect(context, "Authorization", BasicValue, "/loop-b"));
                    server.AddHandler("/loop-b", context => GuardedRedirect(context, "Authorization", BasicValue, "/loop-a"));
                    server.AddHandler("/page", context => GuardHeader(context, "Authorization", BasicValue));

                    Settings settings = WithAuth(server.UrlFor("/"), BasicAuth(), c =>
                    {
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 1;
                    });

                    List<WebResource> resources = await CrawlHelper.CrawlAllWithinAsync(settings, TimeoutSeconds, ct);

                    List<RecordedRequest> unauthorized = server.Requests().Where(r => r.Headers["Authorization"] != BasicValue).ToList();
                    Check.Empty(unauthorized, "Unauthorized requests: " + String.Join(", ", unauthorized.Select(r => r.Path)));

                    WebResource moved = Check.Single(resources.Where(r => r.Url == server.UrlFor("/moved")));
                    Check.Equal(200, moved.Status);
                    Check.Equal(server.UrlFor("/target"), moved.FinalUrl);

                    WebResource loop = Check.Single(resources.Where(r => r.Url == server.UrlFor("/loop-a")));
                    Check.Equal(RedirectOutcomeEnum.LoopDetected, loop.RedirectOutcome);
                    Check.Contains(resources, r => r.Url == server.UrlFor("/page") && r.Status == 200);
                }),

                Case.Sync(Id, "TypeNone_WithUsername_Throws", "Credentials with Type None make the constructor throw instead of crawling unauthenticated", () =>
                {
                    Settings settings = CrawlHelper.CreateSettings("http://127.0.0.1:1/");
                    settings.Authentication = new AuthenticationSettings { Username = "user", Password = "pass" };

                    ArgumentException ex = Check.Throws<ArgumentException>(() => new WebCrawler(settings));
                    Check.Contains("Username", ex.Message);
                    Check.Contains("Password", ex.Message);
                }),

                Case.Sync(Id, "TypeNone_WithBearer_Throws", "A bearer token with Type None makes the constructor throw", () =>
                {
                    Settings settings = CrawlHelper.CreateSettings("http://127.0.0.1:1/");
                    settings.Authentication = new AuthenticationSettings { BearerToken = "tok" };

                    ArgumentException ex = Check.Throws<ArgumentException>(() => new WebCrawler(settings));
                    Check.Contains("BearerToken", ex.Message);
                }),

                Case.Sync(Id, "TypeNone_BlankFields_Allowed", "Empty and whitespace credential fields with Type None are accepted", () =>
                {
                    Settings settings = CrawlHelper.CreateSettings("http://127.0.0.1:1/");
                    settings.Authentication = new AuthenticationSettings
                    {
                        Username = "",
                        Password = "",
                        BearerToken = "   ",
                        ApiKeyHeader = "",
                        ApiKey = null
                    };

                    using WebCrawler crawler = new WebCrawler(settings);
                }),

                Case.Sync(Id, "ApiKey_MissingHeader_Throws", "Type ApiKey without ApiKeyHeader makes the constructor throw", () =>
                {
                    Settings settings = CrawlHelper.CreateSettings("http://127.0.0.1:1/");
                    settings.Authentication = new AuthenticationSettings { Type = AuthenticationTypeEnum.ApiKey, ApiKey = "abc123" };

                    ArgumentException ex = Check.Throws<ArgumentException>(() => new WebCrawler(settings));
                    Check.Contains("ApiKeyHeader", ex.Message);
                }),

                Case.Sync(Id, "ApiKey_MissingKey_Throws", "Type ApiKey without ApiKey makes the constructor throw", () =>
                {
                    Settings settings = CrawlHelper.CreateSettings("http://127.0.0.1:1/");
                    settings.Authentication = new AuthenticationSettings { Type = AuthenticationTypeEnum.ApiKey, ApiKeyHeader = "x-api-key" };

                    ArgumentException ex = Check.Throws<ArgumentException>(() => new WebCrawler(settings));
                    Check.Contains("ApiKey", ex.Message);
                }),

                Case.Sync(Id, "Basic_MissingUsername_Throws", "Type Basic without Username makes the constructor throw", () =>
                {
                    Settings settings = CrawlHelper.CreateSettings("http://127.0.0.1:1/");
                    settings.Authentication = new AuthenticationSettings { Type = AuthenticationTypeEnum.Basic, Password = "pass" };

                    ArgumentException ex = Check.Throws<ArgumentException>(() => new WebCrawler(settings));
                    Check.Contains("Username", ex.Message);
                }),

                Case.Sync(Id, "Bearer_MissingToken_Throws", "Type BearerToken without a token makes the constructor throw", () =>
                {
                    Settings settings = CrawlHelper.CreateSettings("http://127.0.0.1:1/");
                    settings.Authentication = new AuthenticationSettings { Type = AuthenticationTypeEnum.BearerToken };

                    ArgumentException ex = Check.Throws<ArgumentException>(() => new WebCrawler(settings));
                    Check.Contains("BearerToken", ex.Message);
                }),

                Case.Sync(Id, "CredentialOrigins_Invalid_Throws", "A CredentialOrigins entry that is not an absolute http or https URL makes the constructor throw", () =>
                {
                    foreach (string bad in new[] { "ftp://example.com", "example.com", "/relative", "", "not a url" })
                    {
                        Settings settings = CrawlHelper.CreateSettings("http://127.0.0.1:1/");
                        AuthenticationSettings auth = BasicAuth();
                        auth.CredentialOrigins = new List<string> { bad };
                        settings.Authentication = auth;

                        ArgumentException ex = Check.Throws<ArgumentException>(() => new WebCrawler(settings), "Expected '" + bad + "' to be rejected.");
                        Check.Contains("CredentialOrigins", ex.Message);
                    }
                }),
            };

            return new TestSuiteDescriptor(Id, "Authenticated crawling", cases);
        }

        private const int TimeoutSeconds = 15;

        private static readonly string BasicValue = "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("user:pass"));

        private static AuthenticationSettings BasicAuth()
        {
            return new AuthenticationSettings { Type = AuthenticationTypeEnum.Basic, Username = "user", Password = "pass" };
        }

        private static AuthenticationSettings BearerAuth()
        {
            return new AuthenticationSettings { Type = AuthenticationTypeEnum.BearerToken, BearerToken = "secret-token" };
        }

        private static AuthenticationSettings ApiKeyAuth()
        {
            return new AuthenticationSettings { Type = AuthenticationTypeEnum.ApiKey, ApiKeyHeader = "x-api-key", ApiKey = "abc123" };
        }

        private static Settings WithAuth(string startUrl, AuthenticationSettings auth, Action<CrawlSettings> configure = null)
        {
            Settings settings = CrawlHelper.CreateSettings(startUrl, configure: configure);
            settings.Authentication = auth;
            return settings;
        }

        /// <summary>
        /// Redirect to the given location when the named header matches, otherwise return a 401.
        /// </summary>
        private static FixtureResponse GuardedRedirect(HttpListenerContext context, string headerName, string expectedValue, string location)
        {
            if (!String.Equals(context.Request.Headers[headerName], expectedValue, StringComparison.Ordinal)) return Unauthorized();

            return new FixtureResponse
            {
                StatusCode = 302,
                ContentType = "text/plain; charset=utf-8",
                Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "Location", location } }
            };
        }

        /// <summary>
        /// Return an HTML page with the given body when the named header matches, otherwise a 401.
        /// </summary>
        private static FixtureResponse GuardedHtml(HttpListenerContext context, string headerName, string expectedValue, string body)
        {
            if (!String.Equals(context.Request.Headers[headerName], expectedValue, StringComparison.Ordinal)) return Unauthorized();

            return new FixtureResponse
            {
                StatusCode = 200,
                ContentType = "text/html; charset=utf-8",
                Body = Encoding.UTF8.GetBytes("<html><body>" + body + "</body></html>")
            };
        }

        private static FixtureResponse Unauthorized()
        {
            return new FixtureResponse
            {
                StatusCode = 401,
                ContentType = "text/plain; charset=utf-8",
                Body = Encoding.UTF8.GetBytes("unauthorized")
            };
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
