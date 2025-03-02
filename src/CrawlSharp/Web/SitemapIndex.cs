namespace CrawlSharp.Web
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Sitemap index containing multiple sitemap locations.
    /// </summary>
    public class SitemapIndex
    {
        #region Public-Members

        /// <summary>
        /// Sitemap locations.
        /// </summary>
        public List<string> Locations
        {
            get
            {
                return _Locations;
            }
            set
            {
                if (value == null) value = new List<string>();
                _Locations = value;
            }
        }

        #endregion

        #region Private-Members

        private List<string> _Locations = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Sitemap index containing multiple sitemap locations.
        /// </summary>
        public SitemapIndex()
        {

        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
