namespace CrawlSharp.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Threading;

    /// <summary>
    /// The library's meter, activity source and instruments, plus best-effort helpers that record into them.
    /// Every helper swallows its own failures: instrumentation never changes crawler behavior.
    /// Thread safe; the instruments are shared by every crawler in the process.
    /// </summary>
    internal static class CrawlInstruments
    {
        #region Internal-Members

        /// <summary>
        /// Library version stamped on the meter, the activity source and the build-info gauge.
        /// </summary>
        internal static readonly string Version = GetVersion();

        /// <summary>
        /// The library meter.
        /// </summary>
        internal static readonly Meter Meter = new Meter(CrawlSharpTelemetry.MeterName, Version);

        /// <summary>
        /// The library activity source.
        /// </summary>
        internal static readonly ActivitySource Source = new ActivitySource(CrawlSharpTelemetry.ActivitySourceName, Version);

        internal static readonly Counter<long> CrawlJobs = Meter.CreateCounter<long>(CrawlSharpTelemetry.CrawlJobs, "{crawl}", "Crawl jobs that finished, by mode and outcome.");
        internal static readonly Histogram<double> CrawlDuration = Meter.CreateHistogram<double>(CrawlSharpTelemetry.CrawlDuration, "s", "End-to-end crawl job duration.");
        internal static readonly UpDownCounter<long> CrawlActive = Meter.CreateUpDownCounter<long>(CrawlSharpTelemetry.CrawlActive, "{crawl}", "Crawl jobs currently running.");
        internal static readonly Histogram<double> StageDuration = Meter.CreateHistogram<double>(CrawlSharpTelemetry.StageDuration, "s", "Duration of each crawl stage.");
        internal static readonly Counter<long> StageEvents = Meter.CreateCounter<long>(CrawlSharpTelemetry.StageEvents, "{event}", "Crawl stage executions, by stage and outcome.");
        internal static readonly Counter<long> Pages = Meter.CreateCounter<long>(CrawlSharpTelemetry.Pages, "{page}", "Pages processed from the crawl queue.");
        internal static readonly Histogram<long> PageSize = Meter.CreateHistogram<long>(CrawlSharpTelemetry.PageSize, "By", "Retrieved page body size.");
        internal static readonly Counter<long> LinksDiscovered = Meter.CreateCounter<long>(CrawlSharpTelemetry.LinksDiscovered, "{link}", "Links found in retrieved pages.");
        internal static readonly Counter<long> LinksEnqueued = Meter.CreateCounter<long>(CrawlSharpTelemetry.LinksEnqueued, "{link}", "Links added to the crawl queue, by source.");
        internal static readonly Counter<long> LinksSkipped = Meter.CreateCounter<long>(CrawlSharpTelemetry.LinksSkipped, "{link}", "Links not retrieved, by reason.");
        internal static readonly Counter<long> Redirects = Meter.CreateCounter<long>(CrawlSharpTelemetry.Redirects, "{page}", "Pages whose retrieval involved a redirect, by outcome.");
        internal static readonly Histogram<long> RedirectHops = Meter.CreateHistogram<long>(CrawlSharpTelemetry.RedirectHops, "{hop}", "Redirect hops per redirected page.");
        internal static readonly Counter<long> IntegrationRequests = Meter.CreateCounter<long>(CrawlSharpTelemetry.IntegrationRequests, "{request}", "Outbound calls, by service, operation and outcome.");
        internal static readonly Histogram<double> IntegrationDuration = Meter.CreateHistogram<double>(CrawlSharpTelemetry.IntegrationDuration, "s", "Outbound call duration.");
        internal static readonly Counter<long> Retries = Meter.CreateCounter<long>(CrawlSharpTelemetry.Retries, "{retry}", "Retried outbound calls, by service and reason.");
        internal static readonly Counter<long> Errors = Meter.CreateCounter<long>(CrawlSharpTelemetry.Errors, "{error}", "Errors caught by the crawler, by stage and error type.");
        internal static readonly UpDownCounter<long> QueueSize = Meter.CreateUpDownCounter<long>(CrawlSharpTelemetry.QueueSize, "{link}", "Links waiting in crawl queues.");
        internal static readonly UpDownCounter<long> WorkersInUse = Meter.CreateUpDownCounter<long>(CrawlSharpTelemetry.WorkersInUse, "{worker}", "Worker slots currently held.");
        internal static readonly UpDownCounter<long> WorkersCapacity = Meter.CreateUpDownCounter<long>(CrawlSharpTelemetry.WorkersCapacity, "{worker}", "Worker slots available to running crawls.");
        internal static readonly UpDownCounter<long> ResultsBuffered = Meter.CreateUpDownCounter<long>(CrawlSharpTelemetry.ResultsBuffered, "{resource}", "Retrieved resources waiting to be read by the consumer.");
        internal static readonly UpDownCounter<long> VisitedSize = Meter.CreateUpDownCounter<long>(CrawlSharpTelemetry.VisitedSize, "{url}", "URLs held in visited-link tables.");
        internal static readonly UpDownCounter<long> BrowserContextsActive = Meter.CreateUpDownCounter<long>(CrawlSharpTelemetry.BrowserContextsActive, "{context}", "Open headless browser contexts.");
        internal static readonly Counter<long> AutoExpandChanges = Meter.CreateCounter<long>(CrawlSharpTelemetry.AutoExpandChanges, "{change}", "Collapsible elements expanded, by kind.");
        internal static readonly Counter<long> SitemapUrls = Meter.CreateCounter<long>(CrawlSharpTelemetry.SitemapUrls, "{url}", "URLs read from sitemap.xml files.");

        #endregion

        #region Private-Members

        private static long _LastSuccessUnixMs = 0;

        #endregion

        #region Constructors-and-Factories

        static CrawlInstruments()
        {
            Meter.CreateObservableGauge<double>(
                CrawlSharpTelemetry.CrawlLastSuccess,
                ObserveLastSuccess,
                "s",
                "Unix time at which a crawl job last completed successfully.");

            Meter.CreateObservableGauge<int>(
                CrawlSharpTelemetry.BuildInfo,
                () => new Measurement<int>(1, new KeyValuePair<string, object>(CrawlSharpTelemetry.AttributeVersion, Version)),
                "1",
                "CrawlSharp library build information.");
        }

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Seconds elapsed since a <see cref="Stopwatch.GetTimestamp"/> value.
        /// </summary>
        internal static double ElapsedSeconds(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) / (double)Stopwatch.Frequency;
        }

        /// <summary>
        /// Start an activity, returning null when nothing listens or when the listener fails.
        /// A default <paramref name="parent"/> uses <see cref="Activity.Current"/>.
        /// </summary>
        internal static Activity StartActivity(string name, ActivityKind kind, ActivityContext parent = default)
        {
            try
            {
                if (!Source.HasListeners()) return null;
                return Source.StartActivity(name, kind, parent);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Start an activity without making it <see cref="Activity.Current"/>, for a long-lived root whose children are
        /// parented explicitly through its context.
        /// </summary>
        internal static Activity StartDetachedActivity(string name, ActivityKind kind)
        {
            Activity previous = Activity.Current;
            Activity activity = StartActivity(name, kind);
            if (activity != null) RestoreCurrent(previous);
            return activity;
        }

        /// <summary>
        /// Stop an activity and leave <see cref="Activity.Current"/> as it was before the stop.
        /// </summary>
        internal static void StopActivity(Activity activity, Activity restore)
        {
            if (activity == null) return;

            try
            {
                activity.Dispose();
            }
            catch (Exception)
            {
            }

            RestoreCurrent(restore);
        }

        /// <summary>
        /// Set <see cref="Activity.Current"/>, ignoring failures.
        /// </summary>
        internal static void RestoreCurrent(Activity activity)
        {
            try
            {
                if (!ReferenceEquals(Activity.Current, activity)) Activity.Current = activity;
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Set a tag on an activity, ignoring a null activity and failures.
        /// </summary>
        internal static void SetTag(Activity activity, string key, object value)
        {
            if (activity == null) return;

            try
            {
                activity.SetTag(key, value);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Mark an activity as failed and attach the exception as an OpenTelemetry "exception" event.
        /// </summary>
        internal static void SetError(Activity activity, Exception e)
        {
            if (activity == null) return;

            try
            {
                string type = e != null ? e.GetType().FullName : "unknown";
                activity.SetStatus(ActivityStatusCode.Error, e != null ? e.Message : null);
                activity.SetTag(CrawlSharpTelemetry.AttributeErrorType, type);

                if (e != null)
                {
                    ActivityTagsCollection tags = new ActivityTagsCollection
                    {
                        { "exception.type", type },
                        { "exception.message", e.Message },
                        { "exception.stacktrace", e.ToString() }
                    };

                    activity.AddEvent(new ActivityEvent("exception", DateTimeOffset.UtcNow, tags));
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Set an explicit status on an activity from an outcome label.
        /// </summary>
        internal static void SetOutcomeStatus(Activity activity, string outcome)
        {
            if (activity == null) return;

            try
            {
                activity.SetTag(CrawlSharpTelemetry.AttributeOutcome, outcome);
                if (activity.Status == ActivityStatusCode.Error) return;

                if (IsErrorOutcome(outcome)) activity.SetStatus(ActivityStatusCode.Error, outcome);
                else activity.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record the URL of a request on a span, without query string, fragment or user information.
        /// </summary>
        internal static void SetUrl(Activity activity, Uri uri)
        {
            if (activity == null || uri == null) return;

            try
            {
                string url = SanitizeUrl(uri);
                if (url != null) activity.SetTag(CrawlSharpTelemetry.AttributeUrlFull, url);

                if (uri.IsAbsoluteUri)
                {
                    activity.SetTag(CrawlSharpTelemetry.AttributeServerAddress, uri.Host);
                    activity.SetTag(CrawlSharpTelemetry.AttributeServerPort, uri.Port);
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record the URL of a request on a span, parsing it first; an unparseable URL is not recorded.
        /// </summary>
        internal static void SetUrl(Activity activity, string url)
        {
            if (activity == null || String.IsNullOrEmpty(url)) return;

            Uri uri;
            if (Uri.TryCreate(url, UriKind.Absolute, out uri)) SetUrl(activity, uri);
        }

        /// <summary>
        /// Scheme, host, port and path of a URL, dropping the query string, fragment and user information, which can carry secrets.
        /// </summary>
        internal static string SanitizeUrl(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri) return null;

            try
            {
                return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// HTTP status class label ("2xx"), or "none" when no status was received.
        /// </summary>
        internal static string StatusClass(int status)
        {
            if (status >= 100 && status <= 199) return "1xx";
            if (status >= 200 && status <= 299) return "2xx";
            if (status >= 300 && status <= 399) return "3xx";
            if (status >= 400 && status <= 499) return "4xx";
            if (status >= 500 && status <= 599) return "5xx";
            return "none";
        }

        /// <summary>
        /// Integration outcome for an HTTP status.
        /// </summary>
        internal static string OutcomeForStatus(int status)
        {
            if (status == 429) return CrawlSharpTelemetry.OutcomeThrottled;
            if (status >= 500) return CrawlSharpTelemetry.OutcomeServerError;
            if (status >= 400) return CrawlSharpTelemetry.OutcomeClientError;
            if (status < 100) return CrawlSharpTelemetry.OutcomeFailure;
            return CrawlSharpTelemetry.OutcomeSuccess;
        }

        /// <summary>
        /// Integration outcome for an exception thrown by an outbound call.
        /// </summary>
        internal static string OutcomeForException(Exception e, CancellationToken token)
        {
            if (e is OperationCanceledException)
                return token.IsCancellationRequested ? CrawlSharpTelemetry.OutcomeCancelled : CrawlSharpTelemetry.OutcomeTimeout;

            if (e is TimeoutException) return CrawlSharpTelemetry.OutcomeTimeout;
            return CrawlSharpTelemetry.OutcomeFailure;
        }

        /// <summary>
        /// Whether an outcome represents an error for span status purposes.
        /// </summary>
        internal static bool IsErrorOutcome(string outcome)
        {
            return outcome == CrawlSharpTelemetry.OutcomeFailure
                || outcome == CrawlSharpTelemetry.OutcomeTimeout
                || outcome == CrawlSharpTelemetry.OutcomeServerError;
        }

        /// <summary>
        /// Add to a counter or up-down counter with no labels.
        /// </summary>
        internal static void Add(Counter<long> counter, long value)
        {
            if (value == 0 || !counter.Enabled) return;

            try
            {
                counter.Add(value);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Add to a counter with one label.
        /// </summary>
        internal static void Add(Counter<long> counter, long value, string key, string label)
        {
            if (value == 0 || !counter.Enabled) return;

            try
            {
                counter.Add(value, new KeyValuePair<string, object>(key, label));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Add to an up-down counter with no labels.
        /// </summary>
        internal static void Add(UpDownCounter<long> counter, long value)
        {
            if (value == 0 || !counter.Enabled) return;

            try
            {
                counter.Add(value);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Add to an up-down counter with one label.
        /// </summary>
        internal static void Add(UpDownCounter<long> counter, long value, string key, string label)
        {
            if (value == 0 || !counter.Enabled) return;

            try
            {
                counter.Add(value, new KeyValuePair<string, object>(key, label));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record a skipped link.
        /// </summary>
        internal static void RecordSkipped(string reason, long count = 1)
        {
            Add(LinksSkipped, count, CrawlSharpTelemetry.AttributeReason, reason);
        }

        /// <summary>
        /// Record one stage execution.
        /// </summary>
        internal static void RecordStage(string stage, string outcome, double seconds)
        {
            try
            {
                TagList tags = new TagList
                {
                    { CrawlSharpTelemetry.AttributeStage, stage },
                    { CrawlSharpTelemetry.AttributeOutcome, outcome }
                };

                if (StageDuration.Enabled) StageDuration.Record(seconds, tags);
                if (StageEvents.Enabled) StageEvents.Add(1, tags);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record a caught error.
        /// </summary>
        internal static void RecordError(string stage, Exception e)
        {
            if (!Errors.Enabled) return;

            try
            {
                TagList tags = new TagList
                {
                    { CrawlSharpTelemetry.AttributeStage, stage },
                    { CrawlSharpTelemetry.AttributeErrorType, e != null ? e.GetType().FullName : "unknown" }
                };

                Errors.Add(1, tags);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record one outbound call.
        /// </summary>
        internal static void RecordIntegration(string service, string operation, string outcome, double seconds, Exception e)
        {
            try
            {
                TagList tags = new TagList
                {
                    { CrawlSharpTelemetry.AttributeIntegrationService, service },
                    { CrawlSharpTelemetry.AttributeIntegrationOperation, operation },
                    { CrawlSharpTelemetry.AttributeOutcome, outcome }
                };

                if (IntegrationDuration.Enabled) IntegrationDuration.Record(seconds, tags);

                if (e != null) tags.Add(CrawlSharpTelemetry.AttributeErrorType, e.GetType().FullName);
                if (IntegrationRequests.Enabled) IntegrationRequests.Add(1, tags);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record a retried outbound call.
        /// </summary>
        internal static void RecordRetry(string service, string reason)
        {
            if (!Retries.Enabled) return;

            try
            {
                TagList tags = new TagList
                {
                    { CrawlSharpTelemetry.AttributeIntegrationService, service },
                    { CrawlSharpTelemetry.AttributeReason, reason }
                };

                Retries.Add(1, tags);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record a finished crawl job.
        /// </summary>
        internal static void RecordCrawl(string mode, string outcome, double seconds)
        {
            try
            {
                TagList tags = new TagList
                {
                    { CrawlSharpTelemetry.AttributeMode, mode },
                    { CrawlSharpTelemetry.AttributeOutcome, outcome }
                };

                if (CrawlJobs.Enabled) CrawlJobs.Add(1, tags);
                if (CrawlDuration.Enabled) CrawlDuration.Record(seconds, tags);

                if (outcome == CrawlSharpTelemetry.OutcomeCompleted)
                    Interlocked.Exchange(ref _LastSuccessUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record a page taken from the crawl queue.
        /// </summary>
        internal static void RecordPage(string mode, string outcome, int status, long size)
        {
            try
            {
                if (Pages.Enabled)
                {
                    TagList tags = new TagList
                    {
                        { CrawlSharpTelemetry.AttributeMode, mode },
                        { CrawlSharpTelemetry.AttributeOutcome, outcome },
                        { CrawlSharpTelemetry.AttributeStatusClass, StatusClass(status) }
                    };

                    Pages.Add(1, tags);
                }

                if (size >= 0 && PageSize.Enabled)
                    PageSize.Record(size, new KeyValuePair<string, object>(CrawlSharpTelemetry.AttributeMode, mode));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record the redirect outcome of a page.
        /// </summary>
        internal static void RecordRedirect(string outcome, int hops)
        {
            try
            {
                Add(Redirects, 1, CrawlSharpTelemetry.AttributeRedirectOutcome, outcome);
                if (hops > 0 && RedirectHops.Enabled) RedirectHops.Record(hops);
            }
            catch (Exception)
            {
            }
        }

        #endregion

        #region Private-Methods

        private static IEnumerable<Measurement<double>> ObserveLastSuccess()
        {
            long ms = Interlocked.Read(ref _LastSuccessUnixMs);
            if (ms <= 0) return Array.Empty<Measurement<double>>();
            return new[] { new Measurement<double>(ms / 1000.0) };
        }

        private static string GetVersion()
        {
            try
            {
                Version version = typeof(CrawlInstruments).Assembly.GetName().Version;
                return version != null ? version.ToString(3) : "0.0.0";
            }
            catch (Exception)
            {
                return "0.0.0";
            }
        }

        #endregion
    }
}
