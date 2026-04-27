<img src="https://raw.githubusercontent.com/jchristn/CrawlSharp/refs/heads/main/assets/icon.png" width="256" height="256">

# CrawlSharp

[![NuGet Version](https://img.shields.io/nuget/v/CrawlSharp.svg?style=flat)](https://www.nuget.org/packages/CrawlSharp/) [![NuGet](https://img.shields.io/nuget/dt/CrawlSharp.svg)](https://www.nuget.org/packages/CrawlSharp) 

CrawlSharp is a library and integrated webserver for crawling basic web content.

## New in v1.0.22

- Added opt-in auto-expansion of common collapsible content for headless crawls
- Added tunable headless expansion delays, expansion pass count, and custom expansion selectors
- Added a top-right dashboard server endpoint selector for proxy, localhost, and custom server URLs
- Clarified rendered HTML capture behavior for headless navigable pages and direct-download handling for non-navigable assets
- Added automated coverage for rendered HTML capture, revealed-link discovery, and PDF fallback behavior

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
| `FollowRedirects` | `bool` | `true` | Follow HTTP redirect responses |
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
| `ThrottleMs` | `int` | `5000` | Delay in milliseconds when a 429 response is received and retries are exhausted |
| `RetryOn429` | `bool` | `true` | Enable automatic retry with backoff on 429 responses |
| `MaxRetries` | `int` | `3` | Maximum number of retry attempts on 429 (minimum 1) |
| `RetryMinBackoffMs` | `int` | `1000` | Minimum backoff delay in milliseconds (minimum 100) |
| `RetryMaxBackoffMs` | `int` | `30000` | Maximum backoff delay in milliseconds (minimum 1000) |
| `RetryBackoffJitter` | `bool` | `true` | Add random jitter to backoff delay to avoid thundering herd |
| `RequestDelayMs` | `int` | `2500` | Delay in milliseconds between each HTTP request |

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

## Web Resources

Objects crawled using CrawlSharp have the following properties:

- `Url` - the URL from which the resource was retrieved
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

- **New Crawl** — configure all crawl and authentication settings through the UI and launch a crawl against the CrawlSharp server
- **Active Crawl** — monitor a running crawl in real time with a live feed of discovered resources, status code distribution, and content type breakdown
- **Crawl History** — view past crawl results, including per-page status, content types, sizes, and hashes
- **Templates** — save, duplicate, and reuse crawl configurations for repeated jobs

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

1. **localStorage** — the value saved at key `crawlsharp_server_url` (set through the dashboard UI)
2. **Runtime config** — the `CRAWLSHARP_SERVER_URL` value in `public/config.js`, which is overridden at container startup when running in Docker
3. **Default** — `http://localhost:8000`

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
