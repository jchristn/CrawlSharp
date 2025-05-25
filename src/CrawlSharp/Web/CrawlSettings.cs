using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CrawlSharp.Web
{
    /// <summary>
    /// Crawl settings.
    /// </summary>
    public class CrawlSettings
    {
        #region Public-Members

        /// <summary>
        /// User agent.
        /// </summary>
        public string UserAgent
        {
            get
            {
                return _UserAgent;
            }
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(UserAgent));
                _UserAgent = value;
            }
        }

        /// <summary>
        /// Start URL.
        /// </summary>
        public string StartUrl
        {
            get
            {
                return _StartUrl;
            }
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(StartUrl));
                Uri test = new Uri(value);
                _StartUrl = value;
            }
        }

        /// <summary>
        /// Boolean indicating whether or not the contents of the webserver's robots.txt file should be ignored.
        /// This value should remain false unless you are crawling web servers that you own and operate or you have been provided explicit permission to do so.
        /// </summary>
        public bool IgnoreRobotsText { get; set; } = false;

        /// <summary>
        /// Boolean indicating whether or not the contents of the sitemap.xml should be included.
        /// </summary>
        public bool IncludeSitemap { get; set; } = true;

        /// <summary>
        /// Boolean specifying whether or not page links should be followed.
        /// </summary>
        public bool FollowLinks { get; set; } = true;
        
        /// <summary>
        /// Boolean specifying whether or not redirects should be followed.
        /// </summary>
        public bool FollowRedirects { get; set; } = true;

        /// <summary>
        /// Boolean indicating if only links that are child URLs to the entry URL should be followed.
        /// Option is only valid when FollowLinks is set to true.
        /// </summary>
        public bool RestrictToChildUrls { get; set; } = true;

        /// <summary>
        /// Boolean indicating if only links that are within the same domain to the entry URL should be followed.
        /// Option is only valid when FollowLinks is set to true.
        /// </summary>
        public bool RestrictToSameDomain { get; set; } = true;

        /// <summary>
        /// List of allowed domains.  If empty, all domains will be allowed.  Otherwise, only the domains specified here will be allowed.
        /// Option is only valid when FollowLinks is set to true.
        /// </summary>
        public List<string> AllowedDomains
        {
            get
            {
                return _AllowedDomains;
            }
            set
            {
                if (value == null) value = new List<string>();
                _AllowedDomains = value;
            }
        }

        /// <summary>
        /// Maximum crawl depth, that is, how many levels of links to follow from the entry URL.
        /// </summary>
        public int MaxCrawlDepth
        {
            get
            {
                return _MaxCrawlDepth;
            }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(MaxCrawlDepth));
                _MaxCrawlDepth = value;
            }
        }

        /// <summary>
        /// Regular expression patterns to use to exclude from link following.
        /// </summary>
        public List<Regex> ExcludeLinkPatterns
        {
            get
            {
                return _ExcludeLinkPatterns;
            }
            set
            {
                if (value == null) value = new List<Regex>();
                _ExcludeLinkPatterns = value;
            }
        }

        /// <summary>
        /// Boolean indicating whether links leading to external sources should be followed.
        /// </summary>
        public bool FollowExternalLinks { get; set; } = true;

        /// <summary>
        /// Maximum number of tasks to run in parallel.  Default is 8.
        /// </summary>
        public int MaxParallelTasks
        {
            get
            {
                return _MaxParallelTasks;
            }
            set
            {
                if (value < 1) throw new ArgumentOutOfRangeException(nameof(MaxParallelTasks));
                _MaxParallelTasks = value;
            }
        }

        /// <summary>
        /// The number of milliseconds to delay when receiving a 429 response.
        /// </summary>
        public int ThrottleMs
        {
            get
            {
                return _ThrottleMs;
            }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(ThrottleMs));
                _ThrottleMs = value;
            }
        }

        #endregion

        #region Private-Members

        private string _UserAgent = "CrawlSharp";
        private string _StartUrl = null;
        private List<string> _AllowedDomains = new List<string>();
        private int _MaxCrawlDepth = 5;
        private List<Regex> _ExcludeLinkPatterns = new List<Regex>();
        private int _MaxParallelTasks = 8;
        private int _ThrottleMs = 100;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Authentication settings.
        /// </summary>
        public CrawlSettings()
        {

        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
