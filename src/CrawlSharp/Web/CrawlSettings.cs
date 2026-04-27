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
        /// Boolean indicating whether or not a headless browser should be used.
        /// Using a headless browser will make crawling of certain sites possible, specifically those that detect bots and disallow crawling.
        /// Using a headless browser may require installation of certain dependencies (such as on Ubuntu Server).  Please refer to the README for details.
        /// </summary>
        public bool UseHeadlessBrowser { get; set; } = false;

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
        /// Boolean indicating if only links that are within the same subdomain to the entry URL should be followed.
        /// Option is only valid when FollowLinks is set to true.
        /// </summary>
        public bool RestrictToSameSubdomain { get; set; } = true;

        /// <summary>
        /// Boolean indicating if only links that are within the same root domain to the entry URL should be followed.
        /// Option is only valid when FollowLinks is set to true.
        /// </summary>
        public bool RestrictToSameRootDomain { get; set; } = true;

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
        /// List of denied domains.  If empty, no domains will be denied.  Otherwise, only the domains specified here will be denied.
        /// Option is only valid when FollowLinks is set to true.
        /// </summary>
        public List<string> DeniedDomains
        {
            get
            {
                return _DeniedDomains;
            }
            set
            {
                if (value == null) value = new List<string>();
                _DeniedDomains = value;
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
        /// Timeout in milliseconds for retrieving each page.
        /// Default is 30000 (30 seconds).  Minimum value is 1000.
        /// </summary>
        public int PageTimeoutMs
        {
            get
            {
                return _PageTimeoutMs;
            }
            set
            {
                if (value < 1000) value = 1000;
                _PageTimeoutMs = value;
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

        /// <summary>
        /// Boolean indicating whether or not to retry requests that receive a 429 (Too Many Requests) response.
        /// Default is true.
        /// </summary>
        public bool RetryOn429 { get; set; } = true;

        /// <summary>
        /// Maximum number of retry attempts when receiving a 429 response.
        /// Default is 3.  Minimum value is 1.
        /// </summary>
        public int MaxRetries
        {
            get
            {
                return _MaxRetries;
            }
            set
            {
                if (value < 1) value = 1;
                _MaxRetries = value;
            }
        }

        /// <summary>
        /// Minimum backoff time in milliseconds when retrying after a 429 response.
        /// Default is 1000 (1 second).  Minimum value is 100.
        /// </summary>
        public int RetryMinBackoffMs
        {
            get
            {
                return _RetryMinBackoffMs;
            }
            set
            {
                if (value < 100) value = 100;
                _RetryMinBackoffMs = value;
            }
        }

        /// <summary>
        /// Maximum backoff time in milliseconds when retrying after a 429 response.
        /// Default is 30000 (30 seconds).  Minimum value is 1000.
        /// </summary>
        public int RetryMaxBackoffMs
        {
            get
            {
                return _RetryMaxBackoffMs;
            }
            set
            {
                if (value < 1000) value = 1000;
                _RetryMaxBackoffMs = value;
            }
        }

        /// <summary>
        /// Boolean indicating whether or not to add random jitter to the backoff delay when retrying after a 429 response.
        /// Default is true.
        /// </summary>
        public bool RetryBackoffJitter { get; set; } = true;

        /// <summary>
        /// The number of milliseconds to delay between each HTTP request.
        /// Default is 2500.
        /// </summary>
        public int RequestDelayMs
        {
            get
            {
                return _RequestDelayMs;
            }
            set
            {
                if (value < 0) value = 0;
                _RequestDelayMs = value;
            }
        }

        /// <summary>
        /// Boolean indicating whether or not common collapsible content should be expanded
        /// before capturing rendered HTML in headless browser mode.
        /// Ignored when <see cref="UseHeadlessBrowser"/> is false.
        /// Default is false.
        /// </summary>
        public bool AutoExpandCollapsibles { get; set; } = false;

        /// <summary>
        /// Optional delay in milliseconds after page navigation completes and before any
        /// headless auto-expansion logic runs.  Ignored when headless auto-expansion is disabled.
        /// Default is 0.
        /// </summary>
        public int PostLoadDelayMs
        {
            get
            {
                return _PostLoadDelayMs;
            }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(PostLoadDelayMs));
                _PostLoadDelayMs = value;
            }
        }

        /// <summary>
        /// Delay in milliseconds after each headless expansion pass to allow the DOM to settle.
        /// Ignored when headless auto-expansion is disabled.
        /// Default is 250.
        /// </summary>
        public int PostInteractionDelayMs
        {
            get
            {
                return _PostInteractionDelayMs;
            }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(PostInteractionDelayMs));
                _PostInteractionDelayMs = value;
            }
        }

        /// <summary>
        /// Maximum number of headless expansion passes to attempt before capturing HTML.
        /// Ignored when headless auto-expansion is disabled.
        /// Default is 2.
        /// </summary>
        public int MaxExpansionPasses
        {
            get
            {
                return _MaxExpansionPasses;
            }
            set
            {
                if (value < 1) throw new ArgumentOutOfRangeException(nameof(MaxExpansionPasses));
                _MaxExpansionPasses = value;
            }
        }

        /// <summary>
        /// Additional CSS selectors to click during headless auto-expansion.
        /// Ignored when headless auto-expansion is disabled.
        /// </summary>
        public List<string> ExpansionSelectors
        {
            get
            {
                return _ExpansionSelectors;
            }
            set
            {
                if (value == null) value = new List<string>();
                _ExpansionSelectors = value;
            }
        }

        #endregion

        #region Private-Members

        private string _UserAgent = "CrawlSharp";
        private string _StartUrl = null;
        private List<string> _AllowedDomains = new List<string>();
        private List<string> _DeniedDomains = new List<string>();
        private int _MaxCrawlDepth = 5;
        private List<Regex> _ExcludeLinkPatterns = new List<Regex>();
        private int _MaxParallelTasks = 8;
        private int _PageTimeoutMs = 30000;
        private int _ThrottleMs = 5000;
        private int _MaxRetries = 3;
        private int _RetryMinBackoffMs = 1000;
        private int _RetryMaxBackoffMs = 30000;
        private int _RequestDelayMs = 2500;
        private int _PostLoadDelayMs = 0;
        private int _PostInteractionDelayMs = 250;
        private int _MaxExpansionPasses = 2;
        private List<string> _ExpansionSelectors = new List<string>();

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
