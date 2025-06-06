namespace CrawlSharp.Web
{
    using System;
    using System.Collections;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Reflection.Metadata.Ecma335;
    using System.Reflection.PortableExecutable;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using CrawlSharp.Helpers;
    using HtmlAgilityPack;
    using RestWrapper;
    using SerializationHelper;

    /// <summary>
    /// Web crawler.
    /// </summary>
    public class WebCrawler : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Delay in milliseconds between retrievals.
        /// </summary>
        public int Delay
        {
            get
            {
                return _DelayMilliseconds;
            }
            set
            {
                if (value < 0) throw new ArgumentException("Delay must be zero or greater.");
                _DelayMilliseconds = value;
            }
        }

        /// <summary>
        /// Method to invoke to send log messages.
        /// </summary>
        public Action<string> Logger { get; set; } = null;

        /// <summary>
        /// Method to invoke when exceptions are encountered.
        /// </summary>
        public Action<string, Exception> Exception { get; set; } = null;

        /// <summary>
        /// Dictionary of visited links.  
        /// When accessed, a copy is made of the internal dictionary.  
        /// Your copy will not be updated automatically.
        /// </summary>
        public Dictionary<Uri, WebResource> VisitedLinks
        {
            get
            {
                lock (_VisitedLinksLock)
                {
                    return new Dictionary<Uri, WebResource>(_VisitedLinks);
                }
            }
        }

        /// <summary>
        /// Queued links.
        /// When accessed, a copy is made of the internal queue.
        /// Your copy will not be updated automatically.
        /// </summary>
        public Queue<QueuedLink> QueuedLinks
        {
            get
            {
                lock (_QueuedLinksLock)
                {
                    return new Queue<QueuedLink>(_QueuedLinks);
                }
            }
        }

        /// <summary>
        /// Links currently being processed.
        /// When accessed, a copy is made of the internal list.
        /// Your copy will not be updated automatically.
        /// </summary>
        public List<QueuedLink> ProcessingLinks
        {
            get
            {
                lock (_ProcessingLinksLock)
                {
                    return new List<QueuedLink>(_ProcessingLinks);
                }
            }
        }

        #endregion

        #region Private-Members

        private string _Header = "[WebCrawler] ";
        private Settings _Settings = null;
        private Serializer _Serializer = new Serializer();
        private int _DelayMilliseconds = 0;
        private bool _Disposed = false;

        private RobotsFile _RobotsFile = new RobotsFile("");

        private SemaphoreSlim _Semaphore;

        private readonly object _QueuedLinksLock = new object();
        private Queue<QueuedLink> _QueuedLinks = new Queue<QueuedLink>();

        private readonly object _ProcessingLinksLock = new object();
        private List<QueuedLink> _ProcessingLinks = new List<QueuedLink>();

        private readonly object _VisitedLinksLock = new object();
        private Dictionary<Uri, WebResource> _VisitedLinks = new Dictionary<Uri, WebResource>();

        private readonly object _FinishedLinksLock = new object();
        private Queue<WebResource> _FinishedLinks = new Queue<WebResource>();

        private CancellationToken _Token;
        private Task _QueueProcessor = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Web crawler.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <param name="token">Cancellation token.</param>
        public WebCrawler(Settings settings, CancellationToken token = default)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Semaphore = new SemaphoreSlim(_Settings.Crawl.MaxParallelTasks, _Settings.Crawl.MaxParallelTasks);
            _Token = token;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Disposes of resources used by the WebCrawler.
        /// </summary>
        /// <param name="disposing">True if called from Dispose(), false if called from finalizer.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_Disposed)
            {
                if (disposing)
                {
                    CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(_Token);
                    cts.Cancel();

                    if (_QueueProcessor != null && !_QueueProcessor.IsCompleted)
                    {
                        int maxWait = 3000;
                        int waited = 0;

                        while (waited < maxWait)
                        {
                            bool completed = _QueueProcessor.Wait(100);
                            if (completed) break;
                            waited += 100;
                        }
                    }

                    cts.Dispose();

                    _Semaphore?.Dispose();
                    _QueuedLinks?.Clear();
                    _ProcessingLinks?.Clear();
                    _VisitedLinks?.Clear();
                    _FinishedLinks?.Clear();
                }

                _Serializer = null;
                _Semaphore = null;
                _QueuedLinks = null;
                _ProcessingLinks = null;
                _VisitedLinks = null;
                _FinishedLinks = null;

                _Disposed = true;
            }
        }

        /// <summary>
        /// Disposes of resources used by the WebCrawler.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Crawl using the server and configuration defined in the supplied settings.
        /// </summary>
        /// <returns>Enumerable of WebResource objects.</returns>
        public IEnumerable<WebResource> Crawl(HttpMethod method)
        {
            #region Process-Robots-and-Sitemap

            Task robotsFile = RetrieveRobotsFile(_Settings.Crawl.StartUrl, _Token);
            robotsFile.Wait();

            if (_RobotsFile != null)
            {
                decimal crawlDelay = _RobotsFile.GetCrawlDelay(_Settings.Crawl.UserAgent);
                if (crawlDelay > 0)
                {
                    _DelayMilliseconds = (int)(crawlDelay * 1000);
                    Log("crawl delay set to " + _DelayMilliseconds + "ms per robots.txt");
                }
            }

            Task processSitemap = ProcessSitemap(_Settings.Crawl.StartUrl, _Token);
            processSitemap.Wait();

            #endregion

            #region Enqueue-Root-Url

            EnqueueQueuedLink(_Settings.Crawl.StartUrl, null, 0);

            #endregion

            #region Start-Queue-Processor

            _QueueProcessor = Task.Run(() => QueueProcessor(_Token), _Token);

            while (!_QueueProcessor.IsCompleted)
            {
                Task.Delay(10, _Token).Wait();
                WebResource wr = DequeueWebResource();
                if (wr != null) yield return wr;
            }

            #endregion

            #region Drain-the-Queue

            while (true)
            {
                WebResource wr = DequeueWebResource();
                if (wr == null) break;
                yield return wr;
            }

            #endregion
        }

        /// <summary>
        /// Crawl using the server and configuration defined in the supplied settings.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Enumerable of WebResource objects.</returns>
        public async IAsyncEnumerable<WebResource> CrawlAsync([EnumeratorCancellation] CancellationToken token = default)
        {
            #region Process-Robots-and-Sitemap

            CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(_Token, token);

            await RetrieveRobotsFile(_Settings.Crawl.StartUrl, cts.Token).ConfigureAwait(false);

            if (_RobotsFile != null)
            {
                decimal crawlDelay = _RobotsFile.GetCrawlDelay(_Settings.Crawl.UserAgent);
                if (crawlDelay > 0)
                {
                    _DelayMilliseconds = (int)(crawlDelay * 1000);
                    Log("crawl delay set to " + _DelayMilliseconds + "ms per robots.txt");
                }
            }

            await ProcessSitemap(_Settings.Crawl.StartUrl, cts.Token).ConfigureAwait(false);

            #endregion

            #region Enqueue-Root-Url

            EnqueueQueuedLink(_Settings.Crawl.StartUrl, null, 0);

            #endregion

            #region Start-Queue-Processor

            _QueueProcessor = Task.Run(() => QueueProcessor(cts.Token), cts.Token);

            while (!_QueueProcessor.IsCompleted)
            {
                await Task.Delay(10, cts.Token).ConfigureAwait(false);

                WebResource wr = DequeueWebResource();
                if (wr != null) yield return wr;
            }

            #endregion

            #region Drain-the-Queue

            while (true)
            {
                WebResource wr = DequeueWebResource();
                if (wr == null) break;
                yield return wr;
            }

            #endregion
        }

        #endregion

        #region Private-Methods

        private void Log(string msg)
        {
            if (String.IsNullOrEmpty(msg)) return;
            Logger?.Invoke(_Header + msg);
        }

        private RestRequest RequestBuilder(string url)
        {
            RestRequest req = new RestRequest(url);
            req.UserAgent = _Settings.Crawl.UserAgent;

            if (_Settings.Authentication.Type != AuthenticationTypeEnum.None)
            {
                if (_Settings.Authentication.Type == AuthenticationTypeEnum.ApiKey
                    && !String.IsNullOrEmpty(_Settings.Authentication.ApiKeyHeader))
                {
                    req.Headers.Add(_Settings.Authentication.ApiKeyHeader, _Settings.Authentication.ApiKey);
                }
                else if (_Settings.Authentication.Type == AuthenticationTypeEnum.Basic)
                {
                    req.Authorization.User = _Settings.Authentication.Username;
                    req.Authorization.Password = _Settings.Authentication.Password;
                }
                else if (_Settings.Authentication.Type == AuthenticationTypeEnum.BearerToken)
                {
                    req.Authorization.BearerToken = _Settings.Authentication.BearerToken;
                }
                else throw new ArgumentException("Unsupported authentication type " + _Settings.Authentication.Type.ToString());
            }

            return req;
        }

        private async Task<WebResource> RetrieveWebResource(string url, string parentUrl, int depth, CancellationToken token = default)
        {
            try
            {
                await Pause(token).ConfigureAwait(false);

                string fullUrl = NormalizeUrl(_Settings.Crawl.StartUrl, url);
                if (String.IsNullOrEmpty(fullUrl))
                {
                    Log("invalid URL " + url);
                    return null;
                }

                // FIXED: Use case-insensitive comparison for HTTP/HTTPS check
                if (!fullUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !fullUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"URL does not start with http/https: {fullUrl} (original: {url})");
                    return null;
                }

                // Create Uri without fragments
                Uri normalizedUri;
                try
                {
                    normalizedUri = new Uri(fullUrl);
                    if (!String.IsNullOrEmpty(normalizedUri.Fragment))
                    {
                        UriBuilder builder = new UriBuilder(normalizedUri);
                        builder.Fragment = "";
                        normalizedUri = builder.Uri;
                    }
                }
                catch (UriFormatException ufe)
                {
                    Exception?.Invoke(fullUrl, ufe);
                    Log("invalid URI format " + fullUrl);
                    return null;
                }

                if (IsAlreadyVisited(normalizedUri))
                {
                    Log("already visited " + normalizedUri);
                    return GetAlreadyVisited(normalizedUri);
                }

                if (!_RobotsFile.IsPathAllowed(_Settings.Crawl.UserAgent, normalizedUri.AbsolutePath))
                {
                    Log("crawl of " + normalizedUri + " prohibited by robots.txt");
                    return null;
                }

                Log("retrieving " + normalizedUri);

                WebResource wr;

                using (RestRequest req = RequestBuilder(normalizedUri.ToString()))
                {
                    using (RestResponse resp = await req.SendAsync(token).ConfigureAwait(false))
                    {
                        if (resp == null)
                        {
                            Log("unable to retrieve " + normalizedUri);

                            wr = new WebResource
                            {
                                Url = normalizedUri.ToString(),
                                ParentUrl = parentUrl,
                                Depth = depth,
                                Status = 0
                            };

                            AddAlreadyVisited(normalizedUri, wr);

                            return wr;
                        }

                        if (resp.StatusCode >= 300 && resp.StatusCode <= 308)
                        {
                            Log("redirect status " + resp.StatusCode + " for URL " + normalizedUri);

                            if (_Settings.Crawl.FollowRedirects)
                            {
                                string redirectLocation = resp.Headers.AllKeys
                                    .FirstOrDefault(k => string.Equals(k, "Location", StringComparison.OrdinalIgnoreCase))
                                    is string key ? resp.Headers[key] : null;

                                if (String.IsNullOrEmpty(redirectLocation))
                                {
                                    Log("unable to retrieve redirect location from response for URL " + normalizedUri);
                                }
                                else
                                {
                                    string redirectNormalizedUrl = NormalizeUrl(normalizedUri.ToString(), redirectLocation);

                                    Uri redirectUri;
                                    try
                                    {
                                        redirectUri = new Uri(redirectNormalizedUrl);
                                        if (!String.IsNullOrEmpty(redirectUri.Fragment))
                                        {
                                            UriBuilder builder = new UriBuilder(redirectUri);
                                            builder.Fragment = "";
                                            redirectUri = builder.Uri;
                                        }
                                    }
                                    catch (UriFormatException ufe)
                                    {
                                        Exception?.Invoke(redirectNormalizedUrl, ufe);
                                        Log("invalid redirect URI format: " + redirectNormalizedUrl);

                                        // Still need to return something for this URL
                                        wr = new WebResource
                                        {
                                            Url = normalizedUri.ToString(),
                                            ParentUrl = parentUrl,
                                            Depth = depth,
                                            Status = resp.StatusCode,
                                            Headers = resp.Headers,
                                            Data = resp.DataAsBytes
                                        };

                                        AddAlreadyVisited(normalizedUri, wr);
                                        return wr;
                                    }

                                    // Check if we're being redirected to a URL we've already visited
                                    if (IsAlreadyVisited(redirectUri))
                                    {
                                        Log("redirect to already visited URL " + redirectUri + " from " + normalizedUri);

                                        // Store a reference to the redirect target for the original URL
                                        WebResource alreadyVisited = GetAlreadyVisited(redirectUri);
                                        AddAlreadyVisited(normalizedUri, alreadyVisited);
                                        return alreadyVisited;
                                    }

                                    Log("following redirect for URL " + normalizedUri + " to " + redirectUri);
                                    WebResource redirectedResource = await RetrieveWebResource(redirectUri.ToString(), parentUrl, depth, token).ConfigureAwait(false);

                                    // Store a reference to the redirect target for the original URL
                                    if (redirectedResource != null)
                                    {
                                        AddAlreadyVisited(normalizedUri, redirectedResource);
                                    }

                                    return redirectedResource;
                                }
                            }
                            else
                            {
                                Log("ignoring redirect response from URL " + normalizedUri);
                            }
                        }
                        else if (resp.StatusCode == 429)
                        {
                            Log("throttle status " + resp.StatusCode + " for " + normalizedUri);

                            if (_Settings.Crawl.ThrottleMs > 0) await Task.Delay(_Settings.Crawl.ThrottleMs, token).ConfigureAwait(false);
                        }
                        else
                        {
                            Log("status " + resp.StatusCode + " for URL " + normalizedUri);
                        }

                        wr = new WebResource
                        {
                            Url = normalizedUri.ToString(),
                            ParentUrl = parentUrl,
                            Depth = depth,
                            Status = resp.StatusCode,
                            ETag = GetEtag(resp),
                            MD5Hash = resp.DataAsBytes != null ? Convert.ToHexString(HashHelper.MD5Hash(resp.DataAsBytes)) : null,
                            SHA1Hash = resp.DataAsBytes != null ? Convert.ToHexString(HashHelper.SHA1Hash(resp.DataAsBytes)) : null,
                            SHA256Hash = resp.DataAsBytes != null ? Convert.ToHexString(HashHelper.SHA256Hash(resp.DataAsBytes)) : null,
                            Headers = resp.Headers,
                            Data = resp.DataAsBytes
                        };

                        AddAlreadyVisited(normalizedUri, wr);

                        return wr;
                    }
                }
            }
            catch (IOException ioe)
            {
                Exception?.Invoke(url, ioe);
                Log("IO exception while retrieving URL " + url + Environment.NewLine + ioe.ToString());
                return null;
            }
            catch (HttpRequestException hre)
            {
                Exception?.Invoke(url, hre);
                Log("HTTP request exception while retrieving URL " + url + Environment.NewLine + hre.ToString());
                return null;
            }
            catch (Exception e)
            {
                Exception?.Invoke(url, e);
                Log("error processing URL " + url + Environment.NewLine + e.ToString());
                return null;
            }
        }

        private async Task RetrieveRobotsFile(string baseUrl, CancellationToken token = default)
        {
            if (_Settings.Crawl.IgnoreRobotsText)
            {
                Log("skipping retrieval and processing of robots.txt due to settings");
                return;
            }

            if (String.IsNullOrEmpty(baseUrl)) return;

            string robotsFile = baseUrl;
            if (!robotsFile.EndsWith("/")) robotsFile += "/robots.txt";
            else robotsFile += "robots.txt";

            WebResource robots = await RetrieveWebResource(robotsFile, baseUrl, 0, token).ConfigureAwait(false);
            if (robots != null
                && robots.Status >= 200
                && robots.Status <= 299
                && robots.Data != null)
            {
                try
                {
                    _RobotsFile = new RobotsFile(robots.Data);
                    Log("robots file retrieved and processed from " + robotsFile);
                }
                catch (Exception e)
                {
                    Exception?.Invoke(robotsFile, e);
                    Log("error parsing contents from robots file " + robotsFile + Environment.NewLine + Encoding.UTF8.GetString(robots.Data));
                }
            }
            else
            {
                Log("unable to retrieve robots.txt from " + robotsFile);
            }
        }

        private async Task ProcessSitemap(string baseUrl, CancellationToken token = default)
        {
            if (!_Settings.Crawl.IncludeSitemap)
            {
                Log("skipping retrieval and processing of sitemap.xml due to settings");
                return;
            }

            if (String.IsNullOrEmpty(baseUrl)) return;

            string sitemapUrl = baseUrl;
            if (!sitemapUrl.EndsWith("/")) sitemapUrl += "/sitemap.xml";
            else sitemapUrl += "sitemap.xml";

            WebResource sitemap = await RetrieveWebResource(sitemapUrl, baseUrl, 0, token).ConfigureAwait(false);
            if (sitemap != null
                && sitemap.Status >= 200
                && sitemap.Status <= 299
                && sitemap.Data != null)
            {
                try
                {
                    string sitemapData = Encoding.UTF8.GetString(sitemap.Data);
                    if (SitemapParser.IsParseable(sitemapData))
                    {
                        if (!SitemapParser.IsSitemapIndex(sitemapData))
                        {
                            List<SitemapUrl> urls = SitemapParser.ParseSitemap(sitemapData);
                            if (urls != null && urls.Count > 0)
                            {
                                Log("including " + urls.Count + " URLs from sitemap.xml");

                                foreach (SitemapUrl url in urls)
                                {
                                    if (String.IsNullOrEmpty(url.Location)) continue;
                                    EnqueueQueuedLink(url.Location, sitemapUrl, 0);
                                    Log("queuing URL from sitemap: " + url.Location);
                                }
                            }
                            else
                            {
                                Log("no URLs found in sitemap.xml");
                            }
                        }
                        else
                        {
                            Log("sitemap.xml contains a sitemap index and in unable to be parsed");
                        }
                    }
                    else
                    {
                        Log("sitemap.xml is not parseable, skipping");
                    }
                }
                catch (Exception e)
                {
                    Exception?.Invoke(sitemapUrl, e);
                    Log("error parsing contents from sitemap.xml file " + sitemapUrl + Environment.NewLine + Encoding.UTF8.GetString(sitemap.Data));
                }
            }
            else
            {
                Log("unable to retrieve sitemap.xml from " + sitemap);
            }
        }

        private async Task Pause(CancellationToken token = default)
        {
            if (_DelayMilliseconds == 0) return;
            await Task.Delay(_DelayMilliseconds, token).ConfigureAwait(false);
        }

        private List<string> ExtractLinksFromHtml(string url, byte[] bytes)
        {
            try
            {
                string htmlContent = Encoding.UTF8.GetString(bytes);
                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(htmlContent);
                HtmlNodeCollection linkNodes = doc.DocumentNode.SelectNodes("//a[@href]");
                List<string> links = new List<string>();
                if (linkNodes != null)
                {
                    foreach (var link in linkNodes)
                    {
                        string href = link.GetAttributeValue("href", string.Empty);
                        if (!string.IsNullOrWhiteSpace(href))
                        {
                            // Normalize the URL immediately when extracting
                            string normalizedUrl = NormalizeUrl(url, href);
                            if (!String.IsNullOrEmpty(normalizedUrl))
                            {
                                links.Add(normalizedUrl);
                            }
                        }
                    }
                }

                return links;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private string NormalizeUrl(string baseUrl, string relativeUrl)
        {
            // Handle null or empty cases
            if (string.IsNullOrWhiteSpace(relativeUrl))
                return null;

            // Trim whitespace
            relativeUrl = relativeUrl.Trim();
            baseUrl = baseUrl?.Trim();

            // Handle special schemes that should be ignored
            if (relativeUrl.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                relativeUrl.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                relativeUrl.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ||
                relativeUrl.StartsWith("ftp:", StringComparison.OrdinalIgnoreCase) ||
                relativeUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                relativeUrl.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
                relativeUrl.StartsWith("chrome:", StringComparison.OrdinalIgnoreCase) ||
                relativeUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Check if relativeUrl is already an absolute HTTP/HTTPS URL
            if (relativeUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                relativeUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Uri absUri = new Uri(relativeUrl);
                    // Remove fragment
                    if (!string.IsNullOrEmpty(absUri.Fragment))
                    {
                        UriBuilder builder = new UriBuilder(absUri) { Fragment = "" };
                        return builder.Uri.ToString();
                    }
                    return relativeUrl;
                }
                catch
                {
                    return null;
                }
            }

            // Parse the base URL
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                Log("empty base URL provided to NormalizeUrl");
                return null;
            }

            Uri baseUri;
            try
            {
                baseUri = new Uri(baseUrl);
                if (!baseUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
                    !baseUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"base URL has invalid scheme: {baseUri.Scheme}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Log($"invalid base URL format: {baseUrl} - {ex.Message}");
                return null;
            }

            try
            {
                string result = null;

                // Handle protocol-relative URLs (//example.com/path)
                if (relativeUrl.StartsWith("//"))
                {
                    result = baseUri.Scheme + ":" + relativeUrl;
                }
                // Handle absolute paths (/path/to/page)
                else if (relativeUrl.StartsWith("/"))
                {
                    result = $"{baseUri.Scheme}://{baseUri.Host}";
                    if (!baseUri.IsDefaultPort)
                    {
                        result += $":{baseUri.Port}";
                    }
                    result += relativeUrl;
                }
                // Handle query strings only (?param=value)
                else if (relativeUrl.StartsWith("?"))
                {
                    result = baseUri.GetLeftPart(UriPartial.Path) + relativeUrl;
                }
                // Handle fragments only (#section)
                else if (relativeUrl.StartsWith("#"))
                {
                    // Return base URL without fragment
                    result = baseUri.GetLeftPart(UriPartial.Query);
                }
                // Handle relative paths (including ./ and ../)
                else
                {
                    // Use Uri class to resolve relative paths
                    Uri combined = new Uri(baseUri, relativeUrl);
                    result = combined.ToString();
                }

                // Final validation and fragment removal
                if (!string.IsNullOrEmpty(result))
                {
                    try
                    {
                        Uri finalUri = new Uri(result);
                        if (!finalUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
                            !finalUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                        {
                            return null;
                        }

                        // Remove fragment if present
                        if (!string.IsNullOrEmpty(finalUri.Fragment))
                        {
                            int fragmentIndex = result.IndexOf('#');
                            if (fragmentIndex >= 0)
                            {
                                result = result.Substring(0, fragmentIndex);
                            }
                        }

                        return result;
                    }
                    catch
                    {
                        return null;
                    }
                }

                return null;
            }
            catch (Exception e)
            {
                Log($"error normalizing URL '{relativeUrl}' with base URL '{baseUrl}': {e.Message}");
                return null;
            }
        }

        private string GetEtag(RestResponse resp)
        {
            if (resp == null || resp.Headers == null || resp.Headers.Count < 1) return null;

            string etagHeader = resp.Headers["ETag"];
            if (string.IsNullOrEmpty(etagHeader)) return null;

            string etag = etagHeader.Trim();
            if (etag.StartsWith("W/")) etag = etag.Substring(2).Trim();

            if (etag.Length >= 2 && etag.StartsWith("\"") && etag.EndsWith("\""))
                return etag.Substring(1, etag.Length - 2);

            return etag;
        }

        private bool IsExternalUrl(string baseUrl, string testUrl)
        {
            // If testUrl is null or empty, it's not external
            if (string.IsNullOrWhiteSpace(testUrl))
                return false;

            // If it's a relative URL (starts with / or ~/ or ./ or ../), it's not external
            if (testUrl.StartsWith("/") || testUrl.StartsWith("~/") ||
                testUrl.StartsWith("./") || testUrl.StartsWith("../"))
                return false;

            // If it's a fragment or query, it's not external
            if (testUrl.StartsWith("#") || testUrl.StartsWith("?"))
                return false;

            Uri tempBaseUri;
            try
            {
                tempBaseUri = new Uri(baseUrl);
            }
            catch (UriFormatException)
            {
                // If base URL is invalid, consider test URL external to be safe
                return true;
            }

            // If it doesn't have a scheme, add one from the base URL
            if (!testUrl.Contains("://") && !testUrl.StartsWith("//"))
            {
                testUrl = $"{tempBaseUri.Scheme}://{testUrl}";
            }
            else if (testUrl.StartsWith("//"))
            {
                // Handle protocol-relative URLs
                testUrl = $"{tempBaseUri.Scheme}:{testUrl}";
            }

            // Try to create URI from the test URL
            Uri testUri;
            try
            {
                testUri = new Uri(testUrl);
            }
            catch (UriFormatException)
            {
                // If test URL is invalid, consider it external to be safe
                return true;
            }

            // Compare the hosts
            return !string.Equals(testUri.Host, tempBaseUri.Host, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsUrlExcluded(string url)
        {
            if (_Settings.Crawl.ExcludeLinkPatterns == null || _Settings.Crawl.ExcludeLinkPatterns.Count < 1) return false;

            foreach (var regex in _Settings.Crawl.ExcludeLinkPatterns)
            {
                try
                {
                    if (regex.IsMatch(url)) return true;
                }
                catch (Exception)
                {
                    // Skip invalid regex patterns
                    continue;
                }
            }

            return false;
        }

        private bool IsSameDomain(string url1, string url2)
        {
            // Handle null or empty inputs
            if (string.IsNullOrWhiteSpace(url1) || string.IsNullOrWhiteSpace(url2)) return false;

            try
            {
                // Handle relative URLs for url2
                Uri url1Uri = new Uri(url1);
                if (url2.StartsWith("/"))
                {
                    // Relative to root, so it's the same domain
                    return true;
                }
                else if (url2.StartsWith("./") || url2.StartsWith("../") ||
                        (!url2.Contains("://") && !url2.StartsWith("//")))
                {
                    // Relative URL, so it's the same domain
                    return true;
                }

                // If url2 starts with "//", it's protocol-relative, so prepend the scheme from url1
                if (url2.StartsWith("//"))
                {
                    url2 = $"{url1Uri.Scheme}:{url2}";
                }

                // Parse URL2 into a URI object
                Uri url2Uri = new Uri(url2);

                // Compare domains (hosts)
                return string.Equals(url1Uri.Host, url2Uri.Host, StringComparison.OrdinalIgnoreCase);
            }
            catch (UriFormatException)
            {
                // If URLs are invalid, return false
                return false;
            }
        }

        private bool IsAllowedDomain(string baseUrl, List<string> allowedDomains)
        {
            if (allowedDomains == null || allowedDomains.Count < 1) return true;

            if (String.IsNullOrEmpty(baseUrl))
            {
                Log("checking allowed domains and received and empty base URL");
                return false;
            }

            if (!baseUrl.Contains("://") && !baseUrl.StartsWith("//")) return true;
            if (baseUrl.StartsWith("./")) return true;

            try
            {
                // Handle protocol-relative URLs
                if (baseUrl.StartsWith("//")) baseUrl = "http:" + baseUrl;

                // Parse URL and extract domain
                Uri uri = new Uri(baseUrl);
                string domain = uri.Host.ToLowerInvariant();

                // Check for exact match in the allowed domains list
                return allowedDomains.Any(d => string.Equals(d, domain, StringComparison.OrdinalIgnoreCase));
            }
            catch (UriFormatException)
            {
                return false;
            }
        }

        private bool IsChildUrl(string baseUrl, string testUrl)
        {
            // Handle null or empty inputs
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(testUrl)) return false;

            try
            {
                Uri baseUriObj = new Uri(baseUrl);

                if (testUrl.StartsWith("/"))
                {
                    testUrl = $"{baseUriObj.Scheme}://{baseUriObj.Host}{testUrl}";
                }
                else if (testUrl.StartsWith("./") || testUrl.StartsWith("../"))
                {
                    // Create an absolute URL from relative URL
                    testUrl = new Uri(baseUriObj, testUrl).ToString();
                }
                else if (!testUrl.Contains("://") && !testUrl.StartsWith("//"))
                {
                    // Assume it's a relative URL without ./ prefix
                    testUrl = new Uri(baseUriObj, testUrl).ToString();
                }

                // Ensure both URLs end with trailing slash for proper comparison
                string normalizedBase = baseUrl.TrimEnd('/') + "/";
                string normalizedTest = testUrl.TrimEnd('/') + "/";

                // Create URI objects for comparison
                Uri basePathUri = new Uri(normalizedBase);
                Uri testUri = new Uri(normalizedTest);

                // Check if domains match
                if (!string.Equals(basePathUri.Host, testUri.Host, StringComparison.OrdinalIgnoreCase)) return false;

                // Check if test path starts with base path
                string basePath = basePathUri.AbsolutePath;
                string testPath = testUri.AbsolutePath;

                // Special case: If base path is root ("/"), any path on same domain is a child
                if (basePath.Equals("/"))
                    return true;

                // Check if test path starts with base path
                return testPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase);
            }
            catch (UriFormatException)
            {
                return false;
            }
        }

        private bool IsAlreadyVisited(Uri uri)
        {
            lock (_VisitedLinksLock)
            {
                return _VisitedLinks.ContainsKey(uri);
            }
        }

        private void AddAlreadyVisited(Uri uri, WebResource wr)
        {
            lock (_VisitedLinksLock)
            {
                if (_VisitedLinks.ContainsKey(uri)) _VisitedLinks[uri] = wr;
                else _VisitedLinks.Add(uri, wr);
            }
        }

        private WebResource GetAlreadyVisited(Uri uri)
        {
            lock (_VisitedLinksLock)
            {
                return _VisitedLinks[uri];
            }
        }

        private void EnqueueQueuedLink(string url, string parentUrl, int depth)
        {
            lock (_QueuedLinksLock)
            {
                if (!_QueuedLinks.Any(q => q.Url.Equals(url)))
                {
                    _QueuedLinks.Enqueue(new QueuedLink
                    {
                        Url = url,
                        ParentUrl = parentUrl,
                        Depth = depth
                    });
                }
            }
        }

        private QueuedLink DequeueQueuedLink()
        {
            lock (_QueuedLinksLock)
            {
                if (_QueuedLinks.Count < 1) return null;
                return _QueuedLinks.Dequeue();
            }
        }

        private bool AddProcessingLink(QueuedLink queuedLink)
        {
            lock (_ProcessingLinksLock)
            {
                if (!_ProcessingLinks.Any(q => q.Url.Equals(queuedLink.Url)))
                {
                    _ProcessingLinks.Add(queuedLink);
                    return true;
                }

                return false;
            }
        }

        private void RemoveProcessingLink(QueuedLink queuedLink)
        {
            lock (_ProcessingLinksLock)
            {
                List<QueuedLink> itemsToRemove = _ProcessingLinks
                    .Where(q => q.Url.Equals(queuedLink.Url, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var item in itemsToRemove)
                {
                    _ProcessingLinks.Remove(item);
                }
            }
        }

        private void EnqueueWebResource(WebResource wr)
        {
            lock (_FinishedLinksLock)
            {
                _FinishedLinks.Enqueue(wr);
            }
        }

        private WebResource DequeueWebResource()
        {
            lock (_FinishedLinksLock)
            {
                if (_FinishedLinks.Count < 1) return null;
                return _FinishedLinks.Dequeue();
            }
        }

        private async Task QueueProcessor(CancellationToken token = default)
        {
            List<Task> activeTasks = new List<Task>();
            bool isQueueEmpty = false;

            while (!isQueueEmpty || activeTasks.Count > 0)
            {
                activeTasks.RemoveAll(t => t.IsCompleted);

                while (activeTasks.Count < _Settings.Crawl.MaxParallelTasks)
                {
                    QueuedLink link = DequeueQueuedLink();
                    if (link == null)
                    {
                        // Queue is currently empty, but may receive more items later
                        isQueueEmpty = true;
                        break;
                    }
                    else
                    {
                        isQueueEmpty = false;
                    }
                                          
                    try
                    {
                        Uri uri = new Uri(link.Url);
                        if (IsAlreadyVisited(uri))
                        {
                            Log("skipping already visited link " + link.Url);
                            continue;
                        }
                    }
                    catch (UriFormatException ufe)
                    {
                        Exception?.Invoke(link.Url, ufe);
                        Log("invalid URI format " + link.Url + ", skipping");
                        continue;
                    }

                    if (!AddProcessingLink(link))
                    {
                        Log("skipping link " + link.Url + ", already in processing");
                        continue;
                    }

                    Task workerTask = QueueProcessorInternal(link, token);
                    activeTasks.Add(workerTask);
                }

                if (activeTasks.Count > 0)
                {
                    await Task.WhenAny(activeTasks).ConfigureAwait(false);

                    lock (_QueuedLinksLock)
                    {
                        isQueueEmpty = _QueuedLinks.Count == 0;
                    }
                }
                else if (isQueueEmpty)
                {
                    break;
                }
                else
                {
                    await Task.Delay(10, token).ConfigureAwait(false);
                }
            }
        }

        private async Task QueueProcessorInternal(QueuedLink link, CancellationToken token)
        {
            try
            {
                // Acquire semaphore before processing
                await _Semaphore.WaitAsync(token).ConfigureAwait(false);

                try
                {
                    Log("processing queued link " + link.Url + " parent " + (!String.IsNullOrEmpty(link.ParentUrl) ? link.ParentUrl : ".") + " depth " + link.Depth);

                    // IMPORTANT: Normalize the URL before checking if it's already visited
                    string normalizedUrl = NormalizeUrl(_Settings.Crawl.StartUrl, link.Url);
                    if (string.IsNullOrEmpty(normalizedUrl))
                    {
                        Log($"unable to normalize queued link {link.Url}");
                        return;
                    }

                    // Update the link URL to use the normalized version
                    link.Url = normalizedUrl;

                    // Now check with the normalized URL
                    Uri uri;
                    try
                    {
                        uri = new Uri(link.Url);
                        if (IsAlreadyVisited(uri))
                        {
                            Log("already visited link " + link.Url);
                            return;
                        }
                    }
                    catch (UriFormatException)
                    {
                        Log($"invalid URI format for normalized URL: {link.Url}");
                        return;
                    }

                    WebResource wr = await RetrieveWebResource(link.Url, link.ParentUrl, link.Depth, token).ConfigureAwait(false);
                    if (wr == null)
                    {
                        Log("unable to retrieve queued link " + link.Url);
                        return;
                    }

                    // Rest of the method continues as before...
                    if (wr.Data != null)
                    {
                        List<string> links = ExtractLinksFromHtml(link.Url, wr.Data);

                        if (_Settings.Crawl.FollowLinks)
                        {
                            if (links != null && links.Count > 0)
                            {
                                if (link.Depth < _Settings.Crawl.MaxCrawlDepth)
                                {
                                    foreach (string curr in links.Distinct())
                                    {
                                        string currTrimmed = curr.Trim();

                                        // The extracted links are already normalized from ExtractLinksFromHtml
                                        // But let's validate they're proper HTTP/HTTPS URLs
                                        if (!currTrimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                                            !currTrimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                                        {
                                            Log($"skipping non-HTTP URL: {currTrimmed}");
                                            continue;
                                        }

                                        Uri childUri;
                                        try
                                        {
                                            childUri = new Uri(currTrimmed);
                                        }
                                        catch
                                        {
                                            Log($"invalid URI format for child link: {currTrimmed}");
                                            continue;
                                        }

                                        if (IsAlreadyVisited(childUri))
                                        {
                                            Log("already visited child link " + currTrimmed);
                                            continue;
                                        }

                                        // Continue with domain/path checks...
                                        if (_Settings.Crawl.RestrictToSameDomain && !IsSameDomain(_Settings.Crawl.StartUrl, currTrimmed))
                                        {
                                            Log("avoiding link not in start URL domain " + currTrimmed);
                                            continue;
                                        }

                                        if (_Settings.Crawl.RestrictToChildUrls && !IsChildUrl(_Settings.Crawl.StartUrl, currTrimmed))
                                        {
                                            Log("avoiding non-child link " + currTrimmed);
                                            continue;
                                        }

                                        if (!IsAllowedDomain(currTrimmed, _Settings.Crawl.AllowedDomains))
                                        {
                                            Log("avoiding disallowed domain in link " + currTrimmed);
                                            continue;
                                        }

                                        if (!_Settings.Crawl.FollowExternalLinks && IsExternalUrl(_Settings.Crawl.StartUrl, currTrimmed))
                                        {
                                            Log("avoiding external link " + currTrimmed);
                                            continue;
                                        }

                                        if (IsUrlExcluded(currTrimmed))
                                        {
                                            Log("avoiding URL " + currTrimmed + " due to match from exclusion list");
                                            continue;
                                        }

                                        Log("adding link " + currTrimmed + " to queue from parent " + link.Url);

                                        // Store the normalized URL in the queue
                                        EnqueueQueuedLink(currTrimmed, link.Url, link.Depth + 1);
                                    }
                                }
                                else
                                {
                                    Log("max depth reached in " + link.Url + ", not recursing into " + links.Count + " links");
                                }
                            }
                        }
                        else
                        {
                            Log("not following links due to settings");
                        }
                    }

                    EnqueueWebResource(wr);
                }
                finally
                {
                    _Semaphore.Release();
                    RemoveProcessingLink(link);
                }
            }
            catch (Exception e)
            {
                Exception?.Invoke(link.Url, e);
                Log("error processing link " + link.Url + Environment.NewLine + e.ToString());
                try { _Semaphore.Release(); } catch { }
                RemoveProcessingLink(link);
            }
        }

        #endregion
    }
}
