namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using CrawlSharp.Web;
    using Touchstone.Core;

    /// <summary>
    /// Coverage for the smaller model types: <see cref="QueuedLink"/>, <see cref="ContentTypeInfo"/>,
    /// and the sitemap model classes.
    /// </summary>
    public static class ModelsSuite
    {
        private const string Id = "Models";

        /// <summary>
        /// Build the suite.
        /// </summary>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case.Sync(Id, "QueuedLink_Defaults", "QueuedLink defaults are null URLs at depth zero", () =>
                {
                    QueuedLink q = new QueuedLink();
                    Check.Null(q.Url);
                    Check.Null(q.ParentUrl);
                    Check.Equal(0, q.Depth);
                }),

                Case.Sync(Id, "QueuedLink_RoundTrip", "QueuedLink stores assigned values", () =>
                {
                    QueuedLink q = new QueuedLink { Url = "http://x/a", ParentUrl = "http://x", Depth = 3 };
                    Check.Equal("http://x/a", q.Url);
                    Check.Equal("http://x", q.ParentUrl);
                    Check.Equal(3, q.Depth);
                }),

                Case.Sync(Id, "QueuedLink_Depth_Negative_Throws", "QueuedLink depth rejects negatives", () =>
                {
                    QueuedLink q = new QueuedLink();
                    Check.Throws<ArgumentOutOfRangeException>(() => q.Depth = -1);
                }),

                Case.Sync(Id, "ContentTypeInfo_Defaults", "ContentTypeInfo default ctor is non-navigable and unchecked", () =>
                {
                    ContentTypeInfo c = new ContentTypeInfo();
                    Check.False(c.IsNavigable);
                    Check.Null(c.MediaType);
                    Check.Null(c.ContentLength);
                    Check.False(c.CheckSucceeded);
                }),

                Case.Sync(Id, "ContentTypeInfo_ParamCtor", "ContentTypeInfo parameterized ctor stores values", () =>
                {
                    ContentTypeInfo c = new ContentTypeInfo(true, "text/html", 1234, true);
                    Check.True(c.IsNavigable);
                    Check.Equal("text/html", c.MediaType);
                    Check.Equal(1234L, c.ContentLength.Value);
                    Check.True(c.CheckSucceeded);
                }),

                Case.Sync(Id, "ContentTypeInfo_ToString", "ContentTypeInfo.ToString includes key fields", () =>
                {
                    ContentTypeInfo c = new ContentTypeInfo(true, "application/pdf", 10, true);
                    string s = c.ToString();
                    Check.Contains("application/pdf", s);
                    Check.Contains("IsNavigable=True", s);
                }),

                Case.Sync(Id, "SitemapUrl_Defaults", "SitemapUrl exposes empty image and video collections", () =>
                {
                    SitemapUrl u = new SitemapUrl();
                    Check.NotNull(u.Images);
                    Check.Empty(u.Images);
                    Check.NotNull(u.Videos);
                    Check.Empty(u.Videos);
                    Check.Null(u.Location);
                    Check.Null(u.LastModified);
                    Check.Null(u.Priority);
                }),

                Case.Sync(Id, "SitemapIndex_Defaults", "SitemapIndex exposes an empty locations list", () =>
                {
                    SitemapIndex idx = new SitemapIndex();
                    Check.NotNull(idx.Locations);
                    Check.Empty(idx.Locations);
                }),

                Case.Sync(Id, "SitemapIndex_Null_ReplacedWithEmpty", "SitemapIndex locations null assignment becomes empty", () =>
                {
                    SitemapIndex idx = new SitemapIndex();
                    idx.Locations = null;
                    Check.NotNull(idx.Locations);
                    Check.Empty(idx.Locations);
                }),

                Case.Sync(Id, "SitemapImage_RoundTrip", "SitemapImage stores assigned values", () =>
                {
                    SitemapImage img = new SitemapImage
                    {
                        Location = "http://x/i.png",
                        Caption = "cap",
                        GeoLocation = "geo",
                        Title = "title",
                        License = "lic"
                    };
                    Check.Equal("http://x/i.png", img.Location);
                    Check.Equal("cap", img.Caption);
                    Check.Equal("geo", img.GeoLocation);
                    Check.Equal("title", img.Title);
                    Check.Equal("lic", img.License);
                }),

                Case.Sync(Id, "SitemapVideo_RoundTrip", "SitemapVideo stores assigned values", () =>
                {
                    SitemapVideo v = new SitemapVideo
                    {
                        ThumbnailLocation = "http://x/t.jpg",
                        Title = "t",
                        Description = "d",
                        Duration = 60,
                        ViewCount = 100
                    };
                    Check.Equal("http://x/t.jpg", v.ThumbnailLocation);
                    Check.Equal("t", v.Title);
                    Check.Equal("d", v.Description);
                    Check.Equal(60, v.Duration.Value);
                    Check.Equal(100, v.ViewCount.Value);
                }),
            };

            return new TestSuiteDescriptor(Id, "Model types", cases);
        }
    }
}
