# CrawlSharp 1.0.23: Redirect Handling Fix Plan

Status: implemented and released as **1.1.0** on 2026-09-28. The plan below is kept as written, against `main` at commit `4a40b6b` (library version 1.0.22); the list that follows records where the implementation differs from it.

## Implementation Notes (1.1.0)

- **Version.** Released as 1.1.0, a minor version, not the 1.0.23 patch named below: the release adds public API and changes behavior.
- **No authentication inference.** Credentials set while `Type` is `None` make `Validate()` throw instead of being inferred and sent. Empty and whitespace strings count as unset. When `Type` is set, fields from other credential types are ignored rather than rejected.
- **Cookies within a chain.** Each chain uses its own `HttpClientHandler` with `AllowAutoRedirect = false` and a private `CookieContainer`, so .NET parses `Set-Cookie` and sends cookies natively. The resolver sends `HttpRequestMessage`s itself and wraps the response in RestWrapper's `RestResponse`, which also exposes the raw content headers the headless content-type check needs. RestWrapper 3.3.1 separately keeps repeated response headers as separate values.
- **Scope checks on redirect hops apply only when `FollowLinks` is true.** The scope settings are documented as applying only with `FollowLinks`, and a single-page crawl (`FollowLinks = false`) is expected to follow its start URL wherever it redirects. robots.txt and sitemap.xml redirects stay limited to the start URL's host either way.
- **Headless credentials.** The route handler fetches credentialed requests with `MaxRedirects = 0` and fulfills the response. Playwright does not route the requests a redirect starts, so a redirected *navigation* is aborted instead. The crawler then resolves the chain with a credentialed GET and navigates straight to the final URL, so no request goes out unauthenticated. Redirected subresources are followed by the browser without credentials, which is the safe direction. `IRequest.ResponseAsync()` can stay pending for fulfilled requests, so the browser-hop walk bounds that wait.
- **Duplicate yields** are prevented by a reference set of already-returned resources in addition to `RegisterVisited`, which lets the existing resource win when a final URL was already retrieved.
- **Also fixed:** the server now disposes each `WebCrawler`. `FixtureResponse` moved into its own file, and `FixtureServer` records request headers for credential assertions. The other items under "Noticed, Not Fixed Here" remain open.

CrawlSharp hangs forever on a redirect cycle when `FollowRedirects` is on, and when it is off, redirect targets on authenticated sites are fetched without credentials. Both problems showed up while testing AssistantHub and Pneuma crawls against a local stub site, and both consumers now ship a workaround (`FollowRedirects = false`) that trades the hang for the credential problem. This plan fixes the cause in CrawlSharp so the workaround can come out, and folds in two nearby authentication and documentation problems found while reading the code.

Nothing in the repository has been changed yet. Every line reference below is to the 1.0.22 source as it sits on `main`.

## Root Cause

### Two redirect layers, stacked

CrawlSharp has two independent redirect mechanisms, and in 1.0.22 they are layered on top of each other.

The first layer is the .NET HTTP stack. Every page request goes through `RequestBuilder` (`src/CrawlSharp/Web/WebCrawler.cs:391-417`), which creates a RestWrapper `RestRequest` and never touches its `AllowAutoRedirect` property. RestWrapper defaults that property to `true` (`C:\code\RestWrapper\src\RestWrapper\RestRequest.cs:209`) and copies it onto a fresh `HttpClientHandler` for every request (`RestRequest.cs:698-699`). So the `HttpClientHandler` follows redirects on its own, up to its default `MaxAutomaticRedirections` of 50, before CrawlSharp ever sees a response. CrawlSharp references RestWrapper 3.2.0, which exposes the same property with the same default.

The second layer is CrawlSharp's own redirect branch in `RetrieveWithRestClient` (`WebCrawler.cs:519-593`). It only runs when the response that comes back from the HTTP stack is still a 3xx. For an ordinary redirect that never happens, because the handler already followed it. In practice the branch is reached in three situations only: the handler gave up after 50 hops (a loop), the handler refused to follow an HTTPS to HTTP downgrade (which .NET never auto-follows), or the 3xx carried no usable `Location`. `FollowRedirects` therefore does almost nothing for well-behaved sites, and does real damage in exactly the cases where a redirect is misbehaving.

The existing test `Redirect_HttpStackFollowsRegardlessOfSetting` (`src/Test.Shared/Suites/WebCrawlerSuite.cs:220-235`) documents the first half of this: with `FollowRedirects = false`, the crawl still returns the final page with status 200.

### Why A -> B -> A never finishes

Here is what happens when a crawl reaches `/loop-a`, which returns 302 to `/loop-b`, which returns 302 back to `/loop-a`, with `FollowRedirects = true`:

1. `QueueProcessorInternal` takes a semaphore slot (`WebCrawler.cs:1777`) and calls `RetrieveWebResource` for `/loop-a` (`WebCrawler.cs:1808`).
2. `RetrieveWebResource` sleeps for `RequestDelayMs` in `Pause` (`WebCrawler.cs:772`, `982-986`), checks the visited set (`806-810`), and calls `RetrieveWithRestClient`.
3. The `HttpClientHandler` bounces between the two URLs 50 times, then gives up and hands back the last 302 rather than throwing.
4. CrawlSharp's branch sees the 302 (`519`), reads `Location` (`525-527`), and checks whether the target is already visited (`570`). It is not. Nothing in the chain has been recorded yet, because the current URL is only added to the visited set *after* the recursive call returns (`583`).
5. It recurses into `RetrieveWebResource` for the target (`579`), which pauses again and repeats from step 3.

The recursion has no depth limit and no chain-local memory, and the only place a URL is marked visited sits after a call that never returns. Each level costs roughly 51 HTTP requests plus one `RequestDelayMs` pause (2.5 seconds by default), so the crawl looks alive in the logs while making no progress. The worker task never completes, so `QueueProcessor` never sees `activeTasks` drain (`1710`, `1755`), so `CrawlAsync` spins on `while (!_QueueProcessor.IsCompleted)` (`330-336`) until someone cancels it. The only way out is the caller's cancellation token.

With `FollowRedirects = false` the same loop ends differently: the handler gives up after 50 hops, CrawlSharp logs "ignoring redirect response" (`589-592`), and the 302 is returned as the page. That is the "loop ends as an error page" behavior consumers now depend on.

### Why credentials disappear on redirect targets

When the `HttpClientHandler` follows a redirect, .NET deliberately clears the `Authorization` header before sending the next request, whatever the target origin. Basic and bearer credentials are set through RestWrapper's `Authorization` property (`WebCrawler.cs:404-411`), which ends up in exactly that header, so every hop after the first goes out unauthenticated. AssistantHub's test "WebRepositoryCrawler: authenticated crawl uses the crawl delay and survives a redirect loop" (`C:\Code\AssistantHub\src\Test.Shared\ServiceSuite.RetrievalImprovements.cs:900-944`) watched `GET /target` and `GET /loop-b` arrive at its `CrawlStubServer` with no credentials.

The API-key case is worse, and nobody has noticed it yet. The API key goes in a custom header (`WebCrawler.cs:399-402`), and .NET does *not* strip custom headers on redirect. An API-key crawl that gets redirected to another origin sends the key to that origin. The same is true of every other request today: `RequestBuilder` attaches credentials to every URL the crawler fetches, including external links when `FollowExternalLinks` is on and the domain restrictions are relaxed. The downgrade case in the previous section adds one more leak: when an HTTPS page redirects to HTTP, the handler refuses, CrawlSharp's own branch follows it with a fresh request built by `RequestBuilder`, and Basic credentials go out in cleartext.

### The headless path

The headless path has its own version of each problem. Before navigating, `CheckContentTypeAsync` (`WebCrawler.cs:419-473`) sends a HEAD request through a brand-new `HttpClient` (`431`) with default auto-redirect, so it inherits the same `Authorization` stripping. On a loop it gets a 3xx back, treats the check as failed, and defaults to "navigable".

Playwright then follows redirects inside Firefox (`GotoAsync`, `667-671`). Firefox stops a loop at its own limit of 20 and raises a navigation error (`NS_ERROR_REDIRECT_LOOP`). Nothing in `RetrieveWithPlaywright` catches that except the "Download is starting" filter (`673`), so it falls through to the catch-all in `RetrieveWebResource` (`857-861`), which returns `null`. The headless crawl does not hang, but the looping page vanishes from the results without a trace. Separately, the browser context (`643-650`) is created with no credentials at all, so an authenticated headless crawl only authenticates its HEAD pre-check, never the page itself.

### Smaller defects in the same code

Three more problems turned up while tracing the redirect path. They are cheap to fix alongside the main change and would otherwise make the new behavior look wrong.

The redirect check `resp.StatusCode >= 300 && resp.StatusCode <= 308` (`519`) treats 304 Not Modified, 305 and 306 as redirects. Only 301, 302, 303, 307 and 308 are redirects, plus 300 when it carries a `Location`.

Links are extracted with the *requested* URL as the base (`ExtractLinksFromHtml(link.Url, ...)`, `1817`), not the URL the content came from. The classic trailing-slash redirect breaks this: `/docs` redirects to `/docs/`, the page links to `guide.html`, and CrawlSharp resolves that to `/guide.html` instead of `/docs/guide.html`.

When a redirect lands on an already-visited URL, the branch at `570-576` returns the existing `WebResource`, and `QueueProcessorInternal` enqueues it again (`1911`), so the same page is yielded twice.

### Authentication is silently skipped without a type

`RequestBuilder` only attaches anything when `Settings.Authentication.Type != None` (`WebCrawler.cs:397`), and `AuthenticationSettings.Type` defaults to `None` (`src/CrawlSharp/Web/AuthenticationSettings.cs:13`). A caller who sets `Username` and `Password` and forgets the type gets an unauthenticated crawl with no warning. AssistantHub already hit this and now sets the type explicitly (its `WebRepositoryCrawler.cs:219-236` carries a comment about it).

The failure mode for a half-configured API key is worse. With `Type = ApiKey` and an empty `ApiKeyHeader`, the first condition at `399-400` is false, neither of the other branches matches, and line 413 throws `ArgumentException("Unsupported authentication type ApiKey")`. The message is wrong, and the exception is swallowed by the catch-all at `857-861`. Every page, including robots.txt and the sitemap, comes back `null`, and the crawl finishes empty. `CheckContentTypeAsync` handles the same misconfiguration by sending nothing.

### RequestDelayMs versus ThrottleMs

The two names invite confusion, and AssistantHub needed a code comment (`WebRepositoryCrawler.cs:215-216`) to keep them straight. The actual behavior:

| Setting | Default | What it really does | Where |
|---|---|---|---|
| `RequestDelayMs` | 2500 | Pause before *every* request, including robots.txt, sitemap.xml and (after this fix) every redirect hop. The pause is per worker task, so with `MaxParallelTasks = 8` up to eight requests can start per interval. A robots.txt `Crawl-delay` replaces it entirely, whether higher or lower. `WebCrawler.Delay` exposes the effective value. | `CrawlSettings.cs:276-291`; copied at `WebCrawler.cs:157`; overridden at `249-254` and `308-313`; applied in `Pause`, `982-986` |
| `ThrottleMs` | 5000 | One pause after a 429 response, taken only when `RetryOn429` is off or the retries are used up, just before the 429 is returned as the result. It has nothing to do with normal pacing. | `CrawlSettings.cs:197-211`; applied at `WebCrawler.cs:605-606` and `707-708` |

The XML doc for `ThrottleMs` says "delay when receiving a 429 response", which undersells the condition, and the README row (`README.md:90`) mentions exhausted retries but not `RetryOn429 = false`. Neither doc says anything about parallelism or robots.txt overriding `RequestDelayMs`.

## Fix Design

The core decision is to make CrawlSharp the only thing that follows redirects. Turn off `AllowAutoRedirect` on every request and follow the chain in one iterative loop that owns the whole decision: whether to follow, where to, with which method, with or without credentials, and when to stop. Once the HTTP stack stops making those choices invisibly, every property this plan wants (bounded hops, loop detection, credential scoping, crawl-scope checks, a recorded chain) becomes a local check inside that one loop.

### The resolver loop

Replace the recursive branch at `WebCrawler.cs:519-593` with a private `ResolveAsync` method that `RetrieveWithRestClient` calls. It is iterative, never recursive, and never re-enters `RetrieveWebResource`. The steps for one requested URL:

1. Start with `current` = the requested URL, `method` = GET (or HEAD for the headless pre-check), an empty `RedirectChain`, an empty per-chain `CookieContainer`, and an empty seen-map from URL to the cookie header sent to it.
2. If `current` is already in the seen-map with the same cookie header, stop with `LoopDetected`. Otherwise record it.
3. For every hop after the first: call `Pause`, check robots.txt for the target (`RobotsDisallowed` if blocked), and check crawl scope (`OutOfScope` if blocked, see below). The first request keeps today's checks in `RetrieveWebResource`.
4. Send the request with `AllowAutoRedirect = false`. Attach credentials only if the origin of `current` is in the credential scope. Attach the chain's cookies for `current`. Apply the existing 429 retry logic per hop.
5. Store any `Set-Cookie` values from the response in the chain's `CookieContainer`. Read them with `GetValues("Set-Cookie")`, not the comma-joined value, because `Expires` dates contain commas.
6. If the status is not a redirect, or `FollowRedirects` is false, build the `WebResource` and stop (`None` or `NotFollowed`).
7. Resolve `Location` against `current` (`MissingLocation` or `InvalidLocation` if that fails), append a hop to the chain, and stop with `MaxRedirectsExceeded` if the chain is now longer than `MaxRedirects`.
8. Work out the next method from the status, set `current` to the target, and go back to step 2.

When the loop stops, the `WebResource` is built from the last response actually received. `Status` is that response's status, `Data` is its body, and the new `RedirectOutcome` field says why it stopped.

The per-chain cookie jar is not decoration. Today the `HttpClientHandler` carries cookies across hops within one chain, and some sites depend on that: a consent wall or session bootstrap that sets a cookie and redirects back to the same URL. If manual following dropped cookies, those sites would flip from working to `LoopDetected`. Keying the seen-map on URL plus cookie header lets "same URL, new cookie" through while still catching real loops, and `MaxRedirects` bounds the whole thing regardless. Before relying on it, confirm that RestWrapper's `HttpClientHandler` (created with `UseCookies` at its default of `true`) sends a manually added `Cookie` header. If it does not, add a `UseCookies` switch to RestWrapper or send through a dedicated `HttpClient` for chain hops.

### Loop detection and the hop limit

Two independent guards, because each catches something the other misses. The seen-map catches a true cycle on its first repeat, so in the A -> B -> A case each URL is requested exactly once and the crawl moves on. That is also the most useful assertion for the tests. `MaxRedirects` catches chains that never repeat a URL, such as `/r?n=1 -> /r?n=2 -> ...`, and chains whose cookies change on every hop.

New setting `CrawlSettings.MaxRedirects`: default 10, minimum 1, maximum 50, with values outside that range throwing `ArgumentOutOfRangeException`, as `MaxCrawlDepth` and `MaxExpansionPasses` already do. Ten matches common browser and crawler practice and keeps the worst case (11 requests, each with its own `PageTimeoutMs` and `RequestDelayMs`) bounded. A value of 0 is deliberately not allowed, because "follow no redirects" is what `FollowRedirects = false` means.

Both guards produce a result, never an exception and never a hang. The looping page is yielded like any other, with its 3xx status and the chain attached, and the crawl continues.

### Credential scope

One rule covers redirects and ordinary links alike: **a request carries credentials only if its origin (scheme, host and port) is in the credential scope.** The credential scope is:

- the origin of `StartUrl`;
- when `StartUrl` is plain HTTP, the HTTPS origin on the same host at the default port, because an HTTP to HTTPS upgrade is the most common redirect there is and refusing credentials to it would break most authenticated crawls on the first hop;
- any origins listed in a new `AuthenticationSettings.CredentialOrigins` (`List<string>`, default empty), for sites that genuinely span several origins.

An HTTPS start URL never adds its HTTP twin, so a downgrade redirect never carries credentials. The rule is the same for Basic, bearer and API key, and it applies per hop. A chain A -> B -> A therefore sends credentials to both requests on A and to none on B. Credentials scoped this way never need "forwarding": they are attached fresh to each request that qualifies, so there is no header left over from a previous hop to leak.

The rule also closes the older, quieter leak where credentials went to every external link. That is a behavior change for anyone crawling an authenticated site that spans subdomains, and `CredentialOrigins` is the escape hatch. It gets its own entry in the changelog.

### Crawl scope for redirect targets

Pull the link filters out of `QueueProcessorInternal` (`WebCrawler.cs:1836-1893`: denied domains, same root domain, same subdomain, child URLs, allowed domains, external links, exclusion patterns) into one private method, `IsInCrawlScope(string url, out string reason)`. The link loop and the resolver both call it. The depth check stays in the link loop, since redirect hops do not add depth.

A redirect that leaves the crawl scope is not followed, and the page comes back with `RedirectOutcome = OutOfScope`, the 3xx status, and the target in the chain, so the log and the result both say where it tried to go. robots.txt and sitemap.xml are fetched outside the link filters today. Their redirects get a narrower rule: follow only if the target is on the same host as `StartUrl`. Otherwise an HTTP to HTTPS redirect on `/robots.txt` would be blocked by `RestrictToChildUrls` whenever the start URL has a path.

There is a trade-off here, and the plan takes it on purpose. If the start URL itself redirects out of scope (`example.com` to `www.example.com` with `RestrictToSameSubdomain`), the crawl now yields one `OutOfScope` result instead of silently following. Today's silent follow does not help much anyway: the links on `www` pages already fail `IsSameSubdomain` against the configured start URL, so the crawl stops after one page. The README should tell users to configure the canonical start URL, and the `OutOfScope` result shows them what it is.

### Method semantics

Put the per-status rules in a public static helper, `RedirectPolicy`, so they can be unit tested without a server:

| Status | Next method |
|---|---|
| 301, 302 | GET if the original was POST, otherwise unchanged |
| 303 | GET, except HEAD stays HEAD |
| 307, 308 | unchanged, body resent |
| 300 | treated as 302, only when `Location` is present |
| 304, 305, 306, others | not a redirect; returned as the result |

The crawler only issues GET and HEAD today (the `method` parameter of `Crawl(HttpMethod)` at `WebCrawler.cs:240` is ignored), so the POST rows are there for correctness and for the unit tests. `RedirectPolicy` also owns `IsRedirectStatus(int status, bool hasLocation)`, `GetOrigin(Uri)` and `ResolveLocation(Uri current, string location)`. The last one should use `new Uri(current, location)` (RFC 3986 resolution, which handles `../x`, `x`, `?q`, `/abs` and `//host/x` correctly), reject anything that is not http or https, and strip the fragment. It is stateless, so it is thread-safe, and the XML docs should say so.

### What the crawler records

`WebResource` gets three new properties:

| Property | Type | Meaning |
|---|---|---|
| `FinalUrl` | `string` | URL of the response whose body is in `Data`. Equals `Url` when there was no redirect. |
| `RedirectChain` | `List<RedirectHop>` | One entry per redirect received: `Url` (where the 3xx came from), `Status`, `Location` (the resolved absolute target, or the raw header value if it could not be resolved). Never null; empty when there was no redirect. |
| `RedirectOutcome` | `RedirectOutcomeEnum` | `None`, `Followed`, `NotFollowed`, `LoopDetected`, `MaxRedirectsExceeded`, `OutOfScope`, `RobotsDisallowed`, `MissingLocation`, `InvalidLocation`. |

`Url` keeps meaning "the address that was requested", which is what 1.0.22 effectively produced for ordinary redirects via the HTTP stack, and what AssistantHub's test asserts ("redirected page listed under its linking address"). Callers who want the canonical address read `FinalUrl`. Link extraction in `QueueProcessorInternal` switches its base URL from `link.Url` to `wr.FinalUrl`, which fixes the trailing-slash bug.

The visited set records the requested URL, every intermediate hop URL and the final URL, all pointing at the same resource. That way a later link to `/loop-b` or to an intermediate hop does not replay the chain. If the final URL was already visited by another path, the resolver records the alias in the visited set and returns nothing to yield, which removes the duplicate described above. Add a `TryAddAlreadyVisited(Uri, WebResource)` that returns false when the key exists, and use it for the final URL, so two parallel tasks that land on the same target cannot both yield it.

The new fields serialize automatically in the server's SSE stream, since `CrawlRoute` serializes the whole `WebResource` (`src/CrawlSharp.Server/Program.cs:244-249`).

### FollowRedirects = false

After the fix, `false` means what it says. The first 3xx is returned as the result, with its `Location` header intact in `Headers`, `RedirectOutcome = NotFollowed`, and nothing requested from the target. The target is not enqueued as a link either. Keeping the setting literal is simpler to document than "not followed here but discovered there".

A literal `false` is the one change that can surprise an existing caller, and the two known callers are the reason this plan exists. Pneuma and AssistantHub both set `false` as a workaround. On 1.0.23 they would start receiving empty 302 pages instead of redirected content, so each must switch back to `true` in the same change that moves it to 1.0.23. The release steps below include the hand-off.

### Headless path

The headless path reuses the resolver rather than trusting the browser to follow redirects.

`CheckContentTypeAsync` stops creating its own `HttpClient` and runs its HEAD request through `ResolveAsync` with method HEAD. That gives the pre-check the same hop limit, loop detection, scope checks and credential rule. If the chain ends in `LoopDetected`, `MaxRedirectsExceeded`, `OutOfScope` or `RobotsDisallowed`, return that result directly and never launch the browser. If the HEAD request fails outright (some servers answer 405), fall back to today's behavior and navigate to the requested URL.

When the pre-check succeeds, navigate Playwright straight to the resolved final URL, so the browser normally sees no redirects at all. Record `Url` as the requested URL, `FinalUrl` from `response.Url`, and `RedirectChain` as the pre-check's chain plus any browser-side hops found by walking `response.Request.RedirectedFrom`. If the browser still ends up somewhere out of scope (a server that redirects GET differently from HEAD), discard the content and return `OutOfScope`.

Credentials for headless requests go through `context.RouteAsync("**/*", ...)`. The handler adds the credential header through `route.ContinueAsync` only when the request's origin is in the credential scope, and continues every other request untouched. One mechanism covers all three credential types. `HttpCredentials` would cover only Basic, and only after a 401 challenge. `ExtraHTTPHeaders` is not an option at all, because it sends the header to every origin the page loads from. During implementation, verify whether Playwright calls route handlers for redirect hops. If it does not, the injected header only reaches the first request of a browser-side chain, which is the safe direction.

Finally, catch `PlaywrightException` from `GotoAsync` when the message indicates a redirect loop, and return a `LoopDetected` result. The page is then reported instead of silently dropped.

### Authentication validation and inference

Add `AuthenticationSettings.Validate()`, and call it from the `WebCrawler` constructor so a bad configuration fails before a crawl starts. That matters for the server, which switches the response into SSE mode before iterating (`Program.cs:236`) and cannot return a clean 400 once it has. The rules:

- `Type` is `None` and no credential fields are set: nothing is sent, as today.
- `Type` is `None` and exactly one credential family is populated (`Username`, or `BearerToken`, or both `ApiKeyHeader` and `ApiKey`): infer that type, and log a warning at the start of the crawl naming the inferred type. The warning has to wait for `Crawl`/`CrawlAsync`, because `Logger` is assigned after construction.
- `Type` is `None` and more than one family is populated: throw `ArgumentException` saying the type is ambiguous and must be set.
- `Type` is set but its required fields are missing (`Username` for Basic, `BearerToken` for BearerToken, `ApiKeyHeader` and `ApiKey` for ApiKey): throw `ArgumentException` naming the missing field. This replaces the misleading throw at `WebCrawler.cs:413`.
- `CredentialOrigins` entries must be absolute http or https URLs. Anything else throws `ArgumentException`.

The server's `ExceptionRoute` (`Program.cs:167-183`) maps `ArgumentException` to 400 `BadRequest` with the exception message as the description (`ApiErrorResponse` already takes one), instead of the current 500.

### Delay settings documentation

No renames. `ThrottleMs` is a misleading name, but renaming a public setting is a breaking change and belongs in a major version, if anywhere. For 1.0.23, rewrite the XML docs for both properties to state exactly what the table in the root-cause section says: per-request, per-task, overridden by robots.txt `Crawl-delay`, applied to redirect hops, for `RequestDelayMs`; one pause before returning a 429 once retries are off or exhausted, for `ThrottleMs`. Include defaults and minimums in both, as CODE_STYLE.md requires. Mirror the wording in the README, add a short "Pacing" subsection there, and update the two dashboard hints.

## API and Settings Changes

| Change | Kind | Default | Compatibility |
|---|---|---|---|
| `CrawlSettings.MaxRedirects` (int, 1 to 50) | new | 10 | Additive. Existing JSON without it deserializes to 10. |
| `AuthenticationSettings.CredentialOrigins` (`List<string>`) | new | empty | Additive. Setter converts null to an empty list, as `AllowedDomains` does. |
| `AuthenticationSettings.Validate()` | new | n/a | Called by the `WebCrawler` constructor. Throws on configurations that silently failed before. |
| `WebResource.FinalUrl`, `RedirectChain`, `RedirectOutcome` | new | `null`/`Url`, empty, `None` | Additive in C# and JSON. |
| `RedirectHop`, `RedirectOutcomeEnum`, `RedirectPolicy` | new public types | n/a | Additive. One type per file. |
| `FollowRedirects = false` | behavior | n/a | Now literally stops at the first 3xx. Callers using it as a loop workaround must switch it back on. |
| Credentials sent only to credential-scope origins | behavior | n/a | Stops leaking to external and downgraded origins. Multi-origin authenticated crawls need `CredentialOrigins`. |
| Credentials with `Type = None` | behavior | n/a | Now inferred and sent, with a warning. |
| 304, 305, 306 | behavior | n/a | No longer treated as redirects. |
| Server returns 400 for invalid `Authentication` | behavior | n/a | Was 500, or a silently empty crawl. |

A note on the version number. Under SemVer 2.0.0, new public settings and types are backward-compatible additions, which is normally a MINOR bump. The maintainer has asked for a PATCH release, 1.0.23, and this plan follows that instruction. Whoever cuts the release should make that call deliberately rather than by default, and VERSIONING.md is clear that the implementing agent does not change the number on its own initiative either way.

## Implementation Checklist

Library (`src/CrawlSharp/Web/`):

- [ ] Add `RedirectOutcomeEnum.cs`, `RedirectHop.cs` and `RedirectPolicy.cs`, each with a single type, usings inside the namespace, and full XML docs including thread-safety notes.
- [ ] `CrawlSettings.cs`: add `MaxRedirects` with a backing field `_MaxRedirects = 10` and range validation. Rewrite the XML docs for `FollowRedirects`, `ThrottleMs` and `RequestDelayMs`. Move the file's usings inside the namespace while it is open.
- [ ] `AuthenticationSettings.cs`: add `CredentialOrigins` and `Validate()`, with `/// <exception>` tags.
- [ ] `WebResource.cs`: add `FinalUrl`, `RedirectChain` (null-guarded setter) and `RedirectOutcome`.
- [ ] `WebCrawler.cs` constructor: call `Validate()`, compute the effective auth type, and build the credential-scope origin set.
- [ ] `WebCrawler.cs` `RequestBuilder` (`391-417`): set `req.AllowAutoRedirect = false`. Take the target `Uri` and attach credentials only when its origin is in scope, using the effective auth type.
- [ ] `WebCrawler.cs`: add `ResolveAsync` as designed. Move the 429 retry loop inside it, per hop.
- [ ] `WebCrawler.cs` `RetrieveWithRestClient` (`492-635`): delete the recursive branch (`519-593`) and build the result from the resolver's outcome.
- [ ] `WebCrawler.cs`: extract `IsInCrawlScope` from `1836-1893`, and use it in both the link loop and the resolver. Apply the same-host rule for robots.txt and sitemap.xml chains.
- [ ] `WebCrawler.cs`: add `TryAddAlreadyVisited`. Register hop and final URLs, and stop yielding duplicates (`570-576`).
- [ ] `WebCrawler.cs` `QueueProcessorInternal` (`1817`): extract links relative to `wr.FinalUrl`.
- [ ] `WebCrawler.cs` `CheckContentTypeAsync` (`419-473`): route the HEAD request through `ResolveAsync` and drop the private `HttpClient`.
- [ ] `WebCrawler.cs` `RetrieveWithPlaywright` (`637-766`): navigate to the resolved URL, inject credentials with `RouteAsync` for in-scope origins, record `FinalUrl` and the chain, and catch redirect-loop navigation errors.
- [ ] Verify the manual `Cookie` header survives RestWrapper's handler, and that `Set-Cookie` values are read individually.
- [ ] Build both target frameworks with zero new warnings, and commit the regenerated `src/CrawlSharp/CrawlSharp.xml`.
- [ ] No `var`, no tuples, `.ConfigureAwait(false)` on every await, cancellation checked between hops, no console output, no em-dashes in comments or docs.

Server (`src/CrawlSharp.Server/Program.cs`):

- [ ] `ExceptionRoute`: map `ArgumentException` to 400 `BadRequest` with the message as the description.

Dashboard (`dashboard/src/`):

- [ ] `views/NewCrawlView.jsx`: add a "Max Redirects" number input (min 1, max 50, default 10) next to the Follow Redirects toggle (`292-297`). Change that toggle's hint to say redirects are followed by the crawler with loop detection and credentials kept on the same origin only.
- [ ] `views/NewCrawlView.jsx`: change the hints at `184-203` to "Pause after a 429 once retries are off or used up" (Throttle) and "Pause before every request, per parallel task; robots.txt Crawl-delay overrides it" (Request Delay).
- [ ] `views/NewCrawlView.jsx` (`472-513`): add inline validation for required credential fields per auth type, plus a summary error at the top of the form, as DASHBOARD_STYLE_AND_USABILITY.md asks.
- [ ] `utils/api.js` (`buildSettingsPayload`, around `125`): send `MaxRedirects: parseIntSetting(config.maxRedirects, 10)`. Add `maxRedirects: '10'` to the default config. Saved templates without the field fall back to the default.
- [ ] `views/CrawlResultsView.jsx`: when `FinalUrl` differs from `Url`, show it as a secondary line under the URL, and show a badge for `RedirectOutcome` values that are not `None`, `Followed` or `NotFollowed`.
- [ ] `npm run build` and commit the regenerated `dashboard/dist/` (it is tracked).

## Tests

All new cases are Touchstone descriptors in `src/Test.Shared`, written in the existing style: `Case.Async`, the in-process `FixtureServer` bound to `127.0.0.1`, `CrawlHelper.CreateSettings` (which zeroes delays), assertions through `Check`, and no console output. Cross-origin cases use two `FixtureServer` instances. They share a host but have different ports, so they are different origins. Any case that could hang wraps the crawl in a `CancellationTokenSource` with a 15-second timeout and asserts the timeout did not fire. That turns a regression into a fast failure instead of a stuck test run.

The strongest assertions use `FixtureServer.RequestCount(path)`. A loop test that only checks "the crawl finished" would pass against a handler that quietly follows 50 hops. One that checks each loop URL was requested exactly once proves the resolver is doing the work.

New suite `Suites/RedirectPolicySuite.cs` (unit, no server), registered in `CrawlSharpSuites.All`:

| Case | Asserts |
|---|---|
| `IsRedirectStatus_Table` | 301, 302, 303, 307, 308 are redirects; 300 only with `Location`; 304, 305, 306, 200 and 404 are not |
| `RedirectMethod_Table` | (GET,301)->GET, (POST,301)->GET, (POST,302)->GET, (POST,303)->GET, (HEAD,303)->HEAD, (PUT,307)->PUT, (POST,308)->POST |
| `ResolveLocation_Relative` | `b`, `../b`, `/b`, `?q=1`, `//127.0.0.1:9/x` and an absolute URL each resolve correctly against `http://127.0.0.1:8/docs/a`; fragments are stripped |
| `ResolveLocation_RejectsSchemes` | `javascript:`, `mailto:`, `ftp:` and garbage return null |
| `Origin_Comparison` | Scheme, host and port all matter; default ports are normalized (`http://h` equals `http://h:80`) |

New suite `Suites/RedirectSuite.cs` (integration), registered in `CrawlSharpSuites.All`:

| Case | Setup | Asserts |
|---|---|---|
| `Ordinary_Followed` | `/start` 302 to `/final` | Status 200, `Url` is `/start`, `FinalUrl` is `/final`, one hop, outcome `Followed` |
| `Loop_Terminates` | `/loop-a` 302 to `/loop-b` 302 to `/loop-a` | Finishes inside the timeout, outcome `LoopDetected`, `RequestCount` is 1 for each loop URL |
| `Loop_FromLinkedPage` | Home links to `/page2` and `/loop-a`, with `FollowLinks` on | `/page2` is still crawled, the loop page is yielded once, and the crawl completes |
| `HopLimit_Exceeded` | `/r0` to `/r1` to ... `/r15`, `MaxRedirects = 10` | Outcome `MaxRedirectsExceeded`, `/r10` requested, `/r11` never requested |
| `HopLimit_Boundary` | Exactly 10 hops ending in 200 | Status 200, outcome `Followed`, 10 hops recorded |
| `Relative_Locations` | `/docs/a` to `b`, then `/up/x` to `../y`, then `/q` to `?z=1` | Each lands on the correct fixture path |
| `NotFollowed_WhenDisabled` | `FollowRedirects = false` | Status 302, `Location` in `Headers`, outcome `NotFollowed`, `RequestCount("/final")` is 0 |
| `OutOfScope_ChildUrls` | Start `/section/start` 302 to `/other`, `RestrictToChildUrls` | Outcome `OutOfScope`, `/other` never requested |
| `OutOfScope_DeniedDomain` | 302 to `http://denied.invalid/x`, `DeniedDomains` contains it | Outcome `OutOfScope` with no network error |
| `OutOfScope_ExcludePattern` | 302 to `/skip.pdf`, pattern `\.pdf$` | Outcome `OutOfScope`, never requested |
| `Robots_BlocksHop` | robots.txt disallows `/secret`, 302 to `/secret`, robots honored | Outcome `RobotsDisallowed` |
| `NotModified_NotARedirect` | `/nm` returns 304 with a `Location` header | Status 304, outcome `None`, target never requested |
| `LinkBase_UsesFinalUrl` | `/docs` 301 to `/docs/`, which links `guide.html` | `/docs/guide.html` crawled, `/guide.html` never requested |
| `SharedTarget_YieldedOnce` | `/a` and `/b` both 302 to `/final` | `/final` fetched once, and no two yielded resources have the same `FinalUrl` |
| `Cookie_CarriedWithinChain` | `/bounce` sets a cookie and 302s to itself, then returns 200 when the cookie is present | Status 200, outcome `Followed` |
| `SeeOther_UsesGet` | 303 to a handler that records the HTTP method | Recorded method is GET |

Extend `Suites/AuthenticationCrawlSuite.cs`. The existing `GuardHeader` helper already turns the fixture into a credential check; the new cases add a handler on the second server that records what it received.

| Case | Asserts |
|---|---|
| `Basic_SameOriginRedirect_KeepsCredentials` | `/start` and `/target` both guarded; status 200 with "granted" |
| `Bearer_SameOriginRedirect_KeepsCredentials` | Same, with a bearer token |
| `ApiKey_SameOriginRedirect_KeepsCredentials` | Same, with `x-api-key` |
| `Basic_CrossOriginRedirect_DropsCredentials` | Server A 302s to server B; B saw no `Authorization` header |
| `ApiKey_CrossOriginRedirect_DropsCredentials` | B saw no `x-api-key` header (this is the leak 1.0.22 has today) |
| `CrossOrigin_ThenBack_ReattachesOnlyOnOrigin` | A to B to A; both A requests carry credentials, the B request does not |
| `CredentialOrigins_AllowsListedOrigin` | B listed in `CredentialOrigins` receives credentials |
| `ExternalLink_NoCredentials` | Page on A links to B, external links and domains allowed; B's request carries no credentials |
| `Loop_Authenticated_AllRequestsAuthorized` | Mirrors AssistantHub's stub: an authenticated site with `/moved` and a loop; zero unauthorized requests, and the loop terminates |
| `TypeNone_CredentialsInferred` | Basic credentials with `Type = None`; status 200 |
| `ApiKey_MissingHeader_Throws` | Constructor throws `ArgumentException` naming `ApiKeyHeader` |
| `Ambiguous_Throws` | Username and bearer token both set with `Type = None`; throws `ArgumentException` |

Update the existing suites:

- [ ] `WebCrawlerSuite.cs:200-235`: keep `Redirect_ReturnsFinalContent` and add `FinalUrl` and chain assertions. Replace `Redirect_HttpStackFollowsRegardlessOfSetting` with a case asserting the new `NotFollowed` behavior, and give it a display name that says so.
- [ ] `CrawlSettingsSuite.cs`: `MaxRedirects` defaults to 10, and 0 and 51 throw.
- [ ] `WebResourceSuite.cs`: new property defaults; `RedirectChain` is never null; JSON round trip keeps the new fields.
- [ ] `SettingsSuite.cs`: `CredentialOrigins` null becomes an empty list; each `Validate()` rule.

Headless cases in `Suites/HeadlessSuite.cs` stay opt-in behind `CRAWLSHARP_RUN_HEADLESS=1`: `Redirect_Loop_Reported` (a looping navigable page yields a `LoopDetected` result instead of disappearing), `Redirect_FinalUrlRecorded`, `Auth_Basic_SameOrigin_Sent` and `Auth_CrossOrigin_NotSent`.

Run everything on both target frameworks before release:

```
dotnet run --project src/Test.Automated -f net8.0
dotnet run --project src/Test.Automated -f net10.0
dotnet test src/Test.Xunit
dotnet test src/Test.Nunit
set CRAWLSHARP_RUN_HEADLESS=1 && dotnet run --project src/Test.Automated -f net10.0
```

## Documentation Updates

The README is where most consumers learn the settings, so it takes the biggest edit. It also has seven em-dashes, which WRITING_DOCUMENTS.md forbids everywhere, and they should go while the file is open.

- [ ] `README.md`: replace "New in v1.0.22" (line 9) with "New in v1.0.23", summarizing the redirect fix, credential scoping, auth validation and the new fields.
- [ ] `README.md` Crawl Settings table (`66-96`): add `MaxRedirects`; rewrite `FollowRedirects`, `ThrottleMs` and `RequestDelayMs` to match the new XML docs.
- [ ] `README.md`: add a "Redirects" subsection covering the resolver, the hop limit, loop results, `RedirectOutcome` values, the scope rules, and the advice to configure the canonical start URL.
- [ ] `README.md`: add an "Authentication" section (there is none today) covering the three types, inference and validation, the credential scope and `CredentialOrigins`.
- [ ] `README.md`: add a "Pacing" subsection explaining `RequestDelayMs` versus `ThrottleMs`, the per-task behavior and the robots.txt override.
- [ ] `README.md` Web Resources list (`126-140`): add `FinalUrl`, `RedirectChain` and `RedirectOutcome`, and correct `Url` to "the URL that was requested".
- [ ] `README.md`: replace every em-dash (the Dashboard features list uses them).
- [ ] `REST_API.md`: add `MaxRedirects` to the sample body; add `CredentialOrigins` to an authenticated example; show `FinalUrl`, `RedirectChain` and `RedirectOutcome` in the response sample; document the 400 for invalid `Authentication`.
- [ ] `CrawlSharp.postman_collection.json`: add `MaxRedirects` to both request bodies. Add a "Crawl an authenticated site" request using variables for the credentials, with a request-level description of the redirect and credential rules.
- [ ] `Docker/settings.json`: add `"MaxRedirects": 10`.
- [ ] `CHANGELOG.md`: add a `v1.0.23` section at the top and move `v1.0.22` under "Previous Versions". List the behavior changes (`FollowRedirects = false`, credential scope, inference, 304 handling, 400 response) separately from the fixes, so upgraders can find them.
- [ ] `src/CrawlSharp/CrawlSharp.csproj` `PackageReleaseNotes` (line 20): one sentence on the redirect and credential fixes.

## Release 1.0.23

The maintainer has explicitly asked for the PATCH bump to 1.0.23 as part of this work. These are every place the version string appears today:

| File | Line | Change |
|---|---|---|
| `src/CrawlSharp/CrawlSharp.csproj` | 5 | `<Version>1.0.23</Version>` |
| `src/CrawlSharp/CrawlSharp.csproj` | 20 | `PackageReleaseNotes` text |
| `README.md` | 9 | "New in v1.0.23" |
| `CHANGELOG.md` | 3 | new `## v1.0.23` section |
| `Docker/run.sh` | 2 | `IMG_TAG='v1.0.23'` |
| `Docker/run.bat` | 16 | usage example `v1.0.23` |
| `src/CrawlSharp.Server/Dockerrun.sh` | 2 | `IMG_TAG='v1.0.23'` |
| `src/CrawlSharp.Server/Dockerrun.bat` | 16 | usage example |
| `src/CrawlSharp.Server/Dockerbuild.bat` | 20 | usage example |
| `src/Dockerbuild.bat` | 12 | usage example |

Leave these alone. `Docker/compose.yaml` pulls `:latest` for both images and picks up the release without an edit. `dashboard/package.json` and `package-lock.json` sit at `1.0.0` and have never tracked the library version; align them only if the maintainer decides to. `CrawlSharp.Server.csproj` has no `<Version>`. `archive/CRAWLSHARP_IMPROVEMENTS.md` is history. After the edits, `git grep -n "1\.0\.22"` should match only `CHANGELOG.md` and the archive.

Release steps, in order:

1. [ ] All tests pass on net8.0 and net10.0, headless included, and the build has no new warnings.
2. [ ] Commit the code, docs and version changes (the maintainer commits; the implementing agent does not unless asked).
3. [ ] **Publish the NuGet package.** `GeneratePackageOnBuild` is on, so `dotnet build src/CrawlSharp/CrawlSharp.csproj -c Release` writes `CrawlSharp.1.0.23.nupkg` and `CrawlSharp.1.0.23.snupkg` under `src/CrawlSharp/bin/Release/`. Push with `dotnet nuget push src/CrawlSharp/bin/Release/CrawlSharp.1.0.23.nupkg --source https://api.nuget.org/v3/index.json --api-key <key>`; the symbols package goes up with it. CrawlSharp is the only packable project. Confirm 1.0.23 is listed on nuget.org before step 6, because AssistantHub restores from there.
4. [ ] **Build and publish the Docker images** from the repository root: `build-server.bat v1.0.23` (the server image `jchristn77/crawlsharp`, built from `src/CrawlSharp.Server/Dockerfile` with the `src` context) and `build-dashboard.bat v1.0.23` (`jchristn77/crawlsharp-ui`, from `dashboard/`). Each runs one multi-platform cloud build (`linux/amd64`, `linux/arm64/v8`) on the `cloud-jchristn77-jchristn77` builder, pushes both `:v1.0.23` and `:latest`, then pulls them into the local image store. The dashboard image must be rebuilt too, because its form and results view changed.
5. [ ] Smoke test with `Docker/compose-up.bat`. Post a crawl from the Postman collection against a site with a redirect, check `FinalUrl` and `RedirectOutcome` in the SSE output, then `Docker/compose-down.bat`.
6. [ ] **Tell the AssistantHub agent** that CrawlSharp 1.0.23 is published. The message below is ready to paste.
7. [ ] Tell whoever maintains Pneuma the same thing. Pneuma sets `FollowRedirects = false` for the same reason and has to flip it back when it upgrades.

Message for the AssistantHub agent:

> CrawlSharp 1.0.23 is on NuGet. It follows redirects itself: hop limit `MaxRedirects` (default 10), loop detection that ends a cycle after one pass, credentials re-attached only on the start URL's origin (plus its HTTPS upgrade and any `Authentication.CredentialOrigins`), and redirect targets checked against the crawl's domain and child-URL rules. Each `WebResource` now carries `FinalUrl`, `RedirectChain` and `RedirectOutcome`; `Url` is still the requested address. Please:
> 1. Bump `CrawlSharp` from 1.0.22 to 1.0.23 in `src/AssistantHub.Core/AssistantHub.Core.csproj` (line 53).
> 2. In `src/AssistantHub.Core/Services/Crawlers/WebRepositoryCrawler.cs` `BuildSettings`, set `FollowRedirects = true` (or delete the line; true is the default) and replace the workaround comment at lines 202-205. On 1.0.23, `false` returns bare 302 pages with no content. The auth comment at 219-220 can say CrawlSharp now infers the type, though setting it explicitly is still right.
> 3. Consider skipping resources whose `RedirectOutcome` is `LoopDetected`, `MaxRedirectsExceeded`, `OutOfScope`, `RobotsDisallowed`, `MissingLocation` or `InvalidLocation` when indexing, since their `Data` is a redirect body, not content.
> 4. In `src/Test.Shared/ServiceSuite.RetrievalImprovements.cs`, test "WebRepositoryCrawler: authenticated crawl uses the crawl delay and survives a redirect loop" (around lines 900-944): replace the tolerance for unauthenticated `/loop-` paths with an assertion that `site.UnauthorizedPaths` is empty, remove the "Known limitation" comment, and keep the `/moved` and no-hang assertions. `CrawlStubServer` can then require credentials on `/target` too (line 1156 exempts it), which proves same-origin redirects stay authenticated.

## Acceptance Criteria

The release is done when each of these can be demonstrated, not just when the checklist is ticked.

- [ ] An A -> B -> A redirect cycle with `FollowRedirects = true` ends in a `LoopDetected` result, each URL in the cycle is requested exactly once, and the rest of the crawl finishes normally. The same holds in headless mode.
- [ ] A chain longer than `MaxRedirects` ends in `MaxRedirectsExceeded` without requesting the hop past the limit.
- [ ] No crawl in the test suite depends on the caller's cancellation token to finish.
- [ ] On an authenticated site, every same-origin redirect target receives the configured Basic, bearer or API-key credential, with zero unauthorized requests against a stub equivalent to AssistantHub's `CrawlStubServer`.
- [ ] No request to an origin outside the credential scope carries credentials, whether it comes from a redirect, an external link or an HTTPS to HTTP downgrade. The API-key cross-origin test proves the 1.0.22 leak is closed.
- [ ] Redirect targets outside `RestrictToChildUrls`, the domain rules or `ExcludeLinkPatterns` are never requested.
- [ ] `FinalUrl`, `RedirectChain` and `RedirectOutcome` are populated on every resource, and they appear in the server's SSE output.
- [ ] Links on a redirected page resolve against its final URL.
- [ ] `FollowRedirects = false` returns the first 3xx and never requests the target.
- [ ] Credentials with `Type = None` are sent with a logged warning; incomplete or ambiguous authentication settings throw `ArgumentException` from the constructor, and the server answers 400.
- [ ] README, REST_API.md, the Postman collection, CHANGELOG.md, `Docker/settings.json` and the dashboard describe `RequestDelayMs` and `ThrottleMs` accurately and show `MaxRedirects`.
- [ ] Every version location in the release table reads 1.0.23. The NuGet package and both Docker images (`:v1.0.23` and `:latest`) are published, and the AssistantHub agent has been told.
- [ ] No em-dashes in any file this change touches.

## Noticed, Not Fixed Here

A few requirement gaps turned up in the repository while checking this plan against the requirements set. None of them is related to redirects, and folding them in would blur a focused release, so they are listed here for a later pass. The repository has no `DOCKERHUB_README.md` and no `update.bat` helper in the Docker folder. `Docker/compose.yaml` probes `localhost` with `retries: 3`, where REPOSITORY_REQUIREMENTS.md asks for `127.0.0.1` and 2 retries. The Postman collection sits at the repository root without folders or per-request descriptions, rather than under `assets/postman/`. `FixtureServer.cs` holds two classes, and `CrawlSettings.cs` places its usings outside the namespace; the checklist fixes the latter only because the file is being edited anyway. The server also never disposes the `WebCrawler` it creates per request (`Program.cs:234`), which leaks a browser instance per headless crawl.

The hang is the urgent part of all this, but the credential scoping is the change most worth getting exactly right. A crawler that sends a site's API key to whatever host a redirect names is a security bug, however rare the redirect.
