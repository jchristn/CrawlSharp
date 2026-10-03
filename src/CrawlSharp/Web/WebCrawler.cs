namespace CrawlSharp.Web
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using CrawlSharp.Helpers;
    using CrawlSharp.Telemetry;
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
        private HashSet<WebResource> _YieldedResources = new HashSet<WebResource>(ReferenceEqualityComparer.Instance);

        private HashSet<string> _CredentialOrigins = new HashSet<string>(StringComparer.Ordinal);
        private string _StartHost = null;

        private IPlaywright _IPlaywright = null;
        private IBrowser _IBrowser = null;

        private readonly Random _RetryJitter = new Random();
        private readonly string[] _BuiltInExpansionSelectors = new[]
        {
            "button[aria-expanded='false'][aria-controls]",
            "[data-bs-toggle='collapse']",
            "[data-toggle='collapse']",
            "button.accordion-button.collapsed",
            "[role='button'][aria-controls][aria-expanded='false']"
        };

        private CancellationToken _Token;
        private Task _QueueProcessor = null;

        private readonly string _Mode;
        private readonly object _TelemetryLock = new object();
        private Activity _CrawlActivity = null;
        private long _CrawlStartTimestamp = 0;
        private bool _CrawlTelemetryOpen = false;
        private int _ResourcesYielded = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Web crawler.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the authentication settings are incomplete or ambiguous; see <see cref="AuthenticationSettings.Validate"/>.</exception>
        public WebCrawler(Settings settings, CancellationToken token = default)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Settings.Authentication.Validate();
            BuildCredentialScope();

            _Semaphore = new SemaphoreSlim(_Settings.Crawl.MaxParallelTasks, _Settings.Crawl.MaxParallelTasks);
            _Token = token;
            _DelayMilliseconds = _Settings.Crawl.RequestDelayMs;
            _Mode = _Settings.Crawl.UseHeadlessBrowser ? CrawlSharpTelemetry.ModeHeadless : CrawlSharpTelemetry.ModeRest;

            if (_Settings.Crawl.UseHeadlessBrowser) StartBrowser();
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

                    EndCrawlTelemetry(CrawlSharpTelemetry.OutcomeAbandoned, null);
                    ReleaseTelemetryState();

                    _Semaphore?.Dispose();
                    _QueuedLinks?.Clear();
                    _ProcessingLinks?.Clear();
                    _VisitedLinks?.Clear();
                    _FinishedLinks?.Clear();
                    _YieldedResources?.Clear();

                    _IBrowser?.CloseAsync().GetAwaiter().GetResult();
                    _IPlaywright?.Dispose();
                }

                _Serializer = null;
                _Semaphore = null;
                _QueuedLinks = null;
                _ProcessingLinks = null;
                _VisitedLinks = null;
                _FinishedLinks = null;
                _YieldedResources = null;
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
            BeginCrawlTelemetry();
            bool completed = false;
            Exception failure = null;

            try
            {
                #region Process-Robots-and-Sitemap

                try
                {
                    Task robotsFile = RetrieveRobotsFile(_Settings.Crawl.StartUrl, _Token);
                    robotsFile.Wait();

                    ApplyRobotsCrawlDelay();

                    Task processSitemap = ProcessSitemap(_Settings.Crawl.StartUrl, _Token);
                    processSitemap.Wait();
                }
                catch (Exception e)
                {
                    failure = e;
                    throw;
                }

                #endregion

                #region Enqueue-Root-Url

                EnqueueQueuedLink(_Settings.Crawl.StartUrl, null, 0, CrawlSharpTelemetry.LinkSourceStart);

                #endregion

                #region Start-Queue-Processor

                _QueueProcessor = Task.Run(() => QueueProcessor(_Token), _Token);

                while (!_QueueProcessor.IsCompleted)
                {
                    Task.Delay(10, _Token).Wait();
                    WebResource wr = DequeueWebResource();
                    if (wr != null)
                    {
                        Interlocked.Increment(ref _ResourcesYielded);
                        yield return wr;
                    }
                }

                #endregion

                #region Drain-the-Queue

                while (true)
                {
                    WebResource wr = DequeueWebResource();
                    if (wr == null) break;
                    Interlocked.Increment(ref _ResourcesYielded);
                    yield return wr;
                }

                #endregion

                failure = GetQueueProcessorFailure();
                completed = true;
            }
            finally
            {
                EndCrawlTelemetry(GetCrawlOutcome(completed, failure, _Token), failure);
            }
        }

        /// <summary>
        /// Crawl using the server and configuration defined in the supplied settings.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Enumerable of WebResource objects.</returns>
        public async IAsyncEnumerable<WebResource> CrawlAsync([EnumeratorCancellation] CancellationToken token = default)
        {
            CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(_Token, token);

            BeginCrawlTelemetry();
            bool completed = false;
            Exception failure = null;

            try
            {
                #region Process-Robots-and-Sitemap

                try
                {
                    await RetrieveRobotsFile(_Settings.Crawl.StartUrl, cts.Token).ConfigureAwait(false);
                    ApplyRobotsCrawlDelay();
                    await ProcessSitemap(_Settings.Crawl.StartUrl, cts.Token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    failure = e;
                    throw;
                }

                #endregion

                #region Enqueue-Root-Url

                EnqueueQueuedLink(_Settings.Crawl.StartUrl, null, 0, CrawlSharpTelemetry.LinkSourceStart);

                #endregion

                #region Start-Queue-Processor

                _QueueProcessor = Task.Run(() => QueueProcessor(cts.Token), cts.Token);

                while (!_QueueProcessor.IsCompleted)
                {
                    await Task.Delay(10, cts.Token).ConfigureAwait(false);

                    WebResource wr = DequeueWebResource();
                    if (wr != null)
                    {
                        Interlocked.Increment(ref _ResourcesYielded);
                        yield return wr;
                    }
                }

                #endregion

                #region Drain-the-Queue

                while (true)
                {
                    WebResource wr = DequeueWebResource();
                    if (wr == null) break;
                    Interlocked.Increment(ref _ResourcesYielded);
                    yield return wr;
                }

                #endregion

                failure = GetQueueProcessorFailure();
                completed = true;
            }
            finally
            {
                EndCrawlTelemetry(GetCrawlOutcome(completed, failure, cts.Token), failure);
            }
        }

        #endregion

        #region Private-Methods

        private async Task DelayForRetry(Uri uri, int attempt, string service, CancellationToken token)
        {
            int delay = (int)Math.Min(
                _Settings.Crawl.RetryMinBackoffMs * Math.Pow(2, attempt),
                _Settings.Crawl.RetryMaxBackoffMs);

            if (_Settings.Crawl.RetryBackoffJitter)
            {
                lock (_RetryJitter)
                {
                    delay = _RetryJitter.Next(0, delay + 1);
                }
            }

            Log("429 retry attempt " + (attempt + 1) + "/" + _Settings.Crawl.MaxRetries + " for " + uri + ", backing off " + delay + "ms");
            CrawlInstruments.RecordRetry(service, CrawlSharpTelemetry.RetryReasonThrottled);
            await TimedDelay(CrawlSharpTelemetry.StageRetryBackoff, delay, token).ConfigureAwait(false);
        }

        private async Task TimedDelay(string stage, int delayMs, CancellationToken token)
        {
            if (delayMs <= 0) return;

            using (StageScope scope = new StageScope(stage))
            {
                scope.SetTag(CrawlSharpTelemetry.AttributeDelayMs, delayMs);

                try
                {
                    await Task.Delay(delayMs, token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    scope.Fail(e);
                    throw;
                }
            }
        }

        private async Task DelayIfNeeded(int delayMs, CancellationToken token)
        {
            if (delayMs <= 0) return;
            await Task.Delay(delayMs, token).ConfigureAwait(false);
        }

        private void Log(string msg)
        {
            if (String.IsNullOrEmpty(msg)) return;
            Logger?.Invoke(_Header + msg);
        }

        #region Telemetry

        private ActivityContext CrawlParentContext
        {
            get
            {
                Activity activity = _CrawlActivity;
                return activity != null ? activity.Context : default;
            }
        }

        private void BeginCrawlTelemetry()
        {
            Activity activity;

            lock (_TelemetryLock)
            {
                if (_CrawlTelemetryOpen) return;
                _CrawlTelemetryOpen = true;
                _ResourcesYielded = 0;
                _CrawlStartTimestamp = Stopwatch.GetTimestamp();
                _CrawlActivity = CrawlInstruments.StartDetachedActivity(CrawlSharpTelemetry.SpanCrawl, ActivityKind.Internal);
                activity = _CrawlActivity;
            }

            CrawlInstruments.SetTag(activity, CrawlSharpTelemetry.AttributeMode, _Mode);
            CrawlInstruments.SetUrl(activity, _Settings.Crawl.StartUrl);
            CrawlInstruments.SetTag(activity, CrawlSharpTelemetry.AttributeMaxDepth, _Settings.Crawl.MaxCrawlDepth);
            CrawlInstruments.SetTag(activity, CrawlSharpTelemetry.AttributeMaxParallelTasks, _Settings.Crawl.MaxParallelTasks);
            CrawlInstruments.SetTag(activity, CrawlSharpTelemetry.AttributeFollowLinks, _Settings.Crawl.FollowLinks);

            CrawlInstruments.Add(CrawlInstruments.CrawlActive, 1, CrawlSharpTelemetry.AttributeMode, _Mode);
            CrawlInstruments.Add(CrawlInstruments.WorkersCapacity, _Settings.Crawl.MaxParallelTasks);
        }

        private void EndCrawlTelemetry(string outcome, Exception failure)
        {
            Activity activity;
            long startTimestamp;

            lock (_TelemetryLock)
            {
                if (!_CrawlTelemetryOpen) return;
                _CrawlTelemetryOpen = false;
                activity = _CrawlActivity;
                startTimestamp = _CrawlStartTimestamp;
                _CrawlActivity = null;
            }

            CrawlInstruments.RecordCrawl(_Mode, outcome, CrawlInstruments.ElapsedSeconds(startTimestamp));
            CrawlInstruments.Add(CrawlInstruments.CrawlActive, -1, CrawlSharpTelemetry.AttributeMode, _Mode);
            CrawlInstruments.Add(CrawlInstruments.WorkersCapacity, -_Settings.Crawl.MaxParallelTasks);

            CrawlInstruments.SetTag(activity, CrawlSharpTelemetry.AttributeResourceCount, Volatile.Read(ref _ResourcesYielded));

            if (outcome == CrawlSharpTelemetry.OutcomeFailure)
            {
                CrawlInstruments.RecordError(CrawlSharpTelemetry.SpanCrawl, failure);
                CrawlInstruments.SetError(activity, failure);
            }

            CrawlInstruments.SetOutcomeStatus(activity, outcome);
            CrawlInstruments.StopActivity(activity, Activity.Current);
        }

        private static string GetCrawlOutcome(bool completed, Exception failure, CancellationToken token)
        {
            if (failure != null)
            {
                Exception inner = failure is AggregateException ae && ae.InnerException != null ? ae.InnerException : failure;
                return inner is OperationCanceledException ? CrawlSharpTelemetry.OutcomeCancelled : CrawlSharpTelemetry.OutcomeFailure;
            }

            if (completed) return CrawlSharpTelemetry.OutcomeCompleted;
            if (token.IsCancellationRequested) return CrawlSharpTelemetry.OutcomeCancelled;
            return CrawlSharpTelemetry.OutcomeAbandoned;
        }

        private Exception GetQueueProcessorFailure()
        {
            Task processor = _QueueProcessor;
            if (processor == null || !processor.IsFaulted || processor.Exception == null) return null;

            Exception e = processor.Exception.GetBaseException();
            Exception?.Invoke(_Settings.Crawl.StartUrl, e);
            Log("queue processor failed" + Environment.NewLine + e.ToString());
            return e;
        }

        private void ReleaseTelemetryState()
        {
            // Return what this crawler still holds to the process-wide gauges, so a disposed crawler leaves no residue.
            try
            {
                if (_QueuedLinks != null) lock (_QueuedLinksLock) CrawlInstruments.Add(CrawlInstruments.QueueSize, -_QueuedLinks.Count);
                if (_FinishedLinks != null) lock (_FinishedLinksLock) CrawlInstruments.Add(CrawlInstruments.ResultsBuffered, -_FinishedLinks.Count);
                if (_VisitedLinks != null) lock (_VisitedLinksLock) CrawlInstruments.Add(CrawlInstruments.VisitedSize, -_VisitedLinks.Count);
            }
            catch (Exception)
            {
            }
        }

        private static string GetScopeReasonCode(string reason)
        {
            switch (reason)
            {
                case "domain is denied": return CrawlSharpTelemetry.ReasonDeniedDomain;
                case "not in the start URL's root domain": return CrawlSharpTelemetry.ReasonOutsideRootDomain;
                case "not in the start URL's subdomain": return CrawlSharpTelemetry.ReasonOutsideSubdomain;
                case "not a child of the start URL": return CrawlSharpTelemetry.ReasonNotChildUrl;
                case "domain is not in the allowed list": return CrawlSharpTelemetry.ReasonNotAllowedDomain;
                case "external link": return CrawlSharpTelemetry.ReasonExternal;
                case "matches an exclusion pattern": return CrawlSharpTelemetry.ReasonExcluded;
                default: return "out_of_scope";
            }
        }

        private static string GetRedirectOutcomeLabel(RedirectOutcomeEnum outcome)
        {
            switch (outcome)
            {
                case RedirectOutcomeEnum.Followed: return "followed";
                case RedirectOutcomeEnum.NotFollowed: return "not_followed";
                case RedirectOutcomeEnum.LoopDetected: return "loop_detected";
                case RedirectOutcomeEnum.MaxRedirectsExceeded: return "max_redirects_exceeded";
                case RedirectOutcomeEnum.OutOfScope: return "out_of_scope";
                case RedirectOutcomeEnum.RobotsDisallowed: return "robots_disallowed";
                case RedirectOutcomeEnum.MissingLocation: return "missing_location";
                case RedirectOutcomeEnum.InvalidLocation: return "invalid_location";
                default: return "none";
            }
        }

        private void RecordPageResult(WebResource wr)
        {
            bool isContent = wr.RedirectOutcome == RedirectOutcomeEnum.None || wr.RedirectOutcome == RedirectOutcomeEnum.Followed;
            string outcome;

            if (!isContent) outcome = CrawlSharpTelemetry.OutcomeRedirectStopped;
            else if (wr.Status >= 400) outcome = CrawlSharpTelemetry.OutcomeHttpError;
            else if (wr.Status < 100) outcome = CrawlSharpTelemetry.OutcomeFailure;
            else outcome = CrawlSharpTelemetry.OutcomeSuccess;

            CrawlInstruments.RecordPage(_Mode, outcome, wr.Status, wr.Data != null ? wr.Data.LongLength : 0);

            int hops = wr.RedirectChain != null ? wr.RedirectChain.Count : 0;
            if (wr.RedirectOutcome != RedirectOutcomeEnum.None || hops > 0)
                CrawlInstruments.RecordRedirect(GetRedirectOutcomeLabel(wr.RedirectOutcome), hops);
        }

        private void StartBrowser()
        {
            using (StageScope stage = new StageScope(CrawlSharpTelemetry.StageBrowserStartup))
            {
                try
                {
                    using (IntegrationScope install = new IntegrationScope(CrawlSharpTelemetry.ServicePlaywright, CrawlSharpTelemetry.OperationInstall))
                    {
                        int exitCode;

                        try
                        {
                            exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "firefox" });
                        }
                        catch (Exception e)
                        {
                            install.Fail(e);
                            throw;
                        }

                        if (exitCode != 0)
                        {
                            InvalidOperationException ioe = new InvalidOperationException("Unable to install Firefox");
                            install.Fail(ioe);
                            throw ioe;
                        }

                        install.SetOutcome(CrawlSharpTelemetry.OutcomeSuccess);
                    }

                    using (IntegrationScope launch = new IntegrationScope(CrawlSharpTelemetry.ServicePlaywright, CrawlSharpTelemetry.OperationLaunch))
                    {
                        try
                        {
                            _IPlaywright = Playwright.CreateAsync().GetAwaiter().GetResult();
                            _IBrowser = _IPlaywright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions
                            {
                                Headless = true
                            }).GetAwaiter().GetResult();

                            launch.SetOutcome(CrawlSharpTelemetry.OutcomeSuccess);
                        }
                        catch (Exception e)
                        {
                            launch.Fail(e);
                            throw;
                        }
                    }
                }
                catch (Exception e)
                {
                    stage.Fail(e);
                    throw;
                }
            }
        }

        private void ApplyRobotsCrawlDelay()
        {
            if (_RobotsFile == null) return;

            decimal crawlDelay = _RobotsFile.GetCrawlDelay(_Settings.Crawl.UserAgent);
            if (crawlDelay > 0)
            {
                _DelayMilliseconds = (int)(crawlDelay * 1000);
                Log("crawl delay set to " + _DelayMilliseconds + "ms per robots.txt");
            }
        }

        #endregion

        private bool IsAutoExpandEnabled(string contentType)
        {
            if (_Settings == null || _Settings.Crawl == null) return false;
            if (!_Settings.Crawl.UseHeadlessBrowser || !_Settings.Crawl.AutoExpandCollapsibles) return false;
            if (String.IsNullOrEmpty(contentType)) return true;

            contentType = contentType.ToLowerInvariant();
            return contentType.Contains("text/html") || contentType.Contains("application/xhtml+xml");
        }

        #region Credentials

        private void BuildCredentialScope()
        {
            _CredentialOrigins.Clear();

            Uri startUri = null;
            if (!String.IsNullOrEmpty(_Settings.Crawl.StartUrl)
                && Uri.TryCreate(_Settings.Crawl.StartUrl, UriKind.Absolute, out startUri))
            {
                _StartHost = startUri.Host;
            }

            if (_Settings.Authentication.Type == AuthenticationTypeEnum.None) return;

            if (startUri != null)
            {
                _CredentialOrigins.Add(RedirectPolicy.GetOrigin(startUri));

                // An HTTP start URL also trusts its HTTPS upgrade on the default port, the most common redirect there is.
                // An HTTPS start URL never trusts its HTTP twin, so a downgrade never carries credentials.
                if (startUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
                {
                    UriBuilder upgrade = new UriBuilder(startUri);
                    upgrade.Scheme = Uri.UriSchemeHttps;
                    upgrade.Port = 443;
                    _CredentialOrigins.Add(RedirectPolicy.GetOrigin(upgrade.Uri));
                }
            }

            foreach (string origin in _Settings.Authentication.CredentialOrigins)
            {
                _CredentialOrigins.Add(RedirectPolicy.GetOrigin(new Uri(origin.Trim(), UriKind.Absolute)));
            }
        }

        private bool IsInCredentialScope(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri || _CredentialOrigins.Count < 1) return false;
            return _CredentialOrigins.Contains(RedirectPolicy.GetOrigin(uri));
        }

        private bool TryGetCredentialHeader(out string name, out string value)
        {
            name = null;
            value = null;

            switch (_Settings.Authentication.Type)
            {
                case AuthenticationTypeEnum.Basic:
                    name = "Authorization";
                    value = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(
                        _Settings.Authentication.Username + ":" + (_Settings.Authentication.Password ?? String.Empty)));
                    return true;
                case AuthenticationTypeEnum.BearerToken:
                    name = "Authorization";
                    value = "Bearer " + _Settings.Authentication.BearerToken;
                    return true;
                case AuthenticationTypeEnum.ApiKey:
                    name = _Settings.Authentication.ApiKeyHeader;
                    value = _Settings.Authentication.ApiKey;
                    return true;
                default:
                    return false;
            }
        }

        #endregion

        #region Redirect-Resolution

        /// <summary>
        /// Request a URL and follow its redirect chain one hop at a time.  The HTTP stack never follows redirects on its own,
        /// so this loop decides every hop: the limit, loop detection, crawl scope, robots.txt, pacing and which requests carry credentials.
        /// </summary>
        /// <param name="requestUri">URL to request.</param>
        /// <param name="method">GET, or HEAD for the headless content-type check.</param>
        /// <param name="sameHostOnly">True for robots.txt and sitemap.xml, whose redirects are followed only on the start URL's host.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The last response received and why the chain stopped.</returns>
        private async Task<ResolvedResponse> ResolveAsync(Uri requestUri, HttpMethod method, bool sameHostOnly, CancellationToken token)
        {
            ResolvedResponse result = new ResolvedResponse
            {
                RequestedUri = requestUri,
                FinalUri = requestUri
            };

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            CookieContainer cookies = new CookieContainer();

            // Crawled sites are third parties, so no W3C trace context (traceparent/tracestate) is sent to them.
            using (SocketsHttpHandler handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = true,
                CookieContainer = cookies,
                ActivityHeadersPropagator = DistributedContextPropagator.CreateNoOutputPropagator()
            })
            using (HttpClient client = new HttpClient(handler, false))
            {
                client.Timeout = TimeSpan.FromMilliseconds(_Settings.Crawl.PageTimeoutMs);
                Uri current = requestUri;

                while (true)
                {
                    token.ThrowIfCancellationRequested();

                    // Key on URL plus the cookies the chain would send, so a site that sets a cookie and redirects back to the
                    // same URL (a consent wall or session bootstrap) can still make progress, while a true cycle stops on its first repeat.
                    string seenKey = current.AbsoluteUri + "\n" + cookies.GetCookieHeader(current);
                    if (!seen.Add(seenKey))
                    {
                        Log("redirect loop detected at " + current + " while retrieving " + requestUri);
                        result.Outcome = RedirectOutcomeEnum.LoopDetected;
                        return result;
                    }

                    if (result.Chain.Count > 0)
                    {
                        // Checks for every hop after the first; RetrieveWebResource checks the first request.
                        string reason;
                        if (!IsRedirectTargetInScope(current, sameHostOnly, out reason))
                        {
                            Log("not following redirect from " + result.FinalUri + " to " + current + ", " + reason);
                            result.Outcome = RedirectOutcomeEnum.OutOfScope;
                            return result;
                        }

                        if (!_RobotsFile.IsPathAllowed(_Settings.Crawl.UserAgent, current.AbsolutePath))
                        {
                            Log("not following redirect from " + result.FinalUri + " to " + current + ", prohibited by robots.txt");
                            result.Outcome = RedirectOutcomeEnum.RobotsDisallowed;
                            return result;
                        }

                        if (IsAlreadyVisited(current))
                        {
                            Log("redirect from " + result.FinalUri + " reaches already visited URL " + current);
                            result.AliasOf = GetAlreadyVisited(current);
                            result.Outcome = RedirectOutcomeEnum.Followed;
                            return result;
                        }

                        await Pause(token).ConfigureAwait(false);
                        Log("following redirect to " + current);
                    }

                    await SendHopAsync(client, current, method, result, token).ConfigureAwait(false);

                    string location = result.Headers != null ? result.Headers["Location"] : null;
                    bool hasLocation = !String.IsNullOrWhiteSpace(location);

                    if (!RedirectPolicy.IsRedirectStatus(result.Status, hasLocation))
                    {
                        result.Outcome = result.Chain.Count > 0 ? RedirectOutcomeEnum.Followed : RedirectOutcomeEnum.None;
                        return result;
                    }

                    if (!_Settings.Crawl.FollowRedirects)
                    {
                        Log("redirect status " + result.Status + " for " + current + " not followed due to settings");
                        result.Outcome = RedirectOutcomeEnum.NotFollowed;
                        return result;
                    }

                    if (!hasLocation)
                    {
                        Log("redirect status " + result.Status + " for " + current + " has no Location header");
                        result.Outcome = RedirectOutcomeEnum.MissingLocation;
                        return result;
                    }

                    Uri target = RedirectPolicy.ResolveLocation(current, location);
                    if (target == null)
                    {
                        Log("redirect status " + result.Status + " for " + current + " has an invalid Location header: " + location);
                        result.Chain.Add(new RedirectHop(current.ToString(), result.Status, location.Trim()));
                        result.Outcome = RedirectOutcomeEnum.InvalidLocation;
                        return result;
                    }

                    result.Chain.Add(new RedirectHop(current.ToString(), result.Status, target.ToString()));

                    if (result.Chain.Count > _Settings.Crawl.MaxRedirects)
                    {
                        Log("more than " + _Settings.Crawl.MaxRedirects + " redirects while retrieving " + requestUri + ", stopping at " + current);
                        result.Outcome = RedirectOutcomeEnum.MaxRedirectsExceeded;
                        return result;
                    }

                    method = RedirectPolicy.GetRedirectMethod(method, result.Status);
                    current = target;
                }
            }
        }

        /// <summary>
        /// Send one request, retrying on 429 as configured, and record the response on the result.
        /// </summary>
        private async Task SendHopAsync(HttpClient client, Uri uri, HttpMethod method, ResolvedResponse result, CancellationToken token)
        {
            int attempt = 0;

            while (true)
            {
                bool retry = false;
                bool throttled = false;

                // The client span and integration latency cover the request and the body only; backoff and throttle
                // delays run after it closes and are recorded as their own stages.
                using (IntegrationScope call = new IntegrationScope(CrawlSharpTelemetry.ServiceHttp, method.Method))
                using (HttpRequestMessage message = new HttpRequestMessage(method, uri))
                {
                    call.SetTag(CrawlSharpTelemetry.AttributeHttpMethod, method.Method);
                    call.SetTag(CrawlSharpTelemetry.AttributeRetryAttempt, attempt);
                    CrawlInstruments.SetUrl(call.Activity, uri);

                    message.Headers.TryAddWithoutValidation("User-Agent", _Settings.Crawl.UserAgent);

                    string credentialName;
                    string credentialValue;
                    if (IsInCredentialScope(uri) && TryGetCredentialHeader(out credentialName, out credentialValue))
                        message.Headers.TryAddWithoutValidation(credentialName, credentialValue);

                    HttpResponseMessage response;

                    try
                    {
                        response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        call.Fail(e, token);
                        throw;
                    }

                    using (RestResponse resp = new RestResponse(response))
                    {
                        call.SetStatus(resp.StatusCode);

                        if (attempt == 0)
                        {
                            result.RequestedUris.Add(uri);
                            result.FinalUri = uri;
                        }

                        if (resp.StatusCode == 429)
                        {
                            Log("throttle status 429 for " + uri);

                            if (_Settings.Crawl.RetryOn429 && attempt < _Settings.Crawl.MaxRetries) retry = true;
                            else throttled = true;
                        }
                        else
                        {
                            Log("status " + resp.StatusCode + " for URL " + uri);
                        }

                        if (!retry)
                        {
                            result.Status = resp.StatusCode;
                            result.Headers = resp.Headers;
                            result.MediaType = response.Content?.Headers?.ContentType?.MediaType?.ToLowerInvariant();
                            result.ETag = GetEtag(resp);

                            try
                            {
                                result.Data = method == HttpMethod.Head ? null : await ReadResponseBytesAsync(resp, token).ConfigureAwait(false);
                            }
                            catch (Exception e)
                            {
                                call.Fail(e, token);
                                throw;
                            }

                            if (result.Data != null) call.SetTag(CrawlSharpTelemetry.AttributePageSize, result.Data.LongLength);
                        }
                    }
                }

                if (retry)
                {
                    await DelayForRetry(uri, attempt, CrawlSharpTelemetry.ServiceHttp, token).ConfigureAwait(false);
                    attempt++;
                    continue;
                }

                if (throttled) await TimedDelay(CrawlSharpTelemetry.StageThrottleDelay, _Settings.Crawl.ThrottleMs, token).ConfigureAwait(false);
                return;
            }
        }

        private bool IsRedirectTargetInScope(Uri target, bool sameHostOnly, out string reason)
        {
            reason = null;

            if (sameHostOnly)
            {
                if (String.IsNullOrEmpty(_StartHost) || !String.Equals(target.Host, _StartHost, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "not on the start URL's host";
                    return false;
                }

                return true;
            }

            // The domain, child-URL, external-link and exclusion filters apply only when links are followed, as documented on CrawlSettings.
            if (!_Settings.Crawl.FollowLinks) return true;
            return IsInCrawlScope(target.ToString(), out reason);
        }

        private WebResource BuildResource(ResolvedResponse resolved, string parentUrl, int depth, string contentType)
        {
            if (resolved.AliasOf != null)
            {
                RegisterVisited(resolved.RequestedUri, resolved.RequestedUris, null, resolved.AliasOf);
                return resolved.AliasOf;
            }

            byte[] data = resolved.Data;

            WebResource resource = new WebResource
            {
                Url = resolved.RequestedUri.ToString(),
                FinalUrl = resolved.FinalUri.ToString(),
                RedirectChain = resolved.Chain,
                RedirectOutcome = resolved.Outcome,
                ParentUrl = parentUrl,
                Depth = depth,
                Status = resolved.Status,
                ContentType = contentType ?? resolved.MediaType ?? GetContentTypeFromHeaders(resolved.Headers),
                ETag = resolved.ETag,
                MD5Hash = data != null ? Convert.ToHexString(HashHelper.MD5Hash(data)) : null,
                SHA1Hash = data != null ? Convert.ToHexString(HashHelper.SHA1Hash(data)) : null,
                SHA256Hash = data != null ? Convert.ToHexString(HashHelper.SHA256Hash(data)) : null,
                Headers = resolved.Headers,
                Data = data
            };

            return RegisterVisited(resolved.RequestedUri, resolved.RequestedUris, resolved.FinalUri, resource);
        }

        /// <summary>
        /// Record the requested URL, every URL requested along its chain and the final URL as visited, all pointing at one resource,
        /// so a later link to any of them does not replay the chain.  When the final URL was already retrieved by another path,
        /// the existing resource wins and is returned instead, which keeps a page reached twice from being yielded twice.
        /// </summary>
        private WebResource RegisterVisited(Uri requested, IEnumerable<Uri> chainUris, Uri final, WebResource resource)
        {
            lock (_VisitedLinksLock)
            {
                int countBefore = _VisitedLinks.Count;
                WebResource winner = resource;

                if (final != null && !final.Equals(requested))
                {
                    WebResource existing;
                    if (_VisitedLinks.TryGetValue(final, out existing) && existing != null && !ReferenceEquals(existing, resource))
                    {
                        Log("redirect from " + requested + " reaches " + final + ", which was already retrieved");
                        winner = existing;
                    }
                    else
                    {
                        _VisitedLinks[final] = resource;
                    }
                }

                if (chainUris != null)
                {
                    foreach (Uri uri in chainUris)
                    {
                        if (uri.Equals(final)) continue;
                        _VisitedLinks[uri] = winner;
                    }
                }

                _VisitedLinks[requested] = winner;
                CrawlInstruments.Add(CrawlInstruments.VisitedSize, _VisitedLinks.Count - countBefore);
                return winner;
            }
        }

        #endregion

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

        private async Task<WebResource> RetrieveWithRestClient(Uri normalizedUri, string parentUrl, int depth, string contentType, bool sameHostOnly, CancellationToken token)
        {
            ResolvedResponse resolved = await ResolveAsync(normalizedUri, HttpMethod.Get, sameHostOnly, token).ConfigureAwait(false);
            return BuildResource(resolved, parentUrl, depth, contentType);
        }

        private async Task<WebResource> RetrieveWithPlaywright(Uri requestedUri, ResolvedResponse check, string parentUrl, int depth, string contentType, bool sameHostOnly, CancellationToken token)
        {
            // Navigate straight to the URL the content-type check resolved, so the browser normally sees no redirects at all.
            Uri navigateUri = check != null ? check.FinalUri : requestedUri;
            int attempt = 0;
            bool resolvedWithGet = false;

            while (true)
            {
                await using IBrowserContext context = await _IBrowser.NewContextAsync(new BrowserNewContextOptions
                {
                    UserAgent = _Settings.Crawl.UserAgent ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                    ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
                    Locale = "en-US",
                    TimezoneId = "America/New_York",
                    AcceptDownloads = false
                }).ConfigureAwait(false);

                // Set by the route handler when a credentialed navigation answers with a redirect; see RouteWithCredentials.
                bool navigationRedirected = false;

                if (_CredentialOrigins.Count > 0)
                    await context.RouteAsync("**/*", route => RouteWithCredentials(route, () => navigationRedirected = true)).ConfigureAwait(false);

                IPage page = await context.NewPageAsync().ConfigureAwait(false);

                // Track if a download was initiated
                bool downloadInitiated = false;
                page.Download += (sender, e) =>
                {
                    downloadInitiated = true;
                };

                CrawlInstruments.Add(CrawlInstruments.BrowserContextsActive, 1);

                try
                {
                    IResponse response = null;
                    bool downloadStarted = false;
                    PlaywrightException loopError = null;

                    // The navigate span covers the browser navigation only; fallbacks to the REST client run after it closes.
                    using (IntegrationScope navigate = new IntegrationScope(CrawlSharpTelemetry.ServicePlaywright, CrawlSharpTelemetry.OperationNavigate))
                    {
                        CrawlInstruments.SetUrl(navigate.Activity, navigateUri);
                        navigate.SetTag(CrawlSharpTelemetry.AttributeRetryAttempt, attempt);

                        try
                        {
                            response = await page.GotoAsync(navigateUri.ToString(), new PageGotoOptions
                            {
                                WaitUntil = WaitUntilState.Load,
                                Timeout = _Settings.Crawl.PageTimeoutMs
                            }).ConfigureAwait(false);

                            if (navigationRedirected) navigate.SetOutcome(CrawlSharpTelemetry.OutcomeRedirected);
                            else if (response != null) navigate.SetStatus(response.Status);
                            else navigate.SetOutcome(CrawlSharpTelemetry.OutcomeSuccess);
                        }
                        catch (PlaywrightException) when (navigationRedirected)
                        {
                            // The route handler aborted the navigation because it redirected; handled below.
                            navigate.SetOutcome(CrawlSharpTelemetry.OutcomeRedirected);
                        }
                        catch (PlaywrightException ex) when (ex.Message.Contains("Download is starting"))
                        {
                            navigate.SetOutcome(CrawlSharpTelemetry.OutcomeDownload);
                            downloadStarted = true;
                        }
                        catch (PlaywrightException ex) when (IsRedirectLoopError(ex))
                        {
                            navigate.Fail(ex, token);
                            loopError = ex;
                        }
                        catch (Exception e)
                        {
                            navigate.Fail(e, token);
                            throw;
                        }
                    }

                    if (downloadStarted)
                    {
                        // Download was triggered, fall back to REST client
                        Log("download triggered for " + navigateUri + ", using REST client");
                        return await RetrieveWithRestClient(requestedUri, parentUrl, depth, contentType, sameHostOnly, token).ConfigureAwait(false);
                    }

                    if (loopError != null)
                    {
                        // Report the page instead of letting the navigation error drop it from the results.
                        PlaywrightException ex = loopError;
                        Log("browser reported a redirect loop for " + navigateUri + ": " + ex.Message);

                        WebResource looped = new WebResource
                        {
                            Url = requestedUri.ToString(),
                            FinalUrl = navigateUri.ToString(),
                            RedirectChain = check != null ? check.Chain : new List<RedirectHop>(),
                            RedirectOutcome = RedirectOutcomeEnum.LoopDetected,
                            ParentUrl = parentUrl,
                            Depth = depth,
                            Status = 0,
                            ContentType = contentType
                        };

                        return RegisterVisited(requestedUri, check != null ? check.RequestedUris : null, null, looped);
                    }

                    if (navigationRedirected)
                    {
                        // The page redirects on GET even though the HEAD check did not (or resolved elsewhere).  Resolve the chain
                        // with GET through the crawler's own redirect handling, which attaches credentials per hop, then navigate
                        // straight to where it ends.  Only once, so a server that keeps redirecting falls back to the REST client.
                        if (resolvedWithGet)
                        {
                            Log("browser navigation to " + navigateUri + " redirected again, using REST client");
                            return await RetrieveWithRestClient(requestedUri, parentUrl, depth, contentType, sameHostOnly, token).ConfigureAwait(false);
                        }

                        Log("browser navigation to " + navigateUri + " redirected, resolving the redirect chain with GET before navigating");
                        ResolvedResponse resolved = await ResolveAsync(requestedUri, HttpMethod.Get, sameHostOnly, token).ConfigureAwait(false);

                        if (resolved.AliasOf != null
                            || (resolved.Outcome != RedirectOutcomeEnum.None && resolved.Outcome != RedirectOutcomeEnum.Followed))
                        {
                            return BuildResource(resolved, parentUrl, depth, contentType);
                        }

                        check = resolved;
                        navigateUri = resolved.FinalUri;
                        resolvedWithGet = true;
                        continue;
                    }

                    // Check if download was initiated during navigation
                    if (downloadInitiated)
                    {
                        Log("download initiated for " + navigateUri + ", using REST client");
                        return await RetrieveWithRestClient(requestedUri, parentUrl, depth, contentType, sameHostOnly, token).ConfigureAwait(false);
                    }

                    if (response == null)
                    {
                        Log("no response received for " + navigateUri);
                        return await RetrieveWithRestClient(requestedUri, parentUrl, depth, contentType, sameHostOnly, token).ConfigureAwait(false);
                    }

                    if (response.Status == 429)
                    {
                        Log("throttle status 429 for " + navigateUri);

                        if (_Settings.Crawl.RetryOn429 && attempt < _Settings.Crawl.MaxRetries)
                        {
                            if (page != null && !page.IsClosed)
                                await page.CloseAsync().ConfigureAwait(false);

                            await DelayForRetry(navigateUri, attempt, CrawlSharpTelemetry.ServicePlaywright, token).ConfigureAwait(false);
                            attempt++;
                            continue;
                        }

                        await TimedDelay(CrawlSharpTelemetry.StageThrottleDelay, _Settings.Crawl.ThrottleMs, token).ConfigureAwait(false);
                    }
                    else
                    {
                        Log("status " + response.Status + " for URL " + navigateUri);
                    }

                    Dictionary<string, string> headers = await response.AllHeadersAsync().ConfigureAwait(false);

                    NameValueCollection headerCollection = new NameValueCollection(StringComparer.InvariantCultureIgnoreCase);
                    foreach (KeyValuePair<string, string> header in headers)
                    {
                        headerCollection.Add(header.Key, header.Value);
                    }

                    if (RedirectPolicy.IsRedirectStatus(response.Status, !String.IsNullOrEmpty(headerCollection["Location"])))
                    {
                        // The browser stopped on a redirect instead of following it; let the REST client resolve the chain.
                        Log("browser did not follow redirect status " + response.Status + " for " + navigateUri + ", using REST client");
                        return await RetrieveWithRestClient(requestedUri, parentUrl, depth, contentType, sameHostOnly, token).ConfigureAwait(false);
                    }

                    // Redirects the browser followed itself, for example when a server redirects GET differently from HEAD.
                    List<RedirectHop> browserHops = await GetBrowserRedirectHops(response).ConfigureAwait(false);

                    List<RedirectHop> chain = new List<RedirectHop>();
                    List<Uri> chainUris = new List<Uri>();
                    if (check != null)
                    {
                        chain.AddRange(check.Chain);
                        chainUris.AddRange(check.RequestedUris);
                    }

                    foreach (RedirectHop hop in browserHops)
                    {
                        chain.Add(hop);
                        Uri hopUri;
                        if (Uri.TryCreate(hop.Url, UriKind.Absolute, out hopUri)) chainUris.Add(hopUri);
                    }

                    Uri finalUri = RemoveFragment(new Uri(response.Url));

                    if (browserHops.Count > 0)
                    {
                        RedirectOutcomeEnum stopped = RedirectOutcomeEnum.None;
                        string reason;

                        if (chain.Count > _Settings.Crawl.MaxRedirects)
                        {
                            stopped = RedirectOutcomeEnum.MaxRedirectsExceeded;
                            reason = "more than " + _Settings.Crawl.MaxRedirects + " redirects";
                        }
                        else if (!IsRedirectTargetInScope(finalUri, sameHostOnly, out reason))
                        {
                            stopped = RedirectOutcomeEnum.OutOfScope;
                        }

                        if (stopped != RedirectOutcomeEnum.None)
                        {
                            Log("browser followed redirects from " + navigateUri + " to " + finalUri + " (" + reason + "), discarding content");

                            RedirectHop lastHop = browserHops[browserHops.Count - 1];
                            WebResource discarded = new WebResource
                            {
                                Url = requestedUri.ToString(),
                                FinalUrl = lastHop.Url,
                                RedirectChain = chain,
                                RedirectOutcome = stopped,
                                ParentUrl = parentUrl,
                                Depth = depth,
                                Status = lastHop.Status,
                                ContentType = contentType
                            };

                            return RegisterVisited(requestedUri, chainUris, null, discarded);
                        }
                    }

                    if (IsAutoExpandEnabled(contentType))
                    {
                        Log("headless auto-expand enabled for " + navigateUri);
                        if (_Settings.Crawl.PostLoadDelayMs > 0)
                        {
                            Log("waiting " + _Settings.Crawl.PostLoadDelayMs + "ms before auto-expand for " + navigateUri);
                            await DelayIfNeeded(_Settings.Crawl.PostLoadDelayMs, token).ConfigureAwait(false);
                        }

                        using (StageScope expand = new StageScope(CrawlSharpTelemetry.StageAutoExpand))
                        {
                            try
                            {
                                await ExpandCollapsibleContent(page, navigateUri, token).ConfigureAwait(false);
                            }
                            catch (Exception e)
                            {
                                expand.Fail(e);
                                throw;
                            }
                        }
                    }
                    else
                    {
                        Log("headless auto-expand disabled for " + navigateUri);
                    }

                    string content = await page.ContentAsync().ConfigureAwait(false);

                    WebResource resource = new WebResource
                    {
                        Url = requestedUri.ToString(),
                        FinalUrl = finalUri.ToString(),
                        RedirectChain = chain,
                        RedirectOutcome = chain.Count > 0 ? RedirectOutcomeEnum.Followed : RedirectOutcomeEnum.None,
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

                    return RegisterVisited(requestedUri, chainUris, finalUri, resource);
                }
                finally
                {
                    CrawlInstruments.Add(CrawlInstruments.BrowserContextsActive, -1);

                    if (page != null && !page.IsClosed)
                    {
                        await page.CloseAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Attach credentials to browser requests whose origin is in the credential scope.  The request is fetched without
        /// following redirects and its response handed to the browser.  route.ContinueAsync with headers is not used, because
        /// Playwright carries those headers onto every redirect the request starts, including cross-origin ones.
        /// Playwright does not route the requests a redirect starts, so the browser would follow a redirect without credentials.
        /// For a subresource that is the safe direction and is accepted.  A redirected navigation is aborted instead, and
        /// <paramref name="onNavigationRedirect"/> tells the caller to resolve the chain itself and navigate to where it ends.
        /// </summary>
        private async Task RouteWithCredentials(IRoute route, Action onNavigationRedirect)
        {
            Uri uri;
            string name;
            string value;

            if (!Uri.TryCreate(route.Request.Url, UriKind.Absolute, out uri)
                || !IsInCredentialScope(uri)
                || !TryGetCredentialHeader(out name, out value))
            {
                await route.ContinueAsync().ConfigureAwait(false);
                return;
            }

            Dictionary<string, string> headers = new Dictionary<string, string>(route.Request.Headers, StringComparer.OrdinalIgnoreCase);
            headers[name] = value;

            try
            {
                IAPIResponse fetched;

                using (IntegrationScope call = new IntegrationScope(CrawlSharpTelemetry.ServicePlaywright, CrawlSharpTelemetry.OperationRouteFetch))
                {
                    CrawlInstruments.SetUrl(call.Activity, uri);

                    try
                    {
                        fetched = await route.FetchAsync(new RouteFetchOptions
                        {
                            Headers = headers,
                            MaxRedirects = 0,
                            Timeout = _Settings.Crawl.PageTimeoutMs
                        }).ConfigureAwait(false);

                        call.SetStatus(fetched.Status);
                    }
                    catch (Exception e)
                    {
                        call.Fail(e);
                        throw;
                    }
                }

                string location;
                fetched.Headers.TryGetValue("location", out location);

                if (route.Request.IsNavigationRequest && RedirectPolicy.IsRedirectStatus(fetched.Status, !String.IsNullOrEmpty(location)))
                {
                    Log("browser navigation to " + uri + " answered " + fetched.Status + ", aborting so the chain is resolved with credentials");
                    onNavigationRedirect?.Invoke();
                    await route.AbortAsync().ConfigureAwait(false);
                    return;
                }

                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Response = fetched
                }).ConfigureAwait(false);
            }
            catch (PlaywrightException ex)
            {
                Log("credentialed browser request failed for " + uri + ": " + ex.Message);
                await route.AbortAsync().ConfigureAwait(false);
            }
        }

        private async Task<List<RedirectHop>> GetBrowserRedirectHops(IResponse response)
        {
            List<RedirectHop> hops = new List<RedirectHop>();
            if (response == null || response.Request == null) return hops;

            IRequest request = response.Request;
            while (request.RedirectedFrom != null)
            {
                IRequest previous = request.RedirectedFrom;

                // ResponseAsync can stay pending for a response that was fulfilled by a route handler, so bound the wait
                // and record the hop with status 0 rather than stall the crawl.
                Task<IResponse> responseTask = previous.ResponseAsync();
                Task completed = await Task.WhenAny(responseTask, Task.Delay(2000)).ConfigureAwait(false);
                IResponse previousResponse = null;

                if (completed == responseTask) previousResponse = await responseTask.ConfigureAwait(false);
                else _ = responseTask.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);

                hops.Insert(0, new RedirectHop(previous.Url, previousResponse != null ? previousResponse.Status : 0, request.Url));
                request = previous;
            }

            return hops;
        }

        private static bool IsRedirectLoopError(PlaywrightException ex)
        {
            string message = ex != null && ex.Message != null ? ex.Message : String.Empty;
            return message.Contains("NS_ERROR_REDIRECT_LOOP", StringComparison.OrdinalIgnoreCase)
                || message.Contains("ERR_TOO_MANY_REDIRECTS", StringComparison.OrdinalIgnoreCase)
                || message.Contains("redirect loop", StringComparison.OrdinalIgnoreCase);
        }

        private static Uri RemoveFragment(Uri uri)
        {
            if (uri == null || String.IsNullOrEmpty(uri.Fragment)) return uri;
            UriBuilder builder = new UriBuilder(uri);
            builder.Fragment = String.Empty;
            return builder.Uri;
        }

        private async Task<WebResource> RetrieveWebResource(string url, string parentUrl, int depth, bool sameHostOnly, string purpose, CancellationToken token = default)
        {
            // The politeness delay is its own stage, so fetch latency measures retrieval only.
            await Pause(token).ConfigureAwait(false);

            using (StageScope stage = new StageScope(CrawlSharpTelemetry.StageFetch))
            {
                return await RetrieveWebResource(url, parentUrl, depth, sameHostOnly, purpose, stage, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Retrieve one URL within a caller-owned fetch stage, whose outcome tells a null result's cause (skipped, failed, cancelled).
        /// </summary>
        private async Task<WebResource> RetrieveWebResource(string url, string parentUrl, int depth, bool sameHostOnly, string purpose, StageScope stage, CancellationToken token)
        {
            bool isPage = purpose == CrawlSharpTelemetry.PurposePage;

            stage.SetTag(CrawlSharpTelemetry.AttributePurpose, purpose);
            stage.SetTag(CrawlSharpTelemetry.AttributeDepth, depth);

            try
            {
                string fullUrl = NormalizeUrl(_Settings.Crawl.StartUrl, url);
                if (String.IsNullOrEmpty(fullUrl))
                {
                    Log("invalid URL " + url);
                    SkipFetch(stage, isPage, CrawlSharpTelemetry.ReasonInvalidUrl);
                    return null;
                }

                if (!fullUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !fullUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"URL does not start with http/https: {fullUrl} (original: {url})");
                    SkipFetch(stage, isPage, CrawlSharpTelemetry.ReasonNonHttp);
                    return null;
                }

                Uri normalizedUri;
                try
                {
                    normalizedUri = RemoveFragment(new Uri(fullUrl));
                }
                catch (UriFormatException ufe)
                {
                    Exception?.Invoke(fullUrl, ufe);
                    Log("invalid URI format " + fullUrl);
                    SkipFetch(stage, isPage, CrawlSharpTelemetry.ReasonInvalidUrl);
                    return null;
                }

                CrawlInstruments.SetUrl(stage.Activity, normalizedUri);

                if (IsAlreadyVisited(normalizedUri))
                {
                    Log("already visited " + normalizedUri);
                    stage.SetOutcome(CrawlSharpTelemetry.OutcomeSkipped);
                    return GetAlreadyVisited(normalizedUri);
                }

                if (!_RobotsFile.IsPathAllowed(_Settings.Crawl.UserAgent, normalizedUri.AbsolutePath))
                {
                    Log("crawl of " + normalizedUri + " prohibited by robots.txt");
                    SkipFetch(stage, isPage, CrawlSharpTelemetry.ReasonRobotsDisallowed);
                    return null;
                }

                Log("retrieving " + normalizedUri);

                WebResource resource;

                if (_Settings.Crawl.UseHeadlessBrowser)
                    resource = await RetrieveHeadless(normalizedUri, parentUrl, depth, sameHostOnly, token).ConfigureAwait(false);
                else
                    resource = await RetrieveWithRestClient(normalizedUri, parentUrl, depth, null, sameHostOnly, token).ConfigureAwait(false);

                if (resource != null)
                {
                    stage.SetTag(CrawlSharpTelemetry.AttributeHttpStatusCode, resource.Status);
                    stage.SetTag(CrawlSharpTelemetry.AttributeRedirectOutcome, GetRedirectOutcomeLabel(resource.RedirectOutcome));
                    stage.SetTag(CrawlSharpTelemetry.AttributeRedirectHops, resource.RedirectChain != null ? resource.RedirectChain.Count : 0);
                }

                return resource;
            }
            catch (IOException ioe)
            {
                FailFetch(stage, isPage, ioe);
                Exception?.Invoke(url, ioe);
                Log("IO exception while retrieving URL " + url + Environment.NewLine + ioe.ToString());
                return null;
            }
            catch (HttpRequestException hre)
            {
                FailFetch(stage, isPage, hre);
                Exception?.Invoke(url, hre);
                Log("HTTP request exception while retrieving URL " + url + Environment.NewLine + hre.ToString());
                return null;
            }
            catch (Exception e)
            {
                FailFetch(stage, isPage, e);
                Exception?.Invoke(url, e);
                Log("error processing URL " + url + Environment.NewLine + e.ToString());
                return null;
            }
        }

        private void SkipFetch(StageScope stage, bool isPage, string reason)
        {
            stage.SetOutcome(CrawlSharpTelemetry.OutcomeSkipped);
            stage.SetTag(CrawlSharpTelemetry.AttributeReason, reason);
            if (isPage) CrawlInstruments.RecordSkipped(reason);
        }

        private void FailFetch(StageScope stage, bool isPage, Exception e)
        {
            stage.Fail(e);
            if (isPage && !(e is OperationCanceledException))
                CrawlInstruments.RecordPage(_Mode, CrawlSharpTelemetry.OutcomeFailure, 0, -1);
        }

        private async Task<WebResource> RetrieveHeadless(Uri normalizedUri, string parentUrl, int depth, bool sameHostOnly, CancellationToken token)
        {
            // Resolve the redirect chain with a HEAD request before launching the browser, so headless retrieval gets the same hop limit,
            // loop detection, scope rules and credential scope as every other request, and non-navigable content is downloaded directly.
            ResolvedResponse check = null;

            using (StageScope stage = new StageScope(CrawlSharpTelemetry.StageContentTypeCheck))
            {
                try
                {
                    check = await ResolveAsync(normalizedUri, HttpMethod.Head, sameHostOnly, token).ConfigureAwait(false);
                    stage.SetTag(CrawlSharpTelemetry.AttributeHttpStatusCode, check.Status);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    stage.SetOutcome(CrawlSharpTelemetry.OutcomeCancelled);
                    throw;
                }
                catch (Exception e)
                {
                    // Some servers reject HEAD outright; navigate to the requested URL as before.
                    stage.Fail(e);
                    Log("content type check failed for " + normalizedUri + ": " + e.Message);
                }
            }

            if (check != null)
            {
                if (check.AliasOf != null) return BuildResource(check, parentUrl, depth, null);

                switch (check.Outcome)
                {
                    case RedirectOutcomeEnum.LoopDetected:
                    case RedirectOutcomeEnum.MaxRedirectsExceeded:
                    case RedirectOutcomeEnum.OutOfScope:
                    case RedirectOutcomeEnum.RobotsDisallowed:
                        // The chain itself is the problem; report it without launching the browser.
                        return BuildResource(check, parentUrl, depth, null);

                    case RedirectOutcomeEnum.NotFollowed:
                    case RedirectOutcomeEnum.MissingLocation:
                    case RedirectOutcomeEnum.InvalidLocation:
                        // A single redirect response is the result; retrieve it with its body.
                        return await RetrieveWithRestClient(normalizedUri, parentUrl, depth, null, sameHostOnly, token).ConfigureAwait(false);
                }
            }

            // Default to navigable if the check fails
            ContentTypeInfo contentInfo = new ContentTypeInfo(true, null, null, false);

            if (check != null && check.Status >= 200 && check.Status <= 299)
            {
                contentInfo.MediaType = check.MediaType ?? String.Empty;
                contentInfo.CheckSucceeded = true;
                contentInfo.IsNavigable = IsNavigableContentType(contentInfo.MediaType);
                Log($"content type check for {check.FinalUri}: {contentInfo.MediaType} navigable {contentInfo.IsNavigable}");
            }

            if (contentInfo.IsNavigable)
            {
                using (StageScope stage = new StageScope(CrawlSharpTelemetry.StageBrowserNavigate))
                {
                    try
                    {
                        return await RetrieveWithPlaywright(normalizedUri, contentInfo.CheckSucceeded ? check : null, parentUrl, depth, contentInfo.MediaType, sameHostOnly, token).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        stage.Fail(e);
                        throw;
                    }
                }
            }

            return await RetrieveWithRestClient(normalizedUri, parentUrl, depth, contentInfo.MediaType, sameHostOnly, token).ConfigureAwait(false);
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
            using (StageScope stage = new StageScope(CrawlSharpTelemetry.StageRobots, CrawlParentContext))
            {
                if (_Settings.Crawl.IgnoreRobotsText)
                {
                    Log("skipping retrieval and processing of robots.txt due to settings");
                    stage.SetOutcome(CrawlSharpTelemetry.OutcomeSkipped);
                    return;
                }

                if (String.IsNullOrEmpty(baseUrl))
                {
                    stage.SetOutcome(CrawlSharpTelemetry.OutcomeSkipped);
                    return;
                }

                // CHANGE: Use domain root instead of the starting URL
                string domainRoot = GetDomainRoot(baseUrl);
                string robotsFile = domainRoot + "/robots.txt";

                WebResource robots;

                try
                {
                    robots = await RetrieveWebResource(robotsFile, baseUrl, 0, true, CrawlSharpTelemetry.PurposeRobots, token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    stage.Fail(e);
                    throw;
                }

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
                        stage.Fail(e);
                        Exception?.Invoke(robotsFile, e);
                        Log("error parsing contents from robots file " + robotsFile + Environment.NewLine + Encoding.UTF8.GetString(robots.Data));
                    }
                }
                else
                {
                    stage.SetOutcome(CrawlSharpTelemetry.OutcomeNotFound);
                    Log("unable to retrieve robots.txt from " + robotsFile);
                }
            }
        }

        private async Task ProcessSitemap(string baseUrl, CancellationToken token = default)
        {
            using (StageScope stage = new StageScope(CrawlSharpTelemetry.StageSitemap, CrawlParentContext))
            {
                if (!_Settings.Crawl.IncludeSitemap)
                {
                    Log("skipping retrieval and processing of sitemap.xml due to settings");
                    stage.SetOutcome(CrawlSharpTelemetry.OutcomeSkipped);
                    return;
                }

                if (String.IsNullOrEmpty(baseUrl))
                {
                    stage.SetOutcome(CrawlSharpTelemetry.OutcomeSkipped);
                    return;
                }

                // CHANGE: Use domain root instead of the starting URL
                string domainRoot = GetDomainRoot(baseUrl);
                string sitemapUrl = domainRoot + "/sitemap.xml";

                WebResource sitemap;

                try
                {
                    sitemap = await RetrieveWebResource(sitemapUrl, baseUrl, 0, true, CrawlSharpTelemetry.PurposeSitemap, token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    stage.Fail(e);
                    throw;
                }

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
                                    CrawlInstruments.Add(CrawlInstruments.SitemapUrls, urls.Count);
                                    stage.SetTag(CrawlSharpTelemetry.AttributeLinkCount, urls.Count);

                                    foreach (SitemapUrl url in urls)
                                    {
                                        if (String.IsNullOrEmpty(url.Location)) continue;
                                        EnqueueQueuedLink(url.Location, sitemapUrl, 0, CrawlSharpTelemetry.LinkSourceSitemap);
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
                        stage.Fail(e);
                        Exception?.Invoke(sitemapUrl, e);
                        Log("error parsing contents from sitemap.xml file " + sitemapUrl + Environment.NewLine + Encoding.UTF8.GetString(sitemap.Data));
                    }
                }
                else
                {
                    stage.SetOutcome(CrawlSharpTelemetry.OutcomeNotFound);
                    Log("unable to retrieve sitemap.xml from " + sitemapUrl);  // CHANGE: Fixed log message
                }
            }
        }

        private async Task Pause(CancellationToken token = default)
        {
            await TimedDelay(CrawlSharpTelemetry.StagePolitenessDelay, _DelayMilliseconds, token).ConfigureAwait(false);
        }

        private async Task ExpandCollapsibleContent(IPage page, Uri normalizedUri, CancellationToken token)
        {
            if (page == null) return;

            int customSelectorCount = _Settings.Crawl.ExpansionSelectors != null
                ? _Settings.Crawl.ExpansionSelectors.Count(s => !String.IsNullOrWhiteSpace(s))
                : 0;

            Log("auto-expand configured for " + normalizedUri
                + ": passes " + _Settings.Crawl.MaxExpansionPasses
                + ", custom selectors " + customSelectorCount);

            for (int pass = 0; pass < _Settings.Crawl.MaxExpansionPasses; pass++)
            {
                token.ThrowIfCancellationRequested();

                int detailsOpened = await OpenDetailsElements(page).ConfigureAwait(false);
                int builtInClicks = await ExpandBuiltInTargets(page, token).ConfigureAwait(false);
                int customClicks = await ExpandCustomSelectors(page, token).ConfigureAwait(false);
                int totalChanges = detailsOpened + builtInClicks + customClicks;

                CrawlInstruments.Add(CrawlInstruments.AutoExpandChanges, detailsOpened, CrawlSharpTelemetry.AttributeAutoExpandKind, CrawlSharpTelemetry.AutoExpandDetails);
                CrawlInstruments.Add(CrawlInstruments.AutoExpandChanges, builtInClicks, CrawlSharpTelemetry.AttributeAutoExpandKind, CrawlSharpTelemetry.AutoExpandBuiltIn);
                CrawlInstruments.Add(CrawlInstruments.AutoExpandChanges, customClicks, CrawlSharpTelemetry.AttributeAutoExpandKind, CrawlSharpTelemetry.AutoExpandCustom);

                Log("auto-expand pass " + (pass + 1) + "/" + _Settings.Crawl.MaxExpansionPasses
                    + " for " + normalizedUri
                    + ": details " + detailsOpened
                    + ", built-in clicks " + builtInClicks
                    + ", custom clicks " + customClicks);

                if (totalChanges < 1)
                {
                    Log("auto-expand complete for " + normalizedUri + ", no additional changes detected");
                    break;
                }

                await DelayIfNeeded(_Settings.Crawl.PostInteractionDelayMs, token).ConfigureAwait(false);
            }
        }

        private async Task<int> OpenDetailsElements(IPage page)
        {
            if (page == null) return 0;

            try
            {
                return await page.EvaluateAsync<int>(
                    @"() => {
                        let changed = 0;
                        document.querySelectorAll('details').forEach((element) => {
                            if (!element.open) {
                                element.open = true;
                                changed++;
                            }
                        });
                        return changed;
                    }").ConfigureAwait(false);
            }
            catch (PlaywrightException ex)
            {
                Log("auto-expand unable to open <details> elements: " + ex.Message);
                return 0;
            }
        }

        private async Task<int> ExpandBuiltInTargets(IPage page, CancellationToken token)
        {
            return await ClickSelectorTargets(page, _BuiltInExpansionSelectors, token).ConfigureAwait(false);
        }

        private async Task<int> ExpandCustomSelectors(IPage page, CancellationToken token)
        {
            if (_Settings.Crawl.ExpansionSelectors == null || _Settings.Crawl.ExpansionSelectors.Count < 1)
                return 0;

            int clicks = 0;

            foreach (string selector in _Settings.Crawl.ExpansionSelectors)
            {
                if (String.IsNullOrWhiteSpace(selector)) continue;

                try
                {
                    clicks += await ClickSelectorTargets(page, new[] { selector }, token).ConfigureAwait(false);
                }
                catch (PlaywrightException ex)
                {
                    Log("auto-expand custom selector failed '" + selector + "': " + ex.Message);
                }
            }

            return clicks;
        }

        private async Task<int> ClickSelectorTargets(IPage page, IEnumerable<string> selectors, CancellationToken token)
        {
            if (page == null || selectors == null) return 0;

            List<string> selectorList = selectors
                .Where(s => !String.IsNullOrWhiteSpace(s))
                .Distinct()
                .ToList();

            if (selectorList.Count < 1) return 0;

            string selector = String.Join(", ", selectorList);
            ILocator locator = page.Locator(selector);
            int count;

            try
            {
                count = await locator.CountAsync().ConfigureAwait(false);
            }
            catch (PlaywrightException ex)
            {
                Log("auto-expand selector lookup failed '" + selector + "': " + ex.Message);
                return 0;
            }

            int clicks = 0;

            for (int i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                ILocator target = locator.Nth(i);

                try
                {
                    if (!await target.IsVisibleAsync().ConfigureAwait(false))
                        continue;

                    bool disabled = await target.EvaluateAsync<bool>(
                        @"el => !!(el.hasAttribute('disabled') || el.getAttribute('aria-disabled') === 'true')")
                        .ConfigureAwait(false);
                    if (disabled) continue;

                    string href = await target.EvaluateAsync<string>(
                        @"el => el.tagName && el.tagName.toLowerCase() === 'a' ? (el.getAttribute('href') || '') : ''")
                        .ConfigureAwait(false);
                    if (!String.IsNullOrEmpty(href)
                        && !href.StartsWith("#", StringComparison.Ordinal)
                        && !href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    await target.ScrollIntoViewIfNeededAsync().ConfigureAwait(false);
                    await target.ClickAsync(new LocatorClickOptions
                    {
                        Timeout = 1000
                    }).ConfigureAwait(false);

                    clicks++;
                }
                catch (PlaywrightException ex)
                {
                    Log("auto-expand click skipped for selector '" + selector + "': " + ex.Message);
                }
            }

            return clicks;
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

        private async Task<byte[]> ReadResponseBytesAsync(RestResponse resp, CancellationToken token = default)
        {
            if (resp == null) return null;

            if (resp.ChunkedTransferEncoding)
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    ChunkData chunk;
                    while ((chunk = await resp.ReadChunkAsync(token).ConfigureAwait(false)) != null)
                    {
                        if (chunk.Data != null && chunk.Data.Length > 0)
                            ms.Write(chunk.Data, 0, chunk.Data.Length);

                        if (chunk.IsFinal) break;
                    }

                    return ms.Length > 0 ? ms.ToArray() : null;
                }
            }

            return resp.DataAsBytes;
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

        /// <summary>
        /// Apply the crawl-scope filters (denied domains, same root domain, same subdomain, child URLs, allowed domains,
        /// external links and exclusion patterns) to an absolute URL.  Used for discovered links and for redirect targets.
        /// </summary>
        private bool IsInCrawlScope(string url, out string reason)
        {
            reason = null;

            if (IsDeniedDomain(url, _Settings.Crawl.DeniedDomains))
            {
                reason = "domain is denied";
                return false;
            }

            if (_Settings.Crawl.RestrictToSameRootDomain && !IsSameRootDomain(_Settings.Crawl.StartUrl, url))
            {
                reason = "not in the start URL's root domain";
                return false;
            }

            if (_Settings.Crawl.RestrictToSameSubdomain && !IsSameSubdomain(_Settings.Crawl.StartUrl, url))
            {
                reason = "not in the start URL's subdomain";
                return false;
            }

            if (_Settings.Crawl.RestrictToChildUrls && !IsChildUrl(_Settings.Crawl.StartUrl, url))
            {
                reason = "not a child of the start URL";
                return false;
            }

            if (!IsAllowedDomain(url, _Settings.Crawl.AllowedDomains))
            {
                reason = "domain is not in the allowed list";
                return false;
            }

            if (!_Settings.Crawl.FollowExternalLinks && IsExternalUrl(_Settings.Crawl.StartUrl, url))
            {
                reason = "external link";
                return false;
            }

            if (IsUrlExcluded(url))
            {
                reason = "matches an exclusion pattern";
                return false;
            }

            return true;
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

        private WebResource GetAlreadyVisited(Uri uri)
        {
            lock (_VisitedLinksLock)
            {
                return _VisitedLinks[uri];
            }
        }

        private bool EnqueueQueuedLink(string url, string parentUrl, int depth, string source)
        {
            bool added = false;

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

                    added = true;
                }
            }

            if (added)
            {
                CrawlInstruments.Add(CrawlInstruments.QueueSize, 1);
                CrawlInstruments.Add(CrawlInstruments.LinksEnqueued, 1, CrawlSharpTelemetry.AttributeLinkSource, source);
            }
            else
            {
                CrawlInstruments.RecordSkipped(CrawlSharpTelemetry.ReasonAlreadyQueued);
            }

            return added;
        }

        private QueuedLink DequeueQueuedLink()
        {
            QueuedLink link;

            lock (_QueuedLinksLock)
            {
                if (_QueuedLinks.Count < 1) return null;
                link = _QueuedLinks.Dequeue();
            }

            CrawlInstruments.Add(CrawlInstruments.QueueSize, -1);
            return link;
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

            CrawlInstruments.Add(CrawlInstruments.ResultsBuffered, 1);
        }

        private bool TryMarkYielded(WebResource wr)
        {
            lock (_FinishedLinksLock)
            {
                return _YieldedResources.Add(wr);
            }
        }

        private WebResource DequeueWebResource()
        {
            WebResource wr;

            lock (_FinishedLinksLock)
            {
                if (_FinishedLinks.Count < 1) return null;
                wr = _FinishedLinks.Dequeue();
            }

            CrawlInstruments.Add(CrawlInstruments.ResultsBuffered, -1);
            return wr;
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
                            CrawlInstruments.RecordSkipped(CrawlSharpTelemetry.ReasonAlreadyVisited);
                            continue;
                        }
                    }
                    catch (UriFormatException ufe)
                    {
                        Exception?.Invoke(link.Url, ufe);
                        Log("invalid URI format " + link.Url + ", skipping");
                        CrawlInstruments.RecordSkipped(CrawlSharpTelemetry.ReasonInvalidUrl);
                        continue;
                    }

                    if (!AddProcessingLink(link))
                    {
                        Log("skipping link " + link.Url + ", already in processing");
                        CrawlInstruments.RecordSkipped(CrawlSharpTelemetry.ReasonInProcessing);
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
            // Each queued link is its own span under the crawl root.  The parent is explicit because this work runs on the
            // queue processor's task, not on the flow that enumerates the crawl.
            Activity previous = Activity.Current;
            Activity pageActivity = CrawlInstruments.StartActivity(CrawlSharpTelemetry.SpanPage, ActivityKind.Internal, CrawlParentContext);
            CrawlInstruments.SetUrl(pageActivity, link.Url);
            CrawlInstruments.SetTag(pageActivity, CrawlSharpTelemetry.AttributeDepth, link.Depth);

            string pageOutcome = CrawlSharpTelemetry.OutcomeSkipped;
            bool acquired = false;

            try
            {
                using (StageScope queued = new StageScope(CrawlSharpTelemetry.StageQueued))
                {
                    try
                    {
                        await _Semaphore.WaitAsync(token).ConfigureAwait(false);
                        acquired = true;
                    }
                    catch (Exception e)
                    {
                        queued.Fail(e);
                        throw;
                    }
                }

                CrawlInstruments.Add(CrawlInstruments.WorkersInUse, 1);

                Log("processing queued link " + link.Url + " parent " + (!String.IsNullOrEmpty(link.ParentUrl) ? link.ParentUrl : ".") + " depth " + link.Depth);

                string normalizedUrl = NormalizeUrl(_Settings.Crawl.StartUrl, link.Url);
                if (string.IsNullOrEmpty(normalizedUrl))
                {
                    Log($"unable to normalize queued link {link.Url}");
                    CrawlInstruments.RecordSkipped(CrawlSharpTelemetry.ReasonInvalidUrl);
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
                        CrawlInstruments.RecordSkipped(CrawlSharpTelemetry.ReasonAlreadyVisited);
                        return;
                    }
                }
                catch (UriFormatException)
                {
                    Log($"invalid URI format for normalized URL: {link.Url}");
                    CrawlInstruments.RecordSkipped(CrawlSharpTelemetry.ReasonInvalidUrl);
                    return;
                }

                await Pause(token).ConfigureAwait(false);

                WebResource wr;
                string fetchOutcome;

                using (StageScope fetch = new StageScope(CrawlSharpTelemetry.StageFetch))
                {
                    wr = await RetrieveWebResource(link.Url, link.ParentUrl, link.Depth, false, CrawlSharpTelemetry.PurposePage, fetch, token).ConfigureAwait(false);
                    fetchOutcome = fetch.Outcome;
                }

                if (wr == null)
                {
                    Log("unable to retrieve queued link " + link.Url);
                    pageOutcome = fetchOutcome;
                    return;
                }

                if (!TryMarkYielded(wr))
                {
                    // A redirect led to a page another link already produced; it was returned (and its links queued) then.
                    Log("resource for " + link.Url + " was already returned as " + wr.Url + ", skipping");
                    CrawlInstruments.RecordSkipped(CrawlSharpTelemetry.ReasonDuplicate);
                    return;
                }

                RecordPageResult(wr);
                CrawlInstruments.SetTag(pageActivity, CrawlSharpTelemetry.AttributeHttpStatusCode, wr.Status);
                pageOutcome = CrawlSharpTelemetry.OutcomeSuccess;

                // A result that stopped on a redirect carries the redirect body, not page content, so its links are not followed.
                bool isContent = wr.RedirectOutcome == RedirectOutcomeEnum.None || wr.RedirectOutcome == RedirectOutcomeEnum.Followed;

                if (isContent && wr.Data != null && IsNavigableContentType(wr.ContentType))
                {
                    using (StageScope extraction = new StageScope(CrawlSharpTelemetry.StageLinkExtraction))
                    {
                        // Resolve relative links against the URL the content came from, which differs from link.Url after a redirect.
                        string baseUrl = !String.IsNullOrEmpty(wr.FinalUrl) ? wr.FinalUrl : link.Url;
                        List<string> links = ExtractLinksFromHtml(baseUrl, wr.Data);
                        int linkCount = links != null ? links.Count : 0;

                        CrawlInstruments.Add(CrawlInstruments.LinksDiscovered, linkCount);
                        extraction.SetTag(CrawlSharpTelemetry.AttributeLinkCount, linkCount);

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
                                            CrawlInstruments.RecordSkipped(CrawlSharpTelemetry.ReasonNonHttp);
                                            continue;
                                        }

                                        string reason;
                                        if (!IsInCrawlScope(currTrimmed, out reason))
                                        {
                                            Log("avoiding link " + currTrimmed + ", " + reason);
                                            CrawlInstruments.RecordSkipped(GetScopeReasonCode(reason));
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
                                            CrawlInstruments.RecordSkipped(CrawlSharpTelemetry.ReasonInvalidUrl);
                                            continue;
                                        }

                                        if (IsAlreadyVisited(childUri))
                                        {
                                            Log("already visited child link " + currTrimmed);
                                            CrawlInstruments.RecordSkipped(CrawlSharpTelemetry.ReasonAlreadyVisited);
                                            continue;
                                        }

                                        Log("adding link " + currTrimmed + " to queue from parent " + link.Url);
                                        EnqueueQueuedLink(currTrimmed, link.Url, link.Depth + 1, CrawlSharpTelemetry.LinkSourcePage);
                                    }
                                }
                                else
                                {
                                    Log("max depth reached in " + link.Url + ", not recursing into " + links.Count + " links");
                                    CrawlInstruments.RecordSkipped(CrawlSharpTelemetry.ReasonMaxDepth, links.Count);
                                }
                            }
                        }
                        else
                        {
                            Log("not following links due to settings");
                            CrawlInstruments.RecordSkipped(CrawlSharpTelemetry.ReasonFollowLinksDisabled, linkCount);
                        }
                    }
                }

                EnqueueWebResource(wr);
            }
            catch (Exception e)
            {
                if (e is OperationCanceledException && token.IsCancellationRequested)
                {
                    pageOutcome = CrawlSharpTelemetry.OutcomeCancelled;
                }
                else
                {
                    pageOutcome = CrawlSharpTelemetry.OutcomeFailure;
                    CrawlInstruments.RecordError(CrawlSharpTelemetry.SpanPage, e);
                    CrawlInstruments.SetError(pageActivity, e);
                }

                Exception?.Invoke(link.Url, e);
                Log("error processing link " + link.Url + Environment.NewLine + e.ToString());
            }
            finally
            {
                // Release only a slot this task acquired; releasing one it never held would let more than MaxParallelTasks run.
                if (acquired)
                {
                    CrawlInstruments.Add(CrawlInstruments.WorkersInUse, -1);
                    try { _Semaphore?.Release(); } catch (Exception) { }
                }

                RemoveProcessingLink(link);

                CrawlInstruments.SetOutcomeStatus(pageActivity, pageOutcome);
                CrawlInstruments.StopActivity(pageActivity, previous);
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
