namespace CrawlSharp.Web
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Sitemap video.
    /// </summary>
    public class SitemapVideo
    {
        #region Public-Members

        // Required properties

        /// <summary>
        /// Thumbnail location.
        /// </summary>
        public string ThumbnailLocation { get; set; }

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Description.
        /// </summary>
        public string Description { get; set; }

        // Optional properties

        /// <summary>
        /// Content location.
        /// </summary>
        public string ContentLocation { get; set; }

        /// <summary>
        /// Player location.
        /// </summary>
        public string PlayerLocation { get; set; }

        /// <summary>
        /// Duration.
        /// </summary>
        public int? Duration { get; set; }

        /// <summary>
        /// Expiration date.
        /// </summary>
        public DateTime? ExpirationDate { get; set; }

        /// <summary>
        /// Rating.
        /// </summary>
        public double? Rating { get; set; }

        /// <summary>
        /// View count.
        /// </summary>
        public int? ViewCount { get; set; }

        /// <summary>
        /// Publication date.
        /// </summary>
        public DateTime? PublicationDate { get; set; }

        /// <summary>
        /// Tags.
        /// </summary>
        public string[] Tags { get; set; }

        /// <summary>
        /// Category.
        /// </summary>
        public string Category { get; set; }

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Sitemap video.
        /// </summary>
        public SitemapVideo()
        {

        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
