# Change Log

## v1.2.1

### Fixes

- Requests to crawled sites no longer carry W3C trace context.  Outbound HTTP spans were injecting `traceparent` and `tracestate` headers, sending trace and span IDs to third-party sites; the crawler's HTTP handler now suppresses trace context propagation.  Spans and metrics are unchanged.

### Dependencies

- Server: Watson 7.1.0 to 7.2.1.

## v1.2.0

### Additions

- Observability in the library: metrics and traces through the BCL `Meter` and `ActivitySource` named `CrawlSharp`, with no new package dependency.  Covers crawl jobs (count, duration, outcome, running, last success), every pipeline stage (`robots`, `sitemap`, `queued`, `fetch`, `content_type_check`, `browser_navigate`, `auto_expand`, `link_extraction`, `politeness_delay`, `retry_backoff`, `throttle_delay`, `browser_startup`), pages, links discovered/enqueued/skipped by reason, redirects, outbound HTTP and Playwright calls with latency and outcome, 429 retries, queue/worker/result-buffer/visited/browser-context gauges, errors by stage and `error.type`, and build info.  Spans: a `crawl` root with `crawl.page`, `stage:*` and `http GET`/`playwright navigate` client spans, all in one trace.
- `CrawlSharp.Telemetry.CrawlSharpTelemetry`: every meter, source, instrument, span, attribute and label-value name as public constants.
- Server: one Radiant 0.1.2 host subscribed to `Watson`, `CrawlSharp` and `CrawlSharp.Server`, exporting traces over OTLP, logs to Loki, and all metrics (including .NET runtime) on a Prometheus endpoint (`127.0.0.1:9464/metrics` by default).  Configured with `CRAWLSHARP_*` environment variables; see TELEMETRY.md.  Watson's built-in HTTP telemetry is explicitly enabled.
- Server metrics and spans for the crawl endpoint: outcome (`completed`, `bad_request`, `deserialization_error`, `invalid_settings`, `client_disconnected`, `failed`), duration, open streams, server-sent events and bytes, and `deserialize`/`crawler_init`/`sse_send` stages.
- `Docker/compose.yaml`: Prometheus, Tempo, Loki and Grafana (pinned images, healthchecks, `service_healthy` ordering), Grafana provisioned with datasources and six dashboards in a CrawlSharp folder (`assets/grafana/`).  Grafana credentials can be overridden with `GRAFANA_ADMIN_USER` and `GRAFANA_ADMIN_PASSWORD`.
- Dashboard: External Services card on the home page with URLs, default credentials and copy buttons, configurable through `GRAFANA_URL`, `PROMETHEUS_URL`, `TEMPO_URL` and `LOKI_URL`.
- TELEMETRY.md, plus `TelemetrySuite` and `ServerTelemetrySuite` tests; `build-*.sh` equivalents of the image build scripts.

### Fixes

- A page that failed after taking a worker slot released the slot twice, which could let more than `MaxParallelTasks` pages run at once.  Each slot is now released exactly once.
- The 429 backoff jitter no longer shares an unsynchronized `Random` across workers.
- A queue processor that faulted ended the crawl silently; it is now reported through the `Exception` callback and recorded as a failed crawl.
- The server kept crawling after a client stopped reading the event stream; it now stops the crawl.
- The server's Docker healthcheck probes `127.0.0.1` with 2 retries.
- `src/.dockerignore` excludes nested `bin/` and `obj/` folders; the server build context had grown to several gigabytes of local build output. The dashboard gains a `.dockerignore`.

### Behavior notes

- The politeness delay (`RequestDelayMs` or robots.txt `Crawl-delay`) and the 429 throttle pause now run outside the HTTP request, so they appear as their own stages instead of inflating fetch latency; total crawl timing is unchanged.  When retrying a 429, the backoff now happens after the throttled response is released.

## v1.1.0

### Behavior changes (read before upgrading)

- `FollowRedirects = false` now returns the first redirect response (`RedirectOutcome = NotFollowed`) and never requests the target.  In 1.0.22 the HTTP stack followed redirects regardless, so `false` still returned the final page.  Callers that set `false` to avoid the redirect-loop hang should set it back to `true`.
- Credentials are sent only to origins in the credential scope: the start URL's origin, its HTTPS upgrade when the start URL is plain HTTP, and any `Authentication.CredentialOrigins`.  1.0.22 attached them to every request, including external links and redirect targets on other origins.  Authenticated crawls that span several origins must list the extra origins in `CredentialOrigins`.
- `AuthenticationSettings.Validate()` runs in the `WebCrawler` constructor and throws `ArgumentException` for credentials set while `Type` is `None` (previously ignored silently) and for a type whose required fields are missing (previously a misleading exception, swallowed, and an empty crawl).
- 304, 305 and 306 responses are no longer treated as redirects.
- The REST server returns `400 Bad Request` with a description for invalid settings, instead of `500`.

### Fixes

- A redirect loop no longer hangs the crawl.  CrawlSharp turns off automatic redirects in the HTTP stack and follows each hop itself, with loop detection (an A to B to A cycle requests each URL once) and a hop limit.
- Basic and bearer credentials are re-attached to same-origin redirect targets; the HTTP stack used to strip them after the first hop.
- API keys are no longer sent to other origins reached by a redirect, and credentials are no longer sent in cleartext on an HTTPS to HTTP downgrade.
- Links on a redirected page resolve against its final URL, so `/docs` redirecting to `/docs/` finds `/docs/guide.html`.
- A page reached through two redirecting URLs is fetched and returned once, not twice.
- Headless crawls report a looping page as `LoopDetected` instead of dropping it, and authenticated headless crawls send credentials on the page request itself, not only on the content-type check.
- The REST server disposes each `WebCrawler`, which no longer leaks a browser per headless crawl.

### Additions

- `CrawlSettings.MaxRedirects` (default 10, range 1 to 50).
- `AuthenticationSettings.CredentialOrigins` and `AuthenticationSettings.Validate()`.
- `WebResource.FinalUrl`, `WebResource.RedirectChain` and `WebResource.RedirectOutcome`, with the new `RedirectHop`, `RedirectOutcomeEnum` and `RedirectPolicy` types.
- Redirect targets are checked against robots.txt and, when `FollowLinks` is on, the crawl-scope filters; cookies are carried within a redirect chain.
- Dashboard: Max Redirects input, additional credential origins, inline validation of authentication fields, and the final URL, redirect outcome and chain in crawl results.
- Documentation: Redirects, Authentication, Pacing and Upgrading sections in the README; accurate `RequestDelayMs` and `ThrottleMs` descriptions.
- RestWrapper updated to 3.3.1.

## Previous Versions

### v1.0.22

- Added opt-in automatic expansion of common collapsible content during headless browser crawls
- Added tunable headless post-load and post-interaction delays, expansion pass count, and custom expansion selectors
- Added a dashboard toolbar control for selecting proxy, localhost, or custom server endpoints
- Clarified rendered HTML capture behavior for headless navigable pages and direct-download behavior for non-navigable assets
- Added automated coverage for rendered HTML capture, opt-in expansion behavior, revealed-link discovery, and PDF fallback handling

### v1.0.21

- Initial release
- Added support for headless browser crawling
- Added retry with exponential backoff on HTTP 429 responses
