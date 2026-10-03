namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Threading.Tasks;
    using CrawlSharp.Server;
    using CrawlSharp.Telemetry;
    using CrawlSharp.Web;
    using Radiant;
    using Touchstone.Core;

    /// <summary>
    /// Proves the server's telemetry wiring: settings parsing and validation, one Radiant host subscribed to every meter and
    /// activity source (Watson, CrawlSharp, CrawlSharp.Server), a working Prometheus scrape endpoint that serves library,
    /// server and runtime metrics, the server's own instruments, and that an unreachable or disabled backend never throws.
    /// </summary>
    public static class ServerTelemetrySuite
    {
        private const string Id = "ServerTelemetry";

        /// <summary>
        /// Build the suite.
        /// </summary>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case.Sync(Id, "Settings_Defaults", "Defaults use 127.0.0.1 loopback addresses and safe values", () =>
                {
                    TelemetrySettings settings = new TelemetrySettings();
                    Check.True(settings.Enabled);
                    Check.Equal("crawlsharp-server", settings.ServiceName);
                    Check.Equal("http://127.0.0.1:4317", settings.OtlpEndpoint);
                    Check.Equal("grpc", settings.OtlpProtocol);
                    Check.True(settings.PrometheusEnabled);
                    Check.Equal("127.0.0.1", settings.PrometheusHostname);
                    Check.Equal(9464, settings.PrometheusPort);
                    Check.False(settings.LokiEnabled);
                    Check.Equal("http://127.0.0.1:3100/otlp", settings.LokiEndpoint);
                    Check.Equal(1.0, settings.TracesSamplingRatio);
                    Check.Equal(2, settings.LogMinimumSeverity);
                }),

                Case.Sync(Id, "Settings_FromValues", "Every environment variable is read", () =>
                {
                    Dictionary<string, string> env = new Dictionary<string, string>
                    {
                        { TelemetrySettings.EnabledVariable, "true" },
                        { TelemetrySettings.ServiceNameVariable, "crawler-a" },
                        { TelemetrySettings.OtlpEnabledVariable, "off" },
                        { TelemetrySettings.OtlpEndpointVariable, "http://tempo:4318" },
                        { TelemetrySettings.OtlpProtocolVariable, "HttpProtobuf" },
                        { TelemetrySettings.PrometheusEnabledVariable, "0" },
                        { TelemetrySettings.PrometheusHostnameVariable, "*" },
                        { TelemetrySettings.PrometheusPortVariable, "9500" },
                        { TelemetrySettings.LokiEnabledVariable, "yes" },
                        { TelemetrySettings.LokiEndpointVariable, "http://loki:3100/otlp" },
                        { TelemetrySettings.TracesSamplingRatioVariable, "0.25" },
                        { TelemetrySettings.LogMinimumSeverityVariable, "1" }
                    };

                    TelemetrySettings settings = TelemetrySettings.FromValues(name => env.TryGetValue(name, out string v) ? v : null);
                    Check.Equal("crawler-a", settings.ServiceName);
                    Check.False(settings.OtlpEnabled);
                    Check.Equal("http://tempo:4318", settings.OtlpEndpoint);
                    Check.Equal("httpprotobuf", settings.OtlpProtocol);
                    Check.False(settings.PrometheusEnabled);
                    Check.Equal("*", settings.PrometheusHostname);
                    Check.Equal(9500, settings.PrometheusPort);
                    Check.True(settings.LokiEnabled);
                    Check.Equal("http://loki:3100/otlp", settings.LokiEndpoint);
                    Check.Equal(0.25, settings.TracesSamplingRatio);
                    Check.Equal(1, settings.LogMinimumSeverity);
                }),

                Case.Sync(Id, "Settings_InvalidValues", "Invalid values are rejected with the variable name in the message", () =>
                {
                    AssertInvalid(TelemetrySettings.EnabledVariable, "maybe");
                    AssertInvalid(TelemetrySettings.OtlpEndpointVariable, "not a uri");
                    AssertInvalid(TelemetrySettings.OtlpProtocolVariable, "udp");
                    AssertInvalid(TelemetrySettings.PrometheusPortVariable, "70000");
                    AssertInvalid(TelemetrySettings.PrometheusPortVariable, "abc");
                    AssertInvalid(TelemetrySettings.TracesSamplingRatioVariable, "1.5");
                    AssertInvalid(TelemetrySettings.LogMinimumSeverityVariable, "9");
                    Check.Throws<ArgumentNullException>(() => new TelemetrySettings().ServiceName = " ");
                }),

                Case.Sync(Id, "Radiant_SubscribesEverySource", "The Radiant settings subscribe to Watson, CrawlSharp and CrawlSharp.Server", () =>
                {
                    RadiantSettings radiant = TelemetryService.BuildRadiantSettings(new TelemetrySettings());
                    foreach (string name in new[] { "Watson", CrawlSharpTelemetry.MeterName, CrawlSharpTelemetry.ServerMeterName })
                    {
                        Check.True(radiant.Sources.MeterNames.Contains(name), "Subscribed to meter " + name);
                    }

                    foreach (string name in new[] { "Watson", CrawlSharpTelemetry.ActivitySourceName, CrawlSharpTelemetry.ServerActivitySourceName })
                    {
                        Check.True(radiant.Sources.ActivitySourceNames.Contains(name), "Subscribed to activity source " + name);
                    }

                    Check.Equal("crawlsharp-server", radiant.ServiceName);
                    Check.Equal("127.0.0.1", radiant.Prometheus.Hostname);
                    Check.True(radiant.Metrics.IncludeRuntime);
                }),

                Case.Sync(Id, "Disabled_NoHost", "Disabled telemetry starts no host and every call is a safe no-op", () =>
                {
                    using TelemetryService service = new TelemetryService(new TelemetrySettings { Enabled = false }, null);
                    Check.False(service.IsEnabled);
                    Check.False(service.IsDebugEnabled);
                    service.Info("info");
                    service.Warn("warn");
                    service.Debug("debug");
                }),

                Case.Async(Id, "UnreachableBackend_NoThrow", "An unreachable OTLP and Loki backend never throws into the application", async ct =>
                {
                    int port = GetFreePort();
                    TelemetrySettings settings = new TelemetrySettings
                    {
                        OtlpEndpoint = "http://127.0.0.1:" + port,
                        PrometheusEnabled = false,
                        LokiEnabled = true,
                        LokiEndpoint = "http://127.0.0.1:" + port + "/otlp"
                    };

                    using (TelemetryService service = new TelemetryService(settings, null))
                    {
                        Check.True(service.IsEnabled);
                        service.Warn("a log record nobody receives");

                        using FixtureServer server = new FixtureServer();
                        server.AddHtml("/", "<html><body>ok</body></html>");
                        WebResource resource = await CrawlHelper.CrawlSingleWithinAsync(CrawlHelper.CreateSettings(server.UrlFor("/")), 30, ct);
                        Check.Equal(200, resource.Status);
                    }
                }),

                Case.Async(Id, "Prometheus_ServesAllMeters", "The Prometheus endpoint serves library, server, build-info and runtime metrics", async ct =>
                {
                    int port = GetFreePort();
                    TelemetrySettings settings = new TelemetrySettings
                    {
                        OtlpEnabled = false,
                        PrometheusPort = port
                    };

                    using (TelemetryService service = new TelemetryService(settings, null))
                    {
                        Check.True(service.IsEnabled);

                        using FixtureServer server = new FixtureServer();
                        server.AddHtml("/", "<html><body>ok</body></html>");
                        await CrawlHelper.CrawlAllWithinAsync(CrawlHelper.CreateSettings(server.UrlFor("/")), 30, ct);
                        ServerInstruments.RecordRequest(ServerInstruments.OutcomeCompleted, 0.5);

                        using HttpClient client = new HttpClient();
                        string body = await client.GetStringAsync("http://127.0.0.1:" + port + "/metrics", ct);

                        Check.Contains("crawlsharp_crawl_jobs_total", body);
                        Check.Contains("crawlsharp_crawl_stage_duration_seconds_bucket", body);
                        Check.Contains("crawlsharp_integration_requests_total", body);
                        Check.Contains("crawlsharp_build_info", body);
                        Check.Contains("crawlsharp_server_crawl_requests_total", body);
                        Check.Contains("crawlsharp_server_build_info", body);
                        Check.Contains("crawlsharp_server_config_info", body);
                        Check.Contains("dotnet_", body);
                    }
                }),

                Case.Sync(Id, "ServerInstruments_Emit", "The server meter records requests, stages, events, streams, build info and config", () =>
                {
                    using TelemetryCapture capture = new TelemetryCapture(CrawlSharpTelemetry.ServerMeterName);

                    ServerInstruments.RecordRequest(ServerInstruments.OutcomeClientDisconnected, 1.25);
                    ServerInstruments.RecordStage(CrawlSharpTelemetry.ServerStageSseSend, CrawlSharpTelemetry.OutcomeSuccess, 0.01);
                    ServerInstruments.RecordEvent(128);
                    ServerInstruments.AddStream(1);
                    ServerInstruments.AddStream(-1);
                    ServerInstruments.SetConfig(new TelemetrySettings { LokiEnabled = true });
                    capture.CollectObservables();

                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.ServerCrawlRequests));
                    Check.Equal(ServerInstruments.OutcomeClientDisconnected, capture.Measurements(CrawlSharpTelemetry.ServerCrawlRequests).Single().Tag(CrawlSharpTelemetry.AttributeOutcome));
                    Check.Equal(1.25, capture.Measurements(CrawlSharpTelemetry.ServerCrawlRequestDuration).Single().Value);
                    Check.Equal(CrawlSharpTelemetry.ServerStageSseSend, capture.Measurements(CrawlSharpTelemetry.ServerStageDuration).Single().Tag(CrawlSharpTelemetry.AttributeStage));
                    Check.Equal(1.0, capture.Sum(CrawlSharpTelemetry.ServerSseEvents));
                    Check.Equal(128.0, capture.Sum(CrawlSharpTelemetry.ServerSseBytes));
                    Check.Equal(2, capture.Count(CrawlSharpTelemetry.ServerCrawlStreamsActive));
                    Check.Equal(0.0, capture.Sum(CrawlSharpTelemetry.ServerCrawlStreamsActive));

                    RecordedMeasurement config = capture.Measurements(CrawlSharpTelemetry.ServerConfigInfo).Last();
                    Check.Equal("true", config.Tag("crawlsharp.config.loki_enabled"));
                    Check.Equal("grpc", config.Tag("crawlsharp.config.otlp_protocol"));

                    RecordedMeasurement build = capture.Measurements(CrawlSharpTelemetry.ServerBuildInfo).Last();
                    Check.False(String.IsNullOrEmpty(build.Tag(CrawlSharpTelemetry.AttributeVersion)), "Build info carries the version.");
                    Check.False(String.IsNullOrEmpty(build.Tag(CrawlSharpTelemetry.AttributeRuntime)), "Build info carries the runtime.");

                    ServerInstruments.SetConfig(null);
                }),

                Case.Sync(Id, "ServerSpan_Error", "Server spans record exceptions with error status and error.type", () =>
                {
                    using TelemetryCapture capture = new TelemetryCapture(CrawlSharpTelemetry.ServerActivitySourceName);

                    using (Activity activity = ServerInstruments.StartActivity(CrawlSharpTelemetry.SpanServerCrawlRequest))
                    {
                        Check.NotNull(activity, "A listener makes the server source sample.");
                        ServerInstruments.SetError(activity, new InvalidOperationException("boom"));
                    }

                    Activity span = Check.Single(capture.Spans(CrawlSharpTelemetry.SpanServerCrawlRequest));
                    Check.Equal(ActivityStatusCode.Error, span.Status);
                    Check.Equal(typeof(InvalidOperationException).FullName, span.GetTagItem(CrawlSharpTelemetry.AttributeErrorType) as string);
                    Check.True(span.Events.Any(e => e.Name == "exception"), "The exception event is attached.");
                })
            };

            return new TestSuiteDescriptor(Id, "Server telemetry", cases);
        }

        private static void AssertInvalid(string variable, string value)
        {
            ArgumentException e = Check.Throws<ArgumentException>(() => TelemetrySettings.FromValues(name => name == variable ? value : null));
            Check.Contains(variable, e.Message);
        }

        private static int GetFreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
