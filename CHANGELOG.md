# Change Log

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
