namespace CrawlSharp.Server
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Runtime.InteropServices;
    using CrawlSharp.Telemetry;
    using CrawlSharp.Web;

    /// <summary>
    /// The server's meter, activity source and instruments.  Names come from <see cref="CrawlSharpTelemetry"/>.
    /// Every helper is best-effort and never throws.  Thread safe.
    /// </summary>
    public static class ServerInstruments
    {
        #region Public-Members

        /// <summary>
        /// Crawl request outcome: the crawl ran to the end and the final event was sent.
        /// </summary>
        public const string OutcomeCompleted = "completed";

        /// <summary>
        /// Crawl request outcome: the request had no body.
        /// </summary>
        public const string OutcomeBadRequest = "bad_request";

        /// <summary>
        /// Crawl request outcome: the request body was not valid JSON settings.
        /// </summary>
        public const string OutcomeDeserializationError = "deserialization_error";

        /// <summary>
        /// Crawl request outcome: the settings were rejected by the crawler (for example incomplete authentication).
        /// </summary>
        public const string OutcomeInvalidSettings = "invalid_settings";

        /// <summary>
        /// Crawl request outcome: the client stopped reading the event stream, so the crawl was stopped.
        /// </summary>
        public const string OutcomeClientDisconnected = "client_disconnected";

        /// <summary>
        /// Crawl request outcome: an unexpected error.
        /// </summary>
        public const string OutcomeFailed = "failed";

        /// <summary>
        /// Server version reported on the build-info gauge (the CrawlSharp library version the server ships with).
        /// </summary>
        public static readonly string Version = GetVersion();

        /// <summary>
        /// The server meter.
        /// </summary>
        public static readonly Meter Meter = new Meter(CrawlSharpTelemetry.ServerMeterName, Version);

        /// <summary>
        /// The server activity source.
        /// </summary>
        public static readonly ActivitySource Source = new ActivitySource(CrawlSharpTelemetry.ServerActivitySourceName, Version);

        #endregion

        #region Private-Members

        private static readonly Counter<long> _CrawlRequests = Meter.CreateCounter<long>(CrawlSharpTelemetry.ServerCrawlRequests, "{request}", "Crawl requests handled, by outcome.");
        private static readonly Histogram<double> _CrawlRequestDuration = Meter.CreateHistogram<double>(CrawlSharpTelemetry.ServerCrawlRequestDuration, "s", "Crawl request duration, from request to final event.");
        private static readonly UpDownCounter<long> _StreamsActive = Meter.CreateUpDownCounter<long>(CrawlSharpTelemetry.ServerCrawlStreamsActive, "{stream}", "Crawl event streams currently open.");
        private static readonly Histogram<double> _StageDuration = Meter.CreateHistogram<double>(CrawlSharpTelemetry.ServerStageDuration, "s", "Server stage duration.");
        private static readonly Counter<long> _SseEvents = Meter.CreateCounter<long>(CrawlSharpTelemetry.ServerSseEvents, "{event}", "Server-sent events written.");
        private static readonly Counter<long> _SseBytes = Meter.CreateCounter<long>(CrawlSharpTelemetry.ServerSseBytes, "By", "Server-sent event payload bytes written.");
        private static readonly object _ConfigLock = new object();
        private static TelemetrySettings _Config = null;

        #endregion

        #region Constructors-and-Factories

        static ServerInstruments()
        {
            Meter.CreateObservableGauge<int>(
                CrawlSharpTelemetry.ServerBuildInfo,
                () => new Measurement<int>(
                    1,
                    new KeyValuePair<string, object>(CrawlSharpTelemetry.AttributeVersion, Version),
                    new KeyValuePair<string, object>(CrawlSharpTelemetry.AttributeRuntime, RuntimeInformation.FrameworkDescription)),
                "1",
                "CrawlSharp server build information.");

            Meter.CreateObservableGauge<int>(
                CrawlSharpTelemetry.ServerConfigInfo,
                ObserveConfig,
                "1",
                "CrawlSharp server telemetry configuration (no secrets).");
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Publish the non-secret telemetry configuration on the config-info gauge.
        /// </summary>
        /// <param name="settings">Settings; null clears the gauge.</param>
        public static void SetConfig(TelemetrySettings settings)
        {
            lock (_ConfigLock)
            {
                _Config = settings;
            }
        }

        /// <summary>
        /// Start an activity on the server source, or null when nothing listens.
        /// </summary>
        /// <param name="name">Span name.</param>
        /// <param name="kind">Span kind.</param>
        /// <returns>Activity or null.</returns>
        public static Activity StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
        {
            try
            {
                if (!Source.HasListeners()) return null;
                return Source.StartActivity(name, kind);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Record a finished crawl request.
        /// </summary>
        /// <param name="outcome">Outcome, one of the outcome constants on this class.</param>
        /// <param name="seconds">Duration in seconds.</param>
        public static void RecordRequest(string outcome, double seconds)
        {
            try
            {
                KeyValuePair<string, object> tag = new KeyValuePair<string, object>(CrawlSharpTelemetry.AttributeOutcome, outcome);
                if (_CrawlRequests.Enabled) _CrawlRequests.Add(1, tag);
                if (_CrawlRequestDuration.Enabled) _CrawlRequestDuration.Record(seconds, tag);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record a server stage.
        /// </summary>
        /// <param name="stage">Stage name.</param>
        /// <param name="outcome">Outcome label.</param>
        /// <param name="seconds">Duration in seconds.</param>
        public static void RecordStage(string stage, string outcome, double seconds)
        {
            try
            {
                if (!_StageDuration.Enabled) return;

                TagList tags = new TagList
                {
                    { CrawlSharpTelemetry.AttributeStage, stage },
                    { CrawlSharpTelemetry.AttributeOutcome, outcome }
                };

                _StageDuration.Record(seconds, tags);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record one server-sent event written to a client.
        /// </summary>
        /// <param name="bytes">Payload size in bytes.</param>
        public static void RecordEvent(long bytes)
        {
            try
            {
                if (_SseEvents.Enabled) _SseEvents.Add(1);
                if (_SseBytes.Enabled && bytes > 0) _SseBytes.Add(bytes);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Adjust the number of open crawl streams.
        /// </summary>
        /// <param name="delta">Change, +1 or -1.</param>
        public static void AddStream(int delta)
        {
            try
            {
                if (_StreamsActive.Enabled) _StreamsActive.Add(delta);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Seconds elapsed since a <see cref="Stopwatch.GetTimestamp"/> value.
        /// </summary>
        /// <param name="startTimestamp">Start timestamp.</param>
        /// <returns>Seconds.</returns>
        public static double ElapsedSeconds(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) / (double)Stopwatch.Frequency;
        }

        /// <summary>
        /// Mark an activity as failed and attach the exception as an OpenTelemetry "exception" event.
        /// </summary>
        /// <param name="activity">Activity; null is ignored.</param>
        /// <param name="e">Exception.</param>
        public static void SetError(Activity activity, Exception e)
        {
            if (activity == null || e == null) return;

            try
            {
                activity.SetStatus(ActivityStatusCode.Error, e.Message);
                activity.SetTag(CrawlSharpTelemetry.AttributeErrorType, e.GetType().FullName);
                activity.AddEvent(new ActivityEvent("exception", DateTimeOffset.UtcNow, new ActivityTagsCollection
                {
                    { "exception.type", e.GetType().FullName },
                    { "exception.message", e.Message },
                    { "exception.stacktrace", e.ToString() }
                }));
            }
            catch (Exception)
            {
            }
        }

        #endregion

        #region Private-Methods

        private static IEnumerable<Measurement<int>> ObserveConfig()
        {
            TelemetrySettings config;

            lock (_ConfigLock)
            {
                config = _Config;
            }

            if (config == null) return Array.Empty<Measurement<int>>();

            return new[]
            {
                new Measurement<int>(
                    1,
                    new KeyValuePair<string, object>("crawlsharp.config.otlp_enabled", config.OtlpEnabled ? "true" : "false"),
                    new KeyValuePair<string, object>("crawlsharp.config.otlp_protocol", config.OtlpProtocol),
                    new KeyValuePair<string, object>("crawlsharp.config.prometheus_enabled", config.PrometheusEnabled ? "true" : "false"),
                    new KeyValuePair<string, object>("crawlsharp.config.loki_enabled", config.LokiEnabled ? "true" : "false"),
                    new KeyValuePair<string, object>("crawlsharp.config.traces_sampling_ratio", config.TracesSamplingRatio.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)))
            };
        }

        private static string GetVersion()
        {
            try
            {
                Version version = typeof(WebCrawler).Assembly.GetName().Version;
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
