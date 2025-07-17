# REST API for CrawlSharp Server

## Validate Connectivity

Either a `GET` or `HEAD` request to the root URL `/` will return a `200/OK` if the server receives the request.

## Crawl a URL

Use `POST` to `/crawl` to crawl a particular URL.  Set the `Content-Type` header to `application/json` and pass in a `Settings` object.

`WebResource` objects are returned using server-sent events (SSE).

Upon completion, `data` will be sent with the value `[end]`.

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
    "MaxParallelTasks": 16,
    "ExcludeLinkPatterns": [],
    "FollowExternalLinks": true
  }
}

Response:
data: {"Url":"https://somehost.com/page1","ParentUrl":"https://somehost.com","Depth":1,"Status":200,"ContentLength":46586,"Headers":{"Age":"0","Cache-Control":"no-store, must-revalidate, no-cache, max-age=0, private","Date":"Sun, 02 Mar 2025 20:21:54 GMT","ETag":"\u0022b8w9q5o4vtzxw\u0022"},"Data":"[page data as base64]"}

data: {"Url":"https://somehost.com/page2","ParentUrl":"https://somehost.com","Depth":1,"Status":200,"ContentLength":1234,"Headers":{"Age":"0","Cache-Control":"no-store, must-revalidate, no-cache, max-age=0, private","Date":"Sun, 02 Mar 2025 20:21:54 GMT","ETag":"\u0022b8w9q5o4vtzxw\u0022"},"Data":"[page data as base64]"}

data: [end]
```