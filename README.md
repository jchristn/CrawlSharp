<img src="https://raw.githubusercontent.com/jchristn/CrawlSharp/refs/heads/main/assets/icon.png" width="256" height="256">

# CrawlSharp

[![NuGet Version](https://img.shields.io/nuget/v/CrawlSharp.svg?style=flat)](https://www.nuget.org/packages/CrawlSharp/) [![NuGet](https://img.shields.io/nuget/dt/CrawlSharp.svg)](https://www.nuget.org/packages/CrawlSharp) 

CrawlSharp is a library and integrated webserver for crawling basic web content.

## New in v1.1.0

- Redirects are followed by CrawlSharp itself, one hop at a time: a redirect loop ends after one pass instead of hanging the crawl, and chains stop at `MaxRedirects` (default 10)
- Credentials are sent only to the start URL's origin (plus its HTTPS version and any `Authentication.CredentialOrigins`), are re-attached to same-origin redirect targets, and never reach another origin
- `WebResource` gains `FinalUrl`, `RedirectChain` and `RedirectOutcome`, and links on a redirected page resolve against its final URL
- Incomplete or ambiguous `Authentication` settings throw from the `WebCrawler` constructor, and the server answers `400`
- `FollowRedirects = false` now returns the redirect response itself; see [Upgrading to 1.1.0](#upgrading-to-110)

## Bugs, Feedback, or Enhancement Requests

Please feel free to start an issue or a discussion!

## Simple Example, Embedded 

Embedding CrawlSharp into your application is simple and requires minimal configuration.  Refer to the ```Test``` project for a full example.

```csharp
using System.Collections.Generic;
using CrawlSharp.Web;

Settings settings = new Settings();
settings.Crawl.StartUrl = "http://www.mywebpage.com";
settings.Crawl.UseHeadlessBrowser = true; // slow but useful for sites that block bots or where content must be rendered

using (WebCrawler crawler = new WebCrawler(settings))
{
  await foreach (WebResource resource in crawler.CrawlAsync()) 
    Console.WriteLine(resource.Status + ": " + resource.Url);
}
```

`WebCrawler.CrawlAsync` can be `await`ed, returning an `IAsyncEnumerable<WebResource>` whereas `WebCrawler.Crawl` cannot be `await`ed, returning an `IEnumerable<WebResource>`.

Opt-in auto-expansion can be enabled for headless crawls when you need CrawlSharp to open common collapsible UI patterns before HTML capture:

```csharp
using CrawlSharp.Web;

Settings settings = new Settings();
settings.Crawl.StartUrl = "https://www.mywebpage.com";
settings.Crawl.UseHeadlessBrowser = true;
settings.Crawl.AutoExpandCollapsibles = true;
settings.Crawl.PostLoadDelayMs = 500;
settings.Crawl.ExpansionSelectors = new List<string>
{
  ".faq-toggle"
};

using (WebCrawler crawler = new WebCrawler(settings))
{
  await foreach (WebResource resource in crawler.CrawlAsync())
    Console.WriteLine(resource.Status + ": " + resource.Url);
}
```

## Crawl Settings

| Setting | Type | Default | Description |
|---|---|---|---|
| `UserAgent` | `string` | `CrawlSharp` | User agent string sent with requests |
| `StartUrl` | `string` | `null` | The URL from which to begin crawling |
| `UseHeadlessBrowser` | `bool` | `false` | Use a headless browser (Playwright) for crawling |
| `AutoExpandCollapsibles` | `bool` | `false` | Opt in to expanding common collapsible UI patterns before headless HTML capture |
| `PostLoadDelayMs` | `int` | `0` | Delay in milliseconds after navigation and before headless auto-expansion starts |
| `PostInteractionDelayMs` | `int` | `250` | Delay in milliseconds after each headless expansion pass |
| `MaxExpansionPasses` | `int` | `2` | Maximum number of headless expansion passes before HTML capture |
| `ExpansionSelectors` | `List<string>` | `[]` | Additional CSS selectors to click during headless auto-expansion |
| `IgnoreRobotsText` | `bool` | `false` | Ignore the robots.txt file |
| `IncludeSitemap` | `bool` | `true` | Include URLs from sitemap.xml |
| `FollowLinks` | `bool` | `true` | Follow links found on crawled pages |
| `FollowRedirects` | `bool` | `true` | Follow redirects (with loop detection, scope checks and origin-scoped credentials); when `false`, the redirect response itself is returned. See [Redirects](#redirects) |
| `MaxRedirects` | `int` | `10` | Maximum redirects to follow for one resource (1 to 50; values outside throw) |
| `RestrictToChildUrls` | `bool` | `true` | Only follow links that are children of the start URL |
| `RestrictToSameSubdomain` | `bool` | `true` | Only follow links within the same subdomain |
| `RestrictToSameRootDomain` | `bool` | `true` | Only follow links within the same root domain |
| `AllowedDomains` | `List<string>` | `[]` | If non-empty, only these domains will be crawled |
| `DeniedDomains` | `List<string>` | `[]` | If non-empty, these domains will be excluded |
| `MaxCrawlDepth` | `int` | `5` | Maximum depth of links to follow from the start URL |
| `ExcludeLinkPatterns` | `List<Regex>` | `[]` | Regex patterns for URLs to exclude from crawling |
| `FollowExternalLinks` | `bool` | `true` | Follow links to external domains |
| `MaxParallelTasks` | `int` | `8` | Maximum number of concurrent crawl tasks |
| `PageTimeoutMs` | `int` | `30000` | Timeout in milliseconds for retrieving each page (minimum 1000) |
| `ThrottleMs` | `int` | `5000` | One pause in milliseconds before a 429 is returned as the result, taken only when `RetryOn429` is `false` or its retries are used up (minimum 0). See [Pacing](#pacing) |
| `RetryOn429` | `bool` | `true` | Enable automatic retry with backoff on 429 responses |
| `MaxRetries` | `int` | `3` | Maximum number of retry attempts on 429 (minimum 1) |
| `RetryMinBackoffMs` | `int` | `1000` | Minimum backoff delay in milliseconds (minimum 100) |
| `RetryMaxBackoffMs` | `int` | `30000` | Maximum backoff delay in milliseconds (minimum 1000) |
| `RetryBackoffJitter` | `bool` | `true` | Add random jitter to backoff delay to avoid thundering herd |
| `RequestDelayMs` | `int` | `2500` | Pause in milliseconds before every request (pages, robots.txt, sitemap.xml, redirect hops), per parallel task; a robots.txt `Crawl-delay` replaces it (minimum 0). See [Pacing](#pacing) |

### Rendered HTML in Headless Mode

When `UseHeadlessBrowser` is enabled for navigable pages, CrawlSharp captures the rendered DOM HTML from Playwright and stores it in `WebResource.Data`.

When headless crawling is not used, CrawlSharp returns the server response bytes directly. For non-navigable assets such as PDFs, CrawlSharp also uses direct HTTP retrieval even when headless crawling is enabled.

### Headless Auto-Expand

`AutoExpandCollapsibles` is disabled by default. Enable it when a page only inserts usable content into the DOM after a collapsible control is opened.

When enabled in headless mode, CrawlSharp will:

- Open closed `<details>` elements
- Click a conservative set of common collapsible controls such as ARIA-backed toggles and Bootstrap collapse buttons
- Apply any additional selectors supplied through `ExpansionSelectors`

Use `PostLoadDelayMs` when a page hydrates UI after the browser `load` event. Use `PostInteractionDelayMs` and `MaxExpansionPasses` to give nested lazy content time to appear between expansion passes.

`ExpansionSelectors` should stay narrow. Over-broad selectors can trigger unintended clicks and change the captured output.

### Retry on 429 (Too Many Requests)

When `RetryOn429` is enabled, the crawler will automatically retry individual page retrievals that receive a `429` status code. Retries use exponential backoff: the delay for each attempt is calculated as `RetryMinBackoffMs * 2^attempt`, capped at `RetryMaxBackoffMs`. When `RetryBackoffJitter` is enabled, the actual delay is randomized between 0 and the computed value to avoid synchronized retries across parallel tasks.

If all retry attempts are exhausted and the server still returns 429, the crawler falls back to the `ThrottleMs` delay and returns the 429 response as the result for that URL.

### Pacing

Two settings control delays, and they do different jobs:

| Setting | When it applies | Notes |
|---|---|---|
| `RequestDelayMs` | Before every request: pages, robots.txt, sitemap.xml and each redirect hop | Taken per parallel task, so with `MaxParallelTasks = 8` up to eight requests can start per interval. A robots.txt `Crawl-delay` replaces it entirely, whether higher or lower; `WebCrawler.Delay` shows the value in effect |
| `ThrottleMs` | Once, after a 429 response, just before that 429 is returned as the result | Only when `RetryOn429` is `false` or its `MaxRetries` are used up. It does not pace normal requests |

To crawl politely, tune `RequestDelayMs` and `MaxParallelTasks` together. `ThrottleMs` only matters once a server has started refusing requests.

### Redirects

CrawlSharp follows redirects itself rather than letting the HTTP stack do it, so every hop gets the same checks:

- **Hop limit**: at most `MaxRedirects` redirects (default 10) are followed per resource. A longer chain stops with `RedirectOutcome = MaxRedirectsExceeded`, and the hop past the limit is not requested.
- **Loop detection**: when a chain returns to a URL it already requested (with the same cookies), it stops with `LoopDetected`. An A to B to A loop requests each URL once, and the rest of the crawl carries on.
- **Cookies**: cookies set by one hop are sent on the next hop of the same chain, so a consent wall or session bootstrap that sets a cookie and redirects back works. Cookies are not shared between pages.
- **Scope**: when `FollowLinks` is `true`, a redirect target must pass the same filters as a link (`RestrictToChildUrls`, `RestrictToSameSubdomain`, `RestrictToSameRootDomain`, `AllowedDomains`, `DeniedDomains`, `FollowExternalLinks`, `ExcludeLinkPatterns`); one that does not is not requested and the result has `OutOfScope`. robots.txt and sitemap.xml redirects are followed only on the start URL's host.
- **robots.txt**: a redirect to a disallowed path is not requested (`RobotsDisallowed`).
- **Credentials** are attached per hop according to the [credential scope](#authentication).
- **Pacing**: `RequestDelayMs` applies before each hop.

The resource that comes back always describes what happened. `Url` is the requested address, `FinalUrl` is where the content came from, `RedirectChain` lists each redirect, and `RedirectOutcome` is one of:

| Outcome | Meaning |
|---|---|
| `None` | The first response was not a redirect |
| `Followed` | One or more redirects were followed to a response that is not a redirect |
| `NotFollowed` | `FollowRedirects` is `false`; the redirect response is the result |
| `LoopDetected` | The chain returned to a URL it had already requested |
| `MaxRedirectsExceeded` | The chain was longer than `MaxRedirects` |
| `OutOfScope` | The target is outside the crawl scope and was not requested |
| `RobotsDisallowed` | The target is disallowed by robots.txt and was not requested |
| `MissingLocation` | A redirect status arrived without a `Location` header |
| `InvalidLocation` | The `Location` header is not a valid http or https URL |

For every outcome other than `None` and `Followed`, `Status` and `Data` come from the last redirect response, not from page content, and links in that body are not followed.

Only 301, 302, 303, 307 and 308 are redirects, plus 300 when it carries a `Location`; 304 Not Modified is returned as-is. Links on a redirected page resolve against `FinalUrl`, so `/docs` redirecting to `/docs/` resolves `guide.html` to `/docs/guide.html`. When two URLs redirect to the same page, the page is fetched and returned once.

Configure the canonical start URL. If `https://example.com` redirects to `https://www.example.com` and `RestrictToSameSubdomain` is on, the start page comes back as `OutOfScope`, and its `RedirectChain` shows the address to use instead.

When `UseHeadlessBrowser` is enabled, CrawlSharp resolves the chain with a HEAD request before starting the browser, then navigates straight to the final URL, so the same limits apply. A page that redirects differently on GET is resolved again with GET before navigating.

## Authentication

Set `Settings.Authentication` to crawl a site that needs credentials:

| `Type` | Required fields | Sent as |
|---|---|---|
| `None` (default) | none; every credential field must be empty | nothing |
| `Basic` | `Username` (`Password` optional) | `Authorization: Basic ...` |
| `BearerToken` | `BearerToken` | `Authorization: Bearer ...` |
| `ApiKey` | `ApiKeyHeader` and `ApiKey` | the header named by `ApiKeyHeader` |

```csharp
Settings settings = new Settings();
settings.Crawl.StartUrl = "https://intranet.example.com";
settings.Authentication = new AuthenticationSettings
{
  Type = AuthenticationTypeEnum.Basic,
  Username = "crawler",
  Password = "secret"
};
```

**Validation.** The `WebCrawler` constructor calls `AuthenticationSettings.Validate()` and throws `ArgumentException` when the settings are incomplete or ambiguous: credential fields set while `Type` is `None`, a missing required field, or an invalid `CredentialOrigins` entry. Empty and whitespace strings count as unset. The REST server returns these as `400 Bad Request`.

**Credential scope.** Credentials are sent only to requests whose origin (scheme, host and port) is one of:

- the origin of `StartUrl`
- when `StartUrl` is plain HTTP, the HTTPS origin on the same host (the common HTTP to HTTPS upgrade)
- any origin listed in `Authentication.CredentialOrigins`, for sites that span several origins, for example `"https://docs.example.com"`

The rule applies per request and per redirect hop, for the REST client and the headless browser alike. A same-origin redirect target gets credentials; a redirect or link to another origin never does, and an HTTPS start URL never sends credentials to an HTTP downgrade.

## Upgrading to 1.1.0

- **`FollowRedirects = false` is now literal.** In 1.0.22 the HTTP stack followed redirects regardless of this setting, so `false` still returned the final page. It now returns the redirect response (usually an empty 3xx) with `RedirectOutcome = NotFollowed`. If you set `false` to avoid the redirect-loop hang, set it back to `true` (the default): loops are now detected.
- **Credentials stay on their origin.** 1.0.22 attached credentials to every request, including external links and other origins reached by redirects. An authenticated crawl that spans several origins now needs those origins in `Authentication.CredentialOrigins`.
- **Authentication settings are validated.** Credentials with `Type = None`, which were silently ignored, now throw. So does an incomplete configuration such as `Type = ApiKey` without `ApiKeyHeader`, which used to produce an empty crawl.
- **304, 305 and 306 are no longer treated as redirects.**
- **`Url` is the requested address.** Read `FinalUrl` for the address the content came from.

## Web Resources

Objects crawled using CrawlSharp have the following properties:

- `Url` - the URL that was requested
- `FinalUrl` - the URL the content in `Data` came from; equal to `Url` when there was no redirect
- `RedirectChain` - the redirects received, in order, each with `Url`, `Status` and `Location`; empty when there was no redirect
- `RedirectOutcome` - why redirect following stopped; see [Redirects](#redirects)
- `ParentUrl` - the URL from which the `Url` was identified
- `Filename` - the filename component from the URL, if any
- `Depth` - the depth level at which the `Url` was identified
- `Status` - the HTTP status code returned when retrieving the `Url`
- `ContentLength` - the content length of the body returned when retrieving `Url`
- `ContentType` - the content type returned while retrieving `Url`
- `MD5Hash` - the MD5 hash of the `Data`
- `SHA1Hash` - the SHA1 hash of the `Data`
- `SHA256Hash` - the SHA256 hash of the `Data`
- `LastModified` - the `DateTime` from when the headers indicate the object was last modified
- `Headers` - a `NameValueCollection` with the headers returned while retrieving `Url`
- `Data` - a `byte[]` containing the data returned while retrieving `Url`

## REST API

CrawlSharp includes a project called `CrawlSharp.Server` which allows you to deploy a RESTful front-end for CrawlSharp.  Refer to `REST_API.md` and also the Postman collection in the root of this repository for details.

`CrawlSharp.Server` will by default listen on host `localhost` and port `8000`, meaning it will not accept requests from outside of the machine.

To change this, specify the hostname as the first argument and the port as the second, i.e. `dotnet CrawlSharp.Server myhostname.com 8888`.

```
$ dotnet CrawlSharp.Server 

                          _     _  _
   ___ _ __ __ ___      _| |  _| || |_
  / __| '__/ _` \ \ /\ / / | |_  ..  _|
 | (__| | | (_| |\ V  V /| | |_      _|
  \___|_|  \__,_| \_/\_/ |_|   |_||_|

(c)2026 Joel Christner


Usage:
  crawlsharp [hostname] [port]

Where:
  [hostname] is the hostname or IP address on which to listen
  [port] is the port number, greater than or equal to zero, and less than 65536

NOTICE
------
Configured to listen on local address 'localhost'
Service will not receive requests from outside of localhost

Webserver started on http://localhost:8000/

2025-03-01 20:39:17 joel-laptop Info [CrawlSharpServer] server started
```

Refer to `REST_API.md` for more information about using the RESTful API.

## Dashboard

CrawlSharp includes a web-based dashboard for configuring, launching, and monitoring crawls through your browser.  The dashboard is a React (Vite) application located in the `dashboard/` directory.

### Features

- **Server selector** - switch the dashboard between proxy, localhost, and custom server endpoints from the top-right toolbar

- **New Crawl** - configure all crawl and authentication settings through the UI and launch a crawl against the CrawlSharp server
- **Active Crawl** - monitor a running crawl in real time with a live feed of discovered resources, status code distribution, and content type breakdown
- **Crawl History** - view past crawl results, including per-page status, content types, sizes, and hashes
- **Templates** - save, duplicate, and reuse crawl configurations for repeated jobs

### Running the Dashboard Locally

Prerequisites: [Node.js](https://nodejs.org/) (v18 or later).

```bash
cd dashboard
npm install
npm run dev
```

The dashboard will start on `http://localhost:8001` and expects the CrawlSharp server to be running on `http://localhost:8000`.  The Vite dev server proxies `/crawl` requests to the server automatically.

### Building for Production

```bash
cd dashboard
npm run build
```

The compiled output is written to `dashboard/dist/` and can be served by any static file server.

### Configuring the Server URL

The dashboard determines the CrawlSharp server URL in the following order of precedence:

Use the top-right server endpoint icon in the dashboard toolbar to change the active endpoint without editing local storage by hand.

1. **localStorage** - the value saved at key `crawlsharp_server_url` (set through the dashboard UI)
2. **Runtime config** - the `CRAWLSHARP_SERVER_URL` value in `public/config.js`, which is overridden at container startup when running in Docker
3. **Default** - `http://localhost:8000`

### Running with Docker Compose

The easiest way to run both the server and dashboard together is with Docker Compose.  The `Docker/compose.yaml` includes both the `crawlsharp-server` and `crawlsharp-ui` services.  The dashboard container uses nginx to reverse-proxy API requests to the CrawlSharp server internally, so no direct browser-to-server connectivity is needed.

The `CRAWLSHARP_SERVER_URL` environment variable controls the server URL used by the dashboard.  When left empty (the default in Docker Compose), the dashboard routes API requests through its own nginx proxy.  When running the dashboard outside of Docker, set it to the server's URL (e.g. `http://localhost:8000`).

To start both services:

```bash
cd Docker
docker compose up -d
```

The server is available at `http://localhost:8000` and the dashboard at `http://localhost:8001`.

Use `docker compose down` (or the provided `compose-down` scripts) to stop.

## Running in Docker

A Docker image is available in [Docker Hub](https://hub.docker.com/r/jchristn77/crawlsharp) under `jchristn77/crawlsharp`.  Use the Docker Compose start (`compose-up.sh` and `compose-up.bat`) and stop (`compose-down.sh` and `compose-down.bat`) scripts in the `Docker` directory if you wish to run within Docker Compose.

## Using Headless Browser

CrawlSharp can use `Microsoft.Playwright` to crawl content to overcome challenging websites that detect and block bots or require content to be rendered from Javascript.  If you run this code on an Ubuntu machine, use the following script to install dependencies that will be required.  Also note that the `$HOME` directory must be owned by the user running the code.

```
#!/bin/bash

# Detect Ubuntu version
VERSION=$(lsb_release -rs)

if [[ "$VERSION" == "24.04" ]]; then
    # Ubuntu 24.04 packages
    PACKAGES="libasound2t64 libatk-bridge2.0-0t64 libatk1.0-0t64 libcups2t64 libgtk-3-0t64"
else
    # Ubuntu 22.04 and earlier
    PACKAGES="libasound2 libatk-bridge2.0-0 libatk1.0-0 libcups2 libgtk-3-0"
fi

# Install common packages plus version-specific ones
sudo apt-get update
sudo apt-get install -y \
    $PACKAGES \
    libnspr4 \
    libnss3 \
    libdrm2 \
    libxkbcommon0 \
    libxcomposite1 \
    libxdamage1 \
    libxrandr2 \
    libgbm1 \
    libxss1 \
    fonts-liberation \
    ca-certificates
```

## Third-Party Data

CrawlSharp is licensed under MIT and uses the [Nager.PublicSuffix](https://github.com/nager/Nager.PublicSuffix) library (MIT license) for domain matching coupled with [third-party public suffix data](https://publicsuffix.org/list/public_suffix_list.dat) (Mozilla Public License v2.0).  Please be aware of the license for this information.

## Version History

Please refer to ```CHANGELOG.md``` for version history.
