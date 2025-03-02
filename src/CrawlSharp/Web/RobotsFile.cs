namespace CrawlSharp.Web
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Robots.txt file.
    /// </summary>
    public class RobotsFile
    {
        #region Public-Members

        /// <summary>
        /// File contents.
        /// </summary>
        public string Contents
        {
            get
            {
                return _Contents;
            }
        }

        /// <summary>
        /// Dictionary containing disallow values.  
        /// The key is the user agent, and the value is the list of disallowed URL paths.
        /// </summary>
        public Dictionary<string, List<string>> Disallow
        {
            get
            {
                return _Disallow;
            }
        }

        /// <summary>
        /// Dictionary containing allow values.  
        /// The key is the user agent, and the value is the list of allowed URL paths.
        /// </summary>
        public Dictionary<string, List<string>> Allow
        {
            get
            {
                return _Allow;
            }
        }

        /// <summary>
        /// Dictionary containing sitemaps.  
        /// The key is the user agent, and the value is the sitemap.
        /// </summary>
        public Dictionary<string, string> Sitemap
        {
            get
            {
                return _Sitemap;
            }
        }

        /// <summary>
        /// Dictionary containing crawl delay values.  
        /// The key is the user agent, and the value is the crawl delay.
        /// </summary>
        public Dictionary<string, decimal> CrawlDelay
        {
            get
            {
                return _CrawlDelay;
            }
        }

        #endregion

        #region Private-Members

        private string _Contents = "";
        private Dictionary<string, List<string>> _Disallow = new Dictionary<string, List<string>>();
        private Dictionary<string, List<string>> _Allow = new Dictionary<string, List<string>>();
        private Dictionary<string, string> _Sitemap = new Dictionary<string, string>();
        private Dictionary<string, decimal> _CrawlDelay = new Dictionary<string, decimal>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Robots.txt file.
        /// </summary>
        /// <param name="contents">Robots.txt file contents.</param>
        public RobotsFile(string contents)
        {
            if (String.IsNullOrEmpty(contents)) contents = "";

            _Contents = contents;

            ParseContents();
        }

        /// <summary>
        /// Robots.txt file.
        /// </summary>
        /// <param name="contents">Robots.txt file contents.</param>
        public RobotsFile(byte[] contents)
        {
            if (contents == null) contents = Array.Empty<byte>();

            _Contents = Encoding.UTF8.GetString(contents);

            ParseContents();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Retrieve the list of disallow URLs.  If no user agent is supplied, * will be used.
        /// </summary>
        /// <param name="userAgent">User-agent.</param>
        /// <returns>List of URLs specified as disallowed.</returns>
        public IEnumerable<string> GetDisallowUrls(string userAgent = "*")
        {
            if (String.IsNullOrEmpty(userAgent)) userAgent = "*";

            if (_Disallow.ContainsKey(userAgent))
            {
                List<string> urls = _Disallow[userAgent];
                foreach (string url in urls)
                {
                    if (!String.IsNullOrEmpty(url)) yield return url;
                }
            }

            yield break;
        }

        /// <summary>
        /// Retrieve the list of allow URLs.  If no user agent is supplied, * will be used.
        /// </summary>
        /// <param name="userAgent">User-agent.</param>
        /// <returns>List of URLs specified as allowed.</returns>
        public IEnumerable<string> GetAllowUrls(string userAgent = "*")
        {
            if (String.IsNullOrEmpty(userAgent)) userAgent = "*";

            if (_Allow.ContainsKey(userAgent))
            {
                List<string> urls = _Allow[userAgent];
                foreach (string url in urls)
                {
                    if (!String.IsNullOrEmpty(url)) yield return url;
                }
            }

            yield break;
        }

        /// <summary>
        /// Retrieve the sitemap.  If no user agent is supplied, * will be used.
        /// </summary>
        /// <param name="userAgent">User-agent.</param>
        /// <returns>Sitemap, if specified.</returns>
        public string GetSitemap(string userAgent = "*")
        {
            if (String.IsNullOrEmpty(userAgent)) userAgent = "*";

            if (_Sitemap.ContainsKey(userAgent))
            {
                if (!String.IsNullOrEmpty(_Sitemap[userAgent])) return _Sitemap[userAgent];
            }

            return null;
        }

        /// <summary>
        /// Retrieve the crawl delay.  If no user agent is supplied, * will be used.
        /// </summary>
        /// <param name="userAgent">User-agent.</param>
        /// <returns>Crawl delay, if specified.  Otherwise, 0 is returned.</returns>
        public decimal GetCrawlDelay(string userAgent = "*")
        {
            if (String.IsNullOrEmpty(userAgent)) userAgent = "*";

            if (_CrawlDelay.ContainsKey(userAgent))
            {
                return _CrawlDelay[userAgent];
            }

            return 0;
        }

        /// <summary>
        /// Checks if specific rules exist for the given user agent.
        /// </summary>
        /// <param name="userAgent">User agent to check.</param>
        /// <returns>True if the user agent has specific rules, false otherwise.</returns>
        public bool HasRulesForUserAgent(string userAgent)
        {
            if (String.IsNullOrEmpty(userAgent)) return false;

            return _Disallow.ContainsKey(userAgent) ||
                   _Allow.ContainsKey(userAgent) ||
                   _Sitemap.ContainsKey(userAgent) ||
                   _CrawlDelay.ContainsKey(userAgent);
        }

        /// <summary>
        /// Determine if a path can be crawled by a specific user agent.
        /// </summary>
        /// <param name="userAgent">User agent.</param>
        /// <param name="path">Path.</param>
        /// <returns>True if allowed.</returns>
        public bool IsPathAllowed(string userAgent, string path)
        {
            if (String.IsNullOrEmpty(userAgent)) userAgent = "*";
            if (String.IsNullOrEmpty(path)) path = "/";

            // Normalize path to ensure it starts with /
            if (!path.StartsWith("/")) path = "/" + path;

            // Check if we have rules for this user agent
            bool userAgentFound = false;

            // Try specific user agent first
            if (_Disallow.ContainsKey(userAgent) || _Allow.ContainsKey(userAgent))
            {
                userAgentFound = true;
            }
            // Fall back to wildcard if specific user agent not found
            else if (_Disallow.ContainsKey("*") || _Allow.ContainsKey("*"))
            {
                userAgent = "*";
                userAgentFound = true;
            }

            // If no rules found for any applicable user agent, access is allowed by default
            if (!userAgentFound)
            {
                return true;
            }

            // Get allow and disallow rules for the user agent
            List<string> disallowRules = _Disallow.ContainsKey(userAgent) ? _Disallow[userAgent] : new List<string>();
            List<string> allowRules = _Allow.ContainsKey(userAgent) ? _Allow[userAgent] : new List<string>();

            // Check for empty Disallow rule which means allow all
            if (disallowRules.Contains("") || disallowRules.Contains("/"))
            {
                // Remove the empty rule for further processing
                disallowRules.Remove("");
                disallowRules.Remove("/");
            }

            // Find the most specific matching rules (longest path match)
            string matchingDisallow = null;
            int disallowLength = -1;

            foreach (string rule in disallowRules)
            {
                if (PathStartsWith(path, rule) && rule.Length > disallowLength)
                {
                    matchingDisallow = rule;
                    disallowLength = rule.Length;
                }
            }

            string matchingAllow = null;
            int allowLength = -1;

            foreach (string rule in allowRules)
            {
                if (PathStartsWith(path, rule) && rule.Length > allowLength)
                {
                    matchingAllow = rule;
                    allowLength = rule.Length;
                }
            }

            // Decision logic:
            // 1. If no rules match, allow access
            if (matchingDisallow == null && matchingAllow == null)
            {
                return true;
            }

            // 2. If only allow rule matches, allow access
            if (matchingDisallow == null && matchingAllow != null)
            {
                return true;
            }

            // 3. If only disallow rule matches, disallow access
            if (matchingDisallow != null && matchingAllow == null)
            {
                return false;
            }

            // 4. If both match, use the most specific (longest) rule
            if (allowLength > disallowLength)
            {
                return true;
            }
            else if (disallowLength > allowLength)
            {
                return false;
            }

            // 5. If both rules are equally specific, allow takes precedence
            return true;
        }

        #endregion

        #region Private-Methods

        private bool PathStartsWith(string path, string rule)
        {
            // Case-insensitive path matching
            return path.StartsWith(rule, StringComparison.OrdinalIgnoreCase);
        }

        private void ParseContents()
        {
            string[] lines = _Contents.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            string userAgent = "";

            foreach (string line in lines)
            {
                if (String.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("#")) continue;

                if (line.ToLower().StartsWith("user-agent: ") && line.Length > 12) userAgent = line.Substring(12);

                if (String.IsNullOrEmpty(userAgent)) continue; // no user agent set

                if (line.ToLower().StartsWith("allow: ") && line.Length > 7)
                {
                    string allow = line.Substring(7);
                    if (!_Allow.ContainsKey(userAgent)) _Allow.Add(userAgent, new List<string> { allow });
                    else _Allow[userAgent].Add(allow);
                }

                if (line.ToLower().StartsWith("disallow: ") && line.Length > 10)
                {
                    string disallow = line.Substring(10);
                    if (!_Disallow.ContainsKey(userAgent)) _Disallow.Add(userAgent, new List<string> { disallow });
                    else _Disallow[userAgent].Add(disallow);
                }

                if (line.ToLower().StartsWith("sitemap: ") && line.Length > 9)
                {
                    string sitemap = line.Substring(9);
                    if (!_Sitemap.ContainsKey(userAgent)) _Sitemap.Add(userAgent, sitemap);
                    else _Sitemap[userAgent] = sitemap;
                }

                if (line.ToLower().StartsWith("crawl-delay: ") && line.Length > 13)
                {
                    string crawlDelay = line.Substring(13);
                    if (Decimal.TryParse(crawlDelay, out decimal val))
                    {
                        if (!_CrawlDelay.ContainsKey(userAgent)) _CrawlDelay.Add(userAgent, val);
                        else _CrawlDelay[userAgent] = val;
                    }
                }
            }
        }

        #endregion 
    }
}