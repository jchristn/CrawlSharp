# CrawlSharp Telemetry

CrawlSharp ships with metrics and traces built in, plus logs for the server. The library emits; whoever hosts it decides whether and where to collect. This document is the contract: every meter, activity source, instrument, label, span and configuration key, with the queries and dashboards built on them.

The goal is operational. An on-call engineer looking only at Grafana and Tempo should be able to say where a crawl's time went (which stage, which site, which call) and what failed (which stage, which error type, which downstream), without reading source or attaching a debugger.

## Contents

1. [How it fits together](#how-it-fits-together)
2. [Using the library telemetry](#using-the-library-telemetry)
3. [Running the server with the observability stack](#running-the-server-with-the-observability-stack)
4. [Server configuration keys](#server-configuration-keys)
5. [Meters and activity sources](#meters-and-activity-sources)
6. [Metrics catalog](#metrics-catalog)
7. [Label values](#label-values)
8. [Spans catalog](#spans-catalog)
9. [Logs](#logs)
10. [Dashboards](#dashboards)
11. [Recommended alerts](#recommended-alerts)
12. [Cost, cardinality and safety](#cost-cardinality-and-safety)

## How it fits together

```
CrawlSharp.Server process
  Watson 7.1 (meter + source "Watson")          HTTP metrics, one server span per request
  CrawlSharp.Server (meter + source)            crawl endpoint outcome, SSE stream, server stages
  CrawlSharp library (meter + source)           crawl jobs, pipeline stages, pages, links, outbound calls
  Radiant host (one per process)                subscribes to all of the above + .NET runtime
      |-- OTLP gRPC ------------------> Tempo  (traces)
      |-- OTLP HTTP ------------------> Loki   (logs, stamped with trace_id/span_id)
      '-- /metrics on :9464 <--------- Prometheus (scrape)
                                              '--> Grafana (CrawlSharp folder, 6 dashboards)
```

- **Library (NuGet `CrawlSharp`)**: emits only through `System.Diagnostics.Metrics.Meter` and `System.Diagnostics.ActivitySource`. No Radiant, OpenTelemetry SDK or exporter dependency. Unobserved emission is an `Enabled` check and an early return.
- **Server (`CrawlSharp.Server`, Docker image `jchristn77/crawlsharp`)**: one [Radiant](https://www.nuget.org/packages/Radiant) host at the composition root (`TelemetryService`), subscribed to `Watson`, `CrawlSharp` and `CrawlSharp.Server`, exporting traces over OTLP, logs to Loki, and every metric (including .NET runtime metrics) on an in-process Prometheus endpoint. Watson's own HTTP telemetry is on (`Settings.Telemetry.Enable`, `EnableMetrics`, `EnableTraces`, `PropagateContext`), so the HTTP layer is not duplicated.

## Using the library telemetry

Subscribe to the meter and activity source named `CrawlSharp` (constants: `CrawlSharpTelemetry.MeterName` and `CrawlSharpTelemetry.ActivitySourceName` in namespace `CrawlSharp.Telemetry`).

With Radiant:

```csharp
using CrawlSharp.Telemetry;
using Radiant;

RadiantSettings settings = new RadiantSettings("my-crawler");
settings.Sources.AddMeter(CrawlSharpTelemetry.MeterName);
settings.Sources.AddActivitySource(CrawlSharpTelemetry.ActivitySourceName);
settings.Prometheus.Enable = true;            // optional: /metrics on 127.0.0.1:9464
settings.Prometheus.Hostname = "127.0.0.1";

using (RadiantHost host = RadiantHost.Start(settings))
using (WebCrawler crawler = new WebCrawler(crawlSettings))
{
    await foreach (WebResource resource in crawler.CrawlAsync()) { /* ... */ }
}
```

With the OpenTelemetry SDK:

```csharp
using MeterProvider meters = Sdk.CreateMeterProviderBuilder()
    .AddMeter("CrawlSharp")
    .AddOtlpExporter()
    .Build();

using TracerProvider tracer = Sdk.CreateTracerProviderBuilder()
    .AddSource("CrawlSharp")
    .AddOtlpExporter()
    .Build();
```

With nothing at all (tests, diagnostics): a `MeterListener` and an `ActivityListener` from the base class library. `src/Test.Shared/TelemetryCapture.cs` is a complete example.

Trace context: the root `crawl` span is created as a child of `Activity.Current` at the moment the crawl starts (so a crawl started inside an ASP.NET Core or Watson request joins that request's trace), and every page, stage and client span is parented explicitly to it, including work running on the background queue processor. Outbound HTTP requests carry a W3C `traceparent` header through .NET's `HttpClient` propagation whenever a span is active.

## Running the server with the observability stack

```
cd Docker
docker compose up -d
```

`Docker/compose.yaml` brings up the server, the dashboard, Prometheus, Tempo, Loki and Grafana, with healthchecks and `depends_on: service_healthy` ordering (Tempo and Loki before the server, the server before Prometheus, all three backends before Grafana).

| Service | URL (host) | Credentials | Image |
|---|---|---|---|
| CrawlSharp server | http://localhost:8000 | none | `jchristn77/crawlsharp:latest` |
| CrawlSharp dashboard | http://localhost:8001 | none | `jchristn77/crawlsharp-ui:latest` |
| Grafana | http://localhost:3000 | `admin` / `admin` (local only) | `grafana/grafana-oss:13.0.2` |
| Prometheus | http://localhost:9090 | none | `prom/prometheus:v3.5.4` |
| Tempo API | http://localhost:3200 (OTLP 4317/4318) | none | `grafana/tempo:2.6.1` |
| Loki API | http://localhost:3100 | none | `grafana/loki:3.2.1` |

The server's metrics endpoint (port 9464) is deliberately not published to the host; Prometheus scrapes it inside the compose network. The dashboard's home page has an **External Services** card listing these URLs and credentials; set `GRAFANA_URL`, `PROMETHEUS_URL`, `TEMPO_URL` or `LOKI_URL` on the `crawlsharp-ui` container to change them, or set one to an empty string to hide it.

Grafana provisioning is code: `Docker/grafana/provisioning/datasources/crawlsharp-datasources.yaml` declares Prometheus (`uid: prometheus`), Tempo (`uid: tempo`) and Loki (`uid: loki`) with trace-to-logs and log-to-trace links, and `Docker/grafana/provisioning/dashboards/crawlsharp-dashboards.yaml` loads `assets/grafana/*.json` into the **CrawlSharp** folder.

**Production:** `admin` / `admin` is a local-development default. For any shared or hosted deployment set `GRAFANA_ADMIN_USER` and `GRAFANA_ADMIN_PASSWORD` in the environment that runs `docker compose` (the compose file reads them, falling back to `admin` only when unset), keep sign-up disabled, and do not publish Prometheus, Tempo, Loki or port 9464 on a public interface; none of them has authentication.

## Server configuration keys

The server reads telemetry settings from environment variables (`TelemetrySettings.FromEnvironment()`). Unset variables keep their defaults; an invalid value is reported in the log and the defaults are used. Loopback defaults are `127.0.0.1`, not `localhost`.

| Variable | Default | Meaning |
|---|---|---|
| `CRAWLSHARP_TELEMETRY_ENABLED` | `true` | Master switch. `false` starts no Radiant host. |
| `CRAWLSHARP_TELEMETRY_SERVICE_NAME` | `crawlsharp-server` | `service.name` on every signal. |
| `CRAWLSHARP_OTLP_ENABLED` | `true` | Push traces (and metrics) over OTLP. |
| `CRAWLSHARP_OTLP_ENDPOINT` | `http://127.0.0.1:4317` | OTLP endpoint (Tempo or a Collector). |
| `CRAWLSHARP_OTLP_PROTOCOL` | `grpc` | `grpc` (port 4317) or `httpprotobuf` (port 4318). |
| `CRAWLSHARP_PROMETHEUS_ENABLED` | `true` | Serve `/metrics` in-process. |
| `CRAWLSHARP_PROMETHEUS_HOSTNAME` | `127.0.0.1` | Hostname the endpoint binds and answers to. In a container use the container's hostname (the compose stack sets `hostname: crawlsharp-server` and this variable to match). Radiant 0.1.2 rejects `*` and `+`, and the HTTP listener rejects `0.0.0.0`. |
| `CRAWLSHARP_PROMETHEUS_PORT` | `9464` | Scrape port (1 to 65535). |
| `CRAWLSHARP_LOKI_ENABLED` | `false` | Push logs directly to Loki. `true` in the compose stack. |
| `CRAWLSHARP_LOKI_ENDPOINT` | `http://127.0.0.1:3100/otlp` | Loki OTLP base endpoint (`/v1/logs` is appended). |
| `CRAWLSHARP_TRACES_SAMPLING_RATIO` | `1.0` | Head sampling ratio, 0 to 1 (parent-based). |
| `CRAWLSHARP_LOG_MINIMUM_SEVERITY` | `2` | Exported log level: 0 trace, 1 debug (adds the crawler's per-URL lines), 2 information, 4 warning, 7 none. |

Watson settings (`WebserverSettings.Telemetry`) are set explicitly in `Program.cs`: `Enable`, `EnableMetrics`, `EnableTraces` and `PropagateContext` are all `true`. Watson's own in-process `/metrics` is left off because Radiant serves one endpoint for every meter. Forwarded-header trust is off; enable it only behind a known proxy (see Watson's `TELEMETRY.md`).

## Meters and activity sources

| Name | Kind | Emitted by | Version label |
|---|---|---|---|
| `CrawlSharp` | Meter and ActivitySource | CrawlSharp library | library version (`otel_scope_version`) |
| `CrawlSharp.Server` | Meter and ActivitySource | CrawlSharp server | library version |
| `Watson` | Meter and ActivitySource | Watson 7.1 web server | Watson version |
| `System.Runtime` | Meter | .NET runtime (via Radiant `Metrics.IncludeRuntime`) | |

All instrument, span, attribute and label-value strings are constants on `CrawlSharp.Telemetry.CrawlSharpTelemetry`. Treat them as public API.

## Metrics catalog

Instrument names are dotted OpenTelemetry names with UCUM units. Prometheus names (third column) are what Radiant's exporter produces; dots in label keys become underscores (`crawlsharp.outcome` becomes `crawlsharp_outcome`). Every series also carries `otel_scope_name` and `otel_scope_version`.

### Library: crawl jobs and pipeline (`CrawlSharp`)

| Instrument | Type | Unit | Prometheus name | Labels | Description |
|---|---|---|---|---|---|
| `crawlsharp.crawl.jobs` | Counter | {crawl} | `crawlsharp_crawl_jobs_total` | `crawlsharp.crawl.mode`, `crawlsharp.outcome` | Crawl jobs that finished. |
| `crawlsharp.crawl.duration` | Histogram | s | `crawlsharp_crawl_duration_seconds` | `crawlsharp.crawl.mode`, `crawlsharp.outcome` | End-to-end crawl duration. |
| `crawlsharp.crawl.active` | UpDownCounter | {crawl} | `crawlsharp_crawl_active` | `crawlsharp.crawl.mode` | Crawl jobs running now. |
| `crawlsharp.crawl.last_success` | Gauge | s | `crawlsharp_crawl_last_success_seconds` | none | Unix time of the last completed crawl in this process. Absent until the first one. |
| `crawlsharp.crawl.stage.duration` | Histogram | s | `crawlsharp_crawl_stage_duration_seconds` | `crawlsharp.stage`, `crawlsharp.outcome` | Duration of each stage execution. |
| `crawlsharp.crawl.stage.events` | Counter | {event} | `crawlsharp_crawl_stage_events_total` | `crawlsharp.stage`, `crawlsharp.outcome` | Stage executions. |
| `crawlsharp.pages` | Counter | {page} | `crawlsharp_pages_total` | `crawlsharp.crawl.mode`, `crawlsharp.outcome`, `crawlsharp.status_class` | Pages processed from the queue. |
| `crawlsharp.page.size` | Histogram | By | `crawlsharp_page_size_bytes` | `crawlsharp.crawl.mode` | Retrieved body size. |
| `crawlsharp.links.discovered` | Counter | {link} | `crawlsharp_links_discovered_total` | none | Links found in pages. |
| `crawlsharp.links.enqueued` | Counter | {link} | `crawlsharp_links_enqueued_total` | `crawlsharp.link.source` | Links added to the queue. |
| `crawlsharp.links.skipped` | Counter | {link} | `crawlsharp_links_skipped_total` | `crawlsharp.reason` | Links not retrieved, and why. |
| `crawlsharp.redirects` | Counter | {page} | `crawlsharp_redirects_total` | `crawlsharp.redirect.outcome` | Pages whose retrieval involved a redirect. |
| `crawlsharp.redirect.hops` | Histogram | {hop} | `crawlsharp_redirect_hops` | none | Hops per redirected page. |
| `crawlsharp.sitemap.urls` | Counter | {url} | `crawlsharp_sitemap_urls_total` | none | URLs read from sitemap.xml. |
| `crawlsharp.autoexpand.changes` | Counter | {change} | `crawlsharp_autoexpand_changes_total` | `crawlsharp.autoexpand.kind` | Elements expanded by headless auto-expand. |
| `crawlsharp.errors` | Counter | {error} | `crawlsharp_errors_total` | `crawlsharp.stage`, `error.type` | Errors caught by the crawler. |
| `crawlsharp.build.info` | Gauge | 1 | `crawlsharp_build_info` | `crawlsharp.version` | Always 1; carries the library version. |

### Library: integrations (`CrawlSharp`)

| Instrument | Type | Unit | Prometheus name | Labels | Description |
|---|---|---|---|---|---|
| `crawlsharp.integration.requests` | Counter | {request} | `crawlsharp_integration_requests_total` | `crawlsharp.integration.service`, `crawlsharp.integration.operation`, `crawlsharp.outcome`, `error.type` (failures only) | Outbound calls. |
| `crawlsharp.integration.duration` | Histogram | s | `crawlsharp_integration_duration_seconds` | `crawlsharp.integration.service`, `crawlsharp.integration.operation`, `crawlsharp.outcome` | Outbound call latency (request and body; excludes backoff and delays). |
| `crawlsharp.retries` | Counter | {retry} | `crawlsharp_retries_total` | `crawlsharp.integration.service`, `crawlsharp.reason` | Retried calls (429). |

### Library: pools, queues and buffers (`CrawlSharp`)

All are process-wide sums across every live crawler and return to zero when crawlers finish and are disposed.

| Instrument | Type | Unit | Prometheus name | Description |
|---|---|---|---|---|
| `crawlsharp.queue.size` | UpDownCounter | {link} | `crawlsharp_queue_size` | Links waiting in crawl queues. |
| `crawlsharp.workers.in_use` | UpDownCounter | {worker} | `crawlsharp_workers_in_use` | Worker slots held (pages being retrieved). |
| `crawlsharp.workers.capacity` | UpDownCounter | {worker} | `crawlsharp_workers_capacity` | Sum of `MaxParallelTasks` over running crawls. |
| `crawlsharp.results.buffered` | UpDownCounter | {resource} | `crawlsharp_results_buffered` | Retrieved resources not yet read by the consumer (backpressure). |
| `crawlsharp.visited.size` | UpDownCounter | {url} | `crawlsharp_visited_size` | URLs held in visited-link tables (memory). |
| `crawlsharp.browser.contexts.active` | UpDownCounter | {context} | `crawlsharp_browser_contexts_active` | Open headless browser contexts. |

Wait time for a worker slot is the `queued` stage of `crawlsharp.crawl.stage.duration`.

### Server (`CrawlSharp.Server`)

| Instrument | Type | Unit | Prometheus name | Labels | Description |
|---|---|---|---|---|---|
| `crawlsharp.server.crawl.requests` | Counter | {request} | `crawlsharp_server_crawl_requests_total` | `crawlsharp.outcome` | `POST /crawl` requests by outcome. |
| `crawlsharp.server.crawl.request.duration` | Histogram | s | `crawlsharp_server_crawl_request_duration_seconds` | `crawlsharp.outcome` | Request to final event. |
| `crawlsharp.server.crawl.streams.active` | UpDownCounter | {stream} | `crawlsharp_server_crawl_streams_active` | none | Open server-sent event streams. |
| `crawlsharp.server.stage.duration` | Histogram | s | `crawlsharp_server_stage_duration_seconds` | `crawlsharp.stage`, `crawlsharp.outcome` | `deserialize`, `crawler_init`, `sse_send`. |
| `crawlsharp.server.sse.events` | Counter | {event} | `crawlsharp_server_sse_events_total` | none | Events written. |
| `crawlsharp.server.sse.bytes` | Counter | By | `crawlsharp_server_sse_bytes_total` | none | Event payload bytes written. |
| `crawlsharp.server.build.info` | Gauge | 1 | `crawlsharp_server_build_info` | `crawlsharp.version`, `crawlsharp.runtime` | Always 1. |
| `crawlsharp.server.config.info` | Gauge | 1 | `crawlsharp_server_config_info` | `crawlsharp.config.otlp_enabled`, `crawlsharp.config.otlp_protocol`, `crawlsharp.config.prometheus_enabled`, `crawlsharp.config.loki_enabled`, `crawlsharp.config.traces_sampling_ratio` | Always 1; non-secret telemetry configuration. |

### Watson and runtime (not defined by CrawlSharp)

Watson 7.1 emits the HTTP surface: `http_server_request_duration_seconds` (labels `http_request_method`, `http_response_status_code`, `http_route`), `http_server_active_requests`, request and response body-size histograms, and `watson_*` server, connection, route and exception metrics; see Watson's `TELEMETRY.md`. The crawl endpoint's route label is `/crawl/`. Radiant adds .NET runtime metrics (`dotnet_gc_*`, `dotnet_thread_pool_*`, `dotnet_process_*`, `dotnet_exceptions_total`, ...) and process metrics (`process_uptime_seconds`, `process_memory_usage_bytes`, `process_thread_count`).

## Label values

Every label is bounded. URLs, ids and free-form text never appear on metrics.

| Label | Values |
|---|---|
| `crawlsharp.crawl.mode` | `rest`, `headless` |
| `crawlsharp.outcome` (crawl jobs) | `completed`, `cancelled` (token), `abandoned` (the consumer stopped reading or the crawler was disposed mid-crawl), `failure` |
| `crawlsharp.outcome` (stages) | `success`, `failure`, `skipped` (disabled or not applicable), `cancelled`, `not_found` (robots.txt or sitemap.xml absent) |
| `crawlsharp.outcome` (pages) | `success`, `http_error` (4xx/5xx), `redirect_stopped` (loop, hop limit, scope, robots, not followed), `failure` |
| `crawlsharp.outcome` (integrations) | `success`, `client_error` (4xx), `server_error` (5xx), `throttled` (429), `timeout`, `cancelled`, `failure` (exception), `redirected` and `download` (Playwright navigate) |
| `crawlsharp.outcome` (server requests) | `completed`, `bad_request`, `deserialization_error`, `invalid_settings`, `client_disconnected`, `failed` |
| `crawlsharp.stage` (library) | `robots`, `sitemap`, `queued`, `fetch`, `content_type_check`, `browser_navigate`, `auto_expand`, `link_extraction`, `politeness_delay`, `retry_backoff`, `throttle_delay`, `browser_startup` |
| `crawlsharp.stage` (errors only) | the stages above, plus `crawl` (the job) and `crawl.page` (a page's processing) |
| `crawlsharp.stage` (server) | `deserialize`, `crawler_init`, `sse_send` |
| `crawlsharp.status_class` | `1xx` to `5xx`, `none` |
| `crawlsharp.reason` (links skipped) | `already_visited`, `already_queued`, `in_processing`, `duplicate`, `invalid_url`, `non_http`, `robots_disallowed`, `max_depth`, `follow_links_disabled`, `denied_domain`, `outside_root_domain`, `outside_subdomain`, `not_child_url`, `not_allowed_domain`, `external`, `excluded` |
| `crawlsharp.reason` (retries) | `throttled` |
| `crawlsharp.link.source` | `start`, `sitemap`, `page` |
| `crawlsharp.redirect.outcome` | `followed`, `not_followed`, `loop_detected`, `max_redirects_exceeded`, `out_of_scope`, `robots_disallowed`, `missing_location`, `invalid_location` |
| `crawlsharp.integration.service` | `http`, `playwright` |
| `crawlsharp.integration.operation` | `GET`, `HEAD` (http); `install`, `launch`, `navigate`, `route_fetch` (playwright) |
| `crawlsharp.autoexpand.kind` | `details`, `builtin`, `custom` |
| `error.type` | the exception's full type name, for example `System.Net.Http.HttpRequestException` |

## Spans catalog

Span status is always set explicitly: `Ok` on success, skip and cancellation; `Error` on failure, with an `exception` event (`exception.type`, `exception.message`, `exception.stacktrace`) and `error.type`. Client spans with a 4xx or 429 answer are `Error`, following OpenTelemetry client conventions. Every span carries `crawlsharp.outcome`. URLs on spans (`url.full`) drop the query string, fragment and user information.

| Span | Kind | Source | Parent | Attributes |
|---|---|---|---|---|
| `crawlsharp.server crawl` | Internal | CrawlSharp.Server | Watson's `POST /crawl/` server span | `crawlsharp.outcome`, `crawlsharp.server.sse.events` |
| `stage:deserialize`, `stage:crawler_init` | Internal | CrawlSharp.Server | `crawlsharp.server crawl` | `crawlsharp.outcome` |
| `crawl` | Internal | CrawlSharp | `Activity.Current` when the crawl starts | `crawlsharp.crawl.mode`, `url.full`, `server.address`, `server.port`, `crawlsharp.crawl.max_depth`, `crawlsharp.crawl.max_parallel_tasks`, `crawlsharp.crawl.follow_links`, `crawlsharp.crawl.resources`, `crawlsharp.outcome` |
| `stage:robots`, `stage:sitemap` | Internal | CrawlSharp | `crawl` | `crawlsharp.stage`, `crawlsharp.link.count` (sitemap) |
| `crawl.page` | Internal | CrawlSharp | `crawl` (explicit, across the background queue processor) | `url.full`, `crawlsharp.depth`, `http.response.status_code` |
| `stage:queued` | Internal | CrawlSharp | `crawl.page` | time waiting for a worker slot |
| `stage:politeness_delay`, `stage:retry_backoff`, `stage:throttle_delay` | Internal | CrawlSharp | page, fetch or robots/sitemap | `crawlsharp.delay_ms` |
| `stage:fetch` | Internal | CrawlSharp | `crawl.page`, `stage:robots` or `stage:sitemap` | `crawlsharp.fetch.purpose`, `crawlsharp.depth`, `url.full`, `http.response.status_code`, `crawlsharp.redirect.outcome`, `crawlsharp.redirect.hops`, `crawlsharp.reason` (when skipped) |
| `stage:content_type_check` | Internal | CrawlSharp | `stage:fetch` (headless) | `http.response.status_code` |
| `stage:browser_navigate` | Internal | CrawlSharp | `stage:fetch` (headless) | |
| `stage:auto_expand` | Internal | CrawlSharp | `stage:browser_navigate` | |
| `stage:link_extraction` | Internal | CrawlSharp | `crawl.page` | `crawlsharp.link.count` |
| `stage:browser_startup` | Internal | CrawlSharp | `Activity.Current` (crawler constructor) | |
| `http GET`, `http HEAD` | Client | CrawlSharp | the stage making the call | `http.request.method`, `url.full`, `server.address`, `server.port`, `http.response.status_code`, `crawlsharp.retry.attempt`, `crawlsharp.page.size` |
| `playwright install`, `playwright launch` | Client | CrawlSharp | `stage:browser_startup` | |
| `playwright navigate` | Client | CrawlSharp | `stage:browser_navigate` | `url.full`, `http.response.status_code`, `crawlsharp.retry.attempt` |
| `playwright route_fetch` | Client | CrawlSharp | whatever span is active when Playwright invokes the route handler (credentialed headless requests) | `url.full`, `http.response.status_code` |

A typical server trace:

```
POST /crawl/                                  (Watson server span)
  crawlsharp.server crawl
    stage:deserialize
    stage:crawler_init
    crawl
      stage:robots
        stage:fetch -> http GET
      stage:sitemap
        stage:fetch -> http GET
      crawl.page (one per URL)
        stage:queued
        stage:politeness_delay
        stage:fetch
          http GET (one per redirect hop and retry)
          stage:retry_backoff
        stage:link_extraction
```

## Logs

The server writes logs to its existing syslog/file/console logger and, through Radiant, exports them over OTLP: to Loki when `CRAWLSHARP_LOKI_ENABLED=true` (the compose stack) and to the OTLP endpoint when enabled. Records written during a request or a crawl carry `trace_id` and `span_id`, so Grafana links a log line to its trace and a span to its logs (`service_name="crawlsharp-server"`). At the default severity (information), Loki receives server start/stop, warnings, rejected requests, client disconnects and crawler exceptions; set `CRAWLSHARP_LOG_MINIMUM_SEVERITY=1` to add the crawler's per-URL debug lines. Logs carry crawled URLs (as the file log always has) but never credentials or page bodies.

The library itself does not log through telemetry; it keeps its `Logger` and `Exception` callbacks, which the server forwards.

## Dashboards

Six dashboards in the Grafana **CrawlSharp** folder, JSON in `assets/grafana/`. Each links to the others from its header.

| Dashboard | File | Answers |
|---|---|---|
| CrawlSharp / Overview | `crawlsharp-overview.json` | Is it up? Are crawls completing? Failure ratios, crawls running, open streams, time since the last successful crawl, errors by stage. Start here. |
| CrawlSharp / HTTP | `crawlsharp-http.json` | Watson request rate, latency and status by route; crawl endpoint outcomes, stream count, SSE throughput, server stage p95 (a slow client shows in `sse_send`). |
| CrawlSharp / Crawl Pipeline | `crawlsharp-crawl.json` | Jobs and durations; per-stage p95 and time spent per stage (where crawl time goes); queue, worker slots and result buffer; pages by outcome; links skipped by reason; redirects; errors by stage and type. |
| CrawlSharp / Integrations | `crawlsharp-integrations.json` | Outbound HTTP and Playwright calls by operation and outcome, error ratio, p50/p95 latency, failures by error type, 429s and retries, browser contexts and startup time. |
| CrawlSharp / Runtime | `crawlsharp-runtime.json` | CPU, memory, GC, thread pool, exceptions, lock contention, build and configuration. |
| CrawlSharp / Logs and Traces | `crawlsharp-logs-traces.json` | Recent `crawl` traces and failed spans from Tempo; warnings and all server logs from Loki. |

## Recommended alerts

```yaml
groups:
  - name: crawlsharp
    rules:
      - alert: CrawlSharpDown
        expr: absent(watson_server_up{job="crawlsharp-server"}) or max(watson_server_up{job="crawlsharp-server"}) < 1
        for: 2m
        annotations:
          summary: CrawlSharp server is down or not being scraped.

      - alert: CrawlSharpCrawlFailures
        expr: sum(increase(crawlsharp_crawl_jobs_total{crawlsharp_outcome="failure"}[30m])) > 0
        annotations:
          summary: A crawl job failed. Check the Crawl Pipeline dashboard (Errors by stage and type) and failed spans in Tempo.

      - alert: CrawlSharpPageFailureRatioHigh
        expr: |
          sum(rate(crawlsharp_pages_total{crawlsharp_outcome="failure"}[15m]))
            / clamp_min(sum(rate(crawlsharp_pages_total[15m])), 1e-9) > 0.25
        for: 15m
        annotations:
          summary: More than 25% of pages fail outright (connection errors or timeouts).

      - alert: CrawlSharpOutboundErrorsHigh
        expr: |
          sum by (crawlsharp_integration_service) (rate(crawlsharp_integration_requests_total{crawlsharp_outcome=~"failure|timeout|server_error"}[10m]))
            / clamp_min(sum by (crawlsharp_integration_service) (rate(crawlsharp_integration_requests_total[10m])), 1e-9) > 0.2
        for: 10m
        annotations:
          summary: Outbound {{ $labels.crawlsharp_integration_service }} calls are failing.

      - alert: CrawlSharpThrottled
        expr: sum(rate(crawlsharp_integration_requests_total{crawlsharp_outcome="throttled"}[10m])) > 0.5
        for: 10m
        annotations:
          summary: Sites are answering 429; raise RequestDelayMs or lower MaxParallelTasks.

      - alert: CrawlSharpWorkersSaturated
        expr: sum(crawlsharp_workers_in_use) >= sum(crawlsharp_workers_capacity) and sum(crawlsharp_queue_size) > 100
        for: 15m
        annotations:
          summary: Every worker slot is busy and the queue keeps growing.

      - alert: CrawlSharpSlowConsumer
        expr: sum(crawlsharp_results_buffered) > 500
        for: 10m
        annotations:
          summary: Crawled resources are piling up faster than the client reads the event stream.

      - alert: CrawlSharpNoRecentSuccess
        expr: time() - max(crawlsharp_crawl_last_success_seconds) > 86400
        annotations:
          summary: No crawl has completed in 24 hours (resets on restart).

      - alert: CrawlSharpHttp5xx
        expr: |
          sum(rate(http_server_request_duration_seconds_count{job="crawlsharp-server", http_response_status_code=~"5.."}[5m]))
            / clamp_min(sum(rate(http_server_request_duration_seconds_count{job="crawlsharp-server"}[5m])), 1e-9) > 0.05
        for: 5m
        annotations:
          summary: More than 5% of CrawlSharp HTTP requests fail with 5xx.
```

## Cost, cardinality and safety

- **Unobserved cost:** with no listener, every counter and histogram call is an `Enabled` check, and `StartActivity` returns null. The per-stage scope objects are a few small allocations per page, negligible next to an HTTP fetch.
- **Best-effort:** every recording path catches its own exceptions. A throwing listener, a dead OTLP endpoint or an unreachable Loki never changes crawl results (`TelemetrySuite.ThrowingListener_CrawlSucceeds`, `ServerTelemetrySuite.UnreachableBackend_NoThrow`). If the Radiant host cannot start, the server logs a warning and runs without telemetry.
- **Cardinality:** labels are the bounded sets above. URLs, hosts, depths and ids appear only on spans and logs.
- **Secrets:** credentials and authorization headers are never recorded. Span URLs drop query strings and user information. Page bodies are never recorded.
- **Quantiles:** p50/p95/p99 are computed in Grafana from histogram buckets; nothing is precomputed in-process.
- **Retention:** metrics are per-process and reset on restart (including `crawlsharp_crawl_last_success_seconds`); Prometheus keeps history.
- **Prometheus 3 scrape settings:** `Docker/prometheus.yaml` sets `metric_name_validation_scheme: legacy` and `metric_name_escaping_scheme: underscores` on the CrawlSharp job. Without them, Prometheus 3 negotiates UTF-8 metric names, the OpenTelemetry Prometheus exporter then emits dotted names (`crawlsharp.page.size_bytes`) that the dashboards do not use, and it omits the unit suffix on counters whose name ends in `bytes` (`crawlsharp.server.sse.bytes`, Watson's `watson.server.sent.bytes`), which fails the whole scrape. Use the same two settings in any other Prometheus that scrapes the server.
- **OTLP metrics to Tempo:** in the compose stack the OTLP exporter also pushes metrics to Tempo, which ignores them; Prometheus scrapes metrics directly. Point `CRAWLSHARP_OTLP_ENDPOINT` at an OpenTelemetry Collector if you want metrics over OTLP as well.

## Tests

`src/Test.Shared/Suites/TelemetrySuite.cs` and `ServerTelemetrySuite.cs` (run by `Test.Automated`, `Test.Xunit` and `Test.Nunit`) prove the contract with in-memory `MeterListener`/`ActivityListener` capture and a live Prometheus scrape: trace shape and parenting, job/page/link/stage/integration metrics, gauges returning to zero, HTTP 4xx/5xx classification, connection failures with `error.type`, 429 retries, redirects, robots.txt and sitemap.xml stages, politeness delays, cancelled and abandoned crawls, the synchronous `Crawl` API, server settings parsing, the Radiant subscription list, and the no-listener and throwing-listener paths. The headless (Playwright) case runs when `CRAWLSHARP_RUN_HEADLESS=1`.
