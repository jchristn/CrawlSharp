namespace CrawlSharp.Web
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Xml.Linq;

    /// <summary>
    /// Sitemap parser.
    /// </summary>
    public static class SitemapParser
    {
        #region Public-Members

        #endregion

        #region Private-Members
        
        private static readonly XNamespace NS_SITEMAP = "http://www.sitemaps.org/schemas/sitemap/0.9";
        private static readonly XNamespace NS_IMAGE = "http://www.google.com/schemas/sitemap-image/1.1";
        private static readonly XNamespace NS_VIDEO = "http://www.google.com/schemas/sitemap-video/1.1";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Test if text is parseable as an XML document.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>True if parseable.</returns>
        public static bool IsParseable(string text)
        {
            if (String.IsNullOrEmpty(text)) return false;

            try
            {
                XDocument doc = XDocument.Parse(text);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Test if text is a sitemap index.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>True if the text is a sitemap index.</returns>
        public static bool IsSitemapIndex(string text)
        {
            if (String.IsNullOrEmpty(text)) return false;
            XDocument doc = XDocument.Parse(text);
            return doc.Root.Name == NS_SITEMAP + "sitemapindex";
        }

        /// <summary>
        /// Parse a sitemap index.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Sitemap index.</returns>
        public static SitemapIndex ParseSitemapIndex(string text)
        {
            if (String.IsNullOrEmpty(text)) return null;

            XDocument doc = XDocument.Parse(text);
            SitemapIndex index = new SitemapIndex();

            foreach (var sitemapElement in doc.Descendants(NS_SITEMAP + "sitemap"))
            {
                string location = sitemapElement.Element(NS_SITEMAP + "loc")?.Value;
                if (!string.IsNullOrEmpty(location))
                {
                    index.Locations.Add(location);
                }
            }

            return index;
        }

        /// <summary>
        /// Parse a sitemap.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>List of sitemap URLs.</returns>
        public static List<SitemapUrl> ParseSitemap(string text)
        {
            if (String.IsNullOrEmpty(text)) return new List<SitemapUrl>();

            XDocument doc = XDocument.Parse(text);
            List<SitemapUrl> urls = new List<SitemapUrl>();

            foreach (var urlElement in doc.Descendants(NS_SITEMAP + "url"))
            {
                SitemapUrl url = new SitemapUrl
                {
                    Location = urlElement.Element(NS_SITEMAP + "loc")?.Value,
                    LastModified = DateTime.TryParse(urlElement.Element(NS_SITEMAP + "lastmod")?.Value, out DateTime dt) ? dt : null,
                    ChangeFrequency = urlElement.Element(NS_SITEMAP + "changefreq")?.Value,
                    Priority = double.TryParse(urlElement.Element(NS_SITEMAP + "priority")?.Value, out double p) ? p : null
                };

                // Parse image extensions
                foreach (var imageElement in urlElement.Elements(NS_IMAGE + "image"))
                {
                    SitemapImage image = new SitemapImage
                    {
                        Location = imageElement.Element(NS_IMAGE + "loc")?.Value,
                        Caption = imageElement.Element(NS_IMAGE + "caption")?.Value,
                        GeoLocation = imageElement.Element(NS_IMAGE + "geo_location")?.Value,
                        Title = imageElement.Element(NS_IMAGE + "title")?.Value,
                        License = imageElement.Element(NS_IMAGE + "license")?.Value
                    };
                    url.Images.Add(image);
                }

                // Parse video extensions
                foreach (var videoElement in urlElement.Elements(NS_VIDEO + "video"))
                {
                    SitemapVideo video = new SitemapVideo
                    {
                        // Required elements
                        ThumbnailLocation = videoElement.Element(NS_VIDEO + "thumbnail_loc")?.Value,
                        Title = videoElement.Element(NS_VIDEO + "title")?.Value,
                        Description = videoElement.Element(NS_VIDEO + "description")?.Value,

                        // Optional elements
                        ContentLocation = videoElement.Element(NS_VIDEO + "content_loc")?.Value,
                        PlayerLocation = videoElement.Element(NS_VIDEO + "player_loc")?.Value,
                        Duration = int.TryParse(videoElement.Element(NS_VIDEO + "duration")?.Value, out int d) ? d : null,
                        ExpirationDate = DateTime.TryParse(videoElement.Element(NS_VIDEO + "expiration_date")?.Value, out DateTime ed) ? ed : null,
                        Rating = double.TryParse(videoElement.Element(NS_VIDEO + "rating")?.Value, out double r) ? r : null,
                        ViewCount = int.TryParse(videoElement.Element(NS_VIDEO + "view_count")?.Value, out int vc) ? vc : null,
                        PublicationDate = DateTime.TryParse(videoElement.Element(NS_VIDEO + "publication_date")?.Value, out DateTime pd) ? pd : null,
                        Tags = videoElement.Elements(NS_VIDEO + "tag")?.Select(t => t.Value).ToArray(),
                        Category = videoElement.Element(NS_VIDEO + "category")?.Value
                    };

                    url.Videos.Add(video);
                }

                urls.Add(url);
            }

            return urls;
        }

        #endregion

        #region Private-Methods

        #endregion
    }
}
