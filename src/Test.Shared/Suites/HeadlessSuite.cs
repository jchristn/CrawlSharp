namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using CrawlSharp.Web;
    using HtmlAgilityPack;
    using Touchstone.Core;

    /// <summary>
    /// Headless-browser (Playwright) coverage for rendered HTML capture and auto-expansion.
    /// <para>
    /// These cases drive a real headless Firefox instance and are therefore slow and dependent on
    /// browser binaries.  They are skipped by default and run only when the environment variable
    /// <c>CRAWLSHARP_RUN_HEADLESS=1</c> is set.
    /// </para>
    /// </summary>
    public static class HeadlessSuite
    {
        private const string Id = "Headless";

        /// <summary>
        /// True when headless cases should execute (opt-in via environment variable).
        /// </summary>
        public static bool Enabled
        {
            get
            {
                string value = Environment.GetEnvironmentVariable("CRAWLSHARP_RUN_HEADLESS");
                return String.Equals(value, "1", StringComparison.Ordinal)
                    || String.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            }
        }

        private const string SkipReason = "Headless browser cases are opt-in; set CRAWLSHARP_RUN_HEADLESS=1 to enable.";

        private static TestCaseDescriptor Headless(string caseId, string displayName, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(
                Id,
                caseId,
                displayName,
                body,
                skip: !Enabled,
                skipReason: Enabled ? null : SkipReason);
        }

        /// <summary>
        /// Build the suite.
        /// </summary>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Headless("RenderedHtml", "Headless capture returns client-rendered HTML", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/rendered", "<!DOCTYPE html><html><body><div id='content'>server</div>" +
                        "<script>window.addEventListener('load',function(){document.getElementById('content').innerHTML='<span id=\"hydrated\">hydrated</span>';});</script>" +
                        "</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/rendered"), headless: true);
                    WebResource resource = await CrawlHelper.CrawlSingleAsync(settings, ct);
                    string html = Encoding.UTF8.GetString(resource.Data);

                    Check.Contains("id=\"hydrated\"", html);
                    Check.Contains(">hydrated<", html);
                }),

                Headless("DetailsExpanded", "Auto-expand opens closed <details> elements", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/details", "<!DOCTYPE html><html><body>" +
                        "<details id='extra'><summary>More</summary><div>Details content</div></details>" +
                        "</body></html>");

                    Settings disabled = CrawlHelper.CreateSettings(server.UrlFor("/details"), headless: true);
                    Settings enabled = CrawlHelper.CreateSettings(server.UrlFor("/details"), headless: true, configure: c => c.AutoExpandCollapsibles = true);

                    WebResource disabledResource = await CrawlHelper.CrawlSingleAsync(disabled, ct);
                    WebResource enabledResource = await CrawlHelper.CrawlSingleAsync(enabled, ct);

                    HtmlNode disabledDetails = Load(disabledResource).DocumentNode.SelectSingleNode("//details[@id='extra']");
                    HtmlNode enabledDetails = Load(enabledResource).DocumentNode.SelectSingleNode("//details[@id='extra']");

                    Check.NotNull(disabledDetails);
                    Check.NotNull(enabledDetails);
                    Check.Null(disabledDetails.Attributes["open"]);
                    Check.NotNull(enabledDetails.Attributes["open"]);
                }),

                Headless("DynamicAccordion", "Auto-expand reveals dynamic accordion content", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/dynamic", "<!DOCTYPE html><html><body>" +
                        "<button id='toggle' aria-expanded='false' aria-controls='panel' onclick='togglePanel(this)'>Toggle</button>" +
                        "<div id='panel'></div>" +
                        "<script>function togglePanel(b){if(!window.loadedPanel){document.getElementById('panel').innerHTML='<div id=\"dynamic-content\">Dynamic content</div>';window.loadedPanel=true;}b.setAttribute('aria-expanded','true');}</script>" +
                        "</body></html>");

                    Settings disabled = CrawlHelper.CreateSettings(server.UrlFor("/dynamic"), headless: true);
                    Settings enabled = CrawlHelper.CreateSettings(server.UrlFor("/dynamic"), headless: true, configure: c => c.AutoExpandCollapsibles = true);

                    WebResource disabledResource = await CrawlHelper.CrawlSingleAsync(disabled, ct);
                    WebResource enabledResource = await CrawlHelper.CrawlSingleAsync(enabled, ct);

                    Check.Null(Load(disabledResource).DocumentNode.SelectSingleNode("//*[@id='dynamic-content']"));
                    HtmlNode enabledNode = Load(enabledResource).DocumentNode.SelectSingleNode("//*[@id='dynamic-content']");
                    Check.NotNull(enabledNode);
                    Check.Equal("Dynamic content", enabledNode.InnerText.Trim());
                }),

                Headless("CustomSelectors", "Custom expansion selectors are clicked", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/custom", "<!DOCTYPE html><html><body>" +
                        "<button class='faq-toggle' onclick='document.getElementById(\"panel\").innerHTML=\"<div id=\\\"custom-content\\\">Custom content</div>\";'>Open</button>" +
                        "<div id='panel'></div>" +
                        "</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/custom"), headless: true, configure: c =>
                    {
                        c.AutoExpandCollapsibles = true;
                        c.ExpansionSelectors = new List<string> { ".faq-toggle" };
                    });

                    WebResource resource = await CrawlHelper.CrawlSingleAsync(settings, ct);
                    HtmlNode node = Load(resource).DocumentNode.SelectSingleNode("//*[@id='custom-content']");

                    Check.NotNull(node);
                    Check.Equal("Custom content", node.InnerText.Trim());
                }),

                Headless("ClickTimeout", "A click that times out during auto-expand is skipped and the page is still returned", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/blocking", "<!DOCTYPE html><html><body>" +
                        "<div id='static-content'>Static content</div>" +
                        "<button class='faq-toggle' onclick='var t=Date.now()+3000;while(Date.now()<t){}'>Open</button>" +
                        "</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/blocking"), headless: true, configure: c =>
                    {
                        c.AutoExpandCollapsibles = true;
                        c.ExpansionSelectors = new List<string> { ".faq-toggle" };
                    });

                    WebResource resource = await CrawlHelper.CrawlSingleAsync(settings, ct);
                    HtmlNode node = Load(resource).DocumentNode.SelectSingleNode("//*[@id='static-content']");

                    Check.Equal(200, resource.Status);
                    Check.NotNull(node);
                    Check.Equal("Static content", node.InnerText.Trim());
                }),

                Headless("RevealedLinks", "Revealed links are discovered only when auto-expand is enabled", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/links", "<!DOCTYPE html><html><body>" +
                        "<button id='toggle' aria-expanded='false' aria-controls='panel' onclick='togglePanel(this)'>Toggle</button>" +
                        "<div id='panel'></div>" +
                        "<script>function togglePanel(b){if(!window.loadedPanel){document.getElementById('panel').innerHTML='<a id=\"dynamic-link\" href=\"/links/dynamic-child\">Dynamic child</a>';window.loadedPanel=true;}b.setAttribute('aria-expanded','true');}</script>" +
                        "</body></html>");
                    server.AddHtml("/links/dynamic-child", "<!DOCTYPE html><html><body><div id='child'>child page</div></body></html>");

                    Settings disabled = CrawlHelper.CreateSettings(server.UrlFor("/links"), headless: true, configure: c =>
                    {
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 1;
                    });
                    Settings enabled = CrawlHelper.CreateSettings(server.UrlFor("/links"), headless: true, configure: c =>
                    {
                        c.AutoExpandCollapsibles = true;
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 1;
                    });

                    List<WebResource> disabledResources = await CrawlHelper.CrawlAllAsync(disabled, ct);
                    List<WebResource> enabledResources = await CrawlHelper.CrawlAllAsync(enabled, ct);

                    Check.DoesNotContain(disabledResources, r => r.Url == server.UrlFor("/links/dynamic-child"));
                    Check.Contains(enabledResources, r => r.Url == server.UrlFor("/links/dynamic-child"));
                }),

                Headless("PdfFallback", "Non-navigable PDF routes fall back to direct download", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    byte[] pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF");
                    server.AddResponse("/file.pdf", "application/pdf", pdfBytes);

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/file.pdf"), headless: true);
                    WebResource resource = await CrawlHelper.CrawlSingleAsync(settings, ct);

                    Check.Equal("application/pdf", resource.ContentType);
                    Check.BytesEqual(pdfBytes, resource.Data);
                    Check.StartsWith("%PDF-1.7", Encoding.ASCII.GetString(resource.Data));
                }),

                Headless("Redirect_Loop_Reported", "A looping page is reported as LoopDetected instead of disappearing", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddRedirect("/loop-a", "/loop-b", 302);
                    server.AddRedirect("/loop-b", "/loop-a", 302);

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/loop-a"), headless: true);
                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(settings, 60, ct);

                    Check.Equal(RedirectOutcomeEnum.LoopDetected, resource.RedirectOutcome);
                    Check.Equal(server.UrlFor("/loop-a"), resource.Url);
                }),

                Headless("Redirect_FinalUrlRecorded", "A redirected page is rendered from its final URL and the chain is recorded", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddRedirect("/start", "/final", 302);
                    server.AddHtml("/final", "<!DOCTYPE html><html><body><div id='content'>server</div>" +
                        "<script>document.getElementById('content').textContent='rendered-final';</script></body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/start"), headless: true);
                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(settings, 60, ct);

                    Check.Equal(200, resource.Status);
                    Check.Equal(server.UrlFor("/start"), resource.Url);
                    Check.Equal(server.UrlFor("/final"), resource.FinalUrl);
                    Check.Equal(RedirectOutcomeEnum.Followed, resource.RedirectOutcome);
                    Check.Count(1, resource.RedirectChain);
                    Check.Contains("rendered-final", Encoding.UTF8.GetString(resource.Data));
                }),

                Headless("Redirect_BrowserSideHopRecorded", "A redirect the browser follows itself (GET differs from HEAD) is recorded", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHandler("/split", context => context.Request.HttpMethod == "HEAD"
                        ? new FixtureResponse { StatusCode = 200, ContentType = "text/html; charset=utf-8" }
                        : new FixtureResponse
                        {
                            StatusCode = 302,
                            ContentType = "text/plain; charset=utf-8",
                            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "Location", "/split-final" } }
                        });
                    server.AddHtml("/split-final", "<!DOCTYPE html><html><body>split-final</body></html>");

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/split"), headless: true);
                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(settings, 60, ct);

                    Check.Equal(200, resource.Status);
                    Check.Equal(server.UrlFor("/split-final"), resource.FinalUrl);
                    Check.Equal(RedirectOutcomeEnum.Followed, resource.RedirectOutcome);
                    Check.Contains("split-final", Encoding.UTF8.GetString(resource.Data));
                }),

                Headless("Auth_Basic_SameOrigin_Sent", "Basic credentials reach a same-origin page and its same-origin subresources", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    string expected = "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("user:pass"));
                    server.AddHandler("/secure", context => context.Request.Headers["Authorization"] == expected
                        ? new FixtureResponse
                        {
                            StatusCode = 200,
                            ContentType = "text/html; charset=utf-8",
                            Body = Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body>granted<script src='/app.js'></script></body></html>")
                        }
                        : new FixtureResponse { StatusCode = 401, ContentType = "text/plain", Body = Encoding.UTF8.GetBytes("unauthorized") });
                    server.AddResponse("/app.js", "application/javascript", Encoding.UTF8.GetBytes("document.body.setAttribute('data-app','1');"));

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/secure"), headless: true);
                    settings.Authentication = new AuthenticationSettings { Type = AuthenticationTypeEnum.Basic, Username = "user", Password = "pass" };

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(settings, 60, ct);

                    Check.Equal(200, resource.Status);
                    Check.Contains("granted", Encoding.UTF8.GetString(resource.Data));
                    Check.Contains("data-app=\"1\"", Encoding.UTF8.GetString(resource.Data), "The page should have been rendered by the browser.");
                    Check.True(server.Requests("/app.js").Count > 0, "The browser should have loaded the same-origin script.");
                    Check.True(server.Requests("/secure").TrueForAll(r => r.Headers["Authorization"] == expected), "Every /secure request should carry credentials.");
                    Check.True(server.Requests("/app.js").TrueForAll(r => r.Headers["Authorization"] == expected), "Same-origin subresources should carry credentials.");
                }),

                Headless("Auth_BrowserSideRedirect_KeepsCredentials", "A same-origin redirect the browser follows itself still carries credentials", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    string expected = "Bearer secret-token";
                    server.AddHandler("/split", context =>
                    {
                        if (context.Request.Headers["Authorization"] != expected)
                            return new FixtureResponse { StatusCode = 401, ContentType = "text/plain", Body = Encoding.UTF8.GetBytes("unauthorized") };
                        if (context.Request.HttpMethod == "HEAD")
                            return new FixtureResponse { StatusCode = 200, ContentType = "text/html; charset=utf-8" };
                        return new FixtureResponse
                        {
                            StatusCode = 302,
                            ContentType = "text/plain; charset=utf-8",
                            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "Location", "/split-final" } }
                        };
                    });
                    server.AddHandler("/split-final", context => context.Request.Headers["Authorization"] == expected
                        ? new FixtureResponse
                        {
                            StatusCode = 200,
                            ContentType = "text/html; charset=utf-8",
                            Body = Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body><div id='c'>server</div><script>document.getElementById('c').textContent='browser-rendered';</script></body></html>")
                        }
                        : new FixtureResponse { StatusCode = 401, ContentType = "text/plain", Body = Encoding.UTF8.GetBytes("unauthorized") });

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/split"), headless: true);
                    settings.Authentication = new AuthenticationSettings { Type = AuthenticationTypeEnum.BearerToken, BearerToken = "secret-token" };

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(settings, 60, ct);

                    Check.Equal(200, resource.Status);
                    Check.Equal(server.UrlFor("/split-final"), resource.FinalUrl);
                    Check.Contains("browser-rendered", Encoding.UTF8.GetString(resource.Data));
                    Check.True(server.Requests().TrueForAll(r => r.Headers["Authorization"] == expected), "Every request should carry the bearer token.");
                }),

                Headless("Auth_CrossOrigin_NotSent", "Credentials never reach another origin, by redirect or by subresource", async ct =>
                {
                    using FixtureServer origin = new FixtureServer();
                    using FixtureServer other = new FixtureServer();
                    origin.AddHtml("/page", "<!DOCTYPE html><html><body>page<img src='" + other.UrlFor("/pixel.gif") + "'></body></html>");
                    origin.AddRedirect("/start", other.UrlFor("/target"), 302);
                    other.AddResponse("/pixel.gif", "image/gif", new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 });
                    other.AddHtml("/target", "<!DOCTYPE html><html><body>other origin</body></html>");

                    AuthenticationSettings auth = new AuthenticationSettings { Type = AuthenticationTypeEnum.ApiKey, ApiKeyHeader = "x-api-key", ApiKey = "abc123" };

                    Settings pageSettings = CrawlHelper.CreateSettings(origin.UrlFor("/page"), headless: true);
                    pageSettings.Authentication = auth;
                    await CrawlHelper.CrawlSingleWithinAsync(pageSettings, 60, ct);

                    Settings redirectSettings = CrawlHelper.CreateSettings(origin.UrlFor("/start"), headless: true);
                    redirectSettings.Authentication = auth;
                    WebResource redirected = await CrawlHelper.CrawlSingleWithinAsync(redirectSettings, 60, ct);

                    Check.Equal(other.UrlFor("/target"), redirected.FinalUrl);
                    Check.True(origin.Requests("/page").TrueForAll(r => r.Headers["x-api-key"] == "abc123"), "The start origin should receive the key.");
                    Check.True(other.Requests("/pixel.gif").Count > 0, "The browser should have loaded the cross-origin image.");
                    Check.True(other.Requests("/target").Count > 0, "The redirect target on the other origin should have been requested.");
                    Check.True(other.Requests().TrueForAll(r => r.Headers["x-api-key"] == null), "The other origin must never receive the key.");
                }),
            };

            return new TestSuiteDescriptor(Id, "Headless browser crawling", cases);
        }

        private static HtmlDocument Load(WebResource resource)
        {
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(Encoding.UTF8.GetString(resource.Data));
            return document;
        }
    }
}
