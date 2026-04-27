# REST API for CrawlSharp Server

## Validate Connectivity

Either a `GET` or `HEAD` request to the root URL `/` will return a `200/OK` if the server receives the request.

## Crawl a URL

Use `POST` to `/crawl` to crawl a particular URL.  Set the `Content-Type` header to `application/json` and pass in a `Settings` object.

`WebResource` objects are returned using server-sent events (SSE).

Upon completion, `data` will be sent with the value `[DONE]`.

When `UseHeadlessBrowser` is enabled for navigable pages, `WebResource.Data` contains rendered HTML captured from the browser DOM.
`AutoExpandCollapsibles` is opt-in, ignored unless `UseHeadlessBrowser` is `true`, and can be combined with the delay and selector settings shown below.

```
POST /crawl
Content-Type: application/json
{
  "Authentication": {
    "Type": "None"
  },
  "Crawl": {
    "UserAgent": "CrawlSharp",
    "StartUrl": "https://somehost.com",
    "UseHeadlessBrowser": false,
    "AutoExpandCollapsibles": false,
    "PostLoadDelayMs": 0,
    "PostInteractionDelayMs": 250,
    "MaxExpansionPasses": 2,
    "ExpansionSelectors": [],
    "IgnoreRobotsText": false,
    "IncludeSitemap": true,
    "FollowLinks": true,
    "FollowRedirects": true,
    "RestrictToChildUrls": false,
    "RestrictToSameSubdomain": false,
    "RestrictToSameRootDomain": true,
    "AllowedDomains": [],
    "DeniedDomains": [],
    "MaxCrawlDepth": 2,
    "ExcludeLinkPatterns": [],
    "FollowExternalLinks": true,
    "MaxParallelTasks": 16,
    "PageTimeoutMs": 30000,
    "ThrottleMs": 5000,
    "RetryOn429": true,
    "MaxRetries": 3,
    "RetryMinBackoffMs": 1000,
    "RetryMaxBackoffMs": 30000,
    "RetryBackoffJitter": true,
    "RequestDelayMs": 2500
  }
}

Response:
data: {"Url":"https://somehost.com/page1","ParentUrl":"https://somehost.com","Depth":1,"Status":200,"ContentLength":46586,"Headers":{"Age":"0","Cache-Control":"no-store, must-revalidate, no-cache, max-age=0, private","Date":"Sun, 02 Mar 2025 20:21:54 GMT","ETag":"\u0022b8w9q5o4vtzxw\u0022"},"Data":"[page data as base64]"}

data: {"Url":"https://somehost.com/page2","ParentUrl":"https://somehost.com","Depth":1,"Status":200,"ContentLength":1234,"Headers":{"Age":"0","Cache-Control":"no-store, must-revalidate, no-cache, max-age=0, private","Date":"Sun, 02 Mar 2025 20:21:54 GMT","ETag":"\u0022b8w9q5o4vtzxw\u0022"},"Data":"[page data as base64]"}

data: [DONE]
```
