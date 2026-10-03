namespace CrawlSharp.Telemetry
{
    /// <summary>
    /// The public telemetry contract for CrawlSharp: every meter, activity source, instrument, span, attribute and
    /// label value that CrawlSharp (and the CrawlSharp server) emits.  These strings are consumed by dashboards and
    /// alerts, so they are stable across releases; treat a change to any of them as a breaking change.
    /// <para>
    /// The library emits only through the base class library (<see cref="System.Diagnostics.Metrics.Meter"/> and
    /// <see cref="System.Diagnostics.ActivitySource"/>) and takes no exporter dependency.  Emission costs a few
    /// nanoseconds and allocates nothing until a host subscribes, for example with Radiant:
    /// <c>settings.Sources.AddMeter(CrawlSharpTelemetry.MeterName)</c> and
    /// <c>settings.Sources.AddActivitySource(CrawlSharpTelemetry.ActivitySourceName)</c>, or with the OpenTelemetry SDK:
    /// <c>.AddMeter("CrawlSharp")</c> and <c>.AddSource("CrawlSharp")</c>.
    /// </para>
    /// <para>
    /// Instrument names are dotted OpenTelemetry names with UCUM units.  A Prometheus exporter rewrites them to snake case
    /// with unit and type suffixes, for example <c>crawlsharp.crawl.duration</c> becomes
    /// <c>crawlsharp_crawl_duration_seconds</c>.  Every label is bounded; URLs and other identifiers appear only on spans.
    /// </para>
    /// </summary>
    public static class CrawlSharpTelemetry
    {
        #region Sources

        /// <summary>
        /// Name of the meter used by the CrawlSharp library.
        /// </summary>
        public const string MeterName = "CrawlSharp";

        /// <summary>
        /// Name of the activity source used by the CrawlSharp library.
        /// </summary>
        public const string ActivitySourceName = "CrawlSharp";

        /// <summary>
        /// Name of the meter used by the CrawlSharp server (the HTTP front end around the library).
        /// </summary>
        public const string ServerMeterName = "CrawlSharp.Server";

        /// <summary>
        /// Name of the activity source used by the CrawlSharp server.
        /// </summary>
        public const string ServerActivitySourceName = "CrawlSharp.Server";

        #endregion

        #region Library-Metrics

        /// <summary>
        /// Counter of crawl jobs that finished, by mode and outcome.  Unit {crawl}.
        /// </summary>
        public const string CrawlJobs = "crawlsharp.crawl.jobs";

        /// <summary>
        /// Histogram of end-to-end crawl job duration, by mode and outcome.  Unit s.
        /// </summary>
        public const string CrawlDuration = "crawlsharp.crawl.duration";

        /// <summary>
        /// Up-down counter of crawl jobs currently running, by mode.  Unit {crawl}.
        /// </summary>
        public const string CrawlActive = "crawlsharp.crawl.active";

        /// <summary>
        /// Gauge holding the Unix time, in seconds, at which a crawl job last completed successfully in this process.  Unit s.
        /// </summary>
        public const string CrawlLastSuccess = "crawlsharp.crawl.last_success";

        /// <summary>
        /// Histogram of per-stage duration, by stage and outcome.  Unit s.
        /// </summary>
        public const string StageDuration = "crawlsharp.crawl.stage.duration";

        /// <summary>
        /// Counter of stage executions, by stage and outcome.  Unit {event}.
        /// </summary>
        public const string StageEvents = "crawlsharp.crawl.stage.events";

        /// <summary>
        /// Counter of pages processed from the crawl queue, by mode, outcome and status class.  Unit {page}.
        /// </summary>
        public const string Pages = "crawlsharp.pages";

        /// <summary>
        /// Histogram of retrieved page body size, by mode.  Unit By.
        /// </summary>
        public const string PageSize = "crawlsharp.page.size";

        /// <summary>
        /// Counter of links found in retrieved pages.  Unit {link}.
        /// </summary>
        public const string LinksDiscovered = "crawlsharp.links.discovered";

        /// <summary>
        /// Counter of links added to the crawl queue, by source.  Unit {link}.
        /// </summary>
        public const string LinksEnqueued = "crawlsharp.links.enqueued";

        /// <summary>
        /// Counter of links not retrieved, by reason.  Unit {link}.
        /// </summary>
        public const string LinksSkipped = "crawlsharp.links.skipped";

        /// <summary>
        /// Counter of pages whose retrieval involved a redirect, by redirect outcome.  Unit {page}.
        /// </summary>
        public const string Redirects = "crawlsharp.redirects";

        /// <summary>
        /// Histogram of redirect hops per redirected page.  Unit {hop}.
        /// </summary>
        public const string RedirectHops = "crawlsharp.redirect.hops";

        /// <summary>
        /// Counter of outbound calls, by integration service, operation and outcome.  Unit {request}.
        /// </summary>
        public const string IntegrationRequests = "crawlsharp.integration.requests";

        /// <summary>
        /// Histogram of outbound call duration, by integration service, operation and outcome.  Unit s.
        /// </summary>
        public const string IntegrationDuration = "crawlsharp.integration.duration";

        /// <summary>
        /// Counter of retried outbound calls, by integration service and reason.  Unit {retry}.
        /// </summary>
        public const string Retries = "crawlsharp.retries";

        /// <summary>
        /// Counter of errors caught by the crawler, by stage and error type.  Unit {error}.
        /// </summary>
        public const string Errors = "crawlsharp.errors";

        /// <summary>
        /// Up-down counter of links waiting in crawl queues.  Unit {link}.
        /// </summary>
        public const string QueueSize = "crawlsharp.queue.size";

        /// <summary>
        /// Up-down counter of worker slots currently held (pages being retrieved).  Unit {worker}.
        /// </summary>
        public const string WorkersInUse = "crawlsharp.workers.in_use";

        /// <summary>
        /// Up-down counter of worker slots available to running crawls (the sum of MaxParallelTasks).  Unit {worker}.
        /// </summary>
        public const string WorkersCapacity = "crawlsharp.workers.capacity";

        /// <summary>
        /// Up-down counter of retrieved resources waiting to be read by the consumer of the crawl.  Unit {resource}.
        /// </summary>
        public const string ResultsBuffered = "crawlsharp.results.buffered";

        /// <summary>
        /// Up-down counter of URLs held in visited-link tables.  Unit {url}.
        /// </summary>
        public const string VisitedSize = "crawlsharp.visited.size";

        /// <summary>
        /// Up-down counter of open headless browser contexts.  Unit {context}.
        /// </summary>
        public const string BrowserContextsActive = "crawlsharp.browser.contexts.active";

        /// <summary>
        /// Counter of collapsible elements expanded by headless auto-expand, by kind.  Unit {change}.
        /// </summary>
        public const string AutoExpandChanges = "crawlsharp.autoexpand.changes";

        /// <summary>
        /// Counter of URLs read from sitemap.xml files.  Unit {url}.
        /// </summary>
        public const string SitemapUrls = "crawlsharp.sitemap.urls";

        /// <summary>
        /// Gauge with value 1 carrying the library version as a label.  Unit 1.
        /// </summary>
        public const string BuildInfo = "crawlsharp.build.info";

        #endregion

        #region Server-Metrics

        /// <summary>
        /// Counter of crawl requests handled by the server, by outcome.  Unit {request}.
        /// </summary>
        public const string ServerCrawlRequests = "crawlsharp.server.crawl.requests";

        /// <summary>
        /// Histogram of crawl request duration in the server, from request to the final event, by outcome.  Unit s.
        /// </summary>
        public const string ServerCrawlRequestDuration = "crawlsharp.server.crawl.request.duration";

        /// <summary>
        /// Up-down counter of server-sent-event crawl streams currently open.  Unit {stream}.
        /// </summary>
        public const string ServerCrawlStreamsActive = "crawlsharp.server.crawl.streams.active";

        /// <summary>
        /// Histogram of server stage duration (deserialize, crawler_init, sse_send), by stage and outcome.  Unit s.
        /// </summary>
        public const string ServerStageDuration = "crawlsharp.server.stage.duration";

        /// <summary>
        /// Counter of server-sent events written to crawl streams.  Unit {event}.
        /// </summary>
        public const string ServerSseEvents = "crawlsharp.server.sse.events";

        /// <summary>
        /// Counter of bytes written as server-sent event payloads.  Unit By.
        /// </summary>
        public const string ServerSseBytes = "crawlsharp.server.sse.bytes";

        /// <summary>
        /// Gauge with value 1 carrying the server version and runtime as labels.  Unit 1.
        /// </summary>
        public const string ServerBuildInfo = "crawlsharp.server.build.info";

        /// <summary>
        /// Gauge with value 1 carrying non-secret telemetry configuration as labels.  Unit 1.
        /// </summary>
        public const string ServerConfigInfo = "crawlsharp.server.config.info";

        #endregion

        #region Spans

        /// <summary>
        /// Root span of a crawl job.
        /// </summary>
        public const string SpanCrawl = "crawl";

        /// <summary>
        /// Span for one queued link, from waiting for a worker slot to queuing its child links.
        /// </summary>
        public const string SpanPage = "crawl.page";

        /// <summary>
        /// Prefix of stage span names; a stage span is named "stage:" plus the stage name, for example "stage:fetch".
        /// </summary>
        public const string SpanStagePrefix = "stage:";

        /// <summary>
        /// Server span around one crawl request, nested under Watson's HTTP server span.
        /// </summary>
        public const string SpanServerCrawlRequest = "crawlsharp.server crawl";

        #endregion

        #region Stages

        /// <summary>
        /// Stage: retrieve and parse robots.txt.
        /// </summary>
        public const string StageRobots = "robots";

        /// <summary>
        /// Stage: retrieve and parse sitemap.xml.
        /// </summary>
        public const string StageSitemap = "sitemap";

        /// <summary>
        /// Stage: a queued link waiting for a worker slot.
        /// </summary>
        public const string StageQueued = "queued";

        /// <summary>
        /// Stage: retrieve one URL, including redirects and retries.
        /// </summary>
        public const string StageFetch = "fetch";

        /// <summary>
        /// Stage: the HEAD request that resolves redirects and the content type before headless retrieval.
        /// </summary>
        public const string StageContentTypeCheck = "content_type_check";

        /// <summary>
        /// Stage: headless browser retrieval of one page.
        /// </summary>
        public const string StageBrowserNavigate = "browser_navigate";

        /// <summary>
        /// Stage: headless expansion of collapsible content.
        /// </summary>
        public const string StageAutoExpand = "auto_expand";

        /// <summary>
        /// Stage: extract, filter and queue the links of a retrieved page.
        /// </summary>
        public const string StageLinkExtraction = "link_extraction";

        /// <summary>
        /// Stage: the configured or robots.txt delay between requests.
        /// </summary>
        public const string StagePolitenessDelay = "politeness_delay";

        /// <summary>
        /// Stage: the backoff before retrying a throttled (429) request.
        /// </summary>
        public const string StageRetryBackoff = "retry_backoff";

        /// <summary>
        /// Stage: the configured pause after a throttled (429) request that is not retried.
        /// </summary>
        public const string StageThrottleDelay = "throttle_delay";

        /// <summary>
        /// Stage: start the headless browser when a crawler is constructed.
        /// </summary>
        public const string StageBrowserStartup = "browser_startup";

        /// <summary>
        /// Server stage: deserialize the crawl request body.
        /// </summary>
        public const string ServerStageDeserialize = "deserialize";

        /// <summary>
        /// Server stage: construct and validate the crawler.
        /// </summary>
        public const string ServerStageCrawlerInit = "crawler_init";

        /// <summary>
        /// Server stage: write one server-sent event to the client.
        /// </summary>
        public const string ServerStageSseSend = "sse_send";

        #endregion

        #region Attributes

        /// <summary>
        /// Label and span attribute: outcome of an operation.
        /// </summary>
        public const string AttributeOutcome = "crawlsharp.outcome";

        /// <summary>
        /// Label and span attribute: crawl mode, "rest" or "headless".
        /// </summary>
        public const string AttributeMode = "crawlsharp.crawl.mode";

        /// <summary>
        /// Label and span attribute: stage name.
        /// </summary>
        public const string AttributeStage = "crawlsharp.stage";

        /// <summary>
        /// Label: HTTP status class of a page, "1xx" through "5xx", or "none".
        /// </summary>
        public const string AttributeStatusClass = "crawlsharp.status_class";

        /// <summary>
        /// Label: why a link was skipped.
        /// </summary>
        public const string AttributeReason = "crawlsharp.reason";

        /// <summary>
        /// Label: where a queued link came from, "start", "sitemap" or "page".
        /// </summary>
        public const string AttributeLinkSource = "crawlsharp.link.source";

        /// <summary>
        /// Label and span attribute: redirect outcome, for example "followed" or "loop_detected".
        /// </summary>
        public const string AttributeRedirectOutcome = "crawlsharp.redirect.outcome";

        /// <summary>
        /// Label and span attribute: integration service, "http" or "playwright".
        /// </summary>
        public const string AttributeIntegrationService = "crawlsharp.integration.service";

        /// <summary>
        /// Label and span attribute: integration operation, for example "GET", "HEAD" or "navigate".
        /// </summary>
        public const string AttributeIntegrationOperation = "crawlsharp.integration.operation";

        /// <summary>
        /// Label: kind of element expanded by auto-expand, "details", "builtin" or "custom".
        /// </summary>
        public const string AttributeAutoExpandKind = "crawlsharp.autoexpand.kind";

        /// <summary>
        /// Label: library or server version.
        /// </summary>
        public const string AttributeVersion = "crawlsharp.version";

        /// <summary>
        /// Label: .NET runtime description.
        /// </summary>
        public const string AttributeRuntime = "crawlsharp.runtime";

        /// <summary>
        /// Label (OpenTelemetry semantic convention): the exception type of a failure.
        /// </summary>
        public const string AttributeErrorType = "error.type";

        /// <summary>
        /// Span attribute: crawl depth of a page.
        /// </summary>
        public const string AttributeDepth = "crawlsharp.depth";

        /// <summary>
        /// Span attribute: purpose of a fetch, "page", "robots" or "sitemap".
        /// </summary>
        public const string AttributePurpose = "crawlsharp.fetch.purpose";

        /// <summary>
        /// Span attribute: number of redirect hops.
        /// </summary>
        public const string AttributeRedirectHops = "crawlsharp.redirect.hops";

        /// <summary>
        /// Span attribute: zero-based retry attempt of an outbound call.
        /// </summary>
        public const string AttributeRetryAttempt = "crawlsharp.retry.attempt";

        /// <summary>
        /// Span attribute: delay applied, in milliseconds.
        /// </summary>
        public const string AttributeDelayMs = "crawlsharp.delay_ms";

        /// <summary>
        /// Span attribute: number of links found, queued or skipped.
        /// </summary>
        public const string AttributeLinkCount = "crawlsharp.link.count";

        /// <summary>
        /// Span attribute: number of resources returned by a crawl.
        /// </summary>
        public const string AttributeResourceCount = "crawlsharp.crawl.resources";

        /// <summary>
        /// Span attribute: configured maximum crawl depth.
        /// </summary>
        public const string AttributeMaxDepth = "crawlsharp.crawl.max_depth";

        /// <summary>
        /// Span attribute: configured maximum parallel tasks.
        /// </summary>
        public const string AttributeMaxParallelTasks = "crawlsharp.crawl.max_parallel_tasks";

        /// <summary>
        /// Span attribute: whether links are followed.
        /// </summary>
        public const string AttributeFollowLinks = "crawlsharp.crawl.follow_links";

        /// <summary>
        /// Span attribute: body size of a retrieved page, in bytes.
        /// </summary>
        public const string AttributePageSize = "crawlsharp.page.size";

        /// <summary>
        /// Span attribute (OpenTelemetry semantic convention): URL without query string, fragment or user information.
        /// </summary>
        public const string AttributeUrlFull = "url.full";

        /// <summary>
        /// Span attribute (OpenTelemetry semantic convention): host of the remote server.
        /// </summary>
        public const string AttributeServerAddress = "server.address";

        /// <summary>
        /// Span attribute (OpenTelemetry semantic convention): port of the remote server.
        /// </summary>
        public const string AttributeServerPort = "server.port";

        /// <summary>
        /// Span attribute (OpenTelemetry semantic convention): HTTP request method.
        /// </summary>
        public const string AttributeHttpMethod = "http.request.method";

        /// <summary>
        /// Span attribute (OpenTelemetry semantic convention): HTTP response status code.
        /// </summary>
        public const string AttributeHttpStatusCode = "http.response.status_code";

        /// <summary>
        /// Span attribute: number of server-sent events written.
        /// </summary>
        public const string AttributeSseEvents = "crawlsharp.server.sse.events";

        #endregion

        #region Outcomes

        /// <summary>
        /// Outcome: the operation succeeded.
        /// </summary>
        public const string OutcomeSuccess = "success";

        /// <summary>
        /// Outcome: the operation failed with an error.
        /// </summary>
        public const string OutcomeFailure = "failure";

        /// <summary>
        /// Outcome: the operation was skipped (disabled by settings, or not applicable).
        /// </summary>
        public const string OutcomeSkipped = "skipped";

        /// <summary>
        /// Outcome: the operation was cancelled through its cancellation token.
        /// </summary>
        public const string OutcomeCancelled = "cancelled";

        /// <summary>
        /// Outcome: the requested resource (robots.txt, sitemap.xml) was not available.
        /// </summary>
        public const string OutcomeNotFound = "not_found";

        /// <summary>
        /// Crawl outcome: every queued link was processed.
        /// </summary>
        public const string OutcomeCompleted = "completed";

        /// <summary>
        /// Crawl outcome: the consumer stopped reading before the crawl finished.
        /// </summary>
        public const string OutcomeAbandoned = "abandoned";

        /// <summary>
        /// Page outcome: the server answered with a 4xx or 5xx status.
        /// </summary>
        public const string OutcomeHttpError = "http_error";

        /// <summary>
        /// Page outcome: the redirect chain stopped before reaching content (loop, limit, scope, robots, not followed).
        /// </summary>
        public const string OutcomeRedirectStopped = "redirect_stopped";

        /// <summary>
        /// Integration outcome: the remote answered with a 4xx status other than 429.
        /// </summary>
        public const string OutcomeClientError = "client_error";

        /// <summary>
        /// Integration outcome: the remote answered with a 5xx status.
        /// </summary>
        public const string OutcomeServerError = "server_error";

        /// <summary>
        /// Integration outcome: the remote answered 429 Too Many Requests.
        /// </summary>
        public const string OutcomeThrottled = "throttled";

        /// <summary>
        /// Integration outcome: the call timed out.
        /// </summary>
        public const string OutcomeTimeout = "timeout";

        /// <summary>
        /// Integration outcome: the browser navigation was aborted because it redirected and is resolved by the crawler.
        /// </summary>
        public const string OutcomeRedirected = "redirected";

        /// <summary>
        /// Integration outcome: the browser navigation started a download and the REST client was used instead.
        /// </summary>
        public const string OutcomeDownload = "download";

        #endregion

        #region Values

        /// <summary>
        /// Crawl mode: plain HTTP retrieval.
        /// </summary>
        public const string ModeRest = "rest";

        /// <summary>
        /// Crawl mode: headless browser retrieval.
        /// </summary>
        public const string ModeHeadless = "headless";

        /// <summary>
        /// Integration service: HTTP requests to crawled sites.
        /// </summary>
        public const string ServiceHttp = "http";

        /// <summary>
        /// Integration service: the Playwright headless browser.
        /// </summary>
        public const string ServicePlaywright = "playwright";

        /// <summary>
        /// Playwright operation: install the browser.
        /// </summary>
        public const string OperationInstall = "install";

        /// <summary>
        /// Playwright operation: launch the browser.
        /// </summary>
        public const string OperationLaunch = "launch";

        /// <summary>
        /// Playwright operation: navigate a page.
        /// </summary>
        public const string OperationNavigate = "navigate";

        /// <summary>
        /// Playwright operation: fetch a routed request with credentials.
        /// </summary>
        public const string OperationRouteFetch = "route_fetch";

        /// <summary>
        /// Link source: the start URL.
        /// </summary>
        public const string LinkSourceStart = "start";

        /// <summary>
        /// Link source: sitemap.xml.
        /// </summary>
        public const string LinkSourceSitemap = "sitemap";

        /// <summary>
        /// Link source: a link in a retrieved page.
        /// </summary>
        public const string LinkSourcePage = "page";

        /// <summary>
        /// Fetch purpose: a queued page.
        /// </summary>
        public const string PurposePage = "page";

        /// <summary>
        /// Fetch purpose: robots.txt.
        /// </summary>
        public const string PurposeRobots = "robots";

        /// <summary>
        /// Fetch purpose: sitemap.xml.
        /// </summary>
        public const string PurposeSitemap = "sitemap";

        /// <summary>
        /// Retry reason: the remote answered 429.
        /// </summary>
        public const string RetryReasonThrottled = "throttled";

        /// <summary>
        /// Skip reason: the URL was already retrieved.
        /// </summary>
        public const string ReasonAlreadyVisited = "already_visited";

        /// <summary>
        /// Skip reason: the URL is already queued.
        /// </summary>
        public const string ReasonAlreadyQueued = "already_queued";

        /// <summary>
        /// Skip reason: the URL is being retrieved by another worker.
        /// </summary>
        public const string ReasonInProcessing = "in_processing";

        /// <summary>
        /// Skip reason: a redirect led to a resource that was already returned.
        /// </summary>
        public const string ReasonDuplicate = "duplicate";

        /// <summary>
        /// Skip reason: the URL is malformed or cannot be normalized.
        /// </summary>
        public const string ReasonInvalidUrl = "invalid_url";

        /// <summary>
        /// Skip reason: the URL is not http or https.
        /// </summary>
        public const string ReasonNonHttp = "non_http";

        /// <summary>
        /// Skip reason: robots.txt disallows the URL.
        /// </summary>
        public const string ReasonRobotsDisallowed = "robots_disallowed";

        /// <summary>
        /// Skip reason: the page is at the maximum crawl depth, so its links are not followed.
        /// </summary>
        public const string ReasonMaxDepth = "max_depth";

        /// <summary>
        /// Skip reason: FollowLinks is false.
        /// </summary>
        public const string ReasonFollowLinksDisabled = "follow_links_disabled";

        /// <summary>
        /// Skip reason: the domain is in DeniedDomains.
        /// </summary>
        public const string ReasonDeniedDomain = "denied_domain";

        /// <summary>
        /// Skip reason: the URL is outside the start URL's root domain.
        /// </summary>
        public const string ReasonOutsideRootDomain = "outside_root_domain";

        /// <summary>
        /// Skip reason: the URL is outside the start URL's subdomain.
        /// </summary>
        public const string ReasonOutsideSubdomain = "outside_subdomain";

        /// <summary>
        /// Skip reason: the URL is not a child of the start URL.
        /// </summary>
        public const string ReasonNotChildUrl = "not_child_url";

        /// <summary>
        /// Skip reason: the domain is not in AllowedDomains.
        /// </summary>
        public const string ReasonNotAllowedDomain = "not_allowed_domain";

        /// <summary>
        /// Skip reason: the URL is external and FollowExternalLinks is false.
        /// </summary>
        public const string ReasonExternal = "external";

        /// <summary>
        /// Skip reason: the URL matches an exclusion pattern.
        /// </summary>
        public const string ReasonExcluded = "excluded";

        /// <summary>
        /// Auto-expand kind: a details element was opened.
        /// </summary>
        public const string AutoExpandDetails = "details";

        /// <summary>
        /// Auto-expand kind: a built-in selector target was clicked.
        /// </summary>
        public const string AutoExpandBuiltIn = "builtin";

        /// <summary>
        /// Auto-expand kind: a custom selector target was clicked.
        /// </summary>
        public const string AutoExpandCustom = "custom";

        #endregion
    }
}
