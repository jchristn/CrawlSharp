<img src="https://raw.githubusercontent.com/jchristn/CrawlSharp/refs/heads/main/assets/icon.png" width="256" height="256">

# CrawlSharp

[![NuGet Version](https://img.shields.io/nuget/v/CrawlSharp.svg?style=flat)](https://www.nuget.org/packages/CrawlSharp/) [![NuGet](https://img.shields.io/nuget/dt/CrawlSharp.svg)](https://www.nuget.org/packages/CrawlSharp) 

CrawlSharp is a library and integrated webserver for crawling basic web content.

## New in v1.0.x   

- Initial release
- Added support for headless browser crawling (using `Microsoft.Playwright`)

## Bugs, Feedback, or Enhancement Requests

Please feel free to start an issue or a discussion!

## Simple Example, Embedded 

Embedding CrawlSharp into your application is simple and requires minimal configuration.  Refer to the ```Test``` project for a full example.

```csharp
using CrawlSharp;

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

(c)2025 Joel Christner


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

## Running in Docker

A Docker image is available in [Docker Hub](https://hub.docker.com/r/jchristn/crawlsharp) under `jchristn/crawlsharp`.  Use the Docker Compose start (`compose-up.sh` and `compose-up.bat`) and stop (`compose-down.sh` and `compose-down.bat`) scripts in the `Docker` directory if you wish to run within Docker Compose. 

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

## Version History

Please refer to ```CHANGELOG.md``` for version history.