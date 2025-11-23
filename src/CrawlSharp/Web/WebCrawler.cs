namespace CrawlSharp.Web
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using CrawlSharp.Helpers;
    using HtmlAgilityPack;
    using Microsoft.Playwright;
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

        private IPlaywright _IPlaywright = null;
        private IBrowser _IBrowser = null;

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

            if (_Settings.Crawl.UseHeadlessBrowser)
            {
                int exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
                if (exitCode != 0) throw new InvalidOperationException("Unable to install Chromium");

                exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "firefox" });
                if (exitCode != 0) throw new InvalidOperationException("Unable to install Firefox");

                _IPlaywright = Playwright.CreateAsync().GetAwaiter().GetResult();
                _IBrowser = _IPlaywright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true
                }).GetAwaiter().GetResult();
            }
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

                    _IBrowser?.CloseAsync().GetAwaiter().GetResult();
                    _IPlaywright?.Dispose();
                }

                _Serializer = null;
                _Semaphore = null;
                _QueuedLinks = null;
                _ProcessingLinks = null;
                _VisitedLinks = null;
                _FinishedLinks = null;
                _IBrowser = null;
                _IPlaywright = null;

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

        private async Task<ContentTypeInfo> CheckContentTypeAsync(string url, CancellationToken token)
        {
            ContentTypeInfo result = new ContentTypeInfo
            {
                IsNavigable = true,  // Default to navigable if check fails
                MediaType = null,
                ContentLength = null,
                CheckSucceeded = false
            };

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd(_Settings.Crawl.UserAgent);
                client.Timeout = TimeSpan.FromSeconds(10);

                var request = new HttpRequestMessage(HttpMethod.Head, url);

                // Add authentication headers if needed
                if (_Settings.Authentication.Type == AuthenticationTypeEnum.ApiKey
                    && !String.IsNullOrEmpty(_Settings.Authentication.ApiKeyHeader))
                {
                    request.Headers.Add(_Settings.Authentication.ApiKeyHeader, _Settings.Authentication.ApiKey);
                }
                else if (_Settings.Authentication.Type == AuthenticationTypeEnum.Basic)
                {
                    var authBytes = Encoding.ASCII.GetBytes($"{_Settings.Authentication.Username}:{_Settings.Authentication.Password}");
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
                }
                else if (_Settings.Authentication.Type == AuthenticationTypeEnum.BearerToken)
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _Settings.Authentication.BearerToken);
                }

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);

                if (response.IsSuccessStatusCode)
                {
                    result.MediaType = response.Content.Headers.ContentType?.MediaType?.ToLower() ?? "";
                    result.ContentLength = response.Content.Headers.ContentLength;
                    result.CheckSucceeded = true;

                    // Determine if content is navigable based on Content-Type header
                    // Only HTML-like content is considered navigable
                    result.IsNavigable = IsNavigableContentType(result.MediaType);
                }
            }
            catch (Exception ex)
            {
                // Log but don't throw - we'll default to navigable
                Log($"Content type check failed for {url}: {ex.Message}");
            }

            return result;
        }

        private bool IsNavigableContentType(string contentType)
        {
            if (string.IsNullOrEmpty(contentType))
                return true; // Default to navigable if no content type

            contentType = contentType.ToLower();

            // Only these content types should be navigated to in a browser
            // Everything else should be downloaded directly
            return contentType.Contains("text/html") ||
                   contentType.Contains("application/xhtml+xml") ||
                   contentType.Contains("application/xml") ||
                   contentType.Contains("text/xml") ||
                   (contentType.Contains("text/plain") && !contentType.Contains("charset")) || // Plain text might be HTML
                   contentType == "text/plain"; // Sometimes HTML is served as text/plain
        }

        private async Task<WebResource> RetrieveWithRestClient(Uri normalizedUri, string parentUrl, int depth, string contentType, CancellationToken token)
        {
            using (RestRequest req = RequestBuilder(normalizedUri.ToString()))
            {
                using (RestResponse resp = await req.SendAsync(token).ConfigureAwait(false))
                {
                    if (resp == null)
                    {
                        Log("unable to retrieve " + normalizedUri);

                        WebResource failedResource = new WebResource
                        {
                            Url = normalizedUri.ToString(),
                            ParentUrl = parentUrl,
                            Depth = depth,
                            Status = 0,
                            ContentType = contentType
                        };

                        AddAlreadyVisited(normalizedUri, failedResource);
                        return failedResource;
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

                                    WebResource invalidRedirectResource = new WebResource
                                    {
                                        Url = normalizedUri.ToString(),
                                        ParentUrl = parentUrl,
                                        Depth = depth,
                                        Status = resp.StatusCode,
                                        ContentType = GetContentTypeFromHeaders(resp.Headers),
                                        Headers = resp.Headers,
                                        Data = resp.DataAsBytes
                                    };

                                    AddAlreadyVisited(normalizedUri, invalidRedirectResource);
                                    return invalidRedirectResource;
                                }

                                if (IsAlreadyVisited(redirectUri))
                                {
                                    Log("redirect to already visited URL " + redirectUri + " from " + normalizedUri);
                                    WebResource alreadyVisited = GetAlreadyVisited(redirectUri);
                                    AddAlreadyVisited(normalizedUri, alreadyVisited);
                                    return alreadyVisited;
                                }

                                Log("following redirect for URL " + normalizedUri + " to " + redirectUri);
                                WebResource redirectedResource = await RetrieveWebResource(redirectUri.ToString(), parentUrl, depth, token).ConfigureAwait(false);

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
                        if (_Settings.Crawl.ThrottleMs > 0)
                            await Task.Delay(_Settings.Crawl.ThrottleMs, token).ConfigureAwait(false);
                    }
                    else
                    {
                        Log("status " + resp.StatusCode + " for URL " + normalizedUri);
                    }

                    WebResource resource = new WebResource
                    {
                        Url = normalizedUri.ToString(),
                        ParentUrl = parentUrl,
                        Depth = depth,
                        Status = resp.StatusCode,
                        ContentType = contentType ?? GetContentTypeFromHeaders(resp.Headers),
                        ETag = GetEtag(resp),
                        MD5Hash = resp.DataAsBytes != null ? Convert.ToHexString(HashHelper.MD5Hash(resp.DataAsBytes)) : null,
                        SHA1Hash = resp.DataAsBytes != null ? Convert.ToHexString(HashHelper.SHA1Hash(resp.DataAsBytes)) : null,
                        SHA256Hash = resp.DataAsBytes != null ? Convert.ToHexString(HashHelper.SHA256Hash(resp.DataAsBytes)) : null,
                        Headers = resp.Headers,
                        Data = resp.DataAsBytes
                    };

                    AddAlreadyVisited(normalizedUri, resource);
                    return resource;
                }
            }
        }

        private async Task<WebResource> RetrieveWithPlaywright(Uri normalizedUri, string parentUrl, int depth, string contentType, CancellationToken token)
        {
            await using var context = await _IBrowser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = _Settings.Crawl.UserAgent ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
                Locale = "en-US",
                TimezoneId = "America/New_York",
                AcceptDownloads = false
            });

            var page = await context.NewPageAsync();

            // Track if a download was initiated
            bool downloadInitiated = false;
            page.Download += (sender, e) =>
            {
                downloadInitiated = true;
            };

            try
            {
                IResponse response = null;

                try
                {
                    response = await page.GotoAsync(normalizedUri.ToString(), new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.Load,
                        Timeout = 30000
                    });
                }
                catch (PlaywrightException ex) when (ex.Message.Contains("Download is starting"))
                {
                    // Download was triggered, fall back to REST client
                    Log("Download triggered for " + normalizedUri + ", using REST client");
                    return await RetrieveWithRestClient(normalizedUri, parentUrl, depth, contentType, token);
                }

                // Check if download was initiated during navigation
                if (downloadInitiated)
                {
                    Log("Download initiated for " + normalizedUri + ", using REST client");
                    return await RetrieveWithRestClient(normalizedUri, parentUrl, depth, contentType, token);
                }

                if (response == null)
                {
                    Log("No response received for " + normalizedUri);
                    return await RetrieveWithRestClient(normalizedUri, parentUrl, depth, contentType, token);
                }

                if (response.Status == 429)
                {
                    Log("throttle status " + response.Status + " for " + normalizedUri);
                    if (_Settings.Crawl.ThrottleMs > 0)
                        await Task.Delay(_Settings.Crawl.ThrottleMs, token).ConfigureAwait(false);
                }
                else
                {
                    Log("status " + response.Status + " for URL " + normalizedUri);
                }

                string content = await page.ContentAsync();
                var headers = await response.AllHeadersAsync();

                NameValueCollection headerCollection = new NameValueCollection();
                foreach (var header in headers)
                {
                    headerCollection.Add(header.Key, header.Value);
                }

                WebResource resource = new WebResource
                {
                    Url = normalizedUri.ToString(),
                    ParentUrl = parentUrl,
                    Depth = depth,
                    Status = response.Status,
                    ContentType = contentType ?? GetContentTypeFromHeaders(headerCollection),
                    ETag = headers.ContainsKey("etag") ? headers["etag"] : null,
                    MD5Hash = !String.IsNullOrEmpty(content) ? Convert.ToHexString(HashHelper.MD5Hash(content)) : null,
                    SHA1Hash = !String.IsNullOrEmpty(content) ? Convert.ToHexString(HashHelper.SHA1Hash(content)) : null,
                    SHA256Hash = !String.IsNullOrEmpty(content) ? Convert.ToHexString(HashHelper.SHA256Hash(content)) : null,
                    Headers = headerCollection,
                    Data = !String.IsNullOrEmpty(content) ? Encoding.UTF8.GetBytes(content) : Array.Empty<byte>()
                };

                AddAlreadyVisited(normalizedUri, resource);
                return resource;
            }
            finally
            {
                if (page != null && !page.IsClosed)
                {
                    await page.CloseAsync();
                }
            }
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

                if (!fullUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !fullUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"URL does not start with http/https: {fullUrl} (original: {url})");
                    return null;
                }

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

                // Check content type to determine retrieval method
                ContentTypeInfo contentInfo = null;
                if (_Settings.Crawl.UseHeadlessBrowser)
                {
                    contentInfo = await CheckContentTypeAsync(normalizedUri.ToString(), token);

                    if (contentInfo.CheckSucceeded)
                    {
                        Log($"content type check for {normalizedUri}: {contentInfo.MediaType} navigable {contentInfo.IsNavigable}");
                    }
                }

                // Decide which method to use for retrieval
                bool usePlaywright = _Settings.Crawl.UseHeadlessBrowser &&
                                    (contentInfo == null || contentInfo.IsNavigable);

                if (usePlaywright)
                {
                    return await RetrieveWithPlaywright(normalizedUri, parentUrl, depth, contentInfo?.MediaType, token);
                }
                else
                {
                    return await RetrieveWithRestClient(normalizedUri, parentUrl, depth, contentInfo?.MediaType, token);
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

        private string GetContentTypeFromHeaders(NameValueCollection headers)
        {
            if (headers == null) return null;

            string contentType = headers["Content-Type"];
            if (string.IsNullOrEmpty(contentType)) return null;

            // Extract just the media type, ignoring charset and other parameters
            int semicolonIndex = contentType.IndexOf(';');
            return semicolonIndex > 0
                ? contentType.Substring(0, semicolonIndex).Trim().ToLower()
                : contentType.Trim().ToLower();
        }

        private async Task RetrieveRobotsFile(string baseUrl, CancellationToken token = default)
        {
            if (_Settings.Crawl.IgnoreRobotsText)
            {
                Log("skipping retrieval and processing of robots.txt due to settings");
                return;
            }

            if (String.IsNullOrEmpty(baseUrl)) return;

            // CHANGE: Use domain root instead of the starting URL
            string domainRoot = GetDomainRoot(baseUrl);
            string robotsFile = domainRoot + "/robots.txt";

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

            // CHANGE: Use domain root instead of the starting URL
            string domainRoot = GetDomainRoot(baseUrl);
            string sitemapUrl = domainRoot + "/sitemap.xml";

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
                Log("unable to retrieve sitemap.xml from " + sitemapUrl);  // CHANGE: Fixed log message
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
                string content = Encoding.UTF8.GetString(bytes);

                // Quick validation that this is actually HTML-like content
                string trimmedContent = content.TrimStart();
                bool looksLikeHtml = trimmedContent.StartsWith("<", StringComparison.OrdinalIgnoreCase) ||
                                     trimmedContent.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                                     trimmedContent.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
                                     trimmedContent.Contains("<body", StringComparison.OrdinalIgnoreCase) ||
                                     trimmedContent.Contains("<head", StringComparison.OrdinalIgnoreCase);

                if (!looksLikeHtml)
                {
                    return new List<string>();
                }

                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(content);
                HtmlNodeCollection linkNodes = doc.DocumentNode.SelectNodes("//a[@href]");
                List<string> links = new List<string>();

                if (linkNodes != null)
                {
                    foreach (var link in linkNodes)
                    {
                        string href = link.GetAttributeValue("href", string.Empty);
                        if (!string.IsNullOrWhiteSpace(href))
                        {
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
                return new List<string>();
            }
        }

        private string NormalizeUrl(string baseUrl, string relativeUrl)
        {
            if (string.IsNullOrWhiteSpace(relativeUrl))
                return null;

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

                if (relativeUrl.StartsWith("//"))
                {
                    result = baseUri.Scheme + ":" + relativeUrl;
                }
                else if (relativeUrl.StartsWith("/"))
                {
                    result = $"{baseUri.Scheme}://{baseUri.Host}";
                    if (!baseUri.IsDefaultPort)
                    {
                        result += $":{baseUri.Port}";
                    }
                    result += relativeUrl;
                }
                else if (relativeUrl.StartsWith("?"))
                {
                    result = baseUri.GetLeftPart(UriPartial.Path) + relativeUrl;
                }
                else if (relativeUrl.StartsWith("#"))
                {
                    result = baseUri.GetLeftPart(UriPartial.Query);
                }
                else
                {
                    Uri combined = new Uri(baseUri, relativeUrl);
                    result = combined.ToString();
                }

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
            if (string.IsNullOrWhiteSpace(testUrl))
                return false;

            if (testUrl.StartsWith("/") || testUrl.StartsWith("~/") ||
                testUrl.StartsWith("./") || testUrl.StartsWith("../"))
                return false;

            if (testUrl.StartsWith("#") || testUrl.StartsWith("?"))
                return false;

            Uri tempBaseUri;
            try
            {
                tempBaseUri = new Uri(baseUrl);
            }
            catch (UriFormatException)
            {
                return true;
            }

            if (!testUrl.Contains("://") && !testUrl.StartsWith("//"))
            {
                testUrl = $"{tempBaseUri.Scheme}://{testUrl}";
            }
            else if (testUrl.StartsWith("//"))
            {
                testUrl = $"{tempBaseUri.Scheme}:{testUrl}";
            }

            Uri testUri;
            try
            {
                testUri = new Uri(testUrl);
            }
            catch (UriFormatException)
            {
                return true;
            }

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
                    continue;
                }
            }

            return false;
        }

        private bool IsSameSubdomain(string url1, string url2)
        {
            if (string.IsNullOrWhiteSpace(url1) || string.IsNullOrWhiteSpace(url2)) return false;

            try
            {
                Uri url1Uri = new Uri(url1);
                if (url2.StartsWith("/"))
                {
                    return true;
                }
                else if (url2.StartsWith("./") || url2.StartsWith("../") ||
                        (!url2.Contains("://") && !url2.StartsWith("//")))
                {
                    return true;
                }

                if (url2.StartsWith("//"))
                {
                    url2 = $"{url1Uri.Scheme}:{url2}";
                }

                Uri url2Uri = new Uri(url2);
                return string.Equals(url1Uri.Host, url2Uri.Host, StringComparison.OrdinalIgnoreCase);
            }
            catch (UriFormatException)
            {
                return false;
            }
        }

        private bool IsSameRootDomain(string url1, string url2)
        {
            if (string.IsNullOrWhiteSpace(url1) || string.IsNullOrWhiteSpace(url2)) return false;
            try
            {
                Uri url1Uri = new Uri(url1);

                // Handle relative URLs - they're always in the same root domain
                if (url2.StartsWith("/")) return true;
                else if (url2.StartsWith("./") || url2.StartsWith("../") ||
                        (!url2.Contains("://") && !url2.StartsWith("//")))
                {
                    return true;
                }

                // Handle protocol-relative URLs
                if (url2.StartsWith("//"))
                {
                    url2 = $"{url1Uri.Scheme}:{url2}";
                }

                Uri url2Uri = new Uri(url2);

                string host1 = url1Uri.Host.ToLowerInvariant();
                string host2 = url2Uri.Host.ToLowerInvariant();

                // Check if they're the same host
                if (host1 == host2)
                    return true;

                // Check if one is a subdomain of the other
                // url2 is under url1's domain
                if (host2.EndsWith("." + host1, StringComparison.OrdinalIgnoreCase))
                    return true;

                // url1 is under url2's domain  
                if (host1.EndsWith("." + host2, StringComparison.OrdinalIgnoreCase))
                    return true;

                // Without a public suffix list, we can't reliably determine if two different
                // domains share the same root domain (e.g., sub1.example.com and sub2.example.com)
                // This is the limitation of not using the library
                return false;
            }
            catch (UriFormatException)
            {
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
                if (baseUrl.StartsWith("//")) baseUrl = "http:" + baseUrl;

                Uri uri = new Uri(baseUrl);
                string domain = uri.Host.ToLowerInvariant();

                return allowedDomains.Any(d => string.Equals(d, domain, StringComparison.OrdinalIgnoreCase));
            }
            catch (UriFormatException)
            {
                return false;
            }
        }

        private bool IsDeniedDomain(string baseUrl, List<string> deniedDomains)
        {
            if (deniedDomains == null || deniedDomains.Count < 1) return false;

            if (String.IsNullOrEmpty(baseUrl))
            {
                Log("checking denied domains and received an empty base URL");
                return true;
            }

            if (!baseUrl.Contains("://") && !baseUrl.StartsWith("//")) return false;
            if (baseUrl.StartsWith("./")) return false;

            try
            {
                if (baseUrl.StartsWith("//")) baseUrl = "http:" + baseUrl;
                Uri uri = new Uri(baseUrl);
                string domain = uri.Host.ToLowerInvariant();
                return deniedDomains.Any(d => string.Equals(d, domain, StringComparison.OrdinalIgnoreCase));
            }
            catch (UriFormatException)
            {
                return true;
            }
        }

        private bool IsChildUrl(string baseUrl, string testUrl)
        {
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
                    testUrl = new Uri(baseUriObj, testUrl).ToString();
                }
                else if (!testUrl.Contains("://") && !testUrl.StartsWith("//"))
                {
                    testUrl = new Uri(baseUriObj, testUrl).ToString();
                }

                string normalizedBase = baseUrl.TrimEnd('/') + "/";
                string normalizedTest = testUrl.TrimEnd('/') + "/";

                Uri basePathUri = new Uri(normalizedBase);
                Uri testUri = new Uri(normalizedTest);

                if (!string.Equals(basePathUri.Host, testUri.Host, StringComparison.OrdinalIgnoreCase)) return false;

                string basePath = basePathUri.AbsolutePath;
                string testPath = testUri.AbsolutePath;

                if (basePath.Equals("/"))
                    return true;

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
                await _Semaphore.WaitAsync(token).ConfigureAwait(false);

                try
                {
                    Log("processing queued link " + link.Url + " parent " + (!String.IsNullOrEmpty(link.ParentUrl) ? link.ParentUrl : ".") + " depth " + link.Depth);

                    string normalizedUrl = NormalizeUrl(_Settings.Crawl.StartUrl, link.Url);
                    if (string.IsNullOrEmpty(normalizedUrl))
                    {
                        Log($"unable to normalize queued link {link.Url}");
                        return;
                    }

                    link.Url = normalizedUrl;

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

                    if (wr.Data != null && IsNavigableContentType(wr.ContentType))
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

                                        if (!currTrimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                                            !currTrimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                                        {
                                            Log($"skipping non-HTTP URL: {currTrimmed}");
                                            continue;
                                        }

                                        if (IsDeniedDomain(currTrimmed, _Settings.Crawl.DeniedDomains))
                                        {
                                            Log("avoiding denied domain in link " + currTrimmed);
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

                                        if (_Settings.Crawl.RestrictToSameRootDomain && !IsSameRootDomain(_Settings.Crawl.StartUrl, currTrimmed))
                                        {
                                            Log("avoiding link not in root domain " + currTrimmed);
                                            continue;
                                        }

                                        if (_Settings.Crawl.RestrictToSameSubdomain && !IsSameSubdomain(_Settings.Crawl.StartUrl, currTrimmed))
                                        {
                                            Log("avoiding link not in subdomain " + currTrimmed);
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
        
        private string GetDomainRoot(string url)
        {
            try
            {
                Uri uri = new Uri(url);
                return $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : ":" + uri.Port)}";
            }
            catch
            {
                return url;
            }
        }

        #endregion
    }
}