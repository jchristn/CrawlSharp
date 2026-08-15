namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using CrawlSharp.Web;
    using Touchstone.Core;

    /// <summary>
    /// Coverage for <see cref="SitemapParser"/>.
    /// </summary>
    public static class SitemapParserSuite
    {
        private const string Id = "SitemapParser";

        private const string UrlSet =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\" " +
            "xmlns:image=\"http://www.google.com/schemas/sitemap-image/1.1\" " +
            "xmlns:video=\"http://www.google.com/schemas/sitemap-video/1.1\">" +
            "<url>" +
            "<loc>http://example.com/</loc>" +
            "<lastmod>2024-01-15</lastmod>" +
            "<changefreq>daily</changefreq>" +
            "<priority>0.8</priority>" +
            "<image:image><image:loc>http://example.com/img.png</image:loc><image:caption>An image</image:caption></image:image>" +
            "</url>" +
            "<url>" +
            "<loc>http://example.com/video</loc>" +
            "<video:video>" +
            "<video:thumbnail_loc>http://example.com/thumb.jpg</video:thumbnail_loc>" +
            "<video:title>A video</video:title>" +
            "<video:description>Desc</video:description>" +
            "<video:duration>120</video:duration>" +
            "</video:video>" +
            "</url>" +
            "</urlset>";

        private const string SitemapIndexXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">" +
            "<sitemap><loc>http://example.com/sitemap1.xml</loc></sitemap>" +
            "<sitemap><loc>http://example.com/sitemap2.xml</loc></sitemap>" +
            "</sitemapindex>";

        /// <summary>
        /// Build the suite.
        /// </summary>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case.Sync(Id, "IsParseable_ValidXml", "IsParseable is true for valid XML", () =>
                {
                    Check.True(SitemapParser.IsParseable(UrlSet));
                }),

                Case.Sync(Id, "IsParseable_Null_False", "IsParseable is false for null", () =>
                {
                    Check.False(SitemapParser.IsParseable(null));
                }),

                Case.Sync(Id, "IsParseable_Empty_False", "IsParseable is false for empty", () =>
                {
                    Check.False(SitemapParser.IsParseable(""));
                }),

                Case.Sync(Id, "IsParseable_Garbage_False", "IsParseable is false for malformed XML", () =>
                {
                    Check.False(SitemapParser.IsParseable("<broken><xml>"));
                }),

                Case.Sync(Id, "IsSitemapIndex_True", "IsSitemapIndex is true for a sitemap index", () =>
                {
                    Check.True(SitemapParser.IsSitemapIndex(SitemapIndexXml));
                }),

                Case.Sync(Id, "IsSitemapIndex_UrlSet_False", "IsSitemapIndex is false for a urlset", () =>
                {
                    Check.False(SitemapParser.IsSitemapIndex(UrlSet));
                }),

                Case.Sync(Id, "IsSitemapIndex_Null_False", "IsSitemapIndex is false for null", () =>
                {
                    Check.False(SitemapParser.IsSitemapIndex(null));
                }),

                Case.Sync(Id, "ParseSitemap_ReturnsUrls", "ParseSitemap returns each url entry", () =>
                {
                    List<SitemapUrl> urls = SitemapParser.ParseSitemap(UrlSet);
                    Check.Count(2, urls);
                    Check.Equal("http://example.com/", urls[0].Location);
                    Check.Equal("daily", urls[0].ChangeFrequency);
                    Check.NotNull(urls[0].LastModified);
                    Check.Equal(2024, urls[0].LastModified.Value.Year);
                    Check.Equal(1, urls[0].LastModified.Value.Month);
                    Check.Equal(15, urls[0].LastModified.Value.Day);
                    Check.NotNull(urls[0].Priority);
                    Check.True(urls[0].Priority.Value > 0.5);
                }),

                Case.Sync(Id, "ParseSitemap_ParsesImages", "ParseSitemap captures image extensions", () =>
                {
                    List<SitemapUrl> urls = SitemapParser.ParseSitemap(UrlSet);
                    Check.Count(1, urls[0].Images);
                    Check.Equal("http://example.com/img.png", urls[0].Images[0].Location);
                    Check.Equal("An image", urls[0].Images[0].Caption);
                }),

                Case.Sync(Id, "ParseSitemap_ParsesVideos", "ParseSitemap captures video extensions", () =>
                {
                    List<SitemapUrl> urls = SitemapParser.ParseSitemap(UrlSet);
                    Check.Count(1, urls[1].Videos);
                    SitemapVideo video = urls[1].Videos[0];
                    Check.Equal("http://example.com/thumb.jpg", video.ThumbnailLocation);
                    Check.Equal("A video", video.Title);
                    Check.Equal("Desc", video.Description);
                    Check.Equal(120, video.Duration.Value);
                }),

                Case.Sync(Id, "ParseSitemap_Null_Empty", "ParseSitemap returns an empty list for null", () =>
                {
                    Check.Empty(SitemapParser.ParseSitemap(null));
                }),

                Case.Sync(Id, "ParseSitemap_Empty_Empty", "ParseSitemap returns an empty list for empty input", () =>
                {
                    Check.Empty(SitemapParser.ParseSitemap(""));
                }),

                Case.Sync(Id, "ParseSitemap_IndexInput_Empty", "ParseSitemap returns no urls for a sitemap index", () =>
                {
                    Check.Empty(SitemapParser.ParseSitemap(SitemapIndexXml));
                }),

                Case.Sync(Id, "ParseSitemapIndex_ReturnsLocations", "ParseSitemapIndex returns each sitemap location", () =>
                {
                    SitemapIndex index = SitemapParser.ParseSitemapIndex(SitemapIndexXml);
                    Check.NotNull(index);
                    Check.Count(2, index.Locations);
                    Check.Contains(index.Locations, l => l == "http://example.com/sitemap1.xml");
                    Check.Contains(index.Locations, l => l == "http://example.com/sitemap2.xml");
                }),

                Case.Sync(Id, "ParseSitemapIndex_Null_Null", "ParseSitemapIndex returns null for null input", () =>
                {
                    Check.Null(SitemapParser.ParseSitemapIndex(null));
                }),

                Case.Sync(Id, "ParseSitemapIndex_Empty_Null", "ParseSitemapIndex returns null for empty input", () =>
                {
                    Check.Null(SitemapParser.ParseSitemapIndex(""));
                }),
            };

            return new TestSuiteDescriptor(Id, "Sitemap parsing", cases);
        }
    }
}
