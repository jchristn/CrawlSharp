namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using CrawlSharp.Telemetry;
    using CrawlSharp.Web;
    using Touchstone.Core;

    /// <summary>
    /// Proves the library emits its documented metrics and spans: the crawl job and every pipeline stage, pages and links,
    /// outbound HTTP calls, retries, redirects, the queue and worker gauges, and the failure paths (HTTP errors, connection
    /// failures, cancellation, abandoned crawls).  Also proves that crawling works with no listener and with a listener that throws.
    /// </summary>
    public static class TelemetrySuite
    {
        private const string Id = "Telemetry";
        private const int TimeoutSeconds = 30;

        /// <summary>
        /// Build the suite.
        /// </summary>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case.Sync(Id, "Names_Stable", "Meter, activity source and instrument names match the documented contract", () =>
                {
                    Check.Equal("CrawlSharp", CrawlSharpTelemetry.MeterName);
                    Check.Equal("CrawlSharp", CrawlSharpTelemetry.ActivitySourceName);
                    Check.Equal("CrawlSharp.Server", CrawlSharpTelemetry.ServerMeterName);
                    Check.Equal("CrawlSharp.Server", CrawlSharpTelemetry.ServerActivitySourceName);
                    Check.Equal("crawlsharp.crawl.jobs", CrawlSharpTelemetry.CrawlJobs);
                    Check.Equal("crawlsharp.crawl.stage.duration", CrawlSharpTelemetry.StageDuration);
                    Check.Equal("crawlsharp.integration.duration", CrawlSharpTelemetry.IntegrationDuration);
                    Check.Equal("stage:", CrawlSharpTelemetry.SpanStagePrefix);
                }),

                Case.Async(Id, "NoListener_CrawlSucceeds", "A crawl with nothing subscribed emits nothing and still succeeds", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/", "<html><body>quiet</body></html>");

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(CrawlHelper.CreateSettings(server.UrlFor("/")), TimeoutSeconds, ct);
                    Check.Equal(200, resource.Status);
                }),

                Case.Async(Id, "ThrowingListener_CrawlSucceeds", "A subscriber that throws on every measurement and span cannot break a crawl", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/", "<html><body><a href=\"/a\">a</a></body></html>");
                    server.AddHtml("/a", "<html><body>a</body></html>");

                    using MeterListener meterListener = new MeterListener();
                    meterListener.InstrumentPublished = (instrument, listener) =>
                    {
                        if (instrument.Meter.Name == CrawlSharpTelemetry.MeterName) listener.EnableMeasurementEvents(instrument);
                    };
                    meterListener.SetMeasurementEventCallback<long>((i, v, t, s) => throw new InvalidOperationException("listener failure"));
                    meterListener.SetMeasurementEventCallback<double>((i, v, t, s) => throw new InvalidOperationException("listener failure"));
                    meterListener.Start();

                    using ActivityListener activityListener = new ActivityListener
                    {
                        ShouldListenTo = source => source.Name == CrawlSharpTelemetry.ActivitySourceName,
                        Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
                        ActivityStarted = a => throw new InvalidOperationException("listener failure"),
                        ActivityStopped = a => throw new InvalidOperationException("listener failure")
                    };
                    ActivitySource.AddActivityListener(activityListener);

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"), configure: c =>
                    {
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 1;
                    });

                    List<WebResource> resources = await CrawlHelper.CrawlAllWithinAsync(settings, TimeoutSeconds, ct);
                    Check.Count(2, resources);
                }),

                Case.Async(Id, "Crawl_TraceShape", "A crawl produces one root span with page, stage and HTTP client spans in one trace", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/", "<html><body><a href=\"/a\">a</a><a href=\"/b\">b</a></body></html>");
                    server.AddHtml("/a", "<html><body>a</body></html>");
                    server.AddHtml("/b", "<html><body>b</body></html>");

                    using TelemetryCapture capture = new TelemetryCapture(CrawlSharpTelemetry.ActivitySourceName);
                    ActivityTraceId traceId;

                    using (Activity parent = TelemetryCapture.StartTestSpan("test"))
                    {
                        traceId = parent.TraceId;
                        Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"), configure: c =>
                        {
                            c.FollowLinks = true;
                            c.MaxCrawlDepth = 1;
                        });

                        List<WebResource> resources = await CrawlHelper.CrawlAllWithinAsync(settings, TimeoutSeconds, ct);
                        Check.Count(3, resources);
                    }

                    Activity root = Check.Single(capture.Spans(CrawlSharpTelemetry.SpanCrawl, traceId));
                    Check.Equal(ActivityStatusCode.Ok, root.Status);
                    Check.Equal(CrawlSharpTelemetry.OutcomeCompleted, root.GetTagItem(CrawlSharpTelemetry.AttributeOutcome) as string);
                    Check.Equal(CrawlSharpTelemetry.ModeRest, root.GetTagItem(CrawlSharpTelemetry.AttributeMode) as string);
                    Check.Equal(3, Convert.ToInt32(root.GetTagItem(CrawlSharpTelemetry.AttributeResourceCount)));

                    List<Activity> pages = capture.Spans(CrawlSharpTelemetry.SpanPage, traceId);
                    Check.Count(3, pages);
                    Check.True(pages.All(p => p.ParentSpanId == root.SpanId), "Every page span is a child of the crawl span.");
                    Check.True(pages.All(p => p.Status == ActivityStatusCode.Ok), "Every page span succeeded.");

                    Check.Count(3, capture.Spans("stage:queued", traceId));
                    Check.Count(3, capture.Spans("stage:fetch", traceId));
                    Check.Count(3, capture.Spans("stage:link_extraction", traceId));

                    Activity robots = Check.Single(capture.Spans("stage:robots", traceId));
                    Check.Equal(root.SpanId, robots.ParentSpanId);
                    Check.Equal(CrawlSharpTelemetry.OutcomeSkipped, robots.GetTagItem(CrawlSharpTelemetry.AttributeOutcome) as string);
                    Check.Single(capture.Spans("stage:sitemap", traceId));

                    List<Activity> gets = capture.Spans("http GET", traceId);
                    Check.Count(3, gets);
                    Check.True(gets.All(g => g.Kind == ActivityKind.Client), "HTTP spans are client spans.");
                    Check.True(gets.All(g => Convert.ToInt32(g.GetTagItem(CrawlSharpTelemetry.AttributeHttpStatusCode)) == 200), "HTTP spans carry the status code.");
                    Check.True(gets.All(g => (g.GetTagItem(CrawlSharpTelemetry.AttributeUrlFull) as string).StartsWith(server.BaseUrl, StringComparison.Ordinal)), "HTTP spans carry the URL.");

                    Activity fetch = capture.Spans("stage:fetch", traceId).First();
                    Activity fetchParent = pages.FirstOrDefault(p => p.SpanId == fetch.ParentSpanId);
                    Check.NotNull(fetchParent, "Fetch stages nest under page spans.");
                }),

                Case.Async(Id, "Crawl_JobAndGaugeMetrics", "Job, page, link and gauge metrics are recorded and gauges return to zero", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/", "<html><body><a href=\"/a\">a</a><a href=\"http://external.invalid/x\">x</a></body></html>");
                    server.AddHtml("/a", "<html><body>a</body></html>");

                    using TelemetryCapture capture = new TelemetryCapture(CrawlSharpTelemetry.MeterName);

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"), configure: c =>
                    {
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 1;
                        c.MaxParallelTasks = 4;
                    });

                    List<WebResource> resources = await CrawlHelper.CrawlAllWithinAsync(settings, TimeoutSeconds, ct);
                    Check.Count(2, resources);

                    Dictionary<string, string> completed = Tags(CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeCompleted, CrawlSharpTelemetry.AttributeMode, CrawlSharpTelemetry.ModeRest);
                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.CrawlJobs, completed));
                    Check.Equal(1, capture.Count(CrawlSharpTelemetry.CrawlDuration, completed));

                    Check.Equal(2.0, capture.Sum(CrawlSharpTelemetry.Pages, Tags(CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeSuccess, CrawlSharpTelemetry.AttributeStatusClass, "2xx")));
                    Check.Equal(2, capture.Count(CrawlSharpTelemetry.PageSize));
                    Check.True(capture.Sum(CrawlSharpTelemetry.LinksDiscovered) >= 2, "Links found in pages are counted.");
                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.LinksEnqueued, Tags(CrawlSharpTelemetry.AttributeLinkSource, CrawlSharpTelemetry.LinkSourceStart)));
                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.LinksEnqueued, Tags(CrawlSharpTelemetry.AttributeLinkSource, CrawlSharpTelemetry.LinkSourcePage)));
                    Check.True(capture.Sum(CrawlSharpTelemetry.LinksSkipped, Tags(CrawlSharpTelemetry.AttributeReason, CrawlSharpTelemetry.ReasonOutsideRootDomain)) >= 1, "The external link is skipped with a scope reason.");

                    Check.Equal(2.0, capture.Sum(CrawlSharpTelemetry.IntegrationRequests, Tags(CrawlSharpTelemetry.AttributeIntegrationService, "http", CrawlSharpTelemetry.AttributeIntegrationOperation, "GET", CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeSuccess)));
                    Check.Equal(2, capture.Count(CrawlSharpTelemetry.IntegrationDuration, Tags(CrawlSharpTelemetry.AttributeIntegrationService, "http")));

                    Check.Equal(2, capture.Count(CrawlSharpTelemetry.StageDuration, Tags(CrawlSharpTelemetry.AttributeStage, CrawlSharpTelemetry.StageQueued)));
                    Check.Equal(2.0, capture.Sum(CrawlSharpTelemetry.StageEvents, Tags(CrawlSharpTelemetry.AttributeStage, CrawlSharpTelemetry.StageFetch, CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeSuccess)));
                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.StageEvents, Tags(CrawlSharpTelemetry.AttributeStage, CrawlSharpTelemetry.StageRobots, CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeSkipped)));

                    // Gauges: positive while the crawl ran, net zero once the crawler is disposed.
                    Check.Equal(1.0, capture.Measurements(CrawlSharpTelemetry.CrawlActive).Where(m => m.Value > 0).Sum(m => m.Value));
                    Check.Equal(0.0, capture.Sum(CrawlSharpTelemetry.CrawlActive));
                    Check.Equal(4.0, capture.Measurements(CrawlSharpTelemetry.WorkersCapacity).Where(m => m.Value > 0).Sum(m => m.Value));
                    Check.Equal(0.0, capture.Sum(CrawlSharpTelemetry.WorkersCapacity));
                    Check.Equal(2.0, capture.Measurements(CrawlSharpTelemetry.WorkersInUse).Where(m => m.Value > 0).Sum(m => m.Value));
                    Check.Equal(0.0, capture.Sum(CrawlSharpTelemetry.WorkersInUse));
                    Check.Equal(0.0, capture.Sum(CrawlSharpTelemetry.QueueSize));
                    Check.Equal(0.0, capture.Sum(CrawlSharpTelemetry.ResultsBuffered));
                    Check.Equal(0.0, capture.Sum(CrawlSharpTelemetry.VisitedSize));
                    Check.True(capture.Measurements(CrawlSharpTelemetry.VisitedSize).Any(m => m.Value > 0), "Visited URLs are counted while the crawl runs.");

                    capture.CollectObservables();
                    RecordedMeasurement lastSuccess = capture.Measurements(CrawlSharpTelemetry.CrawlLastSuccess).LastOrDefault();
                    Check.NotNull(lastSuccess, "The last-success gauge reports after a completed crawl.");
                    Check.True(Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - lastSuccess.Value) < 120, "The last-success gauge holds a recent Unix time.");

                    RecordedMeasurement buildInfo = capture.Measurements(CrawlSharpTelemetry.BuildInfo).LastOrDefault();
                    Check.NotNull(buildInfo, "The build-info gauge reports.");
                    Check.Equal(1.0, buildInfo.Value);
                    Check.False(String.IsNullOrEmpty(buildInfo.Tag(CrawlSharpTelemetry.AttributeVersion)), "The build-info gauge carries the version.");
                }),

                Case.Async(Id, "HttpErrors_Classified", "4xx and 5xx responses are counted as page HTTP errors and integration client/server errors", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/", "<html><body><a href=\"/missing\">m</a><a href=\"/broken\">b</a></body></html>");
                    server.AddResponse("/missing", "text/html", Encoding.UTF8.GetBytes("missing"), 404);
                    server.AddResponse("/broken", "text/html", Encoding.UTF8.GetBytes("broken"), 500);

                    using TelemetryCapture capture = new TelemetryCapture(CrawlSharpTelemetry.MeterName, CrawlSharpTelemetry.ActivitySourceName);

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"), configure: c =>
                    {
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 1;
                    });

                    await CrawlHelper.CrawlAllWithinAsync(settings, TimeoutSeconds, ct);

                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.Pages, Tags(CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeHttpError, CrawlSharpTelemetry.AttributeStatusClass, "4xx")));
                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.Pages, Tags(CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeHttpError, CrawlSharpTelemetry.AttributeStatusClass, "5xx")));
                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.IntegrationRequests, Tags(CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeClientError)));
                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.IntegrationRequests, Tags(CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeServerError)));

                    Activity serverError = capture.Spans("http GET").Single(a => Convert.ToInt32(a.GetTagItem(CrawlSharpTelemetry.AttributeHttpStatusCode)) == 500);
                    Check.Equal(ActivityStatusCode.Error, serverError.Status);
                }),

                Case.Async(Id, "ConnectionFailure_Recorded", "A connection failure is recorded as a failed call with error.type, a fetch error and a failed page", async ct =>
                {
                    string url = "http://127.0.0.1:" + GetClosedPort() + "/";

                    using TelemetryCapture capture = new TelemetryCapture(CrawlSharpTelemetry.MeterName, CrawlSharpTelemetry.ActivitySourceName);

                    List<WebResource> resources = await CrawlHelper.CrawlAllWithinAsync(CrawlHelper.CreateSettings(url), TimeoutSeconds, ct);
                    Check.Empty(resources);

                    RecordedMeasurement call = Check.Single(capture.Measurements(CrawlSharpTelemetry.IntegrationRequests, Tags(CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeFailure)));
                    Check.Equal(typeof(System.Net.Http.HttpRequestException).FullName, call.Tag(CrawlSharpTelemetry.AttributeErrorType));
                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.Errors, Tags(CrawlSharpTelemetry.AttributeStage, CrawlSharpTelemetry.StageFetch)));
                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.StageEvents, Tags(CrawlSharpTelemetry.AttributeStage, CrawlSharpTelemetry.StageFetch, CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeFailure)));
                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.Pages, Tags(CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeFailure)));

                    Activity get = Check.Single(capture.Spans("http GET"));
                    Check.Equal(ActivityStatusCode.Error, get.Status);
                    Check.True(get.Events.Any(e => e.Name == "exception"), "The client span records the exception event.");

                    Activity page = Check.Single(capture.Spans(CrawlSharpTelemetry.SpanPage));
                    Check.Equal(ActivityStatusCode.Error, page.Status);

                    // The crawl itself ran to the end; a failed page does not fail the job.
                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.CrawlJobs, Tags(CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeCompleted)));
                }),

                Case.Async(Id, "Throttle_RetryRecorded", "A 429 that is retried records a retry, a throttled call and a retry_backoff stage", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    int calls = 0;
                    server.AddHandler("/", ctx =>
                    {
                        if (Interlocked.Increment(ref calls) == 1) return new FixtureResponse { StatusCode = 429 };
                        return new FixtureResponse { StatusCode = 200, ContentType = "text/html", Body = Encoding.UTF8.GetBytes("<html><body>ok</body></html>") };
                    });

                    using TelemetryCapture capture = new TelemetryCapture(CrawlSharpTelemetry.MeterName, CrawlSharpTelemetry.ActivitySourceName);

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"), configure: c =>
                    {
                        c.RetryOn429 = true;
                        c.MaxRetries = 2;
                        c.RetryMinBackoffMs = 20;
                        c.RetryMaxBackoffMs = 40;
                        c.RetryBackoffJitter = false;
                    });

                    WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(settings, TimeoutSeconds, ct);
                    Check.Equal(200, resource.Status);

                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.Retries, Tags(CrawlSharpTelemetry.AttributeIntegrationService, "http", CrawlSharpTelemetry.AttributeReason, CrawlSharpTelemetry.RetryReasonThrottled)));
                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.IntegrationRequests, Tags(CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeThrottled)));
                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.IntegrationRequests, Tags(CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeSuccess)));
                    Check.Equal(1, capture.Count(CrawlSharpTelemetry.StageDuration, Tags(CrawlSharpTelemetry.AttributeStage, CrawlSharpTelemetry.StageRetryBackoff)));

                    // The backoff is a sibling of the HTTP call, not part of its latency.
                    Activity backoff = Check.Single(capture.Spans("stage:retry_backoff"));
                    Check.False(capture.Spans("http GET").Any(g => g.SpanId == backoff.ParentSpanId), "The backoff span is not inside an HTTP client span.");
                }),

                Case.Async(Id, "Redirects_Recorded", "Followed redirects and redirect loops are counted by outcome with hop counts", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/", "<html><body><a href=\"/moved\">m</a><a href=\"/loop-a\">l</a></body></html>");
                    server.AddRedirect("/moved", "/final", 301);
                    server.AddHtml("/final", "<html><body>final</body></html>");
                    server.AddRedirect("/loop-a", "/loop-b", 302);
                    server.AddRedirect("/loop-b", "/loop-a", 302);

                    using TelemetryCapture capture = new TelemetryCapture(CrawlSharpTelemetry.MeterName);

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"), configure: c =>
                    {
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 1;
                    });

                    await CrawlHelper.CrawlAllWithinAsync(settings, TimeoutSeconds, ct);

                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.Redirects, Tags(CrawlSharpTelemetry.AttributeRedirectOutcome, "followed")));
                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.Redirects, Tags(CrawlSharpTelemetry.AttributeRedirectOutcome, "loop_detected")));
                    Check.Equal(2, capture.Count(CrawlSharpTelemetry.RedirectHops));
                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.Pages, Tags(CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeRedirectStopped)));
                }),

                Case.Async(Id, "RobotsAndSitemap_Stages", "robots.txt and sitemap.xml stages record outcomes, sitemap URLs and robots skips", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddResponse("/robots.txt", "text/plain", Encoding.UTF8.GetBytes("User-agent: *\nDisallow: /private\n"));
                    string sitemap = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"
                        + "<url><loc>" + server.UrlFor("/listed") + "</loc></url>"
                        + "<url><loc>" + server.UrlFor("/private/secret") + "</loc></url></urlset>";
                    server.AddResponse("/sitemap.xml", "application/xml", Encoding.UTF8.GetBytes(sitemap));
                    server.AddHtml("/", "<html><body>home</body></html>");
                    server.AddHtml("/listed", "<html><body>listed</body></html>");
                    server.AddHtml("/private/secret", "<html><body>secret</body></html>");

                    using TelemetryCapture capture = new TelemetryCapture(CrawlSharpTelemetry.MeterName);

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"), configure: c =>
                    {
                        c.IgnoreRobotsText = false;
                        c.IncludeSitemap = true;
                    });

                    await CrawlHelper.CrawlAllWithinAsync(settings, TimeoutSeconds, ct);

                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.StageEvents, Tags(CrawlSharpTelemetry.AttributeStage, CrawlSharpTelemetry.StageRobots, CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeSuccess)));
                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.StageEvents, Tags(CrawlSharpTelemetry.AttributeStage, CrawlSharpTelemetry.StageSitemap, CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeSuccess)));
                    Check.Equal(2.0, capture.Sum(CrawlSharpTelemetry.SitemapUrls));
                    Check.Equal(2.0, capture.Sum(CrawlSharpTelemetry.LinksEnqueued, Tags(CrawlSharpTelemetry.AttributeLinkSource, CrawlSharpTelemetry.LinkSourceSitemap)));
                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.LinksSkipped, Tags(CrawlSharpTelemetry.AttributeReason, CrawlSharpTelemetry.ReasonRobotsDisallowed)));
                }),

                Case.Async(Id, "RobotsMissing_NotFound", "A missing robots.txt is recorded as not_found", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/", "<html><body>home</body></html>");

                    using TelemetryCapture capture = new TelemetryCapture(CrawlSharpTelemetry.MeterName);

                    await CrawlHelper.CrawlAllWithinAsync(CrawlHelper.CreateSettings(server.UrlFor("/"), configure: c => c.IgnoreRobotsText = false), TimeoutSeconds, ct);

                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.StageEvents, Tags(CrawlSharpTelemetry.AttributeStage, CrawlSharpTelemetry.StageRobots, CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeNotFound)));
                }),

                Case.Async(Id, "PolitenessDelay_Stage", "The request delay is recorded as its own stage, outside the fetch stage", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/", "<html><body>home</body></html>");

                    using TelemetryCapture capture = new TelemetryCapture(CrawlSharpTelemetry.MeterName, CrawlSharpTelemetry.ActivitySourceName);

                    await CrawlHelper.CrawlAllWithinAsync(CrawlHelper.CreateSettings(server.UrlFor("/"), configure: c => c.RequestDelayMs = 50), TimeoutSeconds, ct);

                    RecordedMeasurement delay = Check.Single(capture.Measurements(CrawlSharpTelemetry.StageDuration, Tags(CrawlSharpTelemetry.AttributeStage, CrawlSharpTelemetry.StagePolitenessDelay)));
                    Check.True(delay.Value >= 0.04, "The politeness delay stage measures the delay.");

                    Activity delaySpan = Check.Single(capture.Spans("stage:politeness_delay"));
                    Check.False(capture.Spans("stage:fetch").Any(f => f.SpanId == delaySpan.ParentSpanId), "The delay is not inside the fetch stage.");
                }),

                Case.Async(Id, "Cancelled_Outcome", "A crawl cancelled through its token is recorded as cancelled", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHandler("/", ctx =>
                    {
                        Thread.Sleep(2000);
                        return new FixtureResponse { StatusCode = 200, ContentType = "text/html", Body = Encoding.UTF8.GetBytes("<html></html>") };
                    });

                    using TelemetryCapture capture = new TelemetryCapture(CrawlSharpTelemetry.MeterName, CrawlSharpTelemetry.ActivitySourceName);
                    using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

                    await Check.ThrowsAsync<OperationCanceledException>(() => CrawlHelper.CrawlAllAsync(CrawlHelper.CreateSettings(server.UrlFor("/")), cts.Token));

                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.CrawlJobs, Tags(CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeCancelled)));
                    Activity root = Check.Single(capture.Spans(CrawlSharpTelemetry.SpanCrawl));
                    Check.Equal(CrawlSharpTelemetry.OutcomeCancelled, root.GetTagItem(CrawlSharpTelemetry.AttributeOutcome) as string);
                    Check.Equal(0.0, capture.Sum(CrawlSharpTelemetry.CrawlActive));
                }),

                Case.Async(Id, "Abandoned_Outcome", "A consumer that stops reading early is recorded as abandoned", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/", "<html><body><a href=\"/a\">a</a><a href=\"/b\">b</a></body></html>");
                    server.AddHtml("/a", "<html><body>a</body></html>");
                    server.AddHtml("/b", "<html><body>b</body></html>");

                    using TelemetryCapture capture = new TelemetryCapture(CrawlSharpTelemetry.MeterName);

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"), configure: c =>
                    {
                        c.FollowLinks = true;
                        c.MaxCrawlDepth = 1;
                    });

                    using (WebCrawler crawler = new WebCrawler(settings, ct))
                    {
                        await foreach (WebResource resource in crawler.CrawlAsync(ct))
                        {
                            break;
                        }
                    }

                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.CrawlJobs, Tags(CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeAbandoned)));
                    Check.Equal(0.0, capture.Sum(CrawlSharpTelemetry.CrawlActive));
                    Check.Equal(0.0, capture.Sum(CrawlSharpTelemetry.QueueSize));
                }),

                Case.Async(Id, "SyncCrawl_Instrumented", "The synchronous Crawl method records the job and its pages", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/", "<html><body>sync</body></html>");

                    using TelemetryCapture capture = new TelemetryCapture(CrawlSharpTelemetry.MeterName);

                    await Task.Run(() =>
                    {
                        using WebCrawler crawler = new WebCrawler(CrawlHelper.CreateSettings(server.UrlFor("/")), ct);
                        Check.Count(1, crawler.Crawl(System.Net.Http.HttpMethod.Get).ToList());
                    }, ct);

                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.CrawlJobs, Tags(CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeCompleted)));
                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.Pages, Tags(CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeSuccess)));
                }),

                new TestCaseDescriptor(Id, "Headless_PlaywrightInstrumented", "Headless crawls record browser startup, navigation and browser contexts", async ct =>
                {
                    using FixtureServer server = new FixtureServer();
                    server.AddHtml("/", "<html><body><details><summary>s</summary>inner</details></body></html>");

                    using TelemetryCapture capture = new TelemetryCapture(CrawlSharpTelemetry.MeterName, CrawlSharpTelemetry.ActivitySourceName);

                    Settings settings = CrawlHelper.CreateSettings(server.UrlFor("/"), headless: true, configure: c => c.AutoExpandCollapsibles = true);
                    await CrawlHelper.CrawlAllWithinAsync(settings, 120, ct);

                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.IntegrationRequests, Tags(CrawlSharpTelemetry.AttributeIntegrationService, "playwright", CrawlSharpTelemetry.AttributeIntegrationOperation, "launch", CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeSuccess)));
                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.IntegrationRequests, Tags(CrawlSharpTelemetry.AttributeIntegrationService, "playwright", CrawlSharpTelemetry.AttributeIntegrationOperation, "navigate", CrawlSharpTelemetry.AttributeOutcome, CrawlSharpTelemetry.OutcomeSuccess)));
                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.StageEvents, Tags(CrawlSharpTelemetry.AttributeStage, CrawlSharpTelemetry.StageContentTypeCheck)));
                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.StageEvents, Tags(CrawlSharpTelemetry.AttributeStage, CrawlSharpTelemetry.StageBrowserNavigate)));
                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.StageEvents, Tags(CrawlSharpTelemetry.AttributeStage, CrawlSharpTelemetry.StageAutoExpand)));
                    Check.True(capture.Sum(CrawlSharpTelemetry.AutoExpandChanges, Tags(CrawlSharpTelemetry.AttributeAutoExpandKind, CrawlSharpTelemetry.AutoExpandDetails)) >= 1, "Opened details elements are counted.");
                    Check.Equal(0.0, capture.Sum(CrawlSharpTelemetry.BrowserContextsActive));
                    Check.Single(capture.Spans("playwright navigate"));
                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.CrawlJobs, Tags(CrawlSharpTelemetry.AttributeMode, CrawlSharpTelemetry.ModeHeadless)));
                },
                skip: !HeadlessSuite.Enabled,
                skipReason: HeadlessSuite.Enabled ? null : "Headless browser cases are opt-in; set CRAWLSHARP_RUN_HEADLESS=1 to enable.")
            };

            return new TestSuiteDescriptor(Id, "Telemetry", cases);
        }

        private static Dictionary<string, string> Tags(params string[] pairs)
        {
            Dictionary<string, string> tags = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i + 1 < pairs.Length; i += 2) tags[pairs[i]] = pairs[i + 1];
            return tags;
        }

        private static int GetClosedPort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
