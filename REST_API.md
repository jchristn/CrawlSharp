# REST API for CrawlSharp Server

## Validate Connectivity

Either a `GET` or `HEAD` request to the root URL `/` will return a `200/OK` if the server receives the request.

## Crawl a URL

Use `POST` to `/crawl` to crawl a particular URL.  Set the `Content-Type` header to `application/json` and pass in a `Settings` object.

`WebResource` objects are returned using server-sent events (SSE).

Upon completion, `data` will be sent with the value `[DONE]`.

When `UseHeadlessBrowser` is enabled for navigable pages, `WebResource.Data` contains rendered HTML captured from the browser DOM.
`AutoExpandCollapsibles` is opt-in, ignored unless `UseHeadlessBrowser` is `true`, and can be combined with the delay and selector settings shown below.

The crawler follows redirects itself, up to `MaxRedirects` hops per page, and stops a redirect loop after one pass.  Each `WebResource` describes what happened with these fields:

- `Url` is the address that was requested
- `FinalUrl` is the address the content in `Data` came from; it equals `Url` when there was no redirect
- `RedirectChain` lists each redirect received (`Url`, `Status`, `Location`)
- `RedirectOutcome` says why redirect following stopped: `None`, `Followed`, `NotFollowed`, `LoopDetected`, `MaxRedirectsExceeded`, `OutOfScope`, `RobotsDisallowed`, `MissingLocation` or `InvalidLocation`

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
    "MaxRedirects": 10,
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
data: {"Url":"https://somehost.com/page1","FinalUrl":"https://somehost.com/page1","RedirectChain":[],"RedirectOutcome":"None","ParentUrl":"https://somehost.com","Depth":1,"Status":200,"ContentLength":46586,"Headers":{"Age":"0","Cache-Control":"no-store, must-revalidate, no-cache, max-age=0, private","Date":"Sun, 02 Mar 2025 20:21:54 GMT","ETag":""b8w9q5o4vtzxw""},"Data":"[page data as base64]"}

data: {"Url":"https://somehost.com/docs","FinalUrl":"https://somehost.com/docs/","RedirectChain":[{"Url":"https://somehost.com/docs","Status":301,"Location":"https://somehost.com/docs/"}],"RedirectOutcome":"Followed","ParentUrl":"https://somehost.com","Depth":1,"Status":200,"ContentLength":1234,"Headers":{"Age":"0","Cache-Control":"no-store, must-revalidate, no-cache, max-age=0, private","Date":"Sun, 02 Mar 2025 20:21:54 GMT"},"Data":"[page data as base64]"}

data: [DONE]
```

## Crawl an Authenticated Site

Set `Authentication.Type` to `Basic`, `ApiKey` or `BearerToken` and supply that type's fields.  Credentials are sent only to the start URL's origin (scheme, host and port), to its HTTPS version when the start URL is plain HTTP, and to any origins listed in `CredentialOrigins`.  They are attached to each qualifying request, including same-origin redirect targets, and never to other origins, whether reached by a redirect or a link.

```
POST /crawl
Content-Type: application/json
{
  "Authentication": {
    "Type": "Basic",
    "Username": "crawler",
    "Password": "secret",
    "CredentialOrigins": [
      "https://docs.somehost.com"
    ]
  },
  "Crawl": {
    "StartUrl": "https://somehost.com",
    "FollowLinks": true,
    "FollowRedirects": true,
    "MaxRedirects": 10,
    "MaxCrawlDepth": 2
  }
}
```

## Errors

Invalid settings are rejected with `400/Bad Request` before the event stream starts, and the `Description` explains the problem.  This includes incomplete or ambiguous `Authentication` (for example `Type` set to `ApiKey` without `ApiKeyHeader`, or credential fields set while `Type` is `None`), a `CredentialOrigins` entry that is not an absolute http or https URL, and out-of-range values such as `MaxRedirects` outside 1 to 50.

```
HTTP/1.1 400 Bad Request
Content-Type: application/json
{
  "Error": "BadRequest",
  "Message": "We were unable to discern your request.  Please check your URL, query, and request body.",
  "StatusCode": 400,
  "Description": "Authentication.Type is ApiKey, but ApiKeyHeader is not set. (Parameter 'ApiKeyHeader')"
}
```
