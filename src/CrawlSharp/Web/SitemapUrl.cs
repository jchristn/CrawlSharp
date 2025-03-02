namespace CrawlSharp.Web
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Sitemap URL.
    /// </summary>
    public class SitemapUrl
    {
        #region Public-Members

        /// <summary>
        /// Location.
        /// </summary>
        public string Location { get; set; }

        /// <summary>
        /// Timestamp from last modification.
        /// </summary>
        public DateTime? LastModified { get; set; }

        /// <summary>
        /// Change frequency.
        /// </summary>
        public string ChangeFrequency { get; set; }

        /// <summary>
        /// Priority.
        /// </summary>
        public double? Priority { get; set; }

        /// <summary>
        /// Images.
        /// </summary>
        public List<SitemapImage> Images { get; set; } = new List<SitemapImage>();

        /// <summary>
        /// Videos.
        /// </summary>
        public List<SitemapVideo> Videos { get; set; } = new List<SitemapVideo>();

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Sitemap URL.
        /// </summary>
        public SitemapUrl()
        {

        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
