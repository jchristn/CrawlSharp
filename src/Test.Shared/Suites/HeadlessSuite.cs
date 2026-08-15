namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
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
