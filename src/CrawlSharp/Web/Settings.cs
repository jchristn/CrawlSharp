using System;

namespace CrawlSharp.Web
{
    /// <summary>
    /// Web crawler settings.
    /// </summary>
    public class Settings
    {
        #region Public-Members

        /// <summary>
        /// Authentication settings.
        /// </summary>
        public AuthenticationSettings Authentication
        {
            get
            {
                return _Authentication;
            }
            set
            {
                if (value == null) value = new AuthenticationSettings();
                _Authentication = value;
            }
        }

        /// <summary>
        /// Crawl settings.
        /// </summary>
        public CrawlSettings Crawl
        {
            get
            {
                return _Crawl;
            }
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(Crawl));
                _Crawl = value;
            }
        }

        #endregion

        #region Private-Members

        private AuthenticationSettings _Authentication = new AuthenticationSettings();
        private CrawlSettings _Crawl = new CrawlSettings();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Web crawler settings.
        /// </summary>
        public Settings()
        {

        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
